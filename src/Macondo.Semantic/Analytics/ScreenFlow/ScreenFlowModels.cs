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
