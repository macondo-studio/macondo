using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Martin;

/// <summary>
/// Conceptual zone of Martin's diagram (Robert C. Martin / Uncle Bob).
/// </summary>
public enum MartinZone
{
    /// <summary>
    /// Balanced components, positioned along the Main Sequence (A + I ≈ 1).
    /// </summary>
    MainSequence,

    /// <summary>
    /// Zone of Pain: Low abstractness (low A) and high stability (low I, many dependent).
    /// Typical of God Objects / Concrete monolithic modules that are very difficult to modify.
    /// </summary>
    ZoneOfPain,

    /// <summary>
    /// Zone of Uselessness: High abstractness (high A) and high instability (high I, no one depends on it).
    /// Over-engineered abstractions or orphaned interfaces.
    /// </summary>
    ZoneOfUselessness,

    /// <summary>
    /// Non-critical balanced intermediate zone.
    /// </summary>
    Balanced
}

/// <summary>
/// Single component analyzed in the Martin Diagram (Module, Class, Form, Interface, Control).
/// </summary>
public sealed class MartinMetricItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }

    /// <summary>Afferent Coupling (Ca): Number of external components that depend on this module.</summary>
    public int AfferentCoupling { get; set; }

    /// <summary>Efferent Coupling (Ce): Number of external components on which this module depends.</summary>
    public int EfferentCoupling { get; set; }

    /// <summary>Instability (I = Ce / (Ca + Ce)): Varies from 0.0 (maximum stability) to 1.0 (maximum instability).</summary>
    public double Instability { get; set; }

    /// <summary>Number of abstract elements (interfaces, abstract methods, empty/pure signatures).</summary>
    public int AbstractSymbolsCount { get; set; }

    /// <summary>Number of concrete elements (implemented methods, fields, concrete properties).</summary>
    public int ConcreteSymbolsCount { get; set; }

    /// <summary>Abstractness (A = Na / (Na + Nc)): Varies from 0.0 (purely concrete) to 1.0 (purely abstract).</summary>
    public double Abstractness { get; set; }

    /// <summary>Normalized distance from the Main Sequence D = |A + I - 1| / sqrt(2) in [0..1].</summary>
    public double NormalizedDistance { get; set; }

    /// <summary>Absolute distance D = |A + I - 1|.</summary>
    public double Distance => Math.Abs(Abstractness + Instability - 1.0);

    /// <summary>Zona architettonica assegnata.</summary>
    public MartinZone Zone { get; set; }

    /// <summary>Estimated lines of code or total member count.</summary>
    public int LocOrSize { get; set; }
}

/// <summary>
/// Complete dataset of Martin Metrics for a project or workspace.
/// </summary>
public sealed class MartinMetricsDataset
{
    public List<MartinMetricItem> Items { get; init; } = new();

    public double AverageDistance => Items.Count > 0 ? Items.Average(i => i.NormalizedDistance) : 0.0;
    public int PainZoneCount => Items.Count(i => i.Zone == MartinZone.ZoneOfPain);
    public int UselessnessZoneCount => Items.Count(i => i.Zone == MartinZone.ZoneOfUselessness);
    public int MainSequenceCount => Items.Count(i => i.Zone == MartinZone.MainSequence);
}
