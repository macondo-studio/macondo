using VB6Analyzer.App.Models;
using VB6Analyzer.Semantic.Graph;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.App.Layout;

/// <summary>
/// Adattatore per convertire il <see cref="VB6Analyzer.Semantic.Model.CodeGraph"/> semantico reale
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
    public static VisualCodeGraph FromSemanticGraph(VB6Analyzer.Semantic.Model.CodeGraph semanticGraph, string projectName = "VB6 Project")
    {
        var visualGraph = new VisualCodeGraph
        {
            SemanticGraph = semanticGraph,
            Queries = new CallGraphQueries(semanticGraph)
        };

        // 1. Crea il nodo radice di Progetto con ID deterministico
        var projectNode = new GraphNode
        {
            Id = ProjectRootId,
            Name = projectName,
            QualifiedName = projectName,
            Kind = SymbolKind.Project,
            IsExpanded = true
        };
        visualGraph.Nodes.Add(projectNode);

        // 2. Mappa tutti i simboli semantici in GraphNode
        var nodesBySymbolId = new Dictionary<Guid, GraphNode>();

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
        }

        // 3. Collega la gerarchia CONTAINS: assegna i ParentId
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

        // 4. I moduli e classi di primo livello che non hanno un parent esplicito vengono agganciati al Project root
        foreach (var node in visualGraph.Nodes)
        {
            if (node.Id != projectNode.Id && node.ParentId == null)
            {
                if (node.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module
                                 or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum)
                {
                    node.ParentId = projectNode.Id;
                    visualGraph.Edges.Add(new GraphEdge
                    {
                        SourceId = projectNode.Id,
                        TargetId = node.Id,
                        Kind = RelationshipKind.Contains,
                        Confidence = ResolutionConfidence.Exact
                    });
                }
            }
        }

        // 5. Mappa tutte le relazioni semantiche in GraphEdge
        foreach (var rel in semanticGraph.Relationships)
        {
            visualGraph.Edges.Add(new GraphEdge
            {
                SourceId = rel.SourceId,
                TargetId = rel.TargetId,
                Kind = rel.Kind,
                Confidence = rel.Confidence,
                Line = rel.Line
            });
        }

        // 6. Calcola il layout iniziale
        RadialLayoutEngine.Apply(visualGraph);
        visualGraph.Invalidate();

        return visualGraph;
    }
}
