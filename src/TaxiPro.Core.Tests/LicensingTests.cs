using System.Security.Cryptography;
using System.Text;
using Rotaidem.Licensing;
using TaxiPro.Core.Models;
using TaxiPro.Core.Physics;
using TaxiPro.Licensing;

namespace TaxiPro.Core.Tests;

public class LicensingTests
{
    [Fact]
    public void TaxiPro_keys_start_with_TPRO_and_carry_a_check_character()
    {
        var key = LicenseKey.Generate(TaxiProLicense.KeyPrefix);
        Assert.StartsWith("TPRO-", key);
        Assert.True(TaxiProLicense.IsWellFormed(key));
        Assert.True(TaxiProLicense.IsWellFormed(" " + key.ToLowerInvariant() + " "));
        // One wrong character fails the check character; a HeavyPro key is not a TaxiPro key.
        var typo = key.Substring(0, key.Length - 1) + (key[key.Length - 1] == 'A' ? 'B' : 'A');
        Assert.False(TaxiProLicense.IsWellFormed(typo));
        Assert.False(TaxiProLicense.IsWellFormed(LicenseKey.Generate(LicenseKey.HeavyProPrefix)));
    }

    [Fact]
    public void The_built_in_public_key_is_the_rotaidem_server_key()
    {
        Assert.NotNull(TaxiProLicense.PublicKeyJwk);
        Assert.NotNull(LicenseToken.FromJwk(TaxiProLicense.PublicKeyJwk));
    }

    [Fact]
    public void Licence_messages_name_TaxiPro()
    {
        _ = TaxiProLicense.Store(); // runs the static set-up (a const would not)
        var store = new LicenseStore("taxipro-test", Path.Combine(Path.GetTempPath(), "tp-" + Guid.NewGuid().ToString("N")));
        store.Save(LicenseKey.Generate("TPRO"), DateTimeOffset.UtcNow, "a.b.c");
        var result = new LicenseClient(product: "taxipro", publicKeyJwk: null).CheckOffline(store);
        Assert.Contains("TaxiPro", result.Message);
        Assert.DoesNotContain("HeavyPro", result.Message);
    }

    [Fact]
    public void Only_answers_signed_by_the_server_for_this_PC_and_product_count()
    {
        using var rsa = RSA.Create(2048);
        var p = rsa.ExportParameters(false);
        var jwk = $"{{\"kty\":\"RSA\",\"n\":\"{B64(p.Modulus!)}\",\"e\":\"{B64(p.Exponent!)}\",\"kid\":\"test\"}}";
        var verifier = LicenseToken.FromJwk(jwk)!;
        var key = LicenseKey.Generate("TPRO");
        var now = DateTimeOffset.UtcNow;
        string Token(string aud, string mid, long exp) => Sign(rsa,
            $"{{\"aud\":\"{aud}\",\"key\":\"{key}\",\"mid\":\"{mid}\",\"nonce\":\"n1\",\"iat\":{now.ToUnixTimeSeconds()},\"exp\":{exp},\"activations_used\":1,\"max_activations\":2}}");
        var later = now.AddDays(14).ToUnixTimeSeconds();

        Assert.NotNull(verifier.Verify(Token("rotaidem-taxipro", "pc1", later), "rotaidem-taxipro", "pc1", key, "n1", now, null, out _, out _));
        Assert.Null(verifier.Verify(Token("rotaidem-taxipro", "pc2", later), "rotaidem-taxipro", "pc1", key, "n1", now, null, out _, out _));
        Assert.Null(verifier.Verify(Token("rotaidem-heavypro", "pc1", later), "rotaidem-taxipro", "pc1", key, "n1", now, null, out var wrong, out _));
        Assert.Equal(LicenseStatus.WrongProduct, wrong);
        Assert.Null(verifier.Verify(Token("rotaidem-taxipro", "pc1", now.AddMinutes(-1).ToUnixTimeSeconds()), "rotaidem-taxipro", "pc1", key, "n1", now, null, out var expired, out _));
        Assert.Equal(LicenseStatus.LeaseExpired, expired);
        // Signed by anyone else: refused.
        using var other = RSA.Create(2048);
        var forged = Sign(other, $"{{\"aud\":\"taxipro\",\"key\":\"{key}\",\"mid\":\"pc1\",\"iat\":{now.ToUnixTimeSeconds()},\"exp\":{later}}}");
        Assert.Null(verifier.Verify(forged, "taxipro", "pc1", key, null, now, null, out _, out _));
    }

    [Fact]
    public void The_gate_opens_until_the_lease_and_locks_at_once()
    {
        var now = DateTimeOffset.UtcNow;
        LicenseGate.Unlock(now.AddMinutes(20));
        Assert.True(LicenseGate.IsUnlockedAt(now));
        Assert.False(LicenseGate.IsUnlockedAt(now.AddMinutes(21)));
        LicenseGate.Lock();
        Assert.False(LicenseGate.IsUnlockedAt(now));
        Assert.Null(LicenseGate.Until);
    }

    [Fact]
    public void Unlicensed_TaxiPro_passes_everything_through()
    {
        var engine = new TaxiEngine(() => false);
        TaxiOutput o = null!;
        for (var i = 0; i < 30; i++) o = engine.Step(Sim.Parked777(), new TaxiInputs(1, 1, 1), new TaxiSettings(), false, Sim.Dt);
        Assert.False(o.Speed.Write);
        Assert.False(o.OwnsBrakes);
        Assert.False(o.OwnsTiller);
        Assert.Equal("NOT LICENSED", o.Badge);
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Sign(RSA rsa, string claims)
    {
        var head = B64(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\",\"kid\":\"test\"}"));
        var body = B64(Encoding.UTF8.GetBytes(claims));
        var sig = rsa.SignData(Encoding.ASCII.GetBytes(head + "." + body), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return head + "." + body + "." + B64(sig);
    }
}
