using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Modello visuale del grafo per il rendering e l'interazione UI (sezione 18 e 24 del charter).
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

    /// <summary>Recupera un nodo del grafo tramite il suo ID univoco.</summary>
    public GraphNode? GetNode(Guid id)
    {
        _nodesById ??= Nodes.ToDictionary(n => n.Id);
        return _nodesById.TryGetValue(id, out var node) ? node : null;
    }

    /// <summary>Restituisce i nodi figli contenuti direttamente nel nodo specificato.</summary>
    public IEnumerable<GraphNode> GetChildren(Guid parentId) =>
        Nodes.Where(n => n.ParentId == parentId);


    /// <summary>Invalida la cache degli ID e delle query dopo modifiche alla struttura del grafo.</summary>
    public void Invalidate()
    {
        _nodesById = null;
        Queries?.ClearCache();
    }
}
