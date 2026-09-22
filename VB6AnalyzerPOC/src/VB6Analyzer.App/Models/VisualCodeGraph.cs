using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Modello visuale del grafo per il rendering e l'interazione UI.
/// Mantiene l'elenco dei <see cref="GraphNode"/> posizionati e dei <see cref="GraphEdge"/>,
/// e fa da ponte verso il <see cref="SemanticGraph"/> e <see cref="CallGraphQueries"/> sottostanti.
/// </summary>
public sealed class VisualCodeGraph
{
    public List<GraphNode> Nodes { get; } = new();
    public List<GraphEdge> Edges { get; } = new();

    /// <summary>Riferimento al CodeGraph semantico reale sottostante.</summary>
    public Macondo.Semantic.Model.CodeGraph? SemanticGraph { get; set; }

    /// <summary>Motore di query semantiche sul Call Graph.</summary>
    public CallGraphQueries? Queries { get; set; }

    private Dictionary<Guid, GraphNode>? _nodesById;
    private Dictionary<Guid, List<GraphNode>>? _childrenByParentId;

    /// <summary>Recupera un nodo del grafo tramite il suo ID univoco.</summary>
    public GraphNode? GetNode(Guid id)
    {
        _nodesById ??= Nodes.ToDictionary(n => n.Id);
        return _nodesById.TryGetValue(id, out var node) ? node : null;
    }

    /// <summary>
    /// Restituisce i nodi figli contenuti direttamente nel nodo specificato.
    /// Esecuzione istantanea O(1) tramite indice gerarchico interno memorizzato nella cache.
    /// </summary>
    public IEnumerable<GraphNode> GetChildren(Guid parentId)
    {
        if (_childrenByParentId == null)
        {
            _childrenByParentId = new Dictionary<Guid, List<GraphNode>>();
            foreach (var node in Nodes)
            {
                if (node.ParentId.HasValue)
                {
                    if (!_childrenByParentId.TryGetValue(node.ParentId.Value, out var list))
                    {
                        list = new List<GraphNode>();
                        _childrenByParentId[node.ParentId.Value] = list;
                    }
                    list.Add(node);
                }
            }
        }

        return _childrenByParentId.TryGetValue(parentId, out var children) ? children : Enumerable.Empty<GraphNode>();
    }

    /// <summary>Invalida la cache degli ID, dell'albero gerarchico e delle query dopo modifiche alla struttura del grafo.</summary>
    public void Invalidate()
    {
        _nodesById = null;
        _childrenByParentId = null;
        Queries?.ClearCache();
    }
}
