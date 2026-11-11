using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.CodeCity;

/// <summary>
/// Urban layout builder and generator for CodeCity.
/// Arranges neighborhoods on a 2D plane and calculates 3D polymetric geometries for each building.
/// </summary>
public static class CodeCityBuilder
{
    /// <summary>
    /// Constructs the CodeCity polymetric dataset by grouping the components into urban districts/blocks
    /// and calculating the 3D geometries (width/depth on LOC, height on Cyclomatic Complexity, color on risk).
    /// </summary>
    /// <param name="graph">The CodeGraph containing symbols and relations of the workspace.</param>
    /// <returns>The <see cref="CodeCityDataset"/> dataset containing the districts and placed buildings.</returns>
    public static CodeCityDataset Build(CodeGraph graph)
    {
        var dataset = new CodeCityDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        // =========================================================================
        // STEP 1: Selecting top-level containers to represent as buildings
        // =========================================================================
        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Module or SymbolKind.Form or SymbolKind.UserControl or SymbolKind.Interface or SymbolKind.Struct)
            .ToList();

        if (containers.Count == 0)
            return dataset;

        // =========================================================================
        // STEP 2: Grouping of components into urban districts (by folder or type)
        // =========================================================================
        var districtGroups = containers
            .GroupBy(c => GetDistrictName(c))
            .OrderByDescending(g => g.Count())
            .ToList();

        // Geometric parameters for the spatial arrangement of districts on the 2D plane
        double districtSpacing = 40.0;
        double currentDistrictX = 0.0;
        double currentDistrictY = 0.0;
        double rowMaxDepth = 0.0;
        int districtsPerRow = (int)Math.Ceiling(Math.Sqrt(districtGroups.Count));
        if (districtsPerRow < 2) districtsPerRow = 2;

        int districtCol = 0;

        // O(1) indexing of outgoing calls for each source symbol
        var outgoingCallsCount = new Dictionary<Guid, int>();
        foreach (var r in graph.Relationships)
        {
            if (r.Kind == RelationshipKind.Calls)

                outgoingCallsCount[r.SourceId] = outgoingCallsCount.GetValueOrDefault(r.SourceId) + 1;
        }

        // =========================================================================
        // STEP 3: Construction of buildings (CityBuilding) for each district
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
                // Calculates the aggregate lines of code of the child members
                var loc = children.Sum(ch => Math.Max(1, ch.LineEnd - ch.LineStart + 1));
                if (loc <= 0) loc = Math.Max(15, c.LineEnd - c.LineStart + 1);

                // Average and maximum cyclomatic complexity of component methods
                var cc = children.Count > 0
                    ? children.Average(ch => 1.0 + (outgoingCallsCount.GetValueOrDefault(ch.Id) * 1.5) + (ch.Parameters.Count * 0.8))
                    : 1.0;
                var maxCc = children.Count > 0
                    ? children.Max(ch => 1.0 + (outgoingCallsCount.GetValueOrDefault(ch.Id) * 1.5) + (ch.Parameters.Count * 0.8))
                    : 1.0;

                // 3a. Base dimensions (W x D) proportional to the square root of LOC
                // Ensures realistic proportions: small buildings ~18px, large buildings up to ~75px
                double baseSize = Math.Clamp(Math.Sqrt(loc) * 2.2 + 14.0, 18.0, 75.0);

                // 3b. Height (H) proportional to Cyclomatic Complexity (McCabe)
                // Tall skyscrapers indicate high decision density and control complexity
                double height = Math.Clamp((cc * 12.0) + (maxCc * 4.0) + 12.0, 14.0, 200.0);

                // 3c. Thematic color palette based on risk level (front façade, roof and shaded side)
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
            // STEP 4: Internal grid arrangement of buildings in the district
            // =====================================================================
            LayoutDistrictBuildings(district, buildings);

            // Position the district in the comprehensive urban plan
            district.X = currentDistrictX;
            district.Y = currentDistrictY;

            // Recalculates the world coordinates for each building
            foreach (var b in district.Buildings)
            {
                b.X += district.X;
                b.Y += district.Y;
            }

            // Advances the grid cursor for the district row
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
    /// Arranges buildings within the city block/district on a grid ordered by decreasing size.
    /// </summary>
    private static void LayoutDistrictBuildings(CityDistrict district, List<CityBuilding> buildings)
    {
        double padding = 16.0;
        double streetSpacing = 12.0;

        // Calculate the number of columns of the district sub-grid
        int cols = (int)Math.Ceiling(Math.Sqrt(buildings.Count));
        if (cols < 1) cols = 1;

        double curX = padding;
        double curY = padding;
        double rowHeight = 0;
        int colIndex = 0;
        double maxDistrictWidth = 0;

        // Arranges the buildings starting from those with the largest footprint to compact the block
        foreach (var b in buildings.OrderByDescending(b => b.Loc))
        {
            b.X = curX;
            b.Y = curY;

            rowHeight = Math.Max(rowHeight, b.Depth);
            curX += b.Width + streetSpacing;
            colIndex++;

            maxDistrictWidth = Math.Max(maxDistrictWidth, curX);

            // Head to the end of the row of buildings
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
    /// Returns the urban district name for a symbol, based on the source folder or module type.
    /// </summary>
    private static string GetDistrictName(Symbol s)
    {
        // 1. If the file path includes a specific directory, use the folder name as the district
        if (!string.IsNullOrEmpty(s.FilePath))
        {
            var dir = Path.GetFileName(Path.GetDirectoryName(s.FilePath));
            if (!string.IsNullOrEmpty(dir)) return dir;
        }

        // 2. Otherwise group by semantic type of component
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
    /// Assign the triad of isometric colors (front facade, roof, shaded side)
    /// based on the level of cyclomatic complexity and risk of the building.
    /// </summary>
    private static (string BaseColor, string RoofColor, string SideColor) GetBuildingColors(double avgCc, double maxCc)
    {
        // Rischio Critico (Rosso Intenso): complessità massima >= 20 o media >= 12
        if (maxCc >= 20 || avgCc >= 12)

            return ("#DC2626", "#EF4444", "#991B1B");
        // Rischio Alto (Arancione): complessità massima >= 12 o media >= 7
        if (maxCc >= 12 || avgCc >= 7)

            return ("#EA580C", "#F97316", "#9A3412");
        // Rischio Moderato (Ambra / Oro): complessità media >= 3.5
        if (maxCc >= 6 || avgCc >= 3.5)

            return ("#D97706", "#FBBF24", "#92400E");
        // Bassa Complessità (Smeraldo / Verde)
        if (avgCc >= 2.0)

            return ("#059669", "#10B981", "#065F46");

        // Simple / Stable (Modern Blue)
        return ("#2563EB", "#60A5FA", "#1E40AF");
    }
}
