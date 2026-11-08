namespace Macondo.Semantic.Model;


/// <summary>
/// Rappresenta una relazione diretta / arco orientato tra due simboli all'interno del <see cref="CodeGraph"/>.
/// </summary>
public sealed class Relationship
{
    /// <summary>ID del simbolo sorgente della relazione.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>ID del simbolo di destinazione della relazione.</summary>
    public required Guid TargetId { get; init; }

    /// <summary>Tipologia della relazione (Contains, Calls, Instantiates, UsesType, ecc.).</summary>
    public required RelationshipKind Kind { get; init; }

    /// <summary>Numero di riga nel file sorgente in cui si origina la relazione (opzionale).</summary>
    public int? Line { get; init; }

    /// <summary>Grado di certezza della relazione semantica.</summary>
    public ResolutionConfidence Confidence { get; init; } = ResolutionConfidence.Exact;
}
