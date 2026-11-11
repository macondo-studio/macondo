using System;

namespace Macondo.App.Models;

/// <summary>
/// Represents the progress when analyzing a VB6 project or folder.
/// </summary>
public sealed class ProjectAnalysisProgress
{
    /// <summary>
    /// Number of files scanned so far (0..TotalFiles).
    /// </summary>
    public int CurrentFileIndex { get; set; }

    /// <summary>
    /// Total number of VB6 source files to analyze.
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Name of the file currently being processed (e.g. "frmMain.frm").
    /// </summary>
    public string CurrentFileName { get; set; } = string.Empty;

    /// <summary>
    /// Cumulative number of nodes/symbols extracted so far in the AST/graph.
    /// </summary>
    public int ExtractedNodesCount { get; set; }

    /// <summary>
    /// Descriptive message of the current phase or operation.
    /// </summary>
    public string PhaseMessage { get; set; } = string.Empty;

    /// <summary>
    /// Completion percentage from 0 to 100.
    /// </summary>
    public double Percentage => TotalFiles > 0 ? Math.Clamp((double)CurrentFileIndex / TotalFiles * 100.0, 0.0, 100.0) : 0.0;

    /// <summary>
    /// Estimated total scan time in seconds.
    /// </summary>
    public double? EstimatedTotalSeconds { get; set; }

    /// <summary>
    /// Estimated time remaining in seconds to complete analysis and CodeGraph generation.
    /// </summary>
    public double? EstimatedRemainingSeconds { get; set; }
}
