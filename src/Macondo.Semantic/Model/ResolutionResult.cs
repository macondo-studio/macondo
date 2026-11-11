namespace Macondo.Semantic.Model;

/// <summary>
/// Represents the outcome of the semantic resolution of an identifier, expression or call-site in VB6.
/// 
/// Contains the target symbol resolved to the <see cref="CodeGraph"/> (or formal parameter),
/// the confidence level (<see cref="ResolutionConfidence"/>), the inferred semantic type
/// and any diagnostic details for cases that cannot be solved statically (e.g. late binding on Object).
/// </summary>
public sealed class ResolutionResult
{
    /// <summary>The target symbol identified in the CodeGraph (e.g. Method Customer.Save, Field mCount, etc.).</summary>
    public Symbol? TargetSymbol { get; init; }

    /// <summary>The target parameter resolved if the reference is to a formal argument of the current routine.</summary>
    public ParameterSymbol? TargetParameter { get; init; }

    /// <summary>
    /// Resolution certainty level:
    /// - Exact: certain and deterministic resolution.
    /// - Inferred: inferred for heuristics or type inference.
    /// - Ambiguous: multiple candidates with the same name in the same scope.
    /// - Unknown: dynamic (Object/Variant), not present in the project or late binding.
    /// </summary>
    public ResolutionConfidence Confidence { get; init; }

    /// <summary>Name of the semantic type associated with the identifier or expression (e.g. "Customer", "Long", "Object").</summary>
    public string? TargetTypeName { get; init; }

    /// <summary>ID of the symbol corresponding to the type in the CodeGraph if it is a user/project defined type.</summary>
    public Guid? ResolvedTypeSymbolId { get; init; }

    /// <summary>Diagnostic detail or reason (e.g. for Unknown, late binding, ambiguity).</summary>
    public string? Details { get; init; }

    /// <summary>Indicates whether the resolution was successful with Exact or Inferred certainty.</summary>
    public bool IsSuccess => Confidence == ResolutionConfidence.Exact || Confidence == ResolutionConfidence.Inferred;

    public override string ToString()
    {
        if (TargetSymbol is not null)
            return $"[{Confidence}] {TargetSymbol.Kind} {TargetSymbol.QualifiedName} (Type: {TargetTypeName ?? "unknown"})";
        if (TargetParameter is not null)
            return $"[{Confidence}] Parameter {TargetParameter.Name} (Type: {TargetTypeName ?? "unknown"})";
        return $"[{Confidence}] {Details ?? TargetTypeName ?? "Unknown"}";
    }
}
