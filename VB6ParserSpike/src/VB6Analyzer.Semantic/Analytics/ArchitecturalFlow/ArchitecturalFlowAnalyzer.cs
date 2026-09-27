using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ArchitecturalFlow;

/// <summary>
/// Analizzatore agnostico dei flussi architetturali e calcolatore dei layout Chord / Sankey.
/// </summary>
public static class ArchitecturalFlowAnalyzer
{
    /// <summary>
    /// Esegue l'analisi completa dei flussi di invocazione e dipendenza tra layer architetturali,
    /// calcolando i volumi di scambio e gli angoli geometrici per il diagramma Chord.
    /// </summary>
    /// <param name="graph">Il <see cref="CodeGraph"/> semantico contenente simboli e relazioni.</param>
    /// <returns>Il dataset <see cref="ArchitecturalFlowDataset"/> con nodi, flussi, volumi e violazioni di layer.</returns>
    public static ArchitecturalFlowDataset Analyze(CodeGraph graph)
    {
        var dataset = new ArchitecturalFlowDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        // =========================================================================
        // PASSO 1: Identificazione dei contenitori di primo livello (Moduli, Classi, Form, ecc.)
        // =========================================================================
        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Module or SymbolKind.Form or SymbolKind.UserControl or SymbolKind.Interface or SymbolKind.Struct)
            .ToList();

        if (containers.Count == 0)
            return dataset;

        var containerById = containers.ToDictionary(c => c.Id);
        var symbolToContainerMap = new Dictionary<Guid, Guid>();

