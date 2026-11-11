using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.TemporalCoupling;

/// <summary>
/// Pair of files showing temporal coupling (frequent co-modification in VCS commits).
/// </summary>
public sealed class TemporalCouplingPair
{
    public required string FileA { get; init; }
    public required string FileB { get; init; }
    public string FileAName => Path.GetFileName(FileA);
    public string FileBName => Path.GetFileName(FileB);

    /// <summary>Number of commits in which both files were modified together.</summary>
    public int CoChangeCount { get; set; }

    /// <summary>Total commit in which File A appears.</summary>
    public int TotalCommitsA { get; set; }

    /// <summary>Total commit in which File B appears.</summary>
    public int TotalCommitsB { get; set; }

    /// <summary>
    /// Jaccard similarity index J(A, B) = CoChange / (CommitsA + CommitsB - CoChange) in [0.0 .. 1.0].
    /// </summary>
    public double JaccardIndex { get; set; }

    /// <summary>Confidence P(B|A) = CoChange / CommitsA in [0.0 .. 1.0].</summary>
    public double ConfidenceAToB => TotalCommitsA > 0 ? (double)CoChangeCount / TotalCommitsA : 0.0;

    /// <summary>Confidence P(A|B) = CoChange / CommitsB in [0.0 .. 1.0].</summary>
    public double ConfidenceBToA => TotalCommitsB > 0 ? (double)CoChangeCount / TotalCommitsB : 0.0;

    /// <summary>
    /// Indicates whether an explicit static dependency (call or instantiation) already exists between the two files.
    /// If false and the Jaccard index is high, it is a critical hidden coupling.
    /// </summary>
    public bool HasStaticDependency { get; set; }
}

/// <summary>
/// Complete Temporal Coupling analysis dataset pulled from Git/VCS history.
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
