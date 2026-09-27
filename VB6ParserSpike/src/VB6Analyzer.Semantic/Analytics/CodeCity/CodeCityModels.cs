using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.CodeCity;

/// <summary>
/// Singolo edificio nella città polimetrica (rappresenta una classe, modulo, form o file).
/// </summary>
public sealed class CityBuilding
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public string DistrictName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }

    /// <summary>Righe di codice (determina l'ingombro base dell'edificio W x D).</summary>
    public int Loc { get; init; }

    /// <summary>Numero di metodi e membri.</summary>
    public int MemberCount { get; init; }

    /// <summary>Complessità Ciclomatica media o totale (determina l'altezza H dell'edificio).</summary>
    public double CyclomaticComplexity { get; init; }

    /// <summary>Posizione X (griglia/mondo 2D).</summary>
    public double X { get; set; }

    /// <summary>Posizione Y (griglia/mondo 2D).</summary>
    public double Y { get; set; }

    /// <summary>Larghezza base (W).</summary>
    public double Width { get; set; }

    /// <summary>Profondità base (D).</summary>
    public double Depth { get; set; }

    /// <summary>Altezza 3D dell'edificio (H).</summary>
    public double Height { get; set; }

    /// <summary>Colore principale della facciata dell'edificio in esadecimale.</summary>
    public string ColorHex { get; set; } = "#3B82F6";

    /// <summary>Colore per la facciata superiore (tetto).</summary>
    public string RoofColorHex { get; set; } = "#60A5FA";

    /// <summary>Colore per la facciata laterale ombreggiata.</summary>
    public string SideColorHex { get; set; } = "#1D4ED8";
}

/// <summary>
/// Quartiere o isolato urbano che raggruppa gli edifici (es. per Modulo genitore, Cartella o Progetto).
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
/// Dataset completo per la mappa polimetrica 3D / Isometrica 2.5D della CodeCity.
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
