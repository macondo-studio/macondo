using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ArchitecturalFlow;

/// <summary>
/// Agnostic architectural flow analyzer and Chord/Sankey layout calculator.
/// </summary>
public static class ArchitecturalFlowAnalyzer
{
    /// <summary>
    /// Performs complete analysis of invocation and dependency flows between architectural layers,
    /// calculating the exchange volumes and geometric angles for the Chord diagram.
    /// </summary>
    /// <param name="graph">The semantic <see cref="CodeGraph"/> containing symbols and relationships.</param>
    /// <returns>The <see cref="ArchitecturalFlowDataset"/> dataset with nodes, flows, volumes and layer violations.</returns>
    public static ArchitecturalFlowDataset Analyze(CodeGraph graph)
    {
        var dataset = new ArchitecturalFlowDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        // =========================================================================
        // STEP 1: Identifying top-level containers (Modules, Classes, Forms, etc.)
        // =========================================================================
        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Module or SymbolKind.Form or SymbolKind.UserControl or SymbolKind.Interface or SymbolKind.Struct)
            .ToList();

        if (containers.Count == 0)
            return dataset;

        var containerById = containers.ToDictionary(c => c.Id);
        var symbolToContainerMap = new Dictionary<Guid, Guid>();

        // Map each symbol or member (methods, properties, fields) to its top-level container
        foreach (var sym in graph.Symbols)
        {
            if (containerById.ContainsKey(sym.Id))

                symbolToContainerMap[sym.Id] = sym.Id;

            else
            {
                var parentRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == sym.Id);
                if (parentRel != null && containerById.ContainsKey(parentRel.SourceId))

                    symbolToContainerMap[sym.Id] = parentRel.SourceId;
            }
        }

        // =========================================================================
        // STEP 2: Creation of flow nodes (FlowNode) with inference of the architectural layer
        // and calculation of aggregate metrics (LOC, cyclomatic complexity, members).
        // =========================================================================
        var nodes = new List<FlowNode>();
        foreach (var c in containers)
        {
            var layer = InferLayer(c);
            var children = graph.GetChildren(c.Id).ToList();
            var loc = children.Sum(ch => Math.Max(1, ch.LineEnd - ch.LineStart + 1));
            if (loc <= 0) loc = Math.Max(10, c.LineEnd - c.LineStart + 1);

            // Estimation of the average cyclomatic complexity of the container
            var avgCc = children.Count > 0
                ? children.Average(ch => 1.0 + (graph.Relationships.Count(r => r.SourceId == ch.Id && r.Kind == RelationshipKind.Calls) * 1.5) + (ch.Parameters.Count * 0.8))
                : 1.0;

            nodes.Add(new FlowNode
            {
                Id = c.Id,
                Name = c.Name,
                QualifiedName = c.QualifiedName,
                Kind = c.Kind,
                Layer = layer,
                Loc = loc,
                MemberCount = children.Count,
                CyclomaticComplexity = avgCc
            });
        }

        var flowNodeById = nodes.ToDictionary(n => n.Id);

        // =========================================================================
        // STEP 3: Aggregation of flow volumes between pairs of containers (Source -> Destination)
        // It counts invocations, instantiations, type uses, inheritance, and implementations.
        // =========================================================================
        var linkCounts = new Dictionary<(Guid Source, Guid Target), int>();

        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind is not (RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.References or RelationshipKind.Implements or RelationshipKind.Inherits))
                continue;

            if (symbolToContainerMap.TryGetValue(rel.SourceId, out var srcContId) &&
                symbolToContainerMap.TryGetValue(rel.TargetId, out var tgtContId))
            {
                // Excludes internal calls to the same module to focus on exchanges between components
                if (srcContId != tgtContId && flowNodeById.ContainsKey(srcContId) && flowNodeById.ContainsKey(tgtContId))
                {
                    var key = (srcContId, tgtContId);
                    linkCounts[key] = linkCounts.GetValueOrDefault(key, 0) + 1;
                }
            }
        }

        var links = new List<FlowLink>();
        foreach (var (key, count) in linkCounts)
        {
            var srcNode = flowNodeById[key.Source];
            var tgtNode = flowNodeById[key.Target];

            // Update the totals of inflows (inflow) and outflows (outflow)
            srcNode.OutflowVolume += count;
            tgtNode.InflowVolume += count;

            // Detect any violations of layer rules (e.g. DataAccess invoking UI)
            var isViolation = IsLayerViolation(srcNode.Layer, tgtNode.Layer);

            links.Add(new FlowLink
            {
                SourceId = key.Source,
                TargetId = key.Target,
                SourceName = srcNode.Name,
                TargetName = tgtNode.Name,
                Volume = count,
                IsLayerViolation = isViolation
            });
        }

        // =========================================================================
        // STEP 4: Filter and sort active nodes for diagram readability
        // =========================================================================
        var activeNodes = nodes
            .Where(n => n.TotalVolume > 0 || n.MemberCount > 0)
            .OrderBy(n => n.Layer)
            .ThenByDescending(n => n.TotalVolume)
            .ToList();

        // If there are too many nodes, select the 45 with the largest volume to avoid visual overlap
        if (activeNodes.Count > 45)
        {
            var topIds = activeNodes.Take(45).Select(n => n.Id).ToHashSet();
            activeNodes = activeNodes.Where(n => topIds.Contains(n.Id)).ToList();
            links = links.Where(l => topIds.Contains(l.SourceId) && topIds.Contains(l.TargetId)).ToList();
        }

        // =========================================================================
        // STEP 5: Trigonometric calculation of angles for the Chord Diagram
        // =========================================================================
        CalculateChordAngles(activeNodes, links);

        return new ArchitecturalFlowDataset
        {
            Nodes = activeNodes,
            Links = links
        };
    }

    /// <summary>
    /// Calculate the angular subdivision of the circumference (360 degrees / 2*PI radians)
    /// for the nodes of the Chord Diagram and the sub-arcs of the flow ribbons.
    /// </summary>
    private static void CalculateChordAngles(List<FlowNode> nodes, List<FlowLink> links)
    {
        if (nodes.Count == 0) return;

        double totalVolume = nodes.Sum(n => Math.Max(n.TotalVolume, 1));
        double padAngle = 0.035; // Spazio in radianti tra gli archi dei nodi
        double totalPadding = padAngle * nodes.Count;
        double availableAngle = (2 * Math.PI) - totalPadding;

        // If the padding exceeds the available space on the circumference, reduce it proportionally
        if (availableAngle <= 0)
        {
            padAngle = 0.01;
            availableAngle = (2 * Math.PI) - (padAngle * nodes.Count);
        }

        // 1. Assign the circular arc (StartAngle .. EndAngle) to each node
        double currentAngle = 0;
        foreach (var node in nodes)
        {
            node.StartAngle = currentAngle;
            double nodeVolume = Math.Max(node.TotalVolume, 1);
            double sweep = (nodeVolume / totalVolume) * availableAngle;
            if (sweep < 0.02) sweep = 0.02; // Ampiezza minima per visibilità visiva

            node.EndAngle = currentAngle + sweep;
            currentAngle = node.EndAngle + padAngle;
        }

        // 2. Calculate the anchoring sub-arcs of the connection ribbons (Chords)
        var nodeOutOffset = nodes.ToDictionary(n => n.Id, n => n.StartAngle);
        var nodeInOffset = nodes.ToDictionary(n => n.Id, n => n.StartAngle);

        foreach (var link in links.OrderByDescending(l => l.Volume))
        {
            if (!nodeOutOffset.ContainsKey(link.SourceId) || !nodeInOffset.ContainsKey(link.TargetId))
                continue;

            var srcNode = nodes.First(n => n.Id == link.SourceId);
            var tgtNode = nodes.First(n => n.Id == link.TargetId);

            // Fraction of angular width occupied by this specific flow
            double srcFraction = srcNode.TotalVolume > 0 ? (double)link.Volume / srcNode.TotalVolume : 0.5;
            double tgtFraction = tgtNode.TotalVolume > 0 ? (double)link.Volume / tgtNode.TotalVolume : 0.5;

            double srcSweep = (srcNode.EndAngle - srcNode.StartAngle) * srcFraction;
            double tgtSweep = (tgtNode.EndAngle - tgtNode.StartAngle) * tgtFraction;

            link.SourceStartAngle = nodeOutOffset[link.SourceId];
            link.SourceEndAngle = link.SourceStartAngle + srcSweep;
            nodeOutOffset[link.SourceId] = Math.Min(link.SourceEndAngle, srcNode.EndAngle);

            link.TargetStartAngle = nodeInOffset[link.TargetId];
            link.TargetEndAngle = link.TargetStartAngle + tgtSweep;
            nodeInOffset[link.TargetId] = Math.Min(link.TargetEndAngle, tgtNode.EndAngle);
        }
    }

    /// <summary>
    /// It deduces the architectural layer to which a symbol belongs using heuristics
    /// on the symbol type, prefix/suffix conventions in the file name and path.
    /// </summary>
    private static ArchitecturalLayerKind InferLayer(Symbol s)
    {
        // 1. Controls and graphical interface elements
        if (s.Kind is SymbolKind.Form or SymbolKind.UserControl)
            return ArchitecturalLayerKind.UserInterface;

        var name = s.Name.ToLowerInvariant();
        var qname = s.QualifiedName.ToLowerInvariant();

        if (name.StartsWith("frm") || name.StartsWith("ctl") || name.EndsWith("view") || name.EndsWith("window") || name.EndsWith("form") || qname.Contains("ui") || qname.Contains("views"))
            return ArchitecturalLayerKind.UserInterface;

        // 2. Data access components (DAO, Repository, DbContext)
        if (name.Contains("db") || name.Contains("dao") || name.Contains("repo") || name.Contains("sql") || name.Contains("data") || name.Contains("entity") || qname.Contains("data"))
            return ArchitecturalLayerKind.DataAccess;

        // 3. Shared utility modules, constants and common types
        if (name.Contains("util") || name.Contains("helper") || name.Contains("const") || name.Contains("common") || name.Contains("global") || s.Kind is SymbolKind.Type or SymbolKind.Enum)
            return ArchitecturalLayerKind.CommonUtilities;

        // 4. Default: Business Logic / Domain Services
        return ArchitecturalLayerKind.BusinessLogic;
    }

    /// <summary>
    /// Evaluate whether a dependency between two layers violates Clean / N-Tier Architecture principles:
    /// - DataAccess must not invoke UserInterface or BusinessLogic.
    /// - CommonUtilities must not depend on specific higher layers.
    /// - BusinessLogic must not invoke the UserInterface directly.
    /// </summary>
    private static bool IsLayerViolation(ArchitecturalLayerKind source, ArchitecturalLayerKind target)
    {
        return (source == ArchitecturalLayerKind.DataAccess && target == ArchitecturalLayerKind.UserInterface) ||
               (source == ArchitecturalLayerKind.DataAccess && target == ArchitecturalLayerKind.BusinessLogic) ||
               (source == ArchitecturalLayerKind.CommonUtilities && target != ArchitecturalLayerKind.CommonUtilities) ||
               (source == ArchitecturalLayerKind.BusinessLogic && target == ArchitecturalLayerKind.UserInterface);
    }
}
