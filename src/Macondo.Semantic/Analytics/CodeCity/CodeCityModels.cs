using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.CodeCity;

/// <summary>
/// Single building in the polymetric city (represents a class, module, form or file).
/// </summary>
public sealed class CityBuilding
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public string DistrictName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }

    /// <summary>Lines of code (determines the basic footprint of the building W x D).</summary>
    public int Loc { get; init; }

    /// <summary>Number of methods and members.</summary>
    public int MemberCount { get; init; }

    /// <summary>Complessità Ciclomatica media o totale (determina l'altezza H dell'edificio).</summary>
    public double CyclomaticComplexity { get; init; }

    /// <summary>Position X (2D grid/world).</summary>
    public double X { get; set; }

    /// <summary>Y position (2D grid/world).</summary>
    public double Y { get; set; }

    /// <summary>Larghezza base (W).</summary>
    public double Width { get; set; }

    /// <summary>Profondità base (D).</summary>
    public double Depth { get; set; }

    /// <summary>Altezza 3D dell'edificio (H).</summary>
    public double Height { get; set; }

    /// <summary>Main color of the building facade in hexadecimal.</summary>
    public string ColorHex { get; set; } = "#3B82F6";

    /// <summary>Color for the upper facade (roof).</summary>
    public string RoofColorHex { get; set; } = "#60A5FA";

    /// <summary>Color for the shaded side façade.</summary>
    public string SideColorHex { get; set; } = "#1D4ED8";
}

/// <summary>
/// Neighborhood or urban block that groups buildings (e.g. by Parent Module, Folder or Project).
/// </summary>
public sealed class CityDistrict
{
    public string Name { get; init; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Depth { get; set; }
    public List<CityBuilding> Buildings { get; init; } = new();

    public int TotalLoc => Buildings.Sum(b => b.Loc);
    public double AvgComplexity => Buildings.Count > 0 ? Buildings.Average(b => b.CyclomaticComplexity) : 1.0;
}

/// <summary>
/// Complete dataset for the 3D / Isometric 2.5D polymetric map of CodeCity.
/// </summary>
public sealed class CodeCityDataset
{
    public List<CityDistrict> Districts { get; init; } = new();

    public int TotalDistrictsCount => Districts.Count;
    public int TotalBuildingsCount => Districts.Sum(d => d.Buildings.Count);
    public int TotalLoc => Districts.Sum(d => d.TotalLoc);
    public double MaxHeight => Districts.SelectMany(d => d.Buildings).DefaultIfEmpty().Max(b => b?.Height ?? 10.0);

    public IEnumerable<CityBuilding> AllBuildings => Districts.SelectMany(d => d.Buildings);
}
