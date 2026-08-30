using VB6Analyzer.Semantic.Graph;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.App.Models;

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
    public VB6Analyzer.Semantic.Model.CodeGraph? SemanticGraph { get; set; }

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

    /// <summary>Restituisce i nodi che chiamano direttamente il nodo specificato (Who calls X?).</summary>
    public IEnumerable<GraphNode> GetCallers(Guid nodeId) =>
        Edges.Where(e => e.Kind == RelationshipKind.Calls && e.TargetId == nodeId)
             .Select(e => GetNode(e.SourceId))
             .Where(n => n is not null)!;

    /// <summary>Restituisce i nodi chiamati direttamente dal nodo specificato (What does X call?).</summary>
    public IEnumerable<GraphNode> GetCallees(Guid nodeId) =>
        Edges.Where(e => e.Kind == RelationshipKind.Calls && e.SourceId == nodeId)
             .Select(e => GetNode(e.TargetId))
             .Where(n => n is not null)!;

    /// <summary>Invalida la cache degli ID dopo modifiche alla lista dei nodi.</summary>
    public void Invalidate() => _nodesById = null;
}
