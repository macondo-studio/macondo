namespace VB6Analyzer.Semantic.Model;

/// <summary>Elenco completo dalla sezione 8 del charter. M2 usa solo Contains (vedi SymbolExtractor).</summary>
public enum RelationshipKind
{
    Contains,
    Declares,
    Calls,
    References,
    UsesType,
    Inherits,
    Implements,
    Instantiates,
    HandlesEvent,
    Reads,
    Writes
}

/// <summary>
/// Livello di certezza della relazione (sezione 11). M2 produce solo relazioni
/// strutturali (CONTAINS), sempre Exact: non c'è ambiguità nel dire "questo modulo
/// contiene questo Sub", è puro fatto sintattico. Confidence diventa rilevante da
/// M3 in poi, quando si proveranno a risolvere CALLS/USES_TYPE/INSTANTIATES su
/// codice con late binding, Variant, Object, ecc.
/// </summary>
public enum ResolutionConfidence
{
    Exact,
    Inferred,
    Ambiguous,
    Unknown
}

/// <summary>Modello Relationship come descritto nella sezione 10 del project charter.</summary>
public sealed class Relationship
{
    public required Guid SourceId { get; init; }
    public required Guid TargetId { get; init; }
    public required RelationshipKind Kind { get; init; }
    public int? Line { get; init; }
    public ResolutionConfidence Confidence { get; init; } = ResolutionConfidence.Exact;
}
