using System.IO;
using System.Windows.Forms;
using Rotaidem.Licensing;
using GroundPro.Core;
using GroundPro.Licensing;

namespace GroundPro.App;

/// <summary>
/// The licence at start-up and while GroundPro runs, as HeavyPro's App does it:
/// - a saved answer signed by the server, for this PC and within its lease, starts at once (offline is fine);
/// - otherwise the saved key is checked online, and failing that the activation window asks for one;
/// - every 20 minutes it is checked again; a rejected licence locks GroundPro at once and closes it.
/// Debug builds skip the check so F5 works without a key. Release builds (all that ship) always check.
/// </summary>
public static class Licence
{
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(20);

    /// <summary>The lease to unlock until, or null: not licensed, quit.</summary>
    public static DateTimeOffset? EnsureLicensed()
    {
#if DEBUG
        Logger.Shared.Info("Debug build: licence check skipped.");
        return DateTimeOffset.UtcNow.AddYears(1);
#else
        var saved = GroundProLicense.Store().Load();
        var offline = GroundProLicense.CheckOffline();
        if (offline.IsValid)
            return offline.LeaseUntil ?? DateTimeOffset.UtcNow.Add(RecheckInterval);

        string? reason = null;
        if (saved != null)
        {
            // No window exists yet: wait on a worker thread so nothing deadlocks on this one.
            var result = Task.Run(GroundProLicense.CheckAsync).GetAwaiter().GetResult();
            if (result.IsValid)
                return result.LeaseUntil ?? DateTimeOffset.UtcNow.Add(RecheckInterval);
            reason = result.Message;
            Log($"Start-up check rejected the saved key: {result.Status} — {result.Message}");
        }

        using var dialog = new LicenseForm(reason, saved?.Key);
        return dialog.ShowDialog() == DialogResult.OK ? dialog.LeaseUntil ?? DateTimeOffset.UtcNow.Add(RecheckInterval) : null;
#endif
    }

    /// <summary>
    /// One recheck. True: still licensed (the gate is extended). False: the licence was rejected; the
    /// gate is locked, the signed answer dropped (the key is kept, so the next start asks pre-filled),
    /// and <paramref name="message"/> says why.
    /// </summary>
    public static async Task<(bool Ok, string? Warning, string Message)> RecheckAsync()
    {
#if DEBUG
        LicenseGate.Unlock(DateTimeOffset.UtcNow.AddYears(1));
        await Task.CompletedTask;
        return (true, null, "");
#else
        var result = await GroundProLicense.CheckAsync();
        switch (result.Status)
        {
            case LicenseStatus.Valid:
            case LicenseStatus.OfflineGrace:
                LicenseGate.Unlock(result.LeaseUntil ?? DateTimeOffset.UtcNow.Add(RecheckInterval));
                // Offline is normal; warn only in the last three days of the lease.
                var warn = result.Status == LicenseStatus.OfflineGrace && result.LeaseUntil is { } until && until - DateTimeOffset.UtcNow <= TimeSpan.FromDays(3);
                return (true, warn ? result.Message : null, result.Message);
            default:
                Log($"Recheck rejected the saved key: {result.Status} — {result.Message}");
                var store = GroundProLicense.Store();
                if (store.Load() is { } rejected)
                    store.Save(rejected.Key, DateTimeOffset.FromUnixTimeSeconds(0));
                LicenseGate.Lock();
                return (false, null, result.Message);
        }
#endif
    }

    /// <summary>Frees this computer's activation (moving GroundPro to another PC) and forgets the key here.</summary>
    public static async Task<(bool Ok, string Message)> DeactivateAsync()
    {
        var saved = GroundProLicense.Store().Load();
        if (saved == null)
            return (false, "No licence key is saved on this computer.");
        var result = await GroundProLicense.DeactivateAsync(saved.Key);
        if (!result.IsValid)
            return (false, result.Message);
        GroundProLicense.Store().Clear();
        LicenseGate.Lock();
        Log("Deactivated on this computer.");
        return (true, result.Message);
    }

    /// <summary>"GPRO-7KQM-····-9HCW": enough to recognise the key, not enough to share it off a screenshot.</summary>
    public static string MaskedKey()
    {
        var key = GroundProLicense.Store().Load()?.Key;
        if (key == null)
            return "no key";
        var parts = key.Split('-');
        return parts.Length == 4 ? $"{parts[0]}-{parts[1]}-····-{parts[3]}" : key;
    }

    /// <summary>Every licence rejection, kept for support in %AppData%\GroundPro\logs\licence.log.</summary>
    public static void Log(string line)
    {
        Logger.Shared.Warn("Licence: " + line);
        try
        {
            Directory.CreateDirectory(Logger.Shared.Folder);
            File.AppendAllText(Path.Combine(Logger.Shared.Folder, "licence.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
}
