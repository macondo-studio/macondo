namespace Macondo.App.Models;

/// <summary>
/// Serializable model for storing user settings and preferences.
/// Saved in %APPDATA%\Macondo\settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Tema grafico selezionato dall'utente.</summary>
    public AppTheme Theme { get; set; } = AppTheme.DarkModern;

    /// <summary>User interface language (Italian or English, automatically detected from the system language if not customized).</summary>
    public AppLanguage Language { get; set; } = Services.LocalizationService.DetectSystemLanguage();

    /// <summary>Project loading and analysis behavior when the application starts.</summary>
    public StartupLoadMode StartupMode { get; set; } = StartupLoadMode.OnlyIfCached;

    /// <summary>Path to the last successfully opened VB6 project folder.</summary>
    public string? LastOpenedFolder { get; set; }

    /// <summary>Default risk filter index for the Blast Radius grid (0: All, 1: Critical, 2: High, 3: Medium, 4: Low).</summary>
    public int DefaultRiskFilterIndex { get; set; } = 0;

    /// <summary>Preferred layout mode for the graph canvas.</summary>
    public Layout.GraphLayoutMode PreferredLayout { get; set; } = Layout.GraphLayoutMode.StandardRadial;

    /// <summary>Width of the window stored on closing.</summary>
    public double WindowWidth { get; set; } = 1400;

    /// <summary>Window height stored when closed.</summary>
    public double WindowHeight { get; set; } = 900;

    /// <summary>X coordinate of the window on the screen.</summary>
    public int? WindowX { get; set; }

    /// <summary>Y coordinate of the window on the screen.</summary>
    public int? WindowY { get; set; }

    /// <summary>Indicates whether the window was maximized when it was last closed.</summary>
    public bool IsMaximized { get; set; } = false;

    /// <summary>Width of the Project Explorer panel (left panel).</summary>
    public double ExplorerWidth { get; set; } = 280;

    /// <summary>Width of the Symbol Inspector panel (right panel).</summary>
    public double InspectorWidth { get; set; } = 360;

    /// <summary>Indicates whether the Project Explorer (left) panel is visible.</summary>
    public bool IsExplorerVisible { get; set; } = true;

    /// <summary>Indicates whether the Symbol Inspector (right) panel is visible.</summary>
    public bool IsInspectorVisible { get; set; } = true;

    /// <summary>Indicates whether to show the methods/procedures (Sub, Function) in the graph.</summary>
    public bool ShowMethods { get; set; } = true;

    /// <summary>Indicates whether to show the properties (Property Get/Let/Set) in the graph.</summary>
    public bool ShowProperties { get; set; } = true;

    /// <summary>Indicates whether to show the variables and fields in the graph.</summary>
    public bool ShowFields { get; set; } = true;

    /// <summary>Indicates whether to show constants in the graph.</summary>
    public bool ShowConstants { get; set; } = true;

    /// <summary>Indicates whether to show user-defined types and enumerations in the graph.</summary>
    public bool ShowTypesAndEnums { get; set; } = true;

    /// <summary>Indicates whether to enable automatic selection/inspection of nodes when the mouse cursor passes over (Hover).</summary>
    public bool EnableHoverSelection { get; set; } = true;

    /// <summary>Indicates whether to show the text names next to the icons in the central display tabs (Graph View, DSM, Treemap...).</summary>
    public bool ShowTabLabels { get; set; } = true;

    /// <summary>Maximum number of source lines displayed in the code preview (default: 500 lines).</summary>
    public int MaxCodePreviewLines { get; set; } = 500;

    /// <summary>Indicates whether to enable automatic background pre-processing of analytical cards when opening the project.</summary>
    public bool EnableBackgroundAnalytics { get; set; } = true;

    /// <summary>Indicates whether the Graph View tab is visible.</summary>
    public bool ShowTabGraph { get; set; } = true;

    /// <summary>Indicates whether the DSM Matrix tab is visible.</summary>
    public bool ShowTabDsm { get; set; } = true;

    /// <summary>Indicates whether the Metrics Treemap tab is visible.</summary>
    public bool ShowTabTreemap { get; set; } = true;

    /// <summary>Indicates whether the Risk Hotspot tab is visible.</summary>
    public bool ShowTabHotspot { get; set; } = true;

    /// <summary>Indicates whether the Screen Flow tab is visible.</summary>
    public bool ShowTabScreenFlow { get; set; } = true;

    /// <summary>Indicates whether the Control Flow (CFG) tab is visible.</summary>
    public bool ShowTabControlFlow { get; set; } = true;

    /// <summary>Indicates whether the Martin Metrics tab is visible.</summary>
    public bool ShowTabMartin { get; set; } = true;

    /// <summary>Indicates whether the Dead Code & Orphans tab is visible.</summary>
    public bool ShowTabDeadCode { get; set; } = true;

    /// <summary>Indicates whether the Shared Status tab is visible.</summary>
    public bool ShowTabSharedState { get; set; } = true;

    /// <summary>Indicates whether the Temporal Coupling tab is visible.</summary>
    public bool ShowTabTemporalCoupling { get; set; } = true;

    /// <summary>Indicates whether the Architectural Flows tab is visible.</summary>
    public bool ShowTabArchitecturalFlow { get; set; } = true;

    /// <summary>Indicates whether the CodeCity 3D tab is visible.</summary>
    public bool ShowTabCodeCity { get; set; } = true;

    /// <summary>Indicates whether the Blast Radius Grid tab is visible.</summary>
    public bool ShowTabBlastRadiusGrid { get; set; } = true;

    /// <summary>Indicates whether the Cohesion & LCOM tab is visible.</summary>
    public bool ShowTabCohesion { get; set; } = true;

    /// <summary>Indicates whether the Maintainability Index tab is visible.</summary>
    public bool ShowTabMaintainability { get; set; } = true;

    /// <summary>Indicates whether the Architectural Rules tab is visible.</summary>
    public bool ShowTabArchitecturalRules { get; set; } = true;

    /// <summary>Indicates whether the Duplications (CPD) tab is visible.</summary>
    public bool ShowTabCodeClone { get; set; } = true;

    /// <summary>Indicates whether the Social & Bus Factor tab is visible.</summary>
    public bool ShowTabSocialCode { get; set; } = true;

    /// <summary>Indicates whether the Data Flow & Taint tab is visible.</summary>
    public bool ShowTabDataFlow { get; set; } = true;

    /// <summary>Indicates whether the Diff & SemVer API tab is visible.</summary>
    public bool ShowTabApiDiff { get; set; } = true;

    /// <summary>Indicates whether the CRUD Matrix tab is visible.</summary>
    public bool ShowTabCrudMatrix { get; set; } = true;

    /// <summary>Indicates whether the Symbol Inspector tab in the right panel is visible.</summary>
    public bool ShowTabInspector { get; set; } = true;

    /// <summary>Indicates whether the Source Code Preview tab in the right panel is visible.</summary>
    public bool ShowTabSourceCode { get; set; } = true;

    /// <summary>List of the latest analyzed projects or folders (maximum 10 elements).</summary>
    public List<RecentProjectItem> RecentProjects { get; set; } = new();

    /// <summary>List of most recently launched or enabled analytics cards (maintained across projects).</summary>
    public List<string> LastEnabledAnalyticsTabs { get; set; } = new();

    /// <summary>
    /// Ordered sequence of names (x:Name) of the analytical tabs of the central panel (CentralTabControl).
    /// Stores the custom sorting configured by the user via drag & drop with the mouse.
    /// If there are no values or when default settings are restored, the native factory order is used.
    /// </summary>
    public List<string> AnalyticsTabOrder { get; set; } = new();
}

/// <summary>
/// Information about a previously analyzed project or folder stored in recent history.
/// </summary>
public sealed class RecentProjectItem
{
    public List<string> Paths { get; set; } = new();
    public string DisplayName { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public DateTime LastOpenedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// How to handle loading and parsing when the application starts.
/// </summary>
public enum StartupLoadMode
{
    /// <summary>Automatically restore the project only if there is already a snapshot in cache. Otherwise show the home screen.</summary>
    OnlyIfCached = 0,

    /// <summary>Always performs automatic analysis from disk of the last opened project even in the absence of cache.</summary>
    AlwaysAnalyze = 1,

    /// <summary>Does not automatically load any projects on startup and always shows the splash screen (empty workspace).</summary>
    EmptyWorkspace = 2
}
