namespace TaxiPro.Core.Physics;

public enum Acceptance
{
    /// <summary>Not enough evidence yet.</summary>
    Unknown,
    /// <summary>The aircraft takes what TaxiPro writes.</summary>
    Works,
    /// <summary>The aircraft overrides it (its own systems put their value back).</summary>
    Ignored
}

/// <summary>
/// Watches whether an aircraft accepts a kind of write: while TaxiPro is writing, it compares what
/// the sim reports back with what was written. Study-level aircraft (PMDG, iniBuilds) run their own
/// systems and may put their own value back; then TaxiPro stops that write instead of fighting the
/// aircraft, and tells the user.
/// </summary>
public sealed class AcceptanceMonitor
{
    private readonly double _window;
    private readonly double _worksShare;
    private readonly double _ignoredShare;
    private double _time, _good;

    /// <param name="windowSeconds">Seconds of active writing before a verdict.</param>
    /// <param name="worksShare">Share of that time the readback must match for "works".</param>
    /// <param name="ignoredShare">At or below this share matching, "ignored". In between: watch another window.</param>
    public AcceptanceMonitor(double windowSeconds = 1.5, double worksShare = 0.6, double ignoredShare = 0.15)
    {
        _window = windowSeconds;
        _worksShare = worksShare;
        _ignoredShare = ignoredShare;
    }

    public Acceptance State { get; private set; } = Acceptance.Unknown;

    /// <summary>Call once per frame while writing.</summary>
    public void Observe(double expected, double actual, double tolerance, double dt)
    {
        if (State != Acceptance.Unknown || dt <= 0 || double.IsNaN(actual))
            return;
        _time += dt;
        if (Math.Abs(actual - expected) <= tolerance)
            _good += dt;
        if (_time < _window)
            return;
        var share = _good / _time;
        if (share >= _worksShare)
            State = Acceptance.Works;
        else if (share <= _ignoredShare)
            State = Acceptance.Ignored;
        _time = _good = 0;
    }

    /// <summary>Records a verdict reached some other way (e.g. a hold that keeps slipping).</summary>
    public void Decide(Acceptance verdict)
    {
        State = verdict;
        _time = _good = 0;
    }

    public void Reset()
    {
        State = Acceptance.Unknown;
        _time = _good = 0;
    }
}
