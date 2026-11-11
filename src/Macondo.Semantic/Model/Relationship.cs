namespace Macondo.Semantic.Model;


/// <summary>
/// Represents a direct relationship/directed arc between two symbols within the <see cref="CodeGraph"/>.
/// </summary>
public sealed class Relationship
{
    /// <summary>ID of the relationship source symbol.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>ID of the relationship target symbol.</summary>
    public required Guid TargetId { get; init; }

    /// <summary>Type of relationship (Contains, Calls, Instantiates, UsesType, etc.).</summary>
    public required RelationshipKind Kind { get; init; }

    /// <summary>Line number in the source file where the relationship originates (optional).</summary>
    public int? Line { get; init; }

    /// <summary>Degree of certainty of the semantic relationship.</summary>
    public ResolutionConfidence Confidence { get; init; } = ResolutionConfidence.Exact;
}
