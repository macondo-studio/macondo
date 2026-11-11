using System;
using System.Collections.Generic;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Complete snapshot of the analyzed project saved in the local cache (%APPDATA%\Macondo\cache\).
/// Allows instant cold start (sub-50ms) by restoring the semantic CodeGraph,
/// the geometric layout of the visual graph and the entire Blast Radius matrix.
/// </summary>
public sealed class ProjectCacheSnapshot
{
    /// <summary>Snapshot schema version for invalidation in case of model evolution.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Absolute path of the analyzed folder.</summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Project display name.</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>UTC date and time the analysis was created.</summary>
    public DateTime AnalyzedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recently modified VB6 source file at the time of analysis.</summary>
    public DateTime LastFileWriteTimeUtc { get; set; }

    /// <summary>Total number of VB6 source files (.cls, .bas, .frm, .ctl) included.</summary>
    public int FileCount { get; set; }

    /// <summary>Complete list of semantic symbols extracted from CodeGraph.</summary>
    public List<Symbol> Symbols { get; set; } = new();

    /// <summary>Complete list of semantic relationships between symbols.</summary>
    public List<Relationship> Relationships { get; set; } = new();

    /// <summary>Geometric coordinates (X, Y) and expansion state for each node of the visual graph.</summary>
    public List<NodeLayoutSnapshot> LayoutNodes { get; set; } = new();

    /// <summary>Indicates whether the user has manually customized the position of the nodes with the mouse.</summary>
    public bool HasCustomUserLayout { get; set; }

    /// <summary>Precomputed rows for the Blast Radius table.</summary>
    public List<BlastRadiusRowSnapshot> BlastRadiusRows { get; set; } = new();

    /// <summary>Identifiers of analysis cards started or enabled by the user for this project.</summary>
    public List<string> EnabledAnalyticsTabs { get; set; } = new();
}

/// <summary>
/// Snapshot of rendering coordinates and state for a single visual graph node.
/// </summary>
public sealed class NodeLayoutSnapshot
{
    public Guid Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsExpanded { get; set; } = true;
}

/// <summary>
/// Serializable snapshot of a Blast Radius table row.
/// </summary>
public sealed class BlastRadiusRowSnapshot
{
    public Guid NodeId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string QualifiedName { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int DirectCallersCount { get; set; }
    public int IndirectCallersCount { get; set; }
    public int MaxDepth { get; set; }
    public ImpactRiskLevel RiskLevel { get; set; }
    public int AffectedFormsCount { get; set; }
    public int TotalImpactedCount { get; set; }
    public string AffectedFormsSummary { get; set; } = string.Empty;
}
