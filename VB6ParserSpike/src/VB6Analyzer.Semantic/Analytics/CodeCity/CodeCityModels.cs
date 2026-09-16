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

/// <summary>
/// Costruttore e generatore di layout urbano per la CodeCity.
/// Dispone i quartieri su un piano 2D e calcola le geometrie polimetriche 3D per ciascun edificio.
/// </summary>
public static class CodeCityBuilder
{
    public static CodeCityDataset Build(CodeGraph graph)
    {
        var dataset = new CodeCityDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        // 1. Identifica i contenitori principali
        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Module or SymbolKind.Form or SymbolKind.UserControl or SymbolKind.Interface or SymbolKind.Struct)
            .ToList();

        if (containers.Count == 0)
            return dataset;

        // Raggruppa i contenitori per estensione file o prefisso (District)
        var districtGroups = containers
            .GroupBy(c => GetDistrictName(c))
            .OrderByDescending(g => g.Count())
            .ToList();

        double districtSpacing = 40.0;
        double currentDistrictX = 0.0;
        double currentDistrictY = 0.0;
        double rowMaxDepth = 0.0;
        int districtsPerRow = (int)Math.Ceiling(Math.Sqrt(districtGroups.Count));
        if (districtsPerRow < 2) districtsPerRow = 2;

        int districtCol = 0;

        foreach (var group in districtGroups)
        {
            var district = new CityDistrict
            {
                Name = group.Key
            };

            // Costruisci gli edifici del distretto
            var buildings = new List<CityBuilding>();
            foreach (var c in group)
            {
                var children = graph.GetChildren(c.Id).ToList();
                var loc = children.Sum(ch => Math.Max(1, ch.LineEnd - ch.LineStart + 1));
                if (loc <= 0) loc = Math.Max(15, c.LineEnd - c.LineStart + 1);

                var cc = children.Count > 0
                    ? children.Average(ch => 1.0 + (graph.Relationships.Count(r => r.SourceId == ch.Id && r.Kind == RelationshipKind.Calls) * 1.5) + (ch.Parameters.Count * 0.8))
                    : 1.0;
                var maxCc = children.Count > 0
                    ? children.Max(ch => 1.0 + (graph.Relationships.Count(r => r.SourceId == ch.Id && r.Kind == RelationshipKind.Calls) * 1.5) + (ch.Parameters.Count * 0.8))
                    : 1.0;

                // Dimensioni base proporzionali a LOC: da 20px a 70px
                double baseSize = Math.Clamp(Math.Sqrt(loc) * 2.2 + 14.0, 18.0, 75.0);

                // Altezza proporzionale alla Complessità Ciclomatica: da 15px a 180px
                double height = Math.Clamp((cc * 12.0) + (maxCc * 4.0) + 12.0, 14.0, 200.0);

                // Calcolo palette colori in base alla complessità/rischio (Verde -> Giallo -> Arancio -> Rosso -> Magenta)
                var (baseCol, roofCol, sideCol) = GetBuildingColors(cc, maxCc);

                buildings.Add(new CityBuilding
                {
                    Id = c.Id,
                    Name = c.Name,
                    QualifiedName = c.QualifiedName,
                    DistrictName = district.Name,
                    FilePath = c.FilePath,
                    Kind = c.Kind,
                    Loc = loc,
                    MemberCount = children.Count,
                    CyclomaticComplexity = cc,
                    Width = baseSize,
                    Depth = baseSize,
                    Height = height,
                    ColorHex = baseCol,
                    RoofColorHex = roofCol,
                    SideColorHex = sideCol
                });
            }

            // Layout 2D compatto a griglia per gli edifici all'interno del distretto
            LayoutDistrictBuildings(district, buildings);

            // Posiziona il distretto nel mondo complessivo
            district.X = currentDistrictX;
            district.Y = currentDistrictY;

            // Aggiorna le coordinate assolute dei singoli edifici
            foreach (var b in district.Buildings)
            {
                b.X += district.X;
                b.Y += district.Y;
            }

            rowMaxDepth = Math.Max(rowMaxDepth, district.Depth);
            currentDistrictX += district.Width + districtSpacing;
            districtCol++;

            if (districtCol >= districtsPerRow)
            {
                districtCol = 0;
                currentDistrictX = 0;
                currentDistrictY += rowMaxDepth + districtSpacing;
                rowMaxDepth = 0;
            }

            dataset.Districts.Add(district);
        }

        return dataset;
    }

    private static void LayoutDistrictBuildings(CityDistrict district, List<CityBuilding> buildings)
    {
        double padding = 16.0;
        double streetSpacing = 12.0;

        int cols = (int)Math.Ceiling(Math.Sqrt(buildings.Count));
        if (cols < 1) cols = 1;

        double curX = padding;
        double curY = padding;
        double rowHeight = 0;
        int colIndex = 0;
        double maxDistrictWidth = 0;

        foreach (var b in buildings.OrderByDescending(b => b.Loc))
        {
            b.X = curX;
            b.Y = curY;

            rowHeight = Math.Max(rowHeight, b.Depth);
            curX += b.Width + streetSpacing;
            colIndex++;

            maxDistrictWidth = Math.Max(maxDistrictWidth, curX);

            if (colIndex >= cols)
            {
                colIndex = 0;
                curX = padding;
                curY += rowHeight + streetSpacing;
                rowHeight = 0;
            }
        }

        double totalWidth = Math.Max(maxDistrictWidth, 100.0) + padding;
        double totalDepth = curY + rowHeight + padding + (colIndex == 0 ? 0 : rowHeight);
        if (totalDepth < 100.0) totalDepth = 100.0;

        district.Width = totalWidth;
        district.Depth = totalDepth;
        district.Buildings.AddRange(buildings);
    }

    private static string GetDistrictName(Symbol s)
    {
        if (!string.IsNullOrEmpty(s.FilePath))
        {
            var dir = Path.GetFileName(Path.GetDirectoryName(s.FilePath));
            if (!string.IsNullOrEmpty(dir)) return dir;
        }

        return s.Kind switch
        {
            SymbolKind.Form => "Forms & UI",
            SymbolKind.UserControl => "User Controls",
            SymbolKind.Module => "Standard Modules (.bas)",
            SymbolKind.Class => "Classes (.cls)",
            SymbolKind.Interface => "Interfaces",
            _ => "Core Components"
        };
    }

    private static (string BaseColor, string RoofColor, string SideColor) GetBuildingColors(double avgCc, double maxCc)
    {
        // Scala Complessità:
        // CC < 3: Bassa (Blu/Verde)
        // CC 3..7: Moderata (Giallo/Ambra)
        // CC 7..15: Alta (Arancione)
        // CC > 15: Critica (Rosso / Magenta)
        if (maxCc >= 20 || avgCc >= 12)
        {
            return ("#DC2626", "#EF4444", "#991B1B"); // Rosso Intenso
        }
        if (maxCc >= 12 || avgCc >= 7)
        {
            return ("#EA580C", "#F97316", "#9A3412"); // Arancione
        }
        if (maxCc >= 6 || avgCc >= 3.5)
        {
            return ("#D97706", "#FBBF24", "#92400E"); // Ambra / Oro
        }
        if (avgCc >= 2.0)
        {
            return ("#059669", "#10B981", "#065F46"); // Smeraldo
        }

        return ("#2563EB", "#60A5FA", "#1E40AF"); // Blu Moderno
    }
}
