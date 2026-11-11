using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Maintainability;

/// <summary>
/// Maintainability level according to the Microsoft / SEI Code Metrics standard.
/// </summary>
public enum MaintainabilityRating
{
    /// <summary>Index 80 - 100: Highly maintainable, clear and well-modulated code.</summary>
    High,

    /// <summary>Index 20 - 79: Moderate maintainability with areas of technical debt or complexity.</summary>
    Moderate,

    /// <summary>Index 0 - 19: Very high risk, monolithic or poorly maintainable code.</summary>
    Low
}

/// <summary>
/// Detailed Halstead Metrics for a Code Element (Maurice Halstead, 1977).
/// </summary>
public sealed class HalsteadMetrics
{
    /// <summary>Number of unique / distinct operators (n1).</summary>
    public int DistinctOperators { get; set; }

    /// <summary>Number of unique / distinct operands (n2).</summary>
    public int DistinctOperands { get; set; }

    /// <summary>Total number of operator occurrences (N1).</summary>
    public int TotalOperators { get; set; }

    /// <summary>Total number of operand occurrences (N2).</summary>
    public int TotalOperands { get; set; }

    /// <summary>Program vocabulary: eta = n1 + n2.</summary>
    public int Vocabulary => DistinctOperators + DistinctOperands;

    /// <summary>Total length of the program: N = N1 + N2.</summary>
    public int Length => TotalOperators + TotalOperands;

    /// <summary>Lunghezza stimata calcolata: N_hat = n1 * log2(n1) + n2 * log2(n2).</summary>
    public double EstimatedLength
    {
        get
        {
            if (DistinctOperators <= 0 || DistinctOperands <= 0) return 0.0;
            return (DistinctOperators * Math.Log2(DistinctOperators)) + (DistinctOperands * Math.Log2(DistinctOperands));
        }
    }

    /// <summary>Halstead volume: V = N * log2(eta). Represents the information space in bits.</summary>
    public double Volume
    {
        get
        {
            int voc = Vocabulary;
            if (voc <= 1 || Length <= 0) return 1.0;
            return Length * Math.Log2(voc);
        }
    }

    /// <summary>Difficulty understanding or implementing: D = (n1 / 2) * (N2 / n2).</summary>
    public double Difficulty
    {
        get
        {
            if (DistinctOperands <= 0) return 1.0;
            return ((double)DistinctOperators / 2.0) * ((double)TotalOperands / DistinctOperands);
        }
    }

    /// <summary>Intellectual effort required to understand/write the code: E = D * V.</summary>
    public double Effort => Difficulty * Volume;

    /// <summary>Estimated comprehension time in seconds (Stroud's law): T = E / 18.</summary>
    public double TimeToUnderstandSeconds => Effort / 18.0;

    /// <summary>Estimated number of bugs / latent defects: B = V / 3000.</summary>
    public double EstimatedBugs => Volume / 3000.0;
}

/// <summary>
/// Single element (File, Class, Module, Routine) evaluated for maintainability.
/// </summary>
public sealed class MaintainabilityItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }
    public SourceLanguage Language { get; init; }

    /// <summary>Total physical lines of code (LOC / LoC).</summary>
    public int LinesOfCode { get; set; }

    /// <summary>Estimated or calculated McCabe Cyclomatic Complexity (CC / V(G)).</summary>
    public int CyclomaticComplexity { get; set; }

    /// <summary>Detail of Halstead metrics.</summary>
    public HalsteadMetrics Halstead { get; init; } = new();

    /// <summary>
    /// Maintainability Index (MI) normalized according to the Microsoft / SEI standard:
    /// MI = MAX(0, (171 - 5.2 * ln(V) - 0.23 * CC - 16.2 * ln(LOC)) * 100 / 171)
    /// </summary>
    public double MaintainabilityIndex { get; set; }

    /// <summary>Livello qualitativo (High, Moderate, Low).</summary>
    public MaintainabilityRating Rating
    {
        get
        {
            if (MaintainabilityIndex >= 80.0) return MaintainabilityRating.High;
            if (MaintainabilityIndex >= 20.0) return MaintainabilityRating.Moderate;
            return MaintainabilityRating.Low;
        }
    }
}

/// <summary>
/// Aggregated dataset for project maintainability analysis.
/// </summary>
public sealed class MaintainabilityDataset
{
    public List<MaintainabilityItem> Items { get; init; } = new();

    /// <summary>Weighted average Maintainability Index of the project.</summary>
    public double AverageMaintainabilityIndex => Items.Count > 0 ? Items.Average(i => i.MaintainabilityIndex) : 100.0;

    /// <summary>Number of highly maintainable elements (MI >= 80).</summary>
    public int HighCount => Items.Count(i => i.Rating == MaintainabilityRating.High);

    /// <summary>Number of moderately maintainable elements (20 <= MI < 80).</summary>
    public int ModerateCount => Items.Count(i => i.Rating == MaintainabilityRating.Moderate);

    /// <summary>Number of elements with low/critical maintainability (MI < 20).</summary>
    public int LowCount => Items.Count(i => i.Rating == MaintainabilityRating.Low);

    /// <summary>Total estimate of latent bugs calculated using the Halstead formula.</summary>
    public double TotalEstimatedBugs => Items.Sum(i => i.Halstead.EstimatedBugs);
}
