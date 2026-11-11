using Macondo.App.Models;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Adapter to convert the real semantic <see cref="Macondo.Semantic.Model.CodeGraph"/>
/// in the visual model <see cref="VisualCodeGraph"/> ready for rendering and layout.
/// </summary>
public static class GraphVisualAdapter
{
    /// <summary>
    /// <summary>Global deterministic ID of the project root node.</summary>
    public static readonly Guid ProjectRootId = new Guid("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// Converts the semantic graph into a visual graph, connecting the structural hierarchy
    /// and calculating the initial layout of the nodes directly in the background.
    /// </summary>
    /// <param name="semanticGraph">The semantic CodeGraph containing real symbols and relationships.</param>
    /// <param name="projectName">Descriptive name of the project displayed.</param>
    /// <param name="preferredLayout">User preferred layout mode to calculate in the background.</param>
    /// <param name="cancellationToken">Optional deletion token.</param>
    public static VisualCodeGraph FromSemanticGraph(
        Macondo.Semantic.Model.CodeGraph semanticGraph,
        string projectName = "Project",
        GraphLayoutMode? preferredLayout = null,
        CancellationToken cancellationToken = default)
    {
        var visualGraph = new VisualCodeGraph
        {
            SemanticGraph = semanticGraph,
            Queries = new CallGraphQueries(semanticGraph)
        };

        if (semanticGraph.Symbols.Count == 0)

            return visualGraph;

        // 1. Map all semantic symbols into GraphNode
        var nodesBySymbolId = new Dictionary<Guid, GraphNode>();
        var projectNodes = new List<GraphNode>();

        foreach (var symbol in semanticGraph.Symbols)
        {
            var node = new GraphNode
            {
                Id = symbol.Id,
                Name = symbol.Name,
                QualifiedName = symbol.QualifiedName,
                Kind = symbol.Kind,
                SemanticSymbol = symbol,
                IsExpanded = true
            };
            nodesBySymbolId[symbol.Id] = node;
            visualGraph.Nodes.Add(node);

            if (symbol.Kind == SymbolKind.Project)
            {
                node.ProjectId = symbol.Id;
                node.ProjectName = symbol.Name;
                projectNodes.Add(node);
            }
        }

        // If there are no semantic Project nodes, create the synthetic root node
        GraphNode defaultProjectNode;
        if (projectNodes.Count == 0)
        {
            var defaultLang = semanticGraph.Symbols
                .Select(s => s.GetEffectiveLanguage())
                .FirstOrDefault(l => l != SourceLanguage.Unknown);

            if (defaultLang == SourceLanguage.Unknown)
                defaultLang = SourceLanguage.VisualBasic6;

            defaultProjectNode = new GraphNode
            {
                Id = ProjectRootId,
                Name = projectName,
                QualifiedName = projectName,
                Kind = SymbolKind.Project,
                Language = defaultLang,
                IsExpanded = true
            };
            defaultProjectNode.ProjectId = defaultProjectNode.Id;
            defaultProjectNode.ProjectName = defaultProjectNode.Name;
            visualGraph.Nodes.Insert(0, defaultProjectNode);
            nodesBySymbolId[defaultProjectNode.Id] = defaultProjectNode;
            projectNodes.Add(defaultProjectNode);
        }
        else
        {
            defaultProjectNode = projectNodes[0];
        }

        // 2. Connect the CONTAINS hierarchy: assign ParentIds.
        // Explicitly discard bad self-relations (rel.SourceId == rel.TargetId) to avoid self-loops.
        var containsRels = semanticGraph.Relationships
            .Where(r => r.Kind == RelationshipKind.Contains)
            .ToList();

        foreach (var rel in containsRels)
        {
            if (rel.SourceId != rel.TargetId && nodesBySymbolId.TryGetValue(rel.TargetId, out var childNode))

                childNode.ParentId = rel.SourceId;
        }

        // 3. Top-level modules and classes that do not have an explicit parent are hooked into the default project
        foreach (var node in visualGraph.Nodes)
        {
            if (node.Kind != SymbolKind.Project && node.ParentId == null)
            {
                if (node.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module
                                 or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum
                                 or SymbolKind.Interface or SymbolKind.Struct or SymbolKind.Record)
                {
                    node.ParentId = defaultProjectNode.Id;
                    visualGraph.Edges.Add(new GraphEdge
                    {
                        SourceId = defaultProjectNode.Id,
                        TargetId = node.Id,
                        Kind = RelationshipKind.Contains,
                        Confidence = ResolutionConfidence.Exact
                    });
                }
            }
        }

        // 4. Propagate ProjectId and ProjectName across all nodes moving up the hierarchy:
        // - No arbitrary depth limit is enforced to enable exhaustive analysis of complex code bases.
        // - A 'visited' set is used to promptly detect and break circular references (closed loops A -> B -> A),
        // preventing infinite 100% CPU freezes on code bases with tens of thousands of symbols.
        // - Immediate memoization in O(1) is applied: if an ancestor has already determined its ProjectId,
        // the current node instantly inherits this membership, drastically reducing the computational cost.
        foreach (var node in visualGraph.Nodes)
        {
            if (node.Kind == SymbolKind.Project) continue;

            var curr = node;
            HashSet<Guid>? visited = null;
            while (curr.ParentId.HasValue && nodesBySymbolId.TryGetValue(curr.ParentId.Value, out var parentNode))
            {
                // Root ancestor of type Project reached
                if (parentNode.Kind == SymbolKind.Project)
                {
                    node.ProjectId = parentNode.Id;
                    node.ProjectName = parentNode.Name;
                    break;
                }

                // Memoization shortcut: If the parent node has already resolved its ProjectId,
                // we can reuse it instantly without going further up the tree.
                if (parentNode.ProjectId.HasValue)
                {
                    node.ProjectId = parentNode.ProjectId;
                    node.ProjectName = parentNode.ProjectName;
                    break;
                }

                // Tracking visited nodes along this branch to break circular loops
                visited ??= new HashSet<Guid>();
                if (!visited.Add(curr.Id) || visited.Contains(parentNode.Id))
                    break;

                curr = parentNode;
            }

            // Fallback: If no project was found in the chain, assign to the workspace's default project
            if (node.ProjectId == null)
            {
                node.ProjectId = defaultProjectNode.Id;
                node.ProjectName = defaultProjectNode.Name;
            }
        }

        // 5. Map all semantic relationships in GraphEdge and identify cross-project edges
        foreach (var rel in semanticGraph.Relationships)
        {
            var isCross = false;
            if (rel.Kind != RelationshipKind.Contains &&
                nodesBySymbolId.TryGetValue(rel.SourceId, out var srcNode) &&
                nodesBySymbolId.TryGetValue(rel.TargetId, out var tgtNode))
            {
                if (srcNode.ProjectId.HasValue && tgtNode.ProjectId.HasValue &&
                    srcNode.ProjectId.Value != tgtNode.ProjectId.Value)

                    isCross = true;
            }

            visualGraph.Edges.Add(new GraphEdge
            {
                SourceId = rel.SourceId,
                TargetId = rel.TargetId,
                Kind = rel.Kind,
                Confidence = rel.Confidence,
                Line = rel.Line,
                IsCrossProject = isCross
            });
        }

        // 6. Calculate your preferred initial layout directly in the background
        cancellationToken.ThrowIfCancellationRequested();
        var layoutMode = preferredLayout ?? GraphLayoutMode.StandardRadial;
        GraphLayoutManager.ApplyLayout(visualGraph, layoutMode, cancellationToken);
        visualGraph.Invalidate();

        return visualGraph;
    }
}
