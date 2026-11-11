using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ScreenFlow;

/// <summary>
/// Source language agnostic UI navigation graph builder.
/// Identifies all UI components (Forms, Windows, Dialogs, UserControls) and the transitions between them.
/// </summary>
public static class ScreenFlowBuilder
{
    /// <summary>
    /// Builds the complete navigation and screen flow graph (<see cref="ScreenFlowGraph"/>) by analyzing forms, visual controls and semantic transitions.
    /// </summary>
    /// <param name="graph">The semantic <see cref="CodeGraph"/> from which to extract the UI components and related interactions.</param>
    /// <returns>The navigation model ready for rendering with calculated spatial coordinates.</returns>
    /// <remarks>
    /// Construction occurs through:
    /// <list type="number">
    /// <item><description>Identification of graphical interface symbols (<see cref="SymbolKind.Form"/>, <see cref="SymbolKind.UserControl"/> or classes with conventional View/Dialog/Window suffixes).</description></item>
    /// <item><description>Extracting member controls (fields) and event handlers (methods) of each screen.</description></item>
    /// <item><description>Detection of semantic relationships between different forms (modal/modal opening calls or instantiations) to create transitions <see cref="ScreenFlowTransition"/>.</description></item>
    /// <item><description>Calculation of the layered layout (<see cref="LayoutScreenGraph"/>) starting from the startup screen (<c>StartupScreen</c>) or with in-degree 0.</description></item>
    /// </list>
    /// </remarks>
    public static ScreenFlowGraph Build(CodeGraph graph)
    {
        var result = new ScreenFlowGraph();
        var symbolMap = graph.Symbols.ToDictionary(s => s.Id);

        // ---------------------------------------------------------------------
        // STEP 1: Locating all user interface symbols.
        // We directly identify Form and UserControl. In case of absence,
        // we look for classes with conventional suffixes (Form, Window, Dialog, View).
        // ---------------------------------------------------------------------
        var uiSymbols = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Form or SymbolKind.UserControl)
            .ToList();

        if (uiSymbols.Count == 0)

            uiSymbols = graph.Symbols
                .Where(s => s.Kind == SymbolKind.Class && 
                           (s.Name.EndsWith("Form", StringComparison.OrdinalIgnoreCase) ||
                            s.Name.EndsWith("Window", StringComparison.OrdinalIgnoreCase) ||
                            s.Name.EndsWith("Dialog", StringComparison.OrdinalIgnoreCase) ||
                            s.Name.EndsWith("View", StringComparison.OrdinalIgnoreCase)))
                .ToList();

        // Map from the ID of the member symbol (UI control or procedure) to the ID of the containing form
        var memberToScreenMap = new Dictionary<Guid, Guid>();
        var screensMap = new Dictionary<Guid, ScreenFlowNode>();

        for (int i = 0; i < uiSymbols.Count; i++)
        {
            var sym = uiSymbols[i];
            var children = graph.GetChildren(sym.Id).ToList();

            // Instantiates the node representing the UI screen
            var node = new ScreenFlowNode
            {
                Id = sym.Id,
                Name = sym.Name,
                Title = sym.Name,
                Kind = sym.Kind,
                // Heuristics for identifying the home screen: first form encountered or typical name (frmMain, MainForm)
                IsStartupScreen = i == 0 || sym.Name.Equals("frmMain", StringComparison.OrdinalIgnoreCase) || sym.Name.Equals("MainForm", StringComparison.OrdinalIgnoreCase),
                ControlsCount = children.Count(child => child.Kind == SymbolKind.Field),
                EventHandlersCount = children.Count(child => child.Kind == SymbolKind.Method)
            };

            result.Screens.Add(node);
            screensMap[sym.Id] = node;
            memberToScreenMap[sym.Id] = sym.Id;

            // Bind all child controls and event handlers to the parent screen
            foreach (var child in children)
            {
                memberToScreenMap[child.Id] = sym.Id;
            }
        }

