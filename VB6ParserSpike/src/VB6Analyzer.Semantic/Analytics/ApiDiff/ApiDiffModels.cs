using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ApiDiff;

/// <summary>
/// Classificazione della severità di una modifica di API secondo il Semantic Versioning (SemVer).
/// </summary>
public enum ApiChangeImpact
{
    /// <summary>Modifica retrocompatibile / interna (Patch: 0.0.X).</summary>
    Patch,

    /// <summary>Aggiunta di nuove funzionalità o API retrocompatibili (Minor: 0.X.0).</summary>
    Minor,

    /// <summary>Rottura di compatibilità binaria o contrattuale dell'API (Major / Breaking Change: X.0.0).</summary>
    MajorBreaking
}

/// <summary>
/// Tipologia di modifica rilevata su un simbolo dell'API pubblica.
/// </summary>
public enum ApiChangeKind
{
    AddedSymbol,
    RemovedSymbol,
    ChangedAccessibility,
    ChangedParameters,
    ChangedReturnType,
    ModifiedBodyOnly
}

/// <summary>
/// Singola differenza rilevata sulla superficie dell'API tra due snapshot del CodeGraph.
/// </summary>
public sealed class ApiDifferenceItem
{
    public string SymbolQualifiedName { get; init; } = string.Empty;
    public SymbolKind SymbolKind { get; init; }
    public ApiChangeKind ChangeKind { get; init; }
    public ApiChangeImpact Impact { get; init; }
    public string Description { get; init; } = string.Empty;
    public Symbol? OldSymbol { get; init; }
    public Symbol? NewSymbol { get; init; }
}

/// <summary>
/// Report complessivo dell'analisi API Diff e raccomandazione SemVer.
/// </summary>
public sealed class ApiDiffReport
{
    public List<ApiDifferenceItem> Differences { get; init; } = new();

    public int TotalChanges => Differences.Count;
    public int BreakingChangesCount => Differences.Count(d => d.Impact == ApiChangeImpact.MajorBreaking);
    public int AdditionsCount => Differences.Count(d => d.Impact == ApiChangeImpact.Minor);
    public int PatchChangesCount => Differences.Count(d => d.Impact == ApiChangeImpact.Patch);

    /// <summary>
    /// Raccomandazione automatica del tipo di rilascio SemVer richiesto.
    /// </summary>
    public ApiChangeImpact RecommendedBump
    {
        get
        {
            if (BreakingChangesCount > 0) return ApiChangeImpact.MajorBreaking;
            if (AdditionsCount > 0) return ApiChangeImpact.Minor;
            return ApiChangeImpact.Patch;
        }
    }
}
