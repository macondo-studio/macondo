using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ScreenFlow;

/// <summary>
/// Tipologia di transizione tra schermate / form / finestre UI.
/// </summary>
public enum ScreenTransitionKind
{
    /// <summary>Apertura standard / non modale (es. Form.Show).</summary>
    OpenModeless,

    /// <summary>Apertura modale / bloccante (es. Form.Show vbModal, ShowDialog).</summary>
    OpenModal,

    /// <summary>Chiusura o scaricamento (es. Unload Me, Form.Close).</summary>
    Close,

    /// <summary>Navigazione di pagina (Page navigation, Tab switch).</summary>
    Navigate,

    /// <summary>Inclusione o hosting di UserControl / Componente visivo.</summary>
    EmbedControl
}

/// <summary>
/// Nodo che rappresenta una Schermata, Form, Finestra o Dialogo UI.
/// </summary>
public sealed class ScreenFlowNode
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; } = SymbolKind.Form;
    public bool IsStartupScreen { get; set; }
    public int ControlsCount { get; set; }
    public int EventHandlersCount { get; set; }
    public int OutgoingTransitionsCount { get; set; }
    public int IncomingTransitionsCount { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 170.0;
    public double Height { get; set; } = 95.0;
}

/// <summary>
/// Transizione o collegamento orientato tra due schermate UI.
/// </summary>
public sealed class ScreenFlowTransition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SourceScreenId { get; init; }
    public Guid TargetScreenId { get; init; }
    public string TriggerRoutineName { get; init; } = string.Empty;
    public string TriggerEventName { get; init; } = string.Empty;
    public ScreenTransitionKind TransitionKind { get; init; } = ScreenTransitionKind.OpenModeless;
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Modello completo del grafo di navigazione UI.
/// </summary>
public sealed class ScreenFlowGraph
{
    public List<ScreenFlowNode> Screens { get; init; } = new();
    public List<ScreenFlowTransition> Transitions { get; init; } = new();

    public ScreenFlowNode? FindScreen(Guid id) => Screens.FirstOrDefault(s => s.Id == id);
}

/// <summary>
/// Costruttore del grafo di navigazione UI agnostico dal linguaggio sorgente.
/// Identifica tutti i componenti UI (Form, Finestre, Dialoghi, UserControls) e le transizioni tra essi.
/// </summary>
public static class ScreenFlowBuilder
{
    public static ScreenFlowGraph Build(CodeGraph graph)
    {
        var result = new ScreenFlowGraph();
        var symbolMap = graph.Symbols.ToDictionary(s => s.Id);

        // 1. Individua tutti i simboli UI
        var uiSymbols = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Form or SymbolKind.UserControl)
            .ToList();

