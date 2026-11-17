namespace TaxiPro.Core;

/// <summary>
/// Whether TaxiPro may write to the aircraft, re-armed by a live licence check rather than set once
/// and trusted (HeavyPro's ControlLimits). The app unlocks it until the lease the licence server
/// signed; the recheck loop extends it. A licence that stops checking out (rejected, or the recheck
/// loop no longer running) leaves TaxiPro passing every input straight through on its own.
/// </summary>
public static class LicenseGate
{
    private static long _unlockedUntilTicks;

    /// <summary>TaxiPro may write until <paramref name="until"/>. Call again before then to keep going.</summary>
    public static void Unlock(DateTimeOffset until) => Interlocked.Exchange(ref _unlockedUntilTicks, until.UtcTicks);

    /// <summary>Stops all writes now, without waiting for the lease to run out.</summary>
    public static void Lock() => Interlocked.Exchange(ref _unlockedUntilTicks, 0);

    public static bool Unlocked => IsUnlockedAt(DateTimeOffset.UtcNow);

    public static bool IsUnlockedAt(DateTimeOffset now) => now.UtcTicks < Interlocked.Read(ref _unlockedUntilTicks);

    /// <summary>When the current unlock runs out, or null when locked.</summary>
    public static DateTimeOffset? Until
    {
        get
        {
            var ticks = Interlocked.Read(ref _unlockedUntilTicks);
            return ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
        }
    }
}
