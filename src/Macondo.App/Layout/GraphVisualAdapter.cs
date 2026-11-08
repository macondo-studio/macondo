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
    /// e calcolando il layout iniziale dei nodi direttamente in background.
    /// </summary>
    /// <param name="semanticGraph">Il CodeGraph semantico contenente simboli e relazioni reali.</param>
    /// <param name="projectName">Nome descrittivo del progetto visualizzato.</param>
    /// <param name="preferredLayout">Modalità di layout preferita dall'utente da calcolare in background.</param>
    /// <param name="cancellationToken">Token opzionale di cancellazione.</param>
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

        // 2. Collega la gerarchia CONTAINS: assegna i ParentId.
        // Scarta esplicitamente le auto-relazioni errate (rel.SourceId == rel.TargetId) per evitare self-loop.
        var containsRels = semanticGraph.Relationships
            .Where(r => r.Kind == RelationshipKind.Contains)
            .ToList();

        foreach (var rel in containsRels)
        {
            if (rel.SourceId != rel.TargetId && nodesBySymbolId.TryGetValue(rel.TargetId, out var childNode))

                childNode.ParentId = rel.SourceId;
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

        // 4. Propaga ProjectId e ProjectName su tutti i nodi risalendo la gerarchia:
        // - Non viene applicato alcun limite arbitrario di profondità per consentire l'analisi esaustiva di basi di codice complesse.
        // - Viene utilizzato un insieme 'visited' per rilevare e spezzare tempestivamente riferimenti circolari (cicli chiusi A -> B -> A),
        //   prevenendo blocchi infiniti al 100% di CPU su basi di codice con decine di migliaia di simboli.
        // - Viene applicata la memoization immediata in O(1): se un antenato ha già determinato il proprio ProjectId,
        //   il nodo corrente eredita istantaneamente tale appartenenza riducendo drasticamente il costo computazionale.
        foreach (var node in visualGraph.Nodes)
        {
            if (node.Kind == SymbolKind.Project) continue;

            var curr = node;
            HashSet<Guid>? visited = null;
            while (curr.ParentId.HasValue && nodesBySymbolId.TryGetValue(curr.ParentId.Value, out var parentNode))
            {
                // Antenato radice di tipo Progetto raggiunto
                if (parentNode.Kind == SymbolKind.Project)
                {
                    node.ProjectId = parentNode.Id;
                    node.ProjectName = parentNode.Name;
                    break;
                }

                // Scorciatoia di memoization: se il nodo genitore ha già risolto il proprio ProjectId,
                // possiamo riutilizzarlo all'istante senza risalire ulteriormente l'albero.
                if (parentNode.ProjectId.HasValue)
                {
                    node.ProjectId = parentNode.ProjectId;
                    node.ProjectName = parentNode.ProjectName;
                    break;
                }

                // Tracciamento dei nodi visitati lungo questo ramo per interrompere loop circolari
                visited ??= new HashSet<Guid>();
                if (!visited.Add(curr.Id) || visited.Contains(parentNode.Id))
                    break;

                curr = parentNode;
            }

            // Fallback: se nessun progetto è stato individuato nella catena, assegna al progetto predefinito del workspace
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

        // 6. Calcola il layout iniziale preferito direttamente in background
        cancellationToken.ThrowIfCancellationRequested();
        var layoutMode = preferredLayout ?? GraphLayoutMode.StandardRadial;
        GraphLayoutManager.ApplyLayout(visualGraph, layoutMode, cancellationToken);
        visualGraph.Invalidate();

        return visualGraph;
    }
}
