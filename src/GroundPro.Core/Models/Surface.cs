namespace GroundPro.Core.Models;

/// <summary>The MSFS SURFACE TYPE enum, and how much harder each surface is to roll on than concrete.</summary>
public static class Surface
{
    public const int Unknown = -1;

    /// <summary>Rolling and breakaway resistance relative to dry concrete.</summary>
    public static double ResistanceFactor(int surfaceType) => surfaceType switch
    {
        0 => 1.0,   // concrete
        1 => 4.0,   // grass
        3 => 5.0,   // grass, bumpy
        4 => 1.0,   // asphalt
        5 => 3.5,   // short grass
        6 => 5.0,   // long grass
        7 => 2.5,   // hard turf
        8 => 2.5,   // snow
        9 => 0.8,   // ice
        10 => 1.0,  // urban
        11 => 5.0,  // forest
        12 => 3.0,  // dirt
        13 => 2.5,  // coral
        14 => 2.5,  // gravel
        15 => 1.2,  // oil treated
        16 => 1.3,  // steel mats
        17 => 1.0,  // bituminous
        18 => 1.1,  // brick
        19 => 1.1,  // macadam
        20 => 1.3,  // planks
        21 => 5.0,  // sand
        22 => 2.0,  // shale
        23 => 1.0,  // tarmac
        _ => 1.0    // water, unknown and anything newer: no change
    };

    public static string Name(int surfaceType) => surfaceType switch
    {
        0 => "concrete", 1 => "grass", 2 => "water", 3 => "bumpy grass", 4 => "asphalt", 5 => "short grass",
        6 => "long grass", 7 => "hard turf", 8 => "snow", 9 => "ice", 10 => "urban", 11 => "forest", 12 => "dirt",
        13 => "coral", 14 => "gravel", 15 => "oil-treated", 16 => "steel mats", 17 => "bituminous", 18 => "brick",
        19 => "macadam", 20 => "planks", 21 => "sand", 22 => "shale", 23 => "tarmac",
        _ => "unknown surface"
    };
}
