namespace Rotaidem.Licensing;

/// <summary>
/// The product name the licence messages use ("TaxiPro has been offline too long…").
/// TaxiPro addition: upstream (rsg.com licensing/, as copied into HeavyPro) had "HeavyPro" written into
/// each message. Set it once at start-up, before the first licence call.
/// </summary>
public static class LicensedProduct
{
    public static string Name { get; set; } = "HeavyPro";
}
