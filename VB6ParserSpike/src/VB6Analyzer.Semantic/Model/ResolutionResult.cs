namespace Macondo.Semantic.Model;

/// <summary>
/// Rappresenta l'esito della risoluzione semantica di un identificatore, espressione o call-site in VB6 (M3).
/// 
/// Contiene il simbolo target risolto nel <see cref="CodeGraph"/> (o parametro formale),
/// il livello di confidenza (<see cref="ResolutionConfidence"/>), il tipo semantico dedotto
/// ed eventuali dettagli diagnostici per casi non risolvibili staticamente (es. late binding su Object).
/// </summary>
public sealed class ResolutionResult
{
    /// <summary>Il simbolo target identificato nel CodeGraph (es. Method Customer.Save, Field mCount, ecc.).</summary>
    public Symbol? TargetSymbol { get; init; }

    /// <summary>Il parametro target risolto se il riferimento è a un argomento formale della routine corrente.</summary>
    public ParameterSymbol? TargetParameter { get; init; }

    /// <summary>
    /// Livello di certezza della risoluzione (sezione 11 del charter):
    /// - Exact: risoluzione certa e deterministica.
    /// - Inferred: dedotta per euristica o inferenza di tipo.
    /// - Ambiguous: molteplici candidati con lo stesso nome nello stesso scope.
    /// - Unknown: dinamico (Object/Variant), non presente nel progetto o late binding.
    /// </summary>
    public ResolutionConfidence Confidence { get; init; }

    /// <summary>Nome del tipo semantico associato all'identificatore o espressione (es. "Customer", "Long", "Object").</summary>
    public string? TargetTypeName { get; init; }

    /// <summary>ID del simbolo corrispondente al tipo nel CodeGraph se si tratta di un tipo definito dall'utente/progetto.</summary>
    public Guid? ResolvedTypeSymbolId { get; init; }

    /// <summary>Dettaglio diagnostico o motivazione (ad es. per Unknown, late binding, ambiguità).</summary>
    public string? Details { get; init; }

    /// <summary>Indica se la risoluzione ha avuto esito positivo con certezza Exact o Inferred.</summary>
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
