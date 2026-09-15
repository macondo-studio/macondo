using Macondo.App.Models;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Adattatore per convertire il <see cref="Macondo.Semantic.Model.CodeGraph"/> semantico reale
/// nel modello visuale <see cref="VisualCodeGraph"/> pronto per il rendering e layout.
/// </summary>
public static class GraphVisualAdapter
{
    /// <summary>
    /// <summary>ID deterministico globale del nodo radice di progetto.</summary>
    public static readonly Guid ProjectRootId = new Guid("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// Converte il grafo semantico in grafo visuale, collegando la gerarchia strutturale
    /// e calcolando il layout radiale dei nodi.
    /// </summary>
    /// <param name="semanticGraph">Il CodeGraph semantico contenente simboli e relazioni reali.</param>
    /// <param name="projectName">Nome descrittivo del progetto visualizzato.</param>
    public static VisualCodeGraph FromSemanticGraph(Macondo.Semantic.Model.CodeGraph semanticGraph, string projectName = "VB6 Project")
    {
        var visualGraph = new VisualCodeGraph
        {
            SemanticGraph = semanticGraph,
            Queries = new CallGraphQueries(semanticGraph)
        };

        if (semanticGraph.Symbols.Count == 0)
        {
            return visualGraph;
        }

        // 1. Mappa tutti i simboli semantici in GraphNode
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

        // Se non ci sono nodi Project semantici, crea il nodo radice sintetico
        GraphNode defaultProjectNode;
        if (projectNodes.Count == 0)
        {
            defaultProjectNode = new GraphNode
            {
                Id = ProjectRootId,
                Name = projectName,
                QualifiedName = projectName,
                Kind = SymbolKind.Project,
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

        // 2. Collega la gerarchia CONTAINS: assegna i ParentId
        var containsRels = semanticGraph.Relationships
            .Where(r => r.Kind == RelationshipKind.Contains)
            .ToList();

        foreach (var rel in containsRels)
        {
            if (nodesBySymbolId.TryGetValue(rel.TargetId, out var childNode))
            {
                childNode.ParentId = rel.SourceId;
            }
        }

        // 3. I moduli e classi di primo livello che non hanno un parent esplicito vengono agganciati al default project
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

        // 4. Propaga ProjectId e ProjectName su tutti i nodi risalendo la gerarchia
        foreach (var node in visualGraph.Nodes)
        {
            if (node.Kind == SymbolKind.Project) continue;

            var curr = node;
            while (curr.ParentId.HasValue && nodesBySymbolId.TryGetValue(curr.ParentId.Value, out var parentNode))
            {
                if (parentNode.Kind == SymbolKind.Project)
                {
                    node.ProjectId = parentNode.Id;
                    node.ProjectName = parentNode.Name;
                    break;
                }
                curr = parentNode;
            }

            if (node.ProjectId == null)
            {
                node.ProjectId = defaultProjectNode.Id;
                node.ProjectName = defaultProjectNode.Name;
            }
        }

        // 5. Mappa tutte le relazioni semantiche in GraphEdge e identifica gli archi cross-project
        foreach (var rel in semanticGraph.Relationships)
        {
            var isCross = false;
            if (rel.Kind != RelationshipKind.Contains &&
                nodesBySymbolId.TryGetValue(rel.SourceId, out var srcNode) &&
                nodesBySymbolId.TryGetValue(rel.TargetId, out var tgtNode))
            {
                if (srcNode.ProjectId.HasValue && tgtNode.ProjectId.HasValue &&
                    srcNode.ProjectId.Value != tgtNode.ProjectId.Value)
                {
                    isCross = true;
                }
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

        // 6. Calcola il layout iniziale
        RadialLayoutEngine.Apply(visualGraph);
        visualGraph.Invalidate();

        return visualGraph;
    }
}
