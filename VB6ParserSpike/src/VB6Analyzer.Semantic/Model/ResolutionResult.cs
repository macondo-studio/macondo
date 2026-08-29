namespace VB6Analyzer.Semantic.Model;

/// <summary>
/// Esito della risoluzione semantica di un identificatore, espressione o chiamata in VB6.
/// </summary>
public sealed class ResolutionResult
{
    /// <summary>Simbolo target risolto nel CodeGraph (se presente).</summary>
    public Symbol? TargetSymbol { get; init; }

    /// <summary>Parametro risolto nel caso di riferimento a parametro locale.</summary>
    public ParameterSymbol? TargetParameter { get; init; }

    /// <summary>Livello di certezza della risoluzione (sezione 11 del charter).</summary>
    public ResolutionConfidence Confidence { get; init; }

    /// <summary>Tipo semantico associato all'identificatore o espressione risolta (es. "Customer", "Long", "Object").</summary>
    public string? TargetTypeName { get; init; }

    /// <summary>ID del simbolo corrispondente al tipo nel CodeGraph se si tratta di un tipo definito dall'utente/progetto.</summary>
    public Guid? ResolvedTypeSymbolId { get; init; }

    /// <summary>Dettaglio diagnostico (ad es. motivo per Unknown, late binding, ambiguo, ecc.).</summary>
    public string? Details { get; init; }

    public bool IsSuccess => Confidence == ResolutionConfidence.Exact || Confidence == ResolutionConfidence.Inferred;

    public static ResolutionResult Exact(Symbol symbol, string? typeName = null, Guid? typeSymbolId = null) =>
        new()
        {
            TargetSymbol = symbol,
            Confidence = ResolutionConfidence.Exact,
            TargetTypeName = typeName ?? symbol.DeclaredTypeName,
            ResolvedTypeSymbolId = typeSymbolId ?? symbol.ResolvedTypeSymbolId
        };

    public static ResolutionResult ExactParam(ParameterSymbol param) =>
        new()
        {
            TargetParameter = param,
            Confidence = ResolutionConfidence.Exact,
            TargetTypeName = param.DeclaredTypeName,
            ResolvedTypeSymbolId = param.ResolvedTypeSymbolId
        };

    public static ResolutionResult Inferred(Symbol symbol, string? details = null) =>
        new()
        {
            TargetSymbol = symbol,
            Confidence = ResolutionConfidence.Inferred,
            TargetTypeName = symbol.DeclaredTypeName,
            ResolvedTypeSymbolId = symbol.ResolvedTypeSymbolId,
            Details = details
        };

    public static ResolutionResult Ambiguous(string name, string details) =>
        new()
        {
            Confidence = ResolutionConfidence.Ambiguous,
            Details = details
        };

    public static ResolutionResult Unknown(string name, string? typeName = null, string? details = null) =>
        new()
        {
            Confidence = ResolutionConfidence.Unknown,
            TargetTypeName = typeName,
            Details = details ?? $"Simbolo non risolvibile staticamente: '{name}'"
        };

    public override string ToString()
    {
        if (TargetSymbol is not null)
            return $"[{Confidence}] {TargetSymbol.Kind} {TargetSymbol.QualifiedName} (Type: {TargetTypeName ?? "unknown"})";
        if (TargetParameter is not null)
            return $"[{Confidence}] Parameter {TargetParameter.Name} (Type: {TargetTypeName ?? "unknown"})";
        return $"[{Confidence}] {Details ?? TargetTypeName ?? "Unknown"}";
    }
}
