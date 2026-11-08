using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.TemporalCoupling;

/// <summary>
/// Coppia di file che mostrano un accoppiamento temporale (co-modifica frequente nei commit del VCS).
/// </summary>
public sealed class TemporalCouplingPair
{
    public required string FileA { get; init; }
    public required string FileB { get; init; }
    public string FileAName => Path.GetFileName(FileA);
    public string FileBName => Path.GetFileName(FileB);

    /// <summary>Numero di commit in cui entrambi i file sono stati modificati insieme.</summary>
    public int CoChangeCount { get; set; }

    /// <summary>Totale commit in cui compare il File A.</summary>
    public int TotalCommitsA { get; set; }

    /// <summary>Totale commit in cui compare il File B.</summary>
    public int TotalCommitsB { get; set; }

    /// <summary>
    /// Indice di similarità Jaccard J(A, B) = CoChange / (CommitsA + CommitsB - CoChange) in [0.0 .. 1.0].
    /// </summary>
    public double JaccardIndex { get; set; }

    /// <summary>Confidenza P(B|A) = CoChange / CommitsA in [0.0 .. 1.0].</summary>
    public double ConfidenceAToB => TotalCommitsA > 0 ? (double)CoChangeCount / TotalCommitsA : 0.0;

    /// <summary>Confidenza P(A|B) = CoChange / CommitsB in [0.0 .. 1.0].</summary>
    public double ConfidenceBToA => TotalCommitsB > 0 ? (double)CoChangeCount / TotalCommitsB : 0.0;

    /// <summary>
    /// Indica se tra i due file esiste già una dipendenza statica esplicita (chiamata o istanziazione).
    /// Se false e l'indice di Jaccard è alto, si tratta di un accoppiamento nascosto critico (Hidden Coupling).
    /// </summary>
    public bool HasStaticDependency { get; set; }
}

/// <summary>
/// Dataset completo dell'analisi di Temporal Coupling estratto dalla cronologia Git / VCS.
/// </summary>
public sealed class TemporalCouplingDataset
{
    public bool HasGitHistory { get; set; }
    public string? GitRoot { get; set; }
    public int TotalCommitsAnalyzed { get; set; }
    public List<TemporalCouplingPair> Pairs { get; init; } = new();

    public int TotalCoupledPairsCount => Pairs.Count;
    public int StrongCouplingCount => Pairs.Count(p => p.JaccardIndex >= 0.5);
    public int HiddenCouplingCount => Pairs.Count(p => p.JaccardIndex >= 0.4 && !p.HasStaticDependency);
}
