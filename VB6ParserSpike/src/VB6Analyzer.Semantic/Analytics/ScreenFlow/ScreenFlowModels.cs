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
    /// <summary>
    /// Costruisce il grafo completo di navigazione e flusso delle schermate (<see cref="ScreenFlowGraph"/>) analizzando form, controlli visivi e transizioni semantiche.
    /// </summary>
    /// <param name="graph">Il <see cref="CodeGraph"/> semantico da cui estrarre i componenti UI e le relative interazioni.</param>
    /// <returns>Il modello di navigazione pronto per il rendering con coordinate spaziali calcolate.</returns>
    /// <remarks>
    /// La costruzione avviene attraverso:
    /// <list type="number">
    ///   <item><description>Identificazione dei simboli di interfaccia grafica (<see cref="SymbolKind.Form"/>, <see cref="SymbolKind.UserControl"/> o classi con suffissi convenzionali View/Dialog/Window).</description></item>
    ///   <item><description>Estrazione di controlli membri (campi) e gestori di eventi (metodi) di ciascuna schermata.</description></item>
    ///   <item><description>Rilevamento delle relazioni semantiche tra diverse form (chiamate di apertura modale/non modale o istanziazioni) per creare le transizioni <see cref="ScreenFlowTransition"/>.</description></item>
    ///   <item><description>Calcolo del layout a livelli (<see cref="LayoutScreenGraph"/>) a partire dalla schermata di avvio (<c>StartupScreen</c>) o con in-degree 0.</description></item>
    /// </list>
    /// </remarks>
    public static ScreenFlowGraph Build(CodeGraph graph)
    {
        var result = new ScreenFlowGraph();
        var symbolMap = graph.Symbols.ToDictionary(s => s.Id);

        // ---------------------------------------------------------------------
        // PASSO 1: Individuazione di tutti i simboli di interfaccia utente.
        // Identifichiamo direttamente Form e UserControl. In caso di assenza,
        // cerchiamo classi con suffissi convenzionali (Form, Window, Dialog, View).
        // ---------------------------------------------------------------------
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

        // Mappa da ID del simbolo membro (controllo UI o routine) a ID della form contenitrice
        var memberToScreenMap = new Dictionary<Guid, Guid>();
        var screensMap = new Dictionary<Guid, ScreenFlowNode>();

        for (int i = 0; i < uiSymbols.Count; i++)
        {
            var sym = uiSymbols[i];
            var children = graph.GetChildren(sym.Id).ToList();

            // Istanzia il nodo rappresentante la schermata UI
            var node = new ScreenFlowNode
            {
                Id = sym.Id,
                Name = sym.Name,
                Title = sym.Name,
                Kind = sym.Kind,
                // Euristica per individuare la schermata iniziale: prima form incontrata o nome tipico (frmMain, MainForm)
                IsStartupScreen = i == 0 || sym.Name.Equals("frmMain", StringComparison.OrdinalIgnoreCase) || sym.Name.Equals("MainForm", StringComparison.OrdinalIgnoreCase),
                ControlsCount = children.Count(child => child.Kind == SymbolKind.Field),
                EventHandlersCount = children.Count(child => child.Kind == SymbolKind.Method)
            };

            result.Screens.Add(node);
            screensMap[sym.Id] = node;
            memberToScreenMap[sym.Id] = sym.Id;

            // Associa tutti i controlli ed event handler figli alla schermata padre
            foreach (var child in children)
            {
                memberToScreenMap[child.Id] = sym.Id;
            }
        }

        // ---------------------------------------------------------------------
        // PASSO 2: Rilevamento delle transizioni tra schermate (chiamate o istanziazioni tra Form differenti).
        // ---------------------------------------------------------------------
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

            // Se la sorgente e la destinazione appartengono a due form distinte, registriamo la transizione
            if (srcScreenId != Guid.Empty && tgtScreenId != Guid.Empty && srcScreenId != tgtScreenId)
            {
                symbolMap.TryGetValue(rel.SourceId, out var srcSym);
                symbolMap.TryGetValue(rel.TargetId, out var tgtSym);

                // Instantiates modella l'apertura non modale/istanza; Calls modella show modal o ShowDialog
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
                        // Estrae l'evento scatenante convenzionale (es. "cmdOpen_Click" -> "Click")
                        TriggerEventName = srcSym?.Name.Contains('_') == true ? srcSym.Name.Split('_').Last() : "Action",
                        TransitionKind = kind,
                        Description = $"{srcSym?.Name ?? "Event"} -> {tgtSym?.Name ?? "Screen"}"
                    });
                }
            }
        }

        // ---------------------------------------------------------------------
        // PASSO 3: Aggiornamento dei contatori di transizioni entranti e uscenti per ciascuna schermata.
        // ---------------------------------------------------------------------
        foreach (var t in result.Transitions)
        {
            if (screensMap.TryGetValue(t.SourceScreenId, out var srcNode)) srcNode.OutgoingTransitionsCount++;
            if (screensMap.TryGetValue(t.TargetScreenId, out var tgtNode)) tgtNode.IncomingTransitionsCount++;
        }

        // ---------------------------------------------------------------------
        // PASSO 4: Calcolo del layout spaziale a livelli gerarchici.
        // ---------------------------------------------------------------------
        LayoutScreenGraph(result);

        return result;
    }

    /// <summary>
    /// Calcola la disposizione a griglia/livelli orizzontali delle schermate UI sul canvas 2D.
    /// </summary>
    /// <param name="graph">Il grafo di navigazione da disporre spazialmente.</param>
    /// <remarks>
    /// Dispone al livello 0 la form principale di avvio (<c>StartupScreen</c>) o le schermate radice senza transizioni in ingresso,
    /// e propaga i livelli verso destra tramite scansione BFS lungo le transizioni di navigazione.
    /// </remarks>
    private static void LayoutScreenGraph(ScreenFlowGraph graph)
    {
        if (graph.Screens.Count == 0) return;

        // 1. Inizializzazione: identifica le schermate di partenza (Startup form o con 0 chiamanti in ingresso)
        var levels = new Dictionary<int, List<ScreenFlowNode>>();
        var assigned = new HashSet<Guid>();

        var level0 = graph.Screens.Where(s => s.IsStartupScreen || s.IncomingTransitionsCount == 0).ToList();
        if (level0.Count == 0 && graph.Screens.Count > 0) level0.Add(graph.Screens[0]);

        levels[0] = level0;
        foreach (var s in level0) assigned.Add(s.Id);

        // 2. Propagazione a livelli orizzontali (BFS lungo le transizioni uscenti)
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

            // Fallback per schermate orfane o disconnesse
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

        // 3. Assegnazione coordinate 2D in pixel (Colonne per livello orizzontale, righe verticali)
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
