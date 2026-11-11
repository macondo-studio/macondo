using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.SocialCode;

/// <summary>
/// Single commit or change record recorded in the VCS history.
/// </summary>
public sealed class FileCommitRecord
{
    public string Author { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public int LinesAdded { get; init; }
    public int LinesDeleted { get; init; }
}

/// <summary>
/// Contribution statistics of an author on a file or module.
/// </summary>
public sealed class AuthorContribution
{
    public string Author { get; init; } = string.Empty;
    public int CommitCount { get; set; }
    public int TotalLinesChanged { get; set; }
    public double OwnershipPercentage { get; set; }
    public DateTime LastCommitDate { get; set; }
}

/// <summary>
/// Risk level linked to the Bus Factor and turnover for a module.
/// </summary>
public enum BusFactorRisk
{
    /// <summary>Low Risk: Multiple developers actively contribute to the component.</summary>
    Low,

    /// <summary>Rischio Medio: 2 autori principali.</summary>
    Medium,

    /// <summary>High / Critical Risk: a single author holds the entire know-how of the component (Bus Factor = 1).</summary>
    High,

    /// <summary>Orphan Code: The primary author no longer contributes to the repository or is inactive.</summary>
    Orphan
}

/// <summary>
/// Social and ownership report for a single project file/module.
/// </summary>
public sealed class FileSocialReport
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public Guid? SymbolId { get; init; }

    /// <summary>Primary author who wrote/modified most of the code.</summary>
    public string PrimaryAuthor { get; set; } = string.Empty;

    /// <summary>Percentage of ownership of the main author (0-100%).</summary>
    public double PrimaryOwnership { get; set; }

    /// <summary>Total number of authors who contributed to this file.</summary>
    public int TotalAuthorsCount => AuthorContributions.Count;

    /// <summary>Bus Factor calculated for this file (minimum number of authors to cover 80% of the code).</summary>
    public int BusFactor { get; set; }

    /// <summary>Assigned risk level.</summary>
    public BusFactorRisk Risk { get; set; }

    /// <summary>Detail of contributions by single author.</summary>
    public List<AuthorContribution> AuthorContributions { get; init; } = new();

    /// <summary>Knowledge Fragmentation (Normalized Shannon Entropy [0..1]).</summary>
    public double KnowledgeEntropy { get; set; }
}

/// <summary>
/// Aggregated dataset of social analysis of Conway's code and law.
/// </summary>
public sealed class SocialCodeDataset
{
    public List<FileSocialReport> Files { get; init; } = new();

    /// <summary>Bus Factor medio dell'intera codebase.</summary>
    public double AverageBusFactor => Files.Count > 0 ? Files.Average(f => f.BusFactor) : 1.0;

    /// <summary>Number of files/modules at critical risk of losing consciousness (Bus Factor = 1).</summary>
    public int HighRiskCount => Files.Count(f => f.Risk == BusFactorRisk.High);

    /// <summary>Number of orphaned files (primary author is no longer active).</summary>
    public int OrphanFilesCount => Files.Count(f => f.Risk == BusFactorRisk.Orphan);

    /// <summary>Global map of authors with file count.</summary>
    public Dictionary<string, int> PrimaryOwnershipCountByAuthor { get; init; } = new();
}
