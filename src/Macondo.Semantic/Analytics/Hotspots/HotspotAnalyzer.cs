using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.Hotspots;

/// <summary>
/// Analizzatore agnostico per la rilevazione di Hotspot di codice (metodologia CodeScene / Adam Tornhill).
/// Integra metriche statiche di grafo (Fan-in/Fan-out) con eventuali dati di commit history (VCS Churn).
/// </summary>
public static class HotspotAnalyzer
{
    /// <summary>
    /// Esegue l'analisi degli hotspot di rischio combinando la complessità strutturale con la frequenza di modifica (Churn) o di utilizzo (Fan-In).
    /// </summary>
    /// <param name="graph">Il <see cref="CodeGraph"/> semantico contenente simboli e relazioni.</param>
    /// <param name="fileChurnHistory">Dizionario opzionale con il conteggio dei commit VCS per ciascun file sorgente.</param>
    /// <returns>Il dataset <see cref="HotspotDataset"/> contenente tutti i simboli mappati sui 4 quadranti cartesiani di rischio.</returns>
    /// <remarks>
    /// L'algoritmo opera secondo le seguenti logiche:
    /// <list type="number">
    ///   <item><description><b>Complessità (Asse Y)</b>: calcolata combinando il Fan-out delle chiamate uscenti e il numero di parametri formali: <c>Complexity = 1.0 + (callees * 1.5) + (params * 0.8)</c>.</description></item>
    ///   <item><description><b>Churn / Fan-In (Asse X)</b>: calcolato sul numero di chiamanti diretti e pesato con lo storico dei commit del file (se presente nei log VCS): <c>Churn = (commits * 2.0) + callers</c>.</description></item>
    ///   <item><description><b>Dimensione (Raggio)</b>: metrica volumetrica stimata da parametri e chiamate.</description></item>
    ///   <item><description><b>Partizionamento nei 4 Quadranti</b>: le soglie cartesiane <c>ThresholdX</c> e <c>ThresholdY</c> vengono determinate dalla media della codebase; gli elementi sopra la media in entrambi gli assi appartengono alla <b>DangerZone</b>.</description></item>
    ///   <item><description><b>Punteggio di Rischio Normalizzato (0-100)</b>: media pesata tra Churn normalizzato e Complessità normalizzata rispetto ai massimi della codebase.</description></item>
    /// </list>
    /// </remarks>
    public static HotspotDataset Analyze(CodeGraph graph, IReadOnlyDictionary<string, int>? fileChurnHistory = null)
    {
        var items = new List<HotspotItem>();

        // ---------------------------------------------------------------------
        // PASSO 1: Selezione dei simboli target per l'analisi Hotspot.
        // Diamo priorità a metodi e proprietà (procedure/funzioni). Se non ce ne
        // sono (es. codebase di soli tipi o moduli vuoti), analizziamo i moduli stessi.
        // ---------------------------------------------------------------------
        var routines = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Method or SymbolKind.Property)
            .ToList();

        // ---------------------------------------------------------------------
        // PASSO 2: Mappatura gerarchica rapida Container -> Figli.
        // Permette di associare istantaneamente ogni procedura al suo file/modulo
        // padre (Classe, Form, Modulo standard, UserControl).
        // ---------------------------------------------------------------------
        var symbolContainers = new Dictionary<Guid, string>();
        foreach (var container in graph.Symbols.Where(s => s.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl))
        {
            foreach (var child in graph.GetChildren(container.Id))
            {
                symbolContainers[child.Id] = container.Name;
            }
        }

        var targets = routines.Count > 0 ? routines : graph.Symbols.Where(s => s.Kind != SymbolKind.Project).ToList();

        // ---------------------------------------------------------------------
        // PASSO 3: Calcolo delle metriche di complessità, accoppiamento e churn per ciascun simbolo.
        // ---------------------------------------------------------------------
        foreach (var sym in targets)
        {
            // Fan-In: quanti altri metodi o procedure chiamano direttamente questo simbolo
            int callers = graph.Relationships.Count(r => r.TargetId == sym.Id && r.Kind == RelationshipKind.Calls);
            // Fan-Out: quante altre procedure vengono invocate dall'interno di questo simbolo
            int callees = graph.Relationships.Count(r => r.SourceId == sym.Id && r.Kind == RelationshipKind.Calls);

            // Complessità strutturale stimata (Asse Y):
            // Base = 1.0; ogni chiamata uscente (callee) aumenta la complessità di 1.5 punti;
            // ogni parametro formale nella firma aumenta l'impegno cognitivo e il rischio di 0.8 punti.
            double complexity = 1.0 + (callees * 1.5) + (sym.Parameters.Count * 0.8);

            // Churn / Frequenza di modifica o impatto (Asse X):
            // Di default coincide con il Fan-In (callers). Se sono disponibili dati storici VCS (Git/SVN),
            // il numero di commit che hanno toccato il file sorgente viene pesato con fattore 2.0.
            double churn = callers;
            if (fileChurnHistory != null && !string.IsNullOrEmpty(sym.FilePath) &&
                fileChurnHistory.TryGetValue(sym.FilePath, out int commits))

                churn = (commits * 2.0) + callers;

            // Dimensione volumetrica dell'elemento (usata per il raggio visivo della bolla nel chart)
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

        // Se non ci sono elementi da analizzare, restituisce un dataset vuoto con valori di default
        if (items.Count == 0)

            return new HotspotDataset { ThresholdX = 5, ThresholdY = 5, MaxX = 10, MaxY = 10 };

        // ---------------------------------------------------------------------
        // PASSO 4: Calcolo delle medie statistiche e dei massimi di scala della codebase.
        // Le medie fungono da soglie di partizione per i 4 quadranti cartesiani.
        // ---------------------------------------------------------------------
        double avgX = Math.Max(1.0, items.Average(i => i.ChurnOrFanIn));
        double avgY = Math.Max(1.0, items.Average(i => i.Complexity));
        double maxX = Math.Max(avgX * 2.0, items.Max(i => i.ChurnOrFanIn));
        double maxY = Math.Max(avgY * 2.0, items.Max(i => i.Complexity));

        // ---------------------------------------------------------------------
        // PASSO 5: Normalizzazione del punteggio di rischio (0-100) e assegnazione del quadrante.
        // ---------------------------------------------------------------------
        foreach (var item in items)
        {
            // Normalizzazione lineare tra 0.0 e 1.0 rispetto ai massimi dell'intero progetto
            double normX = Math.Min(1.0, item.ChurnOrFanIn / maxX);
            double normY = Math.Min(1.0, item.Complexity / maxY);
            
            // Score di rischio normalizzato combinato (50% Churn/Fan-In, 50% Complessità)
            item.RiskScore = Math.Round(((normX * 0.5) + (normY * 0.5)) * 100.0, 1);

            // Classificazione nei 4 quadranti in base alle soglie medie della codebase:
            // 1. DANGER ZONE (Alto Churn, Alta Complessità): codice critico, modificato spesso e complesso.
            // 2. High Churn / Low Complexity: codice modificato frequentemente ma semplice (es. costanti, mapping).
            // 3. Low Churn / High Complexity: codice complesso ma stabile (es. algoritmi core raramente toccati).
            // 4. SAFE (Basso Churn, Bassa Complessità): codice semplice e raramente modificato.
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
