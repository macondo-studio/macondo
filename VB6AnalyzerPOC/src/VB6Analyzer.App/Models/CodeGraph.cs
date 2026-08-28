namespace VB6Analyzer.App.Models;

/// <summary>
/// Struttura dati centrale del prodotto (sezione 18): "Analyzer != Graph != UI".
/// Nel POC è popolata da GraphGenerator con dati fittizi; nella app reale sarà
/// popolata dal Semantic Analyzer. Vive interamente in RAM (sezione 19: no persistence in V1).
/// </summary>
public sealed class CodeGraph
{
    public List<GraphNode> Nodes { get; } = new();
    public List<GraphEdge> Edges { get; } = new();

    private Dictionary<Guid, GraphNode>? _nodesById;

    public GraphNode? GetNode(Guid id)
    {
        _nodesById ??= Nodes.ToDictionary(n => n.Id);
        return _nodesById.TryGetValue(id, out var node) ? node : null;
    }

    public IEnumerable<GraphNode> GetChildren(Guid parentId) =>
        Nodes.Where(n => n.ParentId == parentId);

    /// <summary>Callers diretti (sezione 16: "Who calls X?").</summary>
    public IEnumerable<GraphNode> GetCallers(Guid nodeId) =>
        Edges.Where(e => e.Kind == EdgeKind.Calls && e.TargetId == nodeId)
             .Select(e => GetNode(e.SourceId))
             .Where(n => n is not null)!;

    /// <summary>Callees diretti (sezione 16: "What does X call?").</summary>
    public IEnumerable<GraphNode> GetCallees(Guid nodeId) =>
        Edges.Where(e => e.Kind == EdgeKind.Calls && e.SourceId == nodeId)
             .Select(e => GetNode(e.TargetId))
             .Where(n => n is not null)!;

    public void Invalidate() => _nodesById = null;
}