        // Mappa ogni simbolo o membro (metodi, proprietà, campi) al proprio contenitore di primo livello
        foreach (var sym in graph.Symbols)
        {
            if (containerById.ContainsKey(sym.Id))
            {
                symbolToContainerMap[sym.Id] = sym.Id;
            }
            else
            {
                var parentRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == sym.Id);
                if (parentRel != null && containerById.ContainsKey(parentRel.SourceId))
                {
                    symbolToContainerMap[sym.Id] = parentRel.SourceId;
                }
            }
        }

        // =========================================================================
        // PASSO 2: Creazione dei nodi di flusso (FlowNode) con inferenza del layer architetturale
        // e calcolo di metriche aggregate (LOC, complessità ciclomatica, membri).
        // =========================================================================
        var nodes = new List<FlowNode>();
        foreach (var c in containers)
        {
            var layer = InferLayer(c);
            var children = graph.GetChildren(c.Id).ToList();
            var loc = children.Sum(ch => Math.Max(1, ch.LineEnd - ch.LineStart + 1));
            if (loc <= 0) loc = Math.Max(10, c.LineEnd - c.LineStart + 1);

            // Stima della complessità ciclomatica media del contenitore
            var avgCc = children.Count > 0
                ? children.Average(ch => 1.0 + (graph.Relationships.Count(r => r.SourceId == ch.Id && r.Kind == RelationshipKind.Calls) * 1.5) + (ch.Parameters.Count * 0.8))
                : 1.0;

            nodes.Add(new FlowNode
            {
                Id = c.Id,
                Name = c.Name,
                QualifiedName = c.QualifiedName,
                Kind = c.Kind,
                Layer = layer,
                Loc = loc,
                MemberCount = children.Count,
                CyclomaticComplexity = avgCc
            });
        }

        var flowNodeById = nodes.ToDictionary(n => n.Id);

        // =========================================================================
        // PASSO 3: Aggregazione dei volumi di flusso tra coppie di container (Sorgente -> Destinazione)
        // Conteggia chiamate, istanziazioni, usi di tipo, ereditarietà e implementazioni.
        // =========================================================================
        var linkCounts = new Dictionary<(Guid Source, Guid Target), int>();

        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind is not (RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.References or RelationshipKind.Implements or RelationshipKind.Inherits))
                continue;

            if (symbolToContainerMap.TryGetValue(rel.SourceId, out var srcContId) &&
                symbolToContainerMap.TryGetValue(rel.TargetId, out var tgtContId))
            {
                // Esclude le chiamate interne allo stesso modulo per concentrarsi sugli scambi tra componenti
                if (srcContId != tgtContId && flowNodeById.ContainsKey(srcContId) && flowNodeById.ContainsKey(tgtContId))
                {
                    var key = (srcContId, tgtContId);
                    linkCounts[key] = linkCounts.GetValueOrDefault(key, 0) + 1;
                }
            }
        }

        var links = new List<FlowLink>();
        foreach (var (key, count) in linkCounts)
        {
            var srcNode = flowNodeById[key.Source];
            var tgtNode = flowNodeById[key.Target];

            // Aggiorna i totali dei flussi in entrata (inflow) e in uscita (outflow)
            srcNode.OutflowVolume += count;
            tgtNode.InflowVolume += count;

            // Rileva eventuali violazioni delle regole di layer (es. DataAccess che invoca UI)
            var isViolation = IsLayerViolation(srcNode.Layer, tgtNode.Layer);

            links.Add(new FlowLink
            {
                SourceId = key.Source,
                TargetId = key.Target,
                SourceName = srcNode.Name,
                TargetName = tgtNode.Name,
                Volume = count,
                IsLayerViolation = isViolation
            });
        }

        // =========================================================================
        // PASSO 4: Filtro e ordinamento dei nodi attivi per leggibilità del diagramma
        // =========================================================================
        var activeNodes = nodes
            .Where(n => n.TotalVolume > 0 || n.MemberCount > 0)
            .OrderBy(n => n.Layer)
            .ThenByDescending(n => n.TotalVolume)
            .ToList();

        // Se sono presenti troppi nodi, seleziona i 45 con volume maggiore per evitare sovrapposizioni visive
        if (activeNodes.Count > 45)
        {
            var topIds = activeNodes.Take(45).Select(n => n.Id).ToHashSet();
            activeNodes = activeNodes.Where(n => topIds.Contains(n.Id)).ToList();
            links = links.Where(l => topIds.Contains(l.SourceId) && topIds.Contains(l.TargetId)).ToList();
        }

        // =========================================================================
        // PASSO 5: Calcolo trigonometrico degli angoli per il Chord Diagram
        // =========================================================================
        CalculateChordAngles(activeNodes, links);

        return new ArchitecturalFlowDataset
        {
            Nodes = activeNodes,
            Links = links
        };
    }

    /// <summary>
    /// Calcola la suddivisione angolare della circonferenza (360 gradi / 2*PI radianti)
    /// per i nodi del Chord Diagram e i sotto-archi di innesto dei nastri di flusso.
    /// </summary>
    private static void CalculateChordAngles(List<FlowNode> nodes, List<FlowLink> links)
    {
        if (nodes.Count == 0) return;

        double totalVolume = nodes.Sum(n => Math.Max(n.TotalVolume, 1));
        double padAngle = 0.035; // Spazio in radianti tra gli archi dei nodi
        double totalPadding = padAngle * nodes.Count;
        double availableAngle = (2 * Math.PI) - totalPadding;

        // Se il padding eccede lo spazio disponibile sulla circonferenza, riducilo proporzionalmente
        if (availableAngle <= 0)
        {
            padAngle = 0.01;
            availableAngle = (2 * Math.PI) - (padAngle * nodes.Count);
        }

        // 1. Assegna l'arco circolare (StartAngle .. EndAngle) a ciascun nodo
        double currentAngle = 0;
        foreach (var node in nodes)
        {
            node.StartAngle = currentAngle;
            double nodeVolume = Math.Max(node.TotalVolume, 1);
            double sweep = (nodeVolume / totalVolume) * availableAngle;
            if (sweep < 0.02) sweep = 0.02; // Ampiezza minima per visibilità visiva

            node.EndAngle = currentAngle + sweep;
            currentAngle = node.EndAngle + padAngle;
        }

        // 2. Calcola i sotto-archi di ancoraggio dei nastri di connessione (Chords)
        var nodeOutOffset = nodes.ToDictionary(n => n.Id, n => n.StartAngle);
        var nodeInOffset = nodes.ToDictionary(n => n.Id, n => n.StartAngle);

        foreach (var link in links.OrderByDescending(l => l.Volume))
        {
            if (!nodeOutOffset.ContainsKey(link.SourceId) || !nodeInOffset.ContainsKey(link.TargetId))
                continue;

            var srcNode = nodes.First(n => n.Id == link.SourceId);
            var tgtNode = nodes.First(n => n.Id == link.TargetId);

            // Frazione di ampiezza angolare occupata da questo flusso specifico
            double srcFraction = srcNode.TotalVolume > 0 ? (double)link.Volume / srcNode.TotalVolume : 0.5;
            double tgtFraction = tgtNode.TotalVolume > 0 ? (double)link.Volume / tgtNode.TotalVolume : 0.5;

            double srcSweep = (srcNode.EndAngle - srcNode.StartAngle) * srcFraction;
            double tgtSweep = (tgtNode.EndAngle - tgtNode.StartAngle) * tgtFraction;

            link.SourceStartAngle = nodeOutOffset[link.SourceId];
            link.SourceEndAngle = link.SourceStartAngle + srcSweep;
            nodeOutOffset[link.SourceId] = Math.Min(link.SourceEndAngle, srcNode.EndAngle);

            link.TargetStartAngle = nodeInOffset[link.TargetId];
            link.TargetEndAngle = link.TargetStartAngle + tgtSweep;
            nodeInOffset[link.TargetId] = Math.Min(link.TargetEndAngle, tgtNode.EndAngle);
        }
    }

    /// <summary>
    /// Deduce il layer architetturale di appartenenza di un simbolo tramite euristica
    /// sul tipo di simbolo, convenzioni di prefisso/suffisso nel nome e percorso file.
    /// </summary>
    private static ArchitecturalLayerKind InferLayer(Symbol s)
    {
        // 1. Controlli ed elementi di interfaccia grafica
        if (s.Kind is SymbolKind.Form or SymbolKind.UserControl)
            return ArchitecturalLayerKind.UserInterface;

        var name = s.Name.ToLowerInvariant();
        var qname = s.QualifiedName.ToLowerInvariant();

        if (name.StartsWith("frm") || name.StartsWith("ctl") || name.EndsWith("view") || name.EndsWith("window") || name.EndsWith("form") || qname.Contains("ui") || qname.Contains("views"))
            return ArchitecturalLayerKind.UserInterface;

        // 2. Componenti di accesso ai dati (DAO, Repository, DbContext)
        if (name.Contains("db") || name.Contains("dao") || name.Contains("repo") || name.Contains("sql") || name.Contains("data") || name.Contains("entity") || qname.Contains("data"))
            return ArchitecturalLayerKind.DataAccess;

        // 3. Moduli di utilità condivisa, costanti e tipi comuni
        if (name.Contains("util") || name.Contains("helper") || name.Contains("const") || name.Contains("common") || name.Contains("global") || s.Kind is SymbolKind.Type or SymbolKind.Enum)
            return ArchitecturalLayerKind.CommonUtilities;

        // 4. Default: Logica di business / Servizi di dominio
        return ArchitecturalLayerKind.BusinessLogic;
    }

    /// <summary>
    /// Valuta se una dipendenza tra due layer viola i principi di Clean / N-Tier Architecture:
    /// - DataAccess non deve invocare UserInterface né BusinessLogic.
    /// - CommonUtilities non deve dipendere da layer specifici superiori.
    /// - BusinessLogic non deve invocare direttamente la UserInterface.
    /// </summary>
    private static bool IsLayerViolation(ArchitecturalLayerKind source, ArchitecturalLayerKind target)
    {
        return (source == ArchitecturalLayerKind.DataAccess && target == ArchitecturalLayerKind.UserInterface) ||
               (source == ArchitecturalLayerKind.DataAccess && target == ArchitecturalLayerKind.BusinessLogic) ||
               (source == ArchitecturalLayerKind.CommonUtilities && target != ArchitecturalLayerKind.CommonUtilities) ||
               (source == ArchitecturalLayerKind.BusinessLogic && target == ArchitecturalLayerKind.UserInterface);
    }
}