        if (uiSymbols.Count == 0)
        {
            uiSymbols = graph.Symbols
                .Where(s => s.Kind == SymbolKind.Class && 
                           (s.Name.EndsWith("Form", StringComparison.OrdinalIgnoreCase) ||
                            s.Name.EndsWith("Window", StringComparison.OrdinalIgnoreCase) ||
                            s.Name.EndsWith("Dialog", StringComparison.OrdinalIgnoreCase) ||
                            s.Name.EndsWith("View", StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        // Mappa da id simbolo membro a id form contenitore
        var memberToScreenMap = new Dictionary<Guid, Guid>();
        var screensMap = new Dictionary<Guid, ScreenFlowNode>();

        for (int i = 0; i < uiSymbols.Count; i++)
        {
            var sym = uiSymbols[i];
            var children = graph.GetChildren(sym.Id).ToList();

            var node = new ScreenFlowNode
            {
                Id = sym.Id,
                Name = sym.Name,
                Title = sym.Name,
                Kind = sym.Kind,
                IsStartupScreen = i == 0 || sym.Name.Equals("frmMain", StringComparison.OrdinalIgnoreCase) || sym.Name.Equals("MainForm", StringComparison.OrdinalIgnoreCase),
                ControlsCount = children.Count(child => child.Kind == SymbolKind.Field),
                EventHandlersCount = children.Count(child => child.Kind == SymbolKind.Method)
            };

            result.Screens.Add(node);
            screensMap[sym.Id] = node;
            memberToScreenMap[sym.Id] = sym.Id;

            foreach (var child in children)
            {
                memberToScreenMap[child.Id] = sym.Id;
            }
        }

        // 2. Cerca transizioni (chiamate o istanziazioni tra Form diversi)
        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind != RelationshipKind.Calls && 
                rel.Kind != RelationshipKind.Instantiates && 
                rel.Kind != RelationshipKind.HandlesEvent)
            {
                continue;
            }

            Guid srcScreenId = Guid.Empty;
            Guid tgtScreenId = Guid.Empty;

            if (memberToScreenMap.TryGetValue(rel.SourceId, out var sId)) srcScreenId = sId;
            if (memberToScreenMap.TryGetValue(rel.TargetId, out var tId)) tgtScreenId = tId;

            if (tgtScreenId == Guid.Empty && screensMap.ContainsKey(rel.TargetId))
            {
                tgtScreenId = rel.TargetId;
            }

            if (srcScreenId != Guid.Empty && tgtScreenId != Guid.Empty && srcScreenId != tgtScreenId)
            {
                symbolMap.TryGetValue(rel.SourceId, out var srcSym);
                symbolMap.TryGetValue(rel.TargetId, out var tgtSym);

                var kind = rel.Kind == RelationshipKind.Instantiates 
                    ? ScreenTransitionKind.OpenModeless 
                    : ScreenTransitionKind.OpenModal;

                bool exists = result.Transitions.Any(t => 
                    t.SourceScreenId == srcScreenId && 
                    t.TargetScreenId == tgtScreenId && 
                    t.TriggerRoutineName == (srcSym?.Name ?? ""));

                if (!exists)
                {
                    result.Transitions.Add(new ScreenFlowTransition
                    {
                        SourceScreenId = srcScreenId,
                        TargetScreenId = tgtScreenId,
                        TriggerRoutineName = srcSym?.Name ?? "Handler",
                        TriggerEventName = srcSym?.Name.Contains('_') == true ? srcSym.Name.Split('_').Last() : "Action",
                        TransitionKind = kind,
                        Description = $"{srcSym?.Name ?? "Event"} -> {tgtSym?.Name ?? "Screen"}"
                    });
                }
            }
        }

        foreach (var t in result.Transitions)
        {
            if (screensMap.TryGetValue(t.SourceScreenId, out var srcNode)) srcNode.OutgoingTransitionsCount++;
            if (screensMap.TryGetValue(t.TargetScreenId, out var tgtNode)) tgtNode.IncomingTransitionsCount++;
        }

        LayoutScreenGraph(result);

        return result;
    }

    private static void LayoutScreenGraph(ScreenFlowGraph graph)
    {
        if (graph.Screens.Count == 0) return;

        var levels = new Dictionary<int, List<ScreenFlowNode>>();
        var assigned = new HashSet<Guid>();

        var level0 = graph.Screens.Where(s => s.IsStartupScreen || s.IncomingTransitionsCount == 0).ToList();
        if (level0.Count == 0 && graph.Screens.Count > 0) level0.Add(graph.Screens[0]);

        levels[0] = level0;
        foreach (var s in level0) assigned.Add(s.Id);

        int currentLevel = 0;
        while (assigned.Count < graph.Screens.Count && currentLevel < 10)
        {
            var nextLevel = new List<ScreenFlowNode>();
            var prevLevelIds = levels[currentLevel].Select(s => s.Id).ToHashSet();

            foreach (var t in graph.Transitions.Where(t => prevLevelIds.Contains(t.SourceScreenId)))
            {
                var target = graph.FindScreen(t.TargetScreenId);
                if (target != null && !assigned.Contains(target.Id))
                {
                    nextLevel.Add(target);
                    assigned.Add(target.Id);
                }
            }

            if (nextLevel.Count == 0)
            {
                var remaining = graph.Screens.Where(s => !assigned.Contains(s.Id)).Take(3).ToList();
                foreach (var r in remaining)
                {
                    nextLevel.Add(r);
                    assigned.Add(r.Id);
                }
            }

            currentLevel++;
            levels[currentLevel] = nextLevel;
        }

        double colWidth = 260.0;
        double rowHeight = 150.0;
        double startX = 60.0;
        double startY = 60.0;

        foreach (var (levelIdx, screenList) in levels)
        {
            for (int rowIdx = 0; rowIdx < screenList.Count; rowIdx++)
            {
                var screen = screenList[rowIdx];
                screen.X = startX + (levelIdx * colWidth);
                screen.Y = startY + (rowIdx * rowHeight);
            }
        }
    }
}
