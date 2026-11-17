namespace TaxiPro.Core.Models;

/// <summary>The user's TaxiPro settings, saved in %AppData%\TaxiPro\settings.json.</summary>
public sealed class TaxiSettings
{
    /// <summary>Everything on or off: off, TaxiPro passes every input through untouched and writes nothing.</summary>
    public bool MasterEnable { get; set; } = true;
    /// <summary>A parked jet needs breakaway thrust before it rolls.</summary>
    public bool Breakaway { get; set; } = true;
    /// <summary>Speed builds and fades with thrust, weight and rolling resistance.</summary>
    public bool Weight { get; set; } = true;
    /// <summary>0..100: how strictly the weight model is applied (100 = full physics, 0 = the sim's own).</summary>
    public double WeightFeel { get; set; } = 75;
    /// <summary>Progressive, weight-aware toe brakes and brake keys.</summary>
    public bool Brakes { get; set; } = true;
    /// <summary>Tiller authority that fades with speed, at the steering actuator's rate.</summary>
    public bool Tiller { get; set; } = true;
    /// <summary>Leave the toe brakes to HeavyPro when HeavyPro is running and shaping them.</summary>
    public bool YieldBrakesToHeavyPro { get; set; } = true;

    public TaxiSettings Normalised()
    {
        WeightFeel = Numeric.Clamp(Numeric.Finite(WeightFeel, 75), 0, 100);
        return this;
    }
}
