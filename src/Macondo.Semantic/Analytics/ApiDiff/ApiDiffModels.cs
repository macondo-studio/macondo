using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ApiDiff;

/// <summary>
/// Classification of the severity of an API change according to Semantic Versioning (SemVer).
/// </summary>
public enum ApiChangeImpact
{
    /// <summary>Backwards compatible / internal change (Patch: 0.0.X).</summary>
    Patch,

    /// <summary>Addition of new features or backwards compatible APIs (Minor: 0.X.0).</summary>
    Minor,

    /// <summary>Binary or contractual compatibility break of the API (Major / Breaking Change: X.0.0).</summary>
    MajorBreaking
}

/// <summary>
/// Type of change detected on a public API symbol.
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
/// Single difference detected on the API surface between two CodeGraph snapshots.
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
/// Overall API Diff analysis report and SemVer recommendation.
/// </summary>
public sealed class ApiDiffReport
{
    public List<ApiDifferenceItem> Differences { get; init; } = new();

    public int TotalChanges => Differences.Count;
    public int BreakingChangesCount => Differences.Count(d => d.Impact == ApiChangeImpact.MajorBreaking);
    public int AdditionsCount => Differences.Count(d => d.Impact == ApiChangeImpact.Minor);
    public int PatchChangesCount => Differences.Count(d => d.Impact == ApiChangeImpact.Patch);

    /// <summary>
    /// Automatic recommendation of the required SemVer release type.
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
