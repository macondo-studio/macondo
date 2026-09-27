using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.CodeCity;

/// <summary>
/// Costruttore e generatore di layout urbano per la CodeCity.
/// Dispone i quartieri su un piano 2D e calcola le geometrie polimetriche 3D per ciascun edificio.
/// </summary>
public static class CodeCityBuilder
{
    /// <summary>
    /// Costruisce il dataset polimetrico della CodeCity raggruppando i componenti in distretti/isolati urbani
    /// e calcolando le geometrie 3D (larghezza/profondità su LOC, altezza su Complessità Ciclomatica, colore su rischio).
    /// </summary>
    /// <param name="graph">Il CodeGraph contenente simboli e relazioni del workspace.</param>
    /// <returns>Il dataset <see cref="CodeCityDataset"/> contenente i distretti e gli edifici posizionati.</returns>
    public static CodeCityDataset Build(CodeGraph graph)
    {
        var dataset = new CodeCityDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        // =========================================================================
        // PASSO 1: Selezione dei contenitori di primo livello da rappresentare come edifici
        // =========================================================================
        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Module or SymbolKind.Form or SymbolKind.UserControl or SymbolKind.Interface or SymbolKind.Struct)
            .ToList();

        if (containers.Count == 0)
            return dataset;

        // =========================================================================
        // PASSO 2: Raggruppamento dei componenti in distretti urbani (per cartella o tipologia)
        // =========================================================================
        var districtGroups = containers
            .GroupBy(c => GetDistrictName(c))
            .OrderByDescending(g => g.Count())
            .ToList();

        // Parametri geometrici per la disposizione spaziale dei distretti sul piano 2D
        double districtSpacing = 40.0;
        double currentDistrictX = 0.0;
        double currentDistrictY = 0.0;
        double rowMaxDepth = 0.0;
        int districtsPerRow = (int)Math.Ceiling(Math.Sqrt(districtGroups.Count));
        if (districtsPerRow < 2) districtsPerRow = 2;

        int districtCol = 0;

        // =========================================================================
        // PASSO 3: Costruzione degli edifici (CityBuilding) per ciascun distretto
        // =========================================================================
        foreach (var group in districtGroups)
        {
            var district = new CityDistrict
            {
                Name = group.Key
            };

            var buildings = new List<CityBuilding>();
            foreach (var c in group)
            {
                var children = graph.GetChildren(c.Id).ToList();
                // Calcola le linee di codice aggregate dei membri figli
                var loc = children.Sum(ch => Math.Max(1, ch.LineEnd - ch.LineStart + 1));
                if (loc <= 0) loc = Math.Max(15, c.LineEnd - c.LineStart + 1);

                // Complessità ciclomatica media e massima dei metodi del componente
                var cc = children.Count > 0
                    ? children.Average(ch => 1.0 + (graph.Relationships.Count(r => r.SourceId == ch.Id && r.Kind == RelationshipKind.Calls) * 1.5) + (ch.Parameters.Count * 0.8))
                    : 1.0;
                var maxCc = children.Count > 0
                    ? children.Max(ch => 1.0 + (graph.Relationships.Count(r => r.SourceId == ch.Id && r.Kind == RelationshipKind.Calls) * 1.5) + (ch.Parameters.Count * 0.8))
                    : 1.0;

                // 3a. Dimensioni della base (W x D) proporzionali alla radice quadrata di LOC
                // Assicura proporzioni realistiche: edifici piccoli ~18px, grandi edifici fino a ~75px
                double baseSize = Math.Clamp(Math.Sqrt(loc) * 2.2 + 14.0, 18.0, 75.0);

                // 3b. Altezza (H) proporzionale alla Complessità Ciclomatica (McCabe)
                // Grattacieli elevati indicano forte densità di decisioni e complessità di controllo
                double height = Math.Clamp((cc * 12.0) + (maxCc * 4.0) + 12.0, 14.0, 200.0);

                // 3c. Palette colori tematica in base al livello di rischio (facciata frontale, tetto e lato ombreggiato)
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

            // =====================================================================
            // PASSO 4: Disposizione interna a griglia degli edifici nel distretto
            // =====================================================================
            LayoutDistrictBuildings(district, buildings);

            // Posiziona il distretto nel piano urbano globale
            district.X = currentDistrictX;
            district.Y = currentDistrictY;

            // Ricalcola le coordinate assolute nel mondo per ciascun edificio
            foreach (var b in district.Buildings)
            {
                b.X += district.X;
                b.Y += district.Y;
            }

            // Avanza il cursore di griglia per la riga dei distretti
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

    /// <summary>
    /// Dispone gli edifici all'interno dell'isolato urbano/distretto su una griglia ordinata per dimensione decrescente.
    /// </summary>
    private static void LayoutDistrictBuildings(CityDistrict district, List<CityBuilding> buildings)
    {
        double padding = 16.0;
        double streetSpacing = 12.0;

        // Calcola il numero di colonne della sotto-griglia del distretto
        int cols = (int)Math.Ceiling(Math.Sqrt(buildings.Count));
        if (cols < 1) cols = 1;

        double curX = padding;
        double curY = padding;
        double rowHeight = 0;
        int colIndex = 0;
        double maxDistrictWidth = 0;

        // Dispone gli edifici partendo da quelli con maggiore ingombro per compattare l'isolato
        foreach (var b in buildings.OrderByDescending(b => b.Loc))
        {
            b.X = curX;
            b.Y = curY;

            rowHeight = Math.Max(rowHeight, b.Depth);
            curX += b.Width + streetSpacing;
            colIndex++;

            maxDistrictWidth = Math.Max(maxDistrictWidth, curX);

            // A capo alla fine della riga di edifici
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

    /// <summary>
    /// Restituisce il nome del distretto urbano per un simbolo, basandosi sulla cartella sorgente o sul tipo di modulo.
    /// </summary>
    private static string GetDistrictName(Symbol s)
    {
        // 1. Se il percorso file include una directory specifica, usa il nome della cartella come distretto
        if (!string.IsNullOrEmpty(s.FilePath))
        {
            var dir = Path.GetFileName(Path.GetDirectoryName(s.FilePath));
            if (!string.IsNullOrEmpty(dir)) return dir;
        }

        // 2. Altrimenti raggruppa per tipologia semantica di componente
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

    /// <summary>
    /// Assegna la terna di colori isometrici (facciata frontale, tetto, lato ombreggiato)
    /// in base al livello di complessità ciclomatica e rischio dell'edificio.
    /// </summary>
    private static (string BaseColor, string RoofColor, string SideColor) GetBuildingColors(double avgCc, double maxCc)
    {
        // Rischio Critico (Rosso Intenso): complessità massima >= 20 o media >= 12
        if (maxCc >= 20 || avgCc >= 12)
        {
            return ("#DC2626", "#EF4444", "#991B1B");
        }
        // Rischio Alto (Arancione): complessità massima >= 12 o media >= 7
        if (maxCc >= 12 || avgCc >= 7)
        {
            return ("#EA580C", "#F97316", "#9A3412");
        }
        // Rischio Moderato (Ambra / Oro): complessità media >= 3.5
        if (maxCc >= 6 || avgCc >= 3.5)
        {
            return ("#D97706", "#FBBF24", "#92400E");
        }
        // Bassa Complessità (Smeraldo / Verde)
        if (avgCc >= 2.0)
        {
            return ("#059669", "#10B981", "#065F46");
        }

        // Semplice / Stabile (Blu Moderno)
        return ("#2563EB", "#60A5FA", "#1E40AF");
    }
}
