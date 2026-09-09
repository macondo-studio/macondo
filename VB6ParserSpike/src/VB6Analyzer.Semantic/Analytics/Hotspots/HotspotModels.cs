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

/// <summary>
/// Analizzatore agnostico per la rilevazione di Hotspot di codice.
/// Integra metriche statiche di grafo (Fan-in/Fan-out) con eventuali dati di commit history (VCS Churn).
/// </summary>
public static class HotspotAnalyzer
{
    public static HotspotDataset Analyze(CodeGraph graph, IReadOnlyDictionary<string, int>? fileChurnHistory = null)
    {
        var items = new List<HotspotItem>();

        // Raccoglie tutte le routine e i container principali
        var routines = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Method or SymbolKind.Property)
            .ToList();

        // Mappa dei container padre
        var symbolContainers = new Dictionary<Guid, string>();
        foreach (var container in graph.Symbols.Where(s => s.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl))
        {
            foreach (var child in graph.GetChildren(container.Id))
            {
                symbolContainers[child.Id] = container.Name;
            }
        }

        var targets = routines.Count > 0 ? routines : graph.Symbols.Where(s => s.Kind != SymbolKind.Project).ToList();

        foreach (var sym in targets)
        {
            int callers = graph.Relationships.Count(r => r.TargetId == sym.Id && r.Kind == RelationshipKind.Calls);
            int callees = graph.Relationships.Count(r => r.SourceId == sym.Id && r.Kind == RelationshipKind.Calls);

            double complexity = 1.0 + (callees * 1.5) + (sym.Parameters.Count * 0.8);

            double churn = callers;
            if (fileChurnHistory != null && !string.IsNullOrEmpty(sym.FilePath) &&
                fileChurnHistory.TryGetValue(sym.FilePath, out int commits))
            {
                churn = (commits * 2.0) + callers;
            }

            double size = Math.Max(4.0, (sym.Parameters.Count * 3.0) + (callees * 4.0) + 10.0);

            symbolContainers.TryGetValue(sym.Id, out var containerName);
            containerName ??= sym.Name;

            items.Add(new HotspotItem
            {
                Id = sym.Id,
                Name = sym.Name,
                QualifiedName = sym.QualifiedName,
                ContainerName = containerName,
                Kind = sym.Kind,
                Complexity = complexity,
                ChurnOrFanIn = churn,
                SizeMetric = size,
                DirectCallersCount = callers,
                DirectCalleesCount = callees
            });
        }

        if (items.Count == 0)
        {
            return new HotspotDataset { ThresholdX = 5, ThresholdY = 5, MaxX = 10, MaxY = 10 };
        }

        double avgX = Math.Max(1.0, items.Average(i => i.ChurnOrFanIn));
        double avgY = Math.Max(1.0, items.Average(i => i.Complexity));
        double maxX = Math.Max(avgX * 2.0, items.Max(i => i.ChurnOrFanIn));
        double maxY = Math.Max(avgY * 2.0, items.Max(i => i.Complexity));

        foreach (var item in items)
        {
            double normX = Math.Min(1.0, item.ChurnOrFanIn / maxX);
            double normY = Math.Min(1.0, item.Complexity / maxY);
            item.RiskScore = Math.Round(((normX * 0.5) + (normY * 0.5)) * 100.0, 1);

            if (item.ChurnOrFanIn >= avgX && item.Complexity >= avgY)
            {
                item.Quadrant = HotspotQuadrant.DangerZone;
                item.RiskLevel = item.RiskScore >= 75 ? ImpactRiskLevel.Critical : ImpactRiskLevel.High;
            }
            else if (item.ChurnOrFanIn >= avgX && item.Complexity < avgY)
            {
                item.Quadrant = HotspotQuadrant.HighChurnLowComplexity;
                item.RiskLevel = ImpactRiskLevel.Medium;
            }
            else if (item.ChurnOrFanIn < avgX && item.Complexity >= avgY)
            {
                item.Quadrant = HotspotQuadrant.LowChurnHighComplexity;
                item.RiskLevel = ImpactRiskLevel.Medium;
            }
            else
            {
                item.Quadrant = HotspotQuadrant.Safe;
                item.RiskLevel = ImpactRiskLevel.Low;
            }
        }

        return new HotspotDataset
        {
            Items = items,
            ThresholdX = avgX,
            ThresholdY = avgY,
            MaxX = maxX,
            MaxY = maxY
        };
    }
}
