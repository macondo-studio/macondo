using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ArchitecturalFlow;

/// <summary>
/// Architectural level or layer inferred from the graph or file types.
/// </summary>
public enum ArchitecturalLayerKind
{
    UserInterface,   // Forms, UserControls, Views
    BusinessLogic,   // Classes, Services, Models, Modules
    DataAccess,      // Database access, Repositories, Helpers
    CommonUtilities  // Utility modules, Constants, Types, Enums
}

/// <summary>
/// Node representing a component, module, or grouping entity in the flow diagram.
/// </summary>
public sealed class FlowNode
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }
    public ArchitecturalLayerKind Layer { get; init; }
    public int Loc { get; init; }
    public int MemberCount { get; init; }
    public double CyclomaticComplexity { get; init; }

    /// <summary>Total volume of outgoing calls/dependencies.</summary>
    public int OutflowVolume { get; set; }

    /// <summary>Total volume of incoming calls/dependencies.</summary>
    public int InflowVolume { get; set; }

    /// <summary>Volume complessivo (Inflow + Outflow).</summary>
    public int TotalVolume => InflowVolume + OutflowVolume;

    /// <summary>Starting angle for Chord diagram layout (in radians).</summary>
    public double StartAngle { get; set; }

    /// <summary>Angle of arrival for the Chord diagram layout (in radians).</summary>
    public double EndAngle { get; set; }

    /// <summary>Central angle of the arc.</summary>
    public double MidAngle => (StartAngle + EndAngle) / 2.0;

    /// <summary>Angular amplitude of the arc (in radians).</summary>
    public double SweepAngle => EndAngle - StartAngle;

    public string LayerName => Layer switch
    {
        ArchitecturalLayerKind.UserInterface => "User Interface",
        ArchitecturalLayerKind.BusinessLogic => "Business Logic",
        ArchitecturalLayerKind.DataAccess => "Data Access",
        ArchitecturalLayerKind.CommonUtilities => "Common / Utilities",
        _ => "General"
    };

    public string ColorHex => Layer switch
    {
        ArchitecturalLayerKind.UserInterface => "#3B82F6",    // Blu
        ArchitecturalLayerKind.BusinessLogic => "#10B981",    // Smeraldo / Verde
        ArchitecturalLayerKind.DataAccess => "#F59E0B",       // Ambra
        ArchitecturalLayerKind.CommonUtilities => "#8B5CF6", // Viola
        _ => "#6B7280"
    };
}

/// <summary>
/// Flow connection between two nodes (source and destination) with intensity/volume.
/// </summary>
public sealed class FlowLink
{
    public Guid SourceId { get; init; }
    public Guid TargetId { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public string TargetName { get; init; } = string.Empty;
    public int Volume { get; set; }

    /// <summary>
    /// Indicates whether this flow represents an architectural layer violation (e.g. Data Access calling UI).
    /// </summary>
    public bool IsLayerViolation { get; set; }

    /// <summary>Source starting angle for the Chord tape.</summary>
    public double SourceStartAngle { get; set; }
    public double SourceEndAngle { get; set; }

    /// <summary>Target arrival angle for the Chord tape.</summary>
    public double TargetStartAngle { get; set; }
    public double TargetEndAngle { get; set; }
}

/// <summary>
/// Complete dataset for the Architectural Flow viewer (Chord / Sankey / Layered Flow).
/// </summary>
public sealed class ArchitecturalFlowDataset
{
    public List<FlowNode> Nodes { get; init; } = new();
    public List<FlowLink> Links { get; init; } = new();

    public int TotalNodesCount => Nodes.Count;
    public int TotalLinksCount => Links.Count;
    public int TotalFlowVolume => Links.Sum(l => l.Volume);
    public int LayerViolationsCount => Links.Count(l => l.IsLayerViolation);

    public FlowNode? FindNodeById(Guid id) => Nodes.FirstOrDefault(n => n.Id == id);
}
