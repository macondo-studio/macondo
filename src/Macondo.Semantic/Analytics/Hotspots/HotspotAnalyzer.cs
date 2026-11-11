using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Hotspots;

/// <summary>
/// Agnostic analyzer for detecting code hotspots (CodeScene methodology / Adam Tornhill).
/// Integrates static graph metrics (Fan-in/Fan-out) with any commit history data (VCS Churn).
/// </summary>
public static class HotspotAnalyzer
{
    /// <summary>
    /// Performs risk hotspot analysis by combining structural complexity with frequency of modification (Churn) or usage (Fan-In).
    /// </summary>
    /// <param name="graph">The semantic <see cref="CodeGraph"/> containing symbols and relationships.</param>
    /// <param name="fileChurnHistory">Optional dictionary with VCS commit count for each source file.</param>
    /// <returns>The <see cref="HotspotDataset"/> dataset containing all the symbols mapped onto the 4 Cartesian risk quadrants.</returns>
    /// <remarks>
    /// The algorithm operates according to the following logic:
    /// <list type="number">
    /// <item><description><b>Complexity (Y Axis)</b>: calculated by combining the Fan-out of outgoing calls and the number of formal parameters: <c>Complexity = 1.0 + (callees * 1.5) + (params * 0.8)</c>.</description></item>
    /// <item><description><b>Churn / Fan-In (X Axis)</b>: calculated on the number of direct callers and weighted with the commit history of the file (if present in the VCS logs): <c>Churn = (commits * 2.0) + callers</c>.</description></item>
    /// <item><description><b>Dimension (Radius)</b>: volumetric metric estimated from parameters and calls.</description></item>
    /// <item><description><b>Partitioning in the 4 Quadrants</b>: the Cartesian thresholds <c>ThresholdX</c> and <c>ThresholdY</c> are determined by the average of the codebase; elements above the average in both axes belong to the <b>DangerZone</b>.</description></item>
    /// <item><description><b>Normalized Risk Score (0-100)</b>: weighted average between normalized Churn and normalized Complexity compared to the codebase maximums.</description></item>
    /// </list>
    /// </remarks>
    public static HotspotDataset Analyze(CodeGraph graph, IReadOnlyDictionary<string, int>? fileChurnHistory = null)
    {
        var items = new List<HotspotItem>();

        // ---------------------------------------------------------------------
        // STEP 1: Selecting target symbols for Hotspot analysis.
        // We give priority to methods and properties (procedures/functions). If there is none
        // are (e.g. type-only codebase or empty modules), let's analyze the modules themselves.
        // ---------------------------------------------------------------------
        var routines = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Method or SymbolKind.Property)
            .ToList();

        // ---------------------------------------------------------------------
        // STEP 2: Quick Hierarchical Mapping Container -> Children.
        // It allows you to instantly associate each procedure with its file/module
        // parent (Class, Form, Standard Form, UserControl).
        // ---------------------------------------------------------------------
        var symbolContainers = new Dictionary<Guid, string>();
        foreach (var container in graph.Symbols.Where(s => s.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl))
        {
            foreach (var child in graph.GetChildren(container.Id))
            {
                symbolContainers[child.Id] = container.Name;
            }
        }

        var targets = routines.Count > 0 ? routines : graph.Symbols.Where(s => s.Kind != SymbolKind.Project).ToList();

