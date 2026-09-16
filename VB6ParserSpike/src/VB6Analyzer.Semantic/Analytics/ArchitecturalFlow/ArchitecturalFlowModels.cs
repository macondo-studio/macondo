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

/// <summary>
/// Analizzatore agnostico dei flussi architetturali e calcolatore dei layout Chord / Sankey.
/// </summary>
public static class ArchitecturalFlowAnalyzer
{
    public static ArchitecturalFlowDataset Analyze(CodeGraph graph)
    {
        var dataset = new ArchitecturalFlowDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        // 1. Identifica i contenitori principali (Classi, Moduli, Form, UserControls)
        var containers = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Module or SymbolKind.Form or SymbolKind.UserControl or SymbolKind.Interface or SymbolKind.Struct)
            .ToList();

        if (containers.Count == 0)
            return dataset;

        var containerById = containers.ToDictionary(c => c.Id);
        var symbolToContainerMap = new Dictionary<Guid, Guid>();

        // Mappa ogni simbolo al suo container genitore
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

        // 2. Costruisci i FlowNode
        var nodes = new List<FlowNode>();
        foreach (var c in containers)
        {
            var layer = InferLayer(c);
            var children = graph.GetChildren(c.Id).ToList();
            var loc = children.Sum(ch => Math.Max(1, ch.LineEnd - ch.LineStart + 1));
            if (loc <= 0) loc = Math.Max(10, c.LineEnd - c.LineStart + 1);

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

        // 3. Calcola i volumi di flusso tra container
        var linkCounts = new Dictionary<(Guid Source, Guid Target), int>();

        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind is not (RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.References or RelationshipKind.Implements or RelationshipKind.Inherits))
                continue;

            if (symbolToContainerMap.TryGetValue(rel.SourceId, out var srcContId) &&
                symbolToContainerMap.TryGetValue(rel.TargetId, out var tgtContId))
            {
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

            srcNode.OutflowVolume += count;
            tgtNode.InflowVolume += count;

            // Rileva violazioni di layer (es. DataAccess che chiama UI, o Common che chiama Business/UI)
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

        // Filtra nodi attivi (con volume o LOC significativi)
        var activeNodes = nodes
            .Where(n => n.TotalVolume > 0 || n.MemberCount > 0)
            .OrderBy(n => n.Layer)
            .ThenByDescending(n => n.TotalVolume)
            .ToList();

        // Se sono troppi, mantieni i primi 45 più significativi per leggibilità del Chord Diagram
        if (activeNodes.Count > 45)
        {
            var topIds = activeNodes.Take(45).Select(n => n.Id).ToHashSet();
            activeNodes = activeNodes.Where(n => topIds.Contains(n.Id)).ToList();
            links = links.Where(l => topIds.Contains(l.SourceId) && topIds.Contains(l.TargetId)).ToList();
        }

        // 4. Calcolo angoli geometrici per Chord Diagram (circonferenza a 360 gradi con padding)
        CalculateChordAngles(activeNodes, links);

        return new ArchitecturalFlowDataset
        {
            Nodes = activeNodes,
            Links = links
        };
    }

    private static void CalculateChordAngles(List<FlowNode> nodes, List<FlowLink> links)
    {
        if (nodes.Count == 0) return;

        double totalVolume = nodes.Sum(n => Math.Max(n.TotalVolume, 1));
        double padAngle = 0.035; // Spazio in radianti tra gli archi dei nodi
        double totalPadding = padAngle * nodes.Count;
        double availableAngle = (2 * Math.PI) - totalPadding;

        if (availableAngle <= 0)
        {
            padAngle = 0.01;
            availableAngle = (2 * Math.PI) - (padAngle * nodes.Count);
        }

        double currentAngle = 0;
        foreach (var node in nodes)
        {
            node.StartAngle = currentAngle;
            double nodeVolume = Math.Max(node.TotalVolume, 1);
            double sweep = (nodeVolume / totalVolume) * availableAngle;
            if (sweep < 0.02) sweep = 0.02; // Ampiezza minima visibile

            node.EndAngle = currentAngle + sweep;
            currentAngle = node.EndAngle + padAngle;
        }

        // Calcola i sotto-archi dei nastri di connessione
        var nodeOutOffset = nodes.ToDictionary(n => n.Id, n => n.StartAngle);
        var nodeInOffset = nodes.ToDictionary(n => n.Id, n => n.StartAngle);

        foreach (var link in links.OrderByDescending(l => l.Volume))
        {
            if (!nodeOutOffset.ContainsKey(link.SourceId) || !nodeInOffset.ContainsKey(link.TargetId))
                continue;

            var srcNode = nodes.First(n => n.Id == link.SourceId);
            var tgtNode = nodes.First(n => n.Id == link.TargetId);

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

    private static ArchitecturalLayerKind InferLayer(Symbol s)
    {
        if (s.Kind is SymbolKind.Form or SymbolKind.UserControl)
            return ArchitecturalLayerKind.UserInterface;

        var name = s.Name.ToLowerInvariant();
        var qname = s.QualifiedName.ToLowerInvariant();

        if (name.StartsWith("frm") || name.StartsWith("ctl") || name.EndsWith("view") || name.EndsWith("window") || name.EndsWith("form") || qname.Contains("ui") || qname.Contains("views"))
            return ArchitecturalLayerKind.UserInterface;

        if (name.Contains("db") || name.Contains("dao") || name.Contains("repo") || name.Contains("sql") || name.Contains("data") || name.Contains("entity") || qname.Contains("data"))
            return ArchitecturalLayerKind.DataAccess;

        if (name.Contains("util") || name.Contains("helper") || name.Contains("const") || name.Contains("common") || name.Contains("global") || s.Kind is SymbolKind.Type or SymbolKind.Enum)
            return ArchitecturalLayerKind.CommonUtilities;

        return ArchitecturalLayerKind.BusinessLogic;
    }

    private static bool IsLayerViolation(ArchitecturalLayerKind source, ArchitecturalLayerKind target)
    {
        // Violazioni classiche nei layer:
        // - DataAccess non deve dipendere da UI o Business
        // - CommonUtilities non deve dipendere da nessun layer superiore (UI, Business, DataAccess)
        // - BusinessLogic non deve dipendere da UI
        return (source == ArchitecturalLayerKind.DataAccess && target == ArchitecturalLayerKind.UserInterface) ||
               (source == ArchitecturalLayerKind.DataAccess && target == ArchitecturalLayerKind.BusinessLogic) ||
               (source == ArchitecturalLayerKind.CommonUtilities && target != ArchitecturalLayerKind.CommonUtilities) ||
               (source == ArchitecturalLayerKind.BusinessLogic && target == ArchitecturalLayerKind.UserInterface);
    }
}
