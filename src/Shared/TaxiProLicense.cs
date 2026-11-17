using System.IO;
using System.Net.Http;
using System.Reflection;
using Rotaidem.Licensing;

namespace TaxiPro.Licensing;

/// <summary>
/// Licence settings shared by the app and the installer (linked into both projects), so the
/// server address and product live in one place. The same scheme as HeavyPro's HeavyProLicense:
/// keys look like TPRO-XXXX-XXXX-XXXX, rotaidem.com activates them and signs its answer, and the
/// app trusts only answers signed with the server's private key.
/// </summary>
internal static class TaxiProLicense
{
    public const string ServerUrl = LicenseClient.DefaultBaseUrl; // https://rotaidem.com
    public const string Product = "taxipro";
    public const string KeyPrefix = LicenseKey.TaxiProPrefix;    // TPRO
    public const string KeyExample = "TPRO-XXXX-XXXX-XXXX";
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(14);

    // The server compares product ids exactly. As with HeavyPro, the website's product is the slug of
    // "Rotaidem TaxiPro" and the licence generator defaults to the short id: accept both. A wrong
    // product is rejected before an activation is recorded, so trying the next id costs nothing.
    private static readonly string[] ProductIds = { "rotaidem-taxipro", Product };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    static TaxiProLicense()
    {
        LicensedProduct.Name = "TaxiPro";
#if NETFRAMEWORK
        // .NET Framework can default to TLS versions the host refuses.
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
#endif
    }

    /// <summary>%LOCALAPPDATA%\Rotaidem\taxipro\license.json.</summary>
    public static LicenseStore Store() => new(Product);

    /// <summary>
    /// The licence server's public key, built into the program (packaging/licence-public-key.json, the
    /// key rotaidem.com publishes at /api/licenses/jwks.json, shared with HeavyPro). Null if missing.
    /// </summary>
    public static readonly string? PublicKeyJwk = ReadPublicKey();

    private static string? ReadPublicKey()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TaxiPro.LicensePublicKey.json");
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Trim().TrimStart('﻿');
        return text.Length > 0 ? text : null;
    }

    private static LicenseClient Client(string product) => new(ServerUrl, product, Http, PublicKeyJwk);

    public static bool IsWellFormed(string? key) => LicenseKey.IsWellFormed(key, KeyPrefix);

    /// <summary>Without the network: is the saved signed answer still good here? Used for an instant start.</summary>
    public static LicenseResult CheckOffline()
    {
        // The answer names the product id that activated it; either id is ours.
        LicenseResult? result = null;
        foreach (var id in ProductIds)
        {
            result = Client(id).CheckOffline(Store(), OfflineGrace);
            if (result.Status != LicenseStatus.WrongProduct)
                return result;
        }
        return result!;
    }

    /// <summary>Saves the key with the server's signed answer, so the next start can begin offline.</summary>
    public static void Save(string key, LicenseResult result) => Store().Save(key, DateTimeOffset.UtcNow, result.Token);

    public static Task<LicenseResult> ActivateAsync(string key) => FirstMatchingProduct(client => client.ActivateAsync(key));

    /// <summary>Frees this computer's activation on the server (moving to a new PC).</summary>
    public static Task<LicenseResult> DeactivateAsync(string key) => FirstMatchingProduct(client => client.DeactivateAsync(key));

    public static Task<LicenseResult> CheckAsync() => FirstMatchingProduct(client => client.CheckAsync(Store(), OfflineGrace));

    private static async Task<LicenseResult> FirstMatchingProduct(Func<LicenseClient, Task<LicenseResult>> call)
    {
        LicenseResult? result = null;
        foreach (var id in ProductIds)
        {
            result = await call(Client(id)).ConfigureAwait(false);
            if (result.Status != LicenseStatus.WrongProduct)
                return result;
        }
        return result!;
    }

    /// <summary>Start waking the server while the user is still typing. Fire and forget.</summary>
    public static void WakeServer()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var response = await Http.GetAsync(ServerUrl + "/health").ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The activation call that follows gives the user a proper message.
            }
        });
    }
}
