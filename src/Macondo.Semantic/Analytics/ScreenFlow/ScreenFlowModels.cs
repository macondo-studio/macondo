using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ScreenFlow;

/// <summary>
/// Type of transition between screens / forms / UI windows.
/// </summary>
public enum ScreenTransitionKind
{
    /// <summary>Standard opening / non-modal (e.g. Form.Show).</summary>
    OpenModeless,

    /// <summary>Modal opening / blocking (e.g. Form.Show vbModal, ShowDialog).</summary>
    OpenModal,

    /// <summary>Close or download (e.g. Unload Me, Form.Close).</summary>
    Close,

    /// <summary>Page navigation (Tab switch).</summary>
    Navigate,

    /// <summary>Including or hosting UserControl / Visual Component.</summary>
    EmbedControl
}

/// <summary>
/// Node representing a Screen, Form, Window, or Dialog UI.
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
/// Transition or oriented link between two UI screens.
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
/// Complete UI navigation graph template.
/// </summary>
public sealed class ScreenFlowGraph
{
    public List<ScreenFlowNode> Screens { get; init; } = new();
    public List<ScreenFlowTransition> Transitions { get; init; } = new();

    public ScreenFlowNode? FindScreen(Guid id) => Screens.FirstOrDefault(s => s.Id == id);
}
