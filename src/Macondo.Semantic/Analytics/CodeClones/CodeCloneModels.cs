using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.CodeClones;

/// <summary>
/// Type of clone or code duplication.
/// </summary>
public enum CloneType
{
    /// <summary>Type 1: Exact duplication of code (same tokens, identical syntax, ignore spaces/comments only).</summary>
    Type1Exact,

    /// <summary>Type 2: Structural/parameterized duplication (same logic and renamed constructs, identifiers or constants).</summary>
    Type2Parameterized
}

/// <summary>
/// Single occurrence or code fragment identified as part of a group of clones.
/// </summary>
public sealed class CodeCloneFragment
{
    public Symbol Symbol { get; init; } = null!;
    public string FilePath => Symbol.FilePath;
    public string ModuleName => Symbol.Name;
    public int LineStart { get; init; }
    public int LineEnd { get; init; }
    public int LineCount => Math.Max(1, LineEnd - LineStart + 1);
    public string CodeSnippet { get; init; } = string.Empty;
}

/// <summary>
/// A group of identical or structurally equivalent code fragments duplicated in the project.
/// </summary>
public sealed class CodeCloneGroup
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public CloneType Type { get; init; }
    public int TokenCount { get; init; }
    public int LineCount { get; init; }

    /// <summary>All duplicate fragments belonging to this group.</summary>
    public List<CodeCloneFragment> Occurrences { get; init; } = new();

    /// <summary>Total number of redundant/saving lines with a single abstraction refactoring.</summary>
    public int RedundantLines => Occurrences.Count > 1 ? (Occurrences.Count - 1) * LineCount : 0;
}

/// <summary>
/// Aggregated code clone analysis (CPD) report.
/// </summary>
public sealed class CodeCloneReport
{
    public List<CodeCloneGroup> CloneGroups { get; init; } = new();

    public int TotalDuplicateGroups => CloneGroups.Count;
    public int TotalDuplicateOccurrences => CloneGroups.Sum(g => g.Occurrences.Count);
    public int TotalRedundantLines => CloneGroups.Sum(g => g.RedundantLines);
    public int ExactClonesCount => CloneGroups.Count(g => g.Type == CloneType.Type1Exact);
    public int ParameterizedClonesCount => CloneGroups.Count(g => g.Type == CloneType.Type2Parameterized);
}