        // ---------------------------------------------------------------------
        // STEP 2: Detection of transitions between screens (calls or instantiations between different Forms).
        // ---------------------------------------------------------------------
        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind != RelationshipKind.Calls && 
                rel.Kind != RelationshipKind.Instantiates && 
                rel.Kind != RelationshipKind.HandlesEvent)

                continue;

            Guid srcScreenId = Guid.Empty;
            Guid tgtScreenId = Guid.Empty;

            if (memberToScreenMap.TryGetValue(rel.SourceId, out var sId)) srcScreenId = sId;
            if (memberToScreenMap.TryGetValue(rel.TargetId, out var tId)) tgtScreenId = tId;

            if (tgtScreenId == Guid.Empty && screensMap.ContainsKey(rel.TargetId))

                tgtScreenId = rel.TargetId;

            // If the source and destination belong to two distinct forms, we record the transition
            if (srcScreenId != Guid.Empty && tgtScreenId != Guid.Empty && srcScreenId != tgtScreenId)
            {
                symbolMap.TryGetValue(rel.SourceId, out var srcSym);
                symbolMap.TryGetValue(rel.TargetId, out var tgtSym);

                // Instantiates models the modeless/instantiated aperture; Calls models show modal or ShowDialog
                var kind = rel.Kind == RelationshipKind.Instantiates 
                    ? ScreenTransitionKind.OpenModeless 
                    : ScreenTransitionKind.OpenModal;

                bool exists = result.Transitions.Any(t => 
                    t.SourceScreenId == srcScreenId && 
                    t.TargetScreenId == tgtScreenId && 
                    t.TriggerRoutineName == (srcSym?.Name ?? ""));

                if (!exists)

                    result.Transitions.Add(new ScreenFlowTransition
                    {
                        SourceScreenId = srcScreenId,
                        TargetScreenId = tgtScreenId,
                        TriggerRoutineName = srcSym?.Name ?? "Handler",
                        // Estrae l'evento scatenante convenzionale (es. "cmdOpen_Click" -> "Click")
                        TriggerEventName = srcSym?.Name.Contains('_') == true ? srcSym.Name.Split('_').Last() : "Action",
                        TransitionKind = kind,
                        Description = $"{srcSym?.Name ?? "Event"} -> {tgtSym?.Name ?? "Screen"}"
                    });
            }
        }

        // ---------------------------------------------------------------------
        // STEP 3: Updating the incoming and outgoing transition counters for each screen.
        // ---------------------------------------------------------------------
        foreach (var t in result.Transitions)
        {
            if (screensMap.TryGetValue(t.SourceScreenId, out var srcNode)) srcNode.OutgoingTransitionsCount++;
            if (screensMap.TryGetValue(t.TargetScreenId, out var tgtNode)) tgtNode.IncomingTransitionsCount++;
        }

        // ---------------------------------------------------------------------
        // STEP 4: Calculation of the spatial layout at hierarchical levels.
        // ---------------------------------------------------------------------
        LayoutScreenGraph(result);

        return result;
    }

    /// <summary>
    /// Calculate the horizontal grid/level arrangement of UI screens on the 2D canvas.
    /// </summary>
    /// <param name="graph">The navigation graph to be arranged spatially.</param>
    /// <remarks>
    /// Arranges the main startup form (<c>StartupScreen</c>) or root screens at level 0 without input transitions,
    /// and propagates layers rightward via BFS scan along navigation transitions.
    /// </remarks>
    private static void LayoutScreenGraph(ScreenFlowGraph graph)
    {
        if (graph.Screens.Count == 0) return;

        // 1. Initialization: identifies the starting screens (Startup form or with 0 incoming callers)
        var levels = new Dictionary<int, List<ScreenFlowNode>>();
        var assigned = new HashSet<Guid>();

        var level0 = graph.Screens.Where(s => s.IsStartupScreen || s.IncomingTransitionsCount == 0).ToList();
        if (level0.Count == 0 && graph.Screens.Count > 0) level0.Add(graph.Screens[0]);

        levels[0] = level0;
        foreach (var s in level0) assigned.Add(s.Id);

        // 2. Propagation at horizontal levels (BFS along outgoing transitions)
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

            // Fallback for orphaned or disconnected screens
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

        // 3. Assignment of 2D coordinates in pixels (Columns per horizontal level, vertical rows)
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
