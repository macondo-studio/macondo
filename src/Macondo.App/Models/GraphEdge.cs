using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Visual arc of the CodeGraph rendered by the GraphControl.
/// Encapsulates the real semantic relationship (<see cref="RelationshipKind"/>)
/// connecting a source node to a destination node.
/// </summary>
public sealed class GraphEdge
{
    /// <summary>ID of the source node.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>ID of the destination node.</summary>
    public required Guid TargetId { get; init; }

    /// <summary>Type of semantic relationship (Contains, Calls, Instantiates, UsesType, etc.).</summary>
    public required RelationshipKind Kind { get; init; }

    /// <summary>Resolution confidence level (Exact, Inferred, Unknown).</summary>
    public ResolutionConfidence Confidence { get; init; } = ResolutionConfidence.Exact;

    /// <summary>Line number in the source where the relationship originates.</summary>
    public int? Line { get; init; }

    /// <summary>Indicates whether the arc connects nodes belonging to different projects/folders.</summary>
    public bool IsCrossProject { get; set; }

    public override string ToString() => $"{Kind}: {SourceId} -> {TargetId} ({Confidence}){(IsCrossProject ? " [CrossProject]" : "")}";
}
