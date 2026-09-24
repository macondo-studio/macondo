using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.CodeClones;

/// <summary>
/// Tipologia di clone o duplicazione di codice.
/// </summary>
public enum CloneType
{
    /// <summary>Tipo 1: Duplicazione esatta del codice (stessi token, identica sintassi, ignora solo spazi/commenti).</summary>
    Type1Exact,

    /// <summary>Tipo 2: Duplicazione strutturale/parametrizzata (stessa logica e costrutti, identificatori o costanti rinominati).</summary>
    Type2Parameterized
}

/// <summary>
/// Singola occorrenza o frammento di codice identificato come parte di un gruppo di cloni.
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
/// Gruppo di frammenti di codice identici o strutturalmente equivalenti duplicati nel progetto.
/// </summary>
public sealed class CodeCloneGroup
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public CloneType Type { get; init; }
    public int TokenCount { get; init; }
    public int LineCount { get; init; }

    /// <summary>Tutti i frammenti duplicati appartenenti a questo gruppo.</summary>
    public List<CodeCloneFragment> Occurrences { get; init; } = new();

    /// <summary>Numero totale di righe ridondanti/risparmiabili con un refactoring ad astrazione unica.</summary>
    public int RedundantLines => Occurrences.Count > 1 ? (Occurrences.Count - 1) * LineCount : 0;
}

/// <summary>
/// Report aggregato dell'analisi dei cloni di codice (CPD).
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
