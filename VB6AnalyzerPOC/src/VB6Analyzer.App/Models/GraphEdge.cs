using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.App.Models;

/// <summary>
/// Arco visuale del CodeGraph renderizzato dal GraphControl.
/// Incapsula la relazione semantica reale (<see cref="RelationshipKind"/>)
/// collegando un nodo sorgente a un nodo di destinazione.
/// </summary>
public sealed class GraphEdge
{
    /// <summary>ID del nodo sorgente.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>ID del nodo di destinazione.</summary>
    public required Guid TargetId { get; init; }

    /// <summary>Tipo di relazione semantica (Contains, Calls, Instantiates, UsesType, etc.).</summary>
    public required RelationshipKind Kind { get; init; }

    /// <summary>Grado di confidenza della risoluzione (Exact, Inferred, Unknown).</summary>
    public ResolutionConfidence Confidence { get; init; } = ResolutionConfidence.Exact;

    /// <summary>Numero di riga nel sorgente in cui si origina la relazione.</summary>
    public int? Line { get; init; }

    public override string ToString() => $"{Kind}: {SourceId} -> {TargetId} ({Confidence})";
}
