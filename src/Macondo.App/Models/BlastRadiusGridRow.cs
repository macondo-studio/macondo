using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Represents a Blast Radius summary row for the Grid View DataGrid.
/// Includes direct and indirect caller counts, maximum depth, risk level, and containers.
/// </summary>
public sealed class BlastRadiusGridRow
{
    /// <summary>The semantic symbol of the analyzed routine or type.</summary>
    public required Symbol TargetSymbol { get; init; }

    /// <summary>Unique ID of the corresponding node in the visual graph.</summary>
    public required Guid NodeId { get; init; }

    /// <summary>ID of the project you belong to.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>Name of the project or workspace to which it belongs (e.g. "BillingCore", "SalesApp").</summary>
    public required string ProjectName { get; init; }

    /// <summary>Simple name of the symbol (e.g. "Execute", "Save").</summary>
    public required string Name { get; init; }

    /// <summary>Nome qualificato completo (es. "DatabaseHelper.Execute").</summary>
    public required string QualifiedName { get; init; }

    /// <summary>Name of the file or container it belongs to (e.g. "DatabaseHelper.bas").</summary>
    public required string ContainerName { get; init; }

    /// <summary>Symbol type (e.g. Method, Property, Class).</summary>
    public required string Kind { get; init; }

    /// <summary>Number of direct callers (depth = 1).</summary>
    public required int DirectCallersCount { get; init; }

    /// <summary>Number of indirect / transitive callers (depth >= 2).</summary>
    public required int IndirectCallersCount { get; init; }

    /// <summary>Maximum depth of the chain of calls to this symbol.</summary>
    public required int MaxDepth { get; init; }

    /// <summary>Regression risk level calculated by the Impact Analyzer.</summary>
    public required ImpactRiskLevel RiskLevel { get; init; }

    /// <summary>Text displayed for risk (Low, Medium, High, Critical / Low, Medium, High, Critical).</summary>
    public string RiskText => Services.LocalizationService.Instance.GetRiskLevel(RiskLevel);

    /// <summary>Background color for the risk badge.</summary>
    public string RiskBadgeBackground => RiskLevel switch
    {
        ImpactRiskLevel.Critical => "#B81D2D",
        ImpactRiskLevel.High => "#C86400",
        ImpactRiskLevel.Medium => "#C49000",
        _ => "#1A8A56"
    };

    /// <summary>Text color for risk badge.</summary>
    public string RiskBadgeForeground => "#FFFFFF";

    /// <summary>Number of Form UIs affected by the change.</summary>
    public required int AffectedFormsCount { get; init; }

    /// <summary>Total of impacted entities.</summary>
    public required int TotalImpactedCount { get; init; }

    /// <summary>Names of the impacted forms separated by commas.</summary>
    public required string AffectedFormsSummary { get; init; }

    /// <summary>Localized text for the quick action button.</summary>
    public string ActionButtonText => Services.LocalizationService.Instance["Btn_ShowInGraph"];

    /// <summary>Localized tooltip for quick action button.</summary>
    public string ActionButtonTooltip => Services.LocalizationService.Instance["Tooltip_ShowInGraph"];

    public override string ToString() => $"{QualifiedName} [{RiskLevel}]";
}
