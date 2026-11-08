using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Hotspots;

/// <summary>
/// Quadrante concettuale della matrice di rischio (metodologia Adam Tornhill / CodeScene).
/// </summary>
public enum HotspotQuadrant
{
    /// <summary>Basso rischio: Basso Churn + Bassa Complessità (Stabile e semplice).</summary>
    Safe,

    /// <summary>Rischio Moderato: Alto Churn + Bassa Complessità (Modificato spesso ma semplice).</summary>
    HighChurnLowComplexity,

    /// <summary>Rischio Latente: Basso Churn + Alta Complessità (Complesso ma raramente toccato).</summary>
    LowChurnHighComplexity,

    /// <summary>HOTSPOT CRITICO: Alto Churn + Alta Complessità (Zona ad altissima probabilità di difetti).</summary>
    DangerZone
}

/// <summary>
/// Singolo elemento analizzato nella matrice Hotspot (Metodo, Procedura, Modulo o Classe).
/// </summary>
public sealed class HotspotItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }

    /// <summary>Asse Y: Punteggio di complessità ciclomatica o strutturale (1 .. N).</summary>
    public double Complexity { get; set; }

    /// <summary>Asse X: Frequenza di modifica da VCS (Churn) o Fan-In di dipendenza (quanti chiamanti/utilizzatori).</summary>
    public double ChurnOrFanIn { get; set; }

    /// <summary>Raggio del punto/bolla: Dimensione del codice (LOC / Istruzioni / Parametri).</summary>
    public double SizeMetric { get; set; }

    /// <summary>Punteggio unificato di rischio normalizzato (0 .. 100).</summary>
    public double RiskScore { get; set; }

    /// <summary>Livello di rischio categorizzato.</summary>
    public ImpactRiskLevel RiskLevel { get; set; }

    /// <summary>Quadrante assegnato in base alla media della codebase.</summary>
    public HotspotQuadrant Quadrant { get; set; }

    public int DirectCallersCount { get; set; }
    public int DirectCalleesCount { get; set; }
}

/// <summary>
/// Dataset completo della matrice Hotspots per un intero progetto.
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