        // ---------------------------------------------------------------------
        // STEP 3: Calculate complexity, coupling and churn metrics for each symbol.
        // ---------------------------------------------------------------------
        foreach (var sym in targets)
        {
            // Fan-In: How many other methods or procedures call this symbol directly
            int callers = graph.Relationships.Count(r => r.TargetId == sym.Id && r.Kind == RelationshipKind.Calls);
            // Fan-Out: How many other procedures are invoked from within this symbol
            int callees = graph.Relationships.Count(r => r.SourceId == sym.Id && r.Kind == RelationshipKind.Calls);

            // Complessità strutturale stimata (Asse Y):
            // Base = 1.0; each outgoing call (callee) increases the complexity by 1.5 points;
            // each formal parameter in the signature increases cognitive effort and risk by 0.8 points.
            double complexity = 1.0 + (callees * 1.5) + (sym.Parameters.Count * 0.8);

            // Churn / Rate of change or impact (X-axis):
            // By default it coincides with the Fan-In (callers). If historical VCS data (Git/SVN) is available,
            // the number of commits that touched the source file is weighted with a factor of 2.0.
            double churn = callers;
            if (fileChurnHistory != null && !string.IsNullOrEmpty(sym.FilePath) &&
                fileChurnHistory.TryGetValue(sym.FilePath, out int commits))

                churn = (commits * 2.0) + callers;

            // Volumetric dimension of the element (used for the visual radius of the bubble in the chart)
            double size = Math.Max(4.0, (sym.Parameters.Count * 3.0) + (callees * 4.0) + 10.0);

            symbolContainers.TryGetValue(sym.Id, out var containerName);
            containerName ??= sym.Name;

            items.Add(new HotspotItem
            {
                Id = sym.Id,
                Name = sym.Name,
                QualifiedName = sym.QualifiedName,
                ContainerName = containerName,
                Kind = sym.Kind,
                Complexity = complexity,
                ChurnOrFanIn = churn,
                SizeMetric = size,
                DirectCallersCount = callers,
                DirectCalleesCount = callees
            });
        }

        // If there are no items to analyze, it returns an empty dataset with default values
        if (items.Count == 0)

            return new HotspotDataset { ThresholdX = 5, ThresholdY = 5, MaxX = 10, MaxY = 10 };

        // ---------------------------------------------------------------------
        // STEP 4: Calculate codebase statistical averages and scale maxima.
        // The averages act as partition thresholds for the 4 Cartesian quadrants.
        // ---------------------------------------------------------------------
        double avgX = Math.Max(1.0, items.Average(i => i.ChurnOrFanIn));
        double avgY = Math.Max(1.0, items.Average(i => i.Complexity));
        double maxX = Math.Max(avgX * 2.0, items.Max(i => i.ChurnOrFanIn));
        double maxY = Math.Max(avgY * 2.0, items.Max(i => i.Complexity));

        // ---------------------------------------------------------------------
        // STEP 5: Normalization of the risk score (0-100) and quadrant assignment.
        // ---------------------------------------------------------------------
        foreach (var item in items)
        {
            // Linear normalization between 0.0 and 1.0 relative to the maximums of the entire project
            double normX = Math.Min(1.0, item.ChurnOrFanIn / maxX);
            double normY = Math.Min(1.0, item.Complexity / maxY);
            
            // Combined Normalized Risk Score (50% Churn/Fan-In, 50% Complexity)
            item.RiskScore = Math.Round(((normX * 0.5) + (normY * 0.5)) * 100.0, 1);

            // Classification in the 4 quadrants based on the average thresholds of the codebase:
            // 1. DANGER ZONE (Alto Churn, Alta Complessità): codice critico, modificato spesso e complesso.
            // 2. High Churn / Low Complexity: Frequently modified but simple code (e.g. constants, mappings).
            // 3. Low Churn / High Complexity: complex but stable code (e.g. core algorithms rarely touched).
            // 4. SAFE (Low Churn, Low Complexity): Simple and rarely modified code.
            if (item.ChurnOrFanIn >= avgX && item.Complexity >= avgY)
            {
                item.Quadrant = HotspotQuadrant.DangerZone;
                item.RiskLevel = item.RiskScore >= 75 ? ImpactRiskLevel.Critical : ImpactRiskLevel.High;
            }
            else if (item.ChurnOrFanIn >= avgX && item.Complexity < avgY)
            {
                item.Quadrant = HotspotQuadrant.HighChurnLowComplexity;
                item.RiskLevel = ImpactRiskLevel.Medium;
            }
            else if (item.ChurnOrFanIn < avgX && item.Complexity >= avgY)
            {
                item.Quadrant = HotspotQuadrant.LowChurnHighComplexity;
                item.RiskLevel = ImpactRiskLevel.Medium;
            }
            else
            {
                item.Quadrant = HotspotQuadrant.Safe;
                item.RiskLevel = ImpactRiskLevel.Low;
            }
        }

        return new HotspotDataset
        {
            Items = items,
            ThresholdX = avgX,
            ThresholdY = avgY,
            MaxX = maxX,
            MaxY = maxY
        };
    }
}
