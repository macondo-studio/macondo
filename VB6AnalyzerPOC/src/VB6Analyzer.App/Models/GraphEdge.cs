namespace VB6Analyzer.App.Models;

/// <summary>
/// Sottoinsieme del RelationshipKind descritto nella sezione 8 del charter,
/// sufficiente per il POC (CONTAINS per la gerarchia, CALLS per il call graph fittizio).
/// </summary>
public enum EdgeKind
{
    Contains,
    Calls
}

/// <summary>
/// Arco del CodeGraph fittizio. Corrisponde concettualmente a Relationship
/// (sezione 10), senza ancora Confidence/ResolutionConfidence: quello
/// arriverà quando ci sarà un vero symbol resolver (M3).
/// </summary>
public sealed class GraphEdge
{
    public required Guid SourceId { get; init; }
    public required Guid TargetId { get; init; }
    public required EdgeKind Kind { get; init; }
}
