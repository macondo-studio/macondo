using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ArchitecturalFlow;

/// <summary>
/// Livello o strato architetturale inferito dal grafo o dai tipi di file.
/// </summary>
public enum ArchitecturalLayerKind
{
    UserInterface,   // Forms, UserControls, Views
    BusinessLogic,   // Classes, Services, Models, Modules
    DataAccess,      // Database access, Repositories, Helpers
    CommonUtilities  // Utility modules, Constants, Types, Enums
}

/// <summary>
/// Nodo rappresentante un componente, modulo o entità di raggruppamento nel diagramma dei flussi.
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

    /// <summary>Volume totale di chiamate/dipendenze in uscita.</summary>
    public int OutflowVolume { get; set; }

    /// <summary>Volume totale di chiamate/dipendenze in entrata.</summary>
    public int InflowVolume { get; set; }

    /// <summary>Volume complessivo (Inflow + Outflow).</summary>
    public int TotalVolume => InflowVolume + OutflowVolume;

    /// <summary>Angolo di partenza per il layout a diagramma Chord (in radianti).</summary>
    public double StartAngle { get; set; }

    /// <summary>Angolo di arrivo per il layout a diagramma Chord (in radianti).</summary>
    public double EndAngle { get; set; }

    /// <summary>Angolo centrale dell'arco.</summary>
    public double MidAngle => (StartAngle + EndAngle) / 2.0;

    /// <summary>Ampiezza angolare dell'arco (in radianti).</summary>
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
/// Collegamento di flusso tra due nodi (sorgente e destinazione) con intensità/volume.
/// </summary>
public sealed class FlowLink
{
    public Guid SourceId { get; init; }
    public Guid TargetId { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public string TargetName { get; init; } = string.Empty;
    public int Volume { get; set; }

    /// <summary>
    /// Indica se questo flusso rappresenta una violazione di layer architetturale (es. Data Access che chiama UI).
    /// </summary>
    public bool IsLayerViolation { get; set; }

    /// <summary>Angolo di partenza sorgente per il nastro Chord.</summary>
    public double SourceStartAngle { get; set; }
    public double SourceEndAngle { get; set; }

    /// <summary>Angolo di arrivo target per il nastro Chord.</summary>
    public double TargetStartAngle { get; set; }
    public double TargetEndAngle { get; set; }
}

/// <summary>
/// Dataset completo per il visualizzatore dei Flussi Architetturali (Chord / Sankey / Layered Flow).
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
