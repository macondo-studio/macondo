using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Hotspots;

/// <summary>
/// Conceptual quadrant of the risk matrix (Adam Tornhill / CodeScene methodology).
/// </summary>
public enum HotspotQuadrant
{
    /// <summary>Low Risk: Low Churn + Low Complexity (Stable and simple).</summary>
    Safe,

    /// <summary>Moderate Risk: High Churn + Low Complexity (Often modified but simple).</summary>
    HighChurnLowComplexity,

    /// <summary>Latent Risk: Low Churn + High Complexity (Complex but rarely touched).</summary>
    LowChurnHighComplexity,

    /// <summary>CRITICAL HOTSPOT: High Churn + High Complexity (Area with very high probability of defects).</summary>
    DangerZone
}

/// <summary>
/// Single element analyzed in the Hotspot matrix (Method, Procedure, Module or Class).
/// </summary>
public sealed class HotspotItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }

    /// <summary>Y axis: Cyclomatic or structural complexity score (1 .. N).</summary>
    public double Complexity { get; set; }

    /// <summary>X-Axis: Frequency of change from VCS (Churn) or Fan-In dependency (how many callers/users).</summary>
    public double ChurnOrFanIn { get; set; }

    /// <summary>Dot/Bubble Radius: Code Size (LOC / Instructions / Parameters).</summary>
    public double SizeMetric { get; set; }

    /// <summary>Unified normalized risk score (0 .. 100).</summary>
    public double RiskScore { get; set; }

    /// <summary>Categorized risk level.</summary>
    public ImpactRiskLevel RiskLevel { get; set; }

    /// <summary>Quadrant assigned based on codebase average.</summary>
    public HotspotQuadrant Quadrant { get; set; }

    public int DirectCallersCount { get; set; }
    public int DirectCalleesCount { get; set; }
}

/// <summary>
/// Complete dataset of the Hotspots matrix for an entire project.
/// </summary>
public sealed class HotspotDataset
{
    public List<HotspotItem> Items { get; init; } = new();
    public double ThresholdX { get; set; }
    public double ThresholdY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public int DangerZoneCount => Items.Count(i => i.Quadrant == HotspotQuadrant.DangerZone);
}
