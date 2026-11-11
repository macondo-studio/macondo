using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Macondo.App.Controls;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.App.Views;
using Macondo.Semantic.Analytics.ArchitecturalFlow;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Analytics.CodeCity;
using Macondo.Semantic.Analytics.CodeClones;
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Analytics.ControlFlow;
using Macondo.Semantic.Analytics.DataFlow;
using Macondo.Semantic.Analytics.DataMatrix;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.Hotspots;
using Macondo.Semantic.Analytics.Maintainability;
using Macondo.Semantic.Analytics.Martin;
using Macondo.Semantic.Analytics.ScreenFlow;
using Macondo.Semantic.Analytics.SharedState;
using Macondo.Semantic.Analytics.SocialCode;
using Macondo.Semantic.Analytics.TemporalCoupling;
using Macondo.Semantic.Analytics.Treemap;
using Macondo.Semantic.Export;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;

namespace Macondo.App;

/// <summary>
/// Main window of the VB6 Analyzer application.
/// Manages the interaction between Toolbar, Project Explorer (TreeView),
/// Graph Canvas (<see cref="Controls.GraphControl"/>), Symbol Inspector and Blast Radius Engine.
/// </summary>
public partial class MainWindow : Window
{
    #region Fields and State

    private VisualCodeGraph? _graph;
    private GraphNode? _selectedNode;
    private CancellationTokenSource? _selectionCts;
    private List<BlastRadiusGridRow> _allBlastRadiusRows = new();
    private bool _isSyncingSelection;
    private string? _currentFolderPath;
    private List<string> _currentPaths = new();
    private Layout.GraphLayoutMode _currentLayoutMode = Layout.GraphLayoutMode.StandardRadial;
    private List<VcsBranchInfo> _allVcsBranches = new();
    private AppUpdateInfo? _availableUpdate;
    private double _savedExplorerWidth = 280;
    private double _savedInspectorWidth = 360;
    private bool _isExplorerVisible = true;
    private bool _isInspectorVisible = true;
    private bool _isSyncingFilterUi;
    private CancellationTokenSource? _loadingCts;
    private CodeGraph? _apiDiffBaselineGraph;
    private string? _apiDiffBaselineName;
    // --- Drag & Drop Management and Analysis Card Sorting (CentralTabControl) ---
    /// <summary>Immutable ordered list of TabItems in the original default order defined in the XAML file.</summary>
    private readonly List<TabItem> _defaultCentralTabOrder = new();

    /// <summary>Reference to the TabItem currently grabbed and dragged by the user.</summary>
    private TabItem? _draggedTabItem;

    /// <summary>Coordinates relative to the window at the time of the initial PointerPressed.</summary>
    private Point _tabDragStartPos;

    /// <summary>Relative offset between the mouse click point and the upper left corner of the dragged tab header.</summary>
    private Point _tabDragOffsetWithinTab;

    /// <summary>Boolean flag indicating whether an active drag operation is currently in progress (deadzone exceeded).</summary>
    private bool _isTabDragging;

    /// <summary>Destination index calculated in the CentralTabControl where to insert the tab when the mouse is released.</summary>
    private int _targetDropIndex = -1;

    /// <summary>Translation transformation applied to the floating ghost card to follow its movement under the cursor.</summary>
    private readonly TranslateTransform _tabGhostTransform = new();

    /// <summary>Translation transformation applied to the vertical drop indicator to dynamically position it between tabs.</summary>
    private readonly TranslateTransform _tabDropIndicatorTransform = new();

    #endregion

    #region Constructor & Initialization

    public MainWindow()
    {
        InitializeComponent();

        TabDragGhostBorder.RenderTransform = _tabGhostTransform;
        TabDropIndicator.RenderTransform = _tabDropIndicatorTransform;

        ToggleExplorerTopButton.Click += (_, _) => ToggleExplorer();
        ToggleInspectorTopButton.Click += (_, _) => ToggleInspector();
        ToggleExplorerMenuItem.Command = new ActionCommand(() => ToggleExplorer());
        ToggleInspectorMenuItem.Command = new ActionCommand(() => ToggleInspector());
        ToggleExplorerMenuItem.Click += (_, _) => ToggleExplorer();
        ToggleInspectorMenuItem.Click += (_, _) => ToggleInspector();

        KeyDown += OnMainWindowKeyDown;

        // VCS (Git & SVN) Branch Button
        GitBranchButton.Click += (_, _) => OpenGitBranchPopup();

        // Graph Canvas: right-click context menu over nodes and movement tracking
        Graph.AddHandler(Control.ContextRequestedEvent, OnGraphContextRequested, RoutingStrategies.Tunnel);
        Graph.NodeMoved += (_, _) => OnGraphNodeMoved();

        // Analysis Tabs and Right Panel: right-click context menu to hide or manage tabs
        CentralTabControl.AddHandler(Control.ContextRequestedEvent, OnCentralTabControlContextRequested, RoutingStrategies.Tunnel);
        RightTabControl.AddHandler(Control.ContextRequestedEvent, OnRightTabControlContextRequested, RoutingStrategies.Tunnel);

        // Click on the close tab and Drag & Drop buttons to reorder the analysis tabs with the mouse.
        // CRITICAL NOTE: Handlers registered with RoutingStrategies.Tunnel on CentralTabControl intercept ALL
        // the pointer events of the entire central area (including analysis canvases, HUD buttons, controls and forms).
        // To avoid regressions where internal buttons (e.g. Blast Radius, Export MD) lose their click,
        // the underlying handlers (OnCentralTabPointerPressed/Released) must NEVER reset the pointer capture
        // nor handle events if the click does not specifically come from the header of a TabItem or if a drag is not in progress.
        RightTabControl.AddHandler(InputElement.PointerPressedEvent, OnRightTabPointerPressed, RoutingStrategies.Tunnel);
        CentralTabControl.AddHandler(InputElement.PointerPressedEvent, OnCentralTabPointerPressed, RoutingStrategies.Tunnel);
        CentralTabControl.AddHandler(InputElement.PointerMovedEvent, OnCentralTabPointerMoved, RoutingStrategies.Tunnel);
        CentralTabControl.AddHandler(InputElement.PointerReleasedEvent, OnCentralTabPointerReleased, RoutingStrategies.Tunnel);
        CentralTabControl.AddHandler(InputElement.PointerCaptureLostEvent, OnCentralTabPointerCaptureLost, RoutingStrategies.Tunnel);

        // Stores the original default sorting and applies custom sorting
        _defaultCentralTabOrder.AddRange(CentralTabControl.Items.OfType<TabItem>());
        ApplySavedTabOrder();

        // Upper Bar Menu - File (Command + Click for maximum reliability)
        OpenProjectFilesMenuItem.Command = new ActionCommand(async () => await OpenProjectFilesAsync());
        OpenFolderMenuItem.Command = new ActionCommand(async () => await OpenFolderAsync());
        RefreshProjectMenuItem.Command = new ActionCommand(async () => await RefreshCurrentProjectAsync());
#if DEBUG
        LoadFixturesMenuItem.IsVisible = true;
        GenerateFakeMenuItem.IsVisible = true;
        LoadFixturesMenuItem.Command = new ActionCommand(() => LoadDefaultFixtures());
        GenerateFakeMenuItem.Command = new ActionCommand(() => LoadSyntheticGraph());
#endif
        ExportMarkdownMenuItem.Command = new ActionCommand(async () => await ExportMarkdownAsync());
        ExportJsonMenuItem.Command = new ActionCommand(async () => await ExportJsonAsync());
        ImportJsonMenuItem.Command = new ActionCommand(async () => await ImportJsonAsync());
        ExportSliceMenuItem.Command = new ActionCommand(async () => await ExportLlmSliceAsync());
        SettingsMenuItem.Command = new ActionCommand(() => OpenSettings());
        CheckUpdatesMenuItem.Command = new ActionCommand(async () => await CheckForUpdatesManualAsync());
        ResetSettingsMenuItem.Command = new ActionCommand(ResetSettingsToDefaults);
        ClearCacheMenuItem.Command = new ActionCommand(ClearProjectCache);
        ExitMenuItem.Command = new ActionCommand(() => Close());

        OpenProjectFilesMenuItem.Click += async (_, _) => await OpenProjectFilesAsync();
        OpenFolderMenuItem.Click += async (_, _) => await OpenFolderAsync();

        // Drag & Drop files and folders directly across the entire window
        AddHandler(DragDrop.DropEvent, OnWindowDrop);
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);

        RefreshProjectMenuItem.Click += async (_, _) => await RefreshCurrentProjectAsync();
#if DEBUG
        LoadFixturesMenuItem.Click += (_, _) => LoadDefaultFixtures();
        GenerateFakeMenuItem.Click += (_, _) => LoadSyntheticGraph();
#endif
        ExportMarkdownMenuItem.Click += async (_, _) => await ExportMarkdownAsync();
        ExportJsonMenuItem.Click += async (_, _) => await ExportJsonAsync();
        ImportJsonMenuItem.Click += async (_, _) => await ImportJsonAsync();
        ExportSliceMenuItem.Click += async (_, _) => await ExportLlmSliceAsync();
        SettingsMenuItem.Click += (_, _) => OpenSettings();
        CheckUpdatesMenuItem.Click += async (_, _) => await CheckForUpdatesManualAsync();
        InfoMenuItem.Click += (_, _) => OpenInfo();
        ResetSettingsMenuItem.Click += (_, _) => ResetSettingsToDefaults();
        ClearCacheMenuItem.Click += (_, _) => ClearProjectCache();
        ExitMenuItem.Click += (_, _) => Close();

        // Menu Bar Superiore - Visualizza & Analisi
        ResetViewMenuItem.Command = new ActionCommand(() => ResetGraphView());
        ExpandAllMenuItem.Command = new ActionCommand(() => SetAllExpanded(true));
        CollapseAllMenuItem.Command = new ActionCommand(() => SetAllExpanded(false));
        LayoutStandardMenuItem.Command = new ActionCommand(() => RequestSetLayoutMode(Layout.GraphLayoutMode.StandardRadial));
        LayoutAffinityMenuItem.Command = new ActionCommand(() => RequestSetLayoutMode(Layout.GraphLayoutMode.AffinityRadial));
        LayoutIslandsMenuItem.Command = new ActionCommand(() => RequestSetLayoutMode(Layout.GraphLayoutMode.ClusteredIslands));
        LayoutForceMenuItem.Command = new ActionCommand(() => RequestSetLayoutMode(Layout.GraphLayoutMode.ForceDirected));
        BlastRadiusMenuItem.Command = new ActionCommand(() => OnBlastRadiusClicked());
        ShowCallersMenuItem.Command = new ActionCommand(() => OnShowCallersClicked());
        ShowCalleesMenuItem.Command = new ActionCommand(() => OnShowCalleesClicked());
        ToggleTabLabelsMenuItem.Command = new ActionCommand(() => ToggleTabLabels());
        ClearHighlightMenuItem.Command = new ActionCommand(() =>
        {
            Graph.ClearHighlightMode();
            ScreenFlow.ClearHighlight();
            StatusText.Text = LocalizationService.Instance["Menu_ClearHighlight"];
        });

        ResetViewMenuItem.Click += (_, _) => ResetGraphView();
        ExpandAllMenuItem.Click += (_, _) => SetAllExpanded(true);
        CollapseAllMenuItem.Click += (_, _) => SetAllExpanded(false);
        LayoutStandardMenuItem.Click += (_, _) => RequestSetLayoutMode(Layout.GraphLayoutMode.StandardRadial);
        LayoutAffinityMenuItem.Click += (_, _) => RequestSetLayoutMode(Layout.GraphLayoutMode.AffinityRadial);
        LayoutIslandsMenuItem.Click += (_, _) => RequestSetLayoutMode(Layout.GraphLayoutMode.ClusteredIslands);
        LayoutForceMenuItem.Click += (_, _) => RequestSetLayoutMode(Layout.GraphLayoutMode.ForceDirected);
        ToggleTabLabelsMenuItem.Click += (_, _) => ToggleTabLabels();
        BlastRadiusMenuItem.Click += (_, _) => OnBlastRadiusClicked();
        ShowCallersMenuItem.Click += (_, _) => OnShowCallersClicked();
        ShowCalleesMenuItem.Click += (_, _) => OnShowCalleesClicked();
        ClearHighlightMenuItem.Click += (_, _) =>
        {
            Graph.ClearHighlightMode();
            ScreenFlow.ClearHighlight();
            StatusText.Text = LocalizationService.Instance["Menu_ClearHighlight"];
        };
                
        // Toolbar Azioni Rapide
        ResetViewButton.Click += (_, _) => ResetGraphView();
        ExpandAllButton.Click += (_, _) => SetAllExpanded(true);
        CollapseAllButton.Click += (_, _) => SetAllExpanded(false);
        RefreshProjectButton.Click += async (_, _) => await RefreshCurrentProjectAsync();

        BlastRadiusButton.Click += (_, _) => OnBlastRadiusClicked();
        ShowCallersButton.Click += (_, _) => OnShowCallersClicked();
        ShowCalleesButton.Click += (_, _) => OnShowCalleesClicked();
        ClearHighlightButton.Click += (_, _) =>
        {
            Graph.ClearHighlightMode();
            ScreenFlow.ClearHighlight();
            StatusText.Text = LocalizationService.Instance["Menu_ClearHighlight"];
        };

        // Layout ComboBox Selection
        LayoutComboBox.SelectionChanged += OnLayoutComboBoxSelectionChanged;

        // Export & Import HUD buttons
        ExportMarkdownButton.Click += async (_, _) => await ExportMarkdownAsync();
        ExportJsonButton.Click += async (_, _) => await ExportJsonAsync();
        ImportJsonButton.Click += async (_, _) => await ImportJsonAsync();

        // Show/Hide Contents Button Graph Legend
        ToggleLegendButton.Click += (_, _) =>
        {
            LegendContentPanel.IsVisible = !LegendContentPanel.IsVisible;
            if (Application.Current != null)
            {
                var iconKey = LegendContentPanel.IsVisible ? "IconChevronLeft" : "IconChevronRight";
                if (Application.Current.TryFindResource(iconKey, out var res) && res is StreamGeometry geo)

                    LegendToggleIcon.Data = geo;
            }
        };

        // IDE Style Window Controls (Settings, Minimize, Maximize/Restore, Close)
        SettingsButton.Click += (_, _) => OpenSettings();
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => Close();

        PropertyChanged += (s, e) =>
        {
            if (e.Property == WindowStateProperty && MaximizeIcon != null)
            {
                var resourceKey = WindowState == WindowState.Maximized ? "IconWindowRestore" : "IconWindowMaximize";
                if (this.TryFindResource(resourceKey, out var res) && res is Geometry geo)

                    MaximizeIcon.Data = geo;
            }
        };

        SearchBox.GotFocus += (_, _) =>
        {
            var query = SearchBox.Text?.Trim();
            if (!string.IsNullOrEmpty(query) && SearchResultsList.ItemsSource is IList<SearchResultItem> { Count: > 0 })

                SearchResultsPopup.IsOpen = true;
        };

        // Global keyboard shortcuts (Ctrl+F, Ctrl+O, Ctrl+,,F5)
        AddHandler(KeyDownEvent, (sender, e) =>
        {
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            else if ((e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt)) && e.Key == Key.O)
            {
                _ = OpenFolderAsync();
                e.Handled = true;
            }
            else if ((e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)) && e.Key == Key.O)
            {
                _ = OpenProjectFilesAsync();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.O)
            {
                _ = OpenFolderAsync();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.R)
            {
                _ = RefreshCurrentProjectAsync();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && (e.Key == Key.OemComma || e.Key == Key.OemPeriod))
            {
                OpenSettings();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F1)
            {
                OpenInfo();
                e.Handled = true;
            }
            else if ((e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)) && e.Key == Key.Tab)
            {
                CycleCentralTab(forward: false);
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Tab)
            {
                CycleCentralTab(forward: true);
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F5)
            {
                OnBlastRadiusClicked();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        // Selection synchronization between Graph Tab and Grid Tab Blast Radius / Analytics
        CentralTabControl.SelectionChanged += OnCentralTabSelectionChanged;

        // Selection and synchronization events for new analytic views
        Dsm.ItemSelected += (_, item) =>
        {
            if (item != null && _graph != null)
            {
                var targetNode = _graph.GetNode(item.Id);
                if (targetNode != null) SelectAndFocusGraphNode(targetNode);
            }
        };

        Dsm.CellSelected += (_, cell) =>
        {
            if (cell != null && _graph != null && cell.HasDependency)
            {
                var rowNode = _graph.GetNode(cell.RowContainerId);
                if (rowNode != null) SelectAndFocusGraphNode(rowNode);
                StatusText.Text = LocalizationService.Instance.Get("Status_DsmDependency", cell.Weight);
            }
        };

        Treemap.ItemSelected += (_, item) =>
        {
            if (item != null && _graph != null)
            {
                var targetNode = _graph.GetNode(item.Id);
                if (targetNode != null) SelectAndFocusGraphNode(targetNode);
            }
        };

        Hotspot.ItemSelected += (_, item) =>
        {
            if (item != null && _graph != null)
            {
                var targetNode = _graph.GetNode(item.Id);
                if (targetNode != null) SelectAndFocusGraphNode(targetNode);
            }
        };

        ScreenFlow.ScreenSelected += (_, screen) =>
        {
            if (screen != null && _graph != null)
            {
                var targetNode = _graph.GetNode(screen.Id);
                if (targetNode != null) SelectAndFocusGraphNode(targetNode);
            }
        };

        ControlFlow.BlockSelected += (_, block) =>
        {
            if (block != null)

                StatusText.Text = LocalizationService.Instance.Get("Status_CfgBlock", block.BlockKind, block.Title);
        };

        MartinMetrics.ItemSelected += (_, item) =>
        {
            if (item != null && _graph != null)
            {
                var targetNode = _graph.GetNode(item.Id);
                if (targetNode != null) SelectAndFocusGraphNode(targetNode);
            }
        };

        DeadCode.ItemSelected += (_, item) =>
        {
            if (item != null && _graph != null)
            {
                var targetNode = _graph.GetNode(item.Id);
                if (targetNode != null) SelectAndFocusGraphNode(targetNode);
            }
        };

        DeadCode.ItemDoubleTapped += (_, item) =>
        {
            if (item != null)

                OpenOriginalFile(item.FilePath, item.LineStart);
        };

        SharedState.VariableSelected += (_, item) =>
        {
            if (item != null && _graph != null)
            {
                var targetNode = _graph.GetNode(item.VariableId);
                if (targetNode != null) SelectAndFocusGraphNode(targetNode);
            }
        };

        SharedState.AccessorDoubleTapped += (_, acc) =>
        {
            if (acc != null && !string.IsNullOrEmpty(acc.FilePath))

                OpenOriginalFile(acc.FilePath, acc.Line);
        };

        TemporalCoupling.PairDoubleTapped += (_, pair) =>
        {
            if (pair != null && !string.IsNullOrEmpty(pair.FileA))

                OpenOriginalFile(pair.FileA, 1);
        };

        ArchitecturalFlow.NodeSelected += (_, node) =>
        {
            if (node != null && _graph != null)
            {
                var targetNode = _graph.GetNode(node.Id);
                if (targetNode != null) SelectAndFocusGraphNode(targetNode);
            }
        };

        CodeCity.BuildingSelected += (_, b) =>
        {
            if (b != null && _graph != null)
            {
                var targetNode = _graph.GetNode(b.Id);
                if (targetNode != null) SelectAndFocusGraphNode(targetNode);
            }
        };

        CodeCity.BuildingDoubleTapped += (_, b) =>
        {
            if (b != null && !string.IsNullOrEmpty(b.FilePath))

                OpenOriginalFile(b.FilePath, 1);
        };

        // Baseline handling events for the Diff & SemVer API tab
        ApiDiff.SetCurrentAsBaselineRequested += (_, _) => OnApiDiffSetCurrentAsBaseline();
        ApiDiff.LoadBaselineRequested += async (_, _) => await OnApiDiffLoadBaselineAsync();
        ApiDiff.ClearBaselineRequested += (_, _) => OnApiDiffClearBaseline();

        // Initializing User Settings and Preferences (%APPDATA%\Macondo\settings.json)
        var settings = SettingsService.Instance.Settings;

        // Restoring Window Geometry and Panel Width
        if (settings.WindowWidth >= 600 && settings.WindowHeight >= 400)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }

        if (settings.WindowX.HasValue && settings.WindowY.HasValue)

            Position = new PixelPoint(settings.WindowX.Value, settings.WindowY.Value);

        if (settings.IsMaximized)

            WindowState = WindowState.Maximized;

        _savedExplorerWidth = (settings.ExplorerWidth >= 160 && settings.ExplorerWidth <= 700) ? settings.ExplorerWidth : 280;
        _savedInspectorWidth = (settings.InspectorWidth >= 200 && settings.InspectorWidth <= 800) ? settings.InspectorWidth : 360;

        if (settings.IsExplorerVisible)

            CentralGrid.ColumnDefinitions[0].Width = new GridLength(_savedExplorerWidth);

        else
        {
            SetExplorerVisibility(false, updateSettings: false);
        }

        if (settings.IsInspectorVisible && (settings.ShowTabInspector || settings.ShowTabSourceCode))

            CentralGrid.ColumnDefinitions[4].Width = new GridLength(_savedInspectorWidth);

        else
        {
            SetInspectorVisibility(false, updateSettings: false);
        }

        UpdatePanelToggleVisuals();

        // Ripristino Modalità Layout Preferita
        _currentLayoutMode = settings.PreferredLayout;
        LayoutComboBox.SelectedIndex = (int)_currentLayoutMode;

        // Initialization of Filters by Symbol Level and Category
        InitFilterControls();

        // Saving Window Geometry, Width and Panel Visibility when closing
        Closing += (_, _) =>
        {
            SettingsService.Instance.Update(s =>
            {
                s.IsMaximized = WindowState == WindowState.Maximized;
                if (WindowState == WindowState.Normal)
                {
                    s.WindowWidth = Width;
                    s.WindowHeight = Height;
                    s.WindowX = Position.X;
                    s.WindowY = Position.Y;
                }
                s.IsExplorerVisible = _isExplorerVisible;
                s.IsInspectorVisible = _isInspectorVisible;
                if (_isExplorerVisible && ProjectExplorerControl.Bounds.Width >= 160)

                    s.ExplorerWidth = ProjectExplorerControl.Bounds.Width;
                if (_isInspectorVisible && (RightPanelContainer.Bounds.Width >= 200 || SymbolInspectorControl.Bounds.Width >= 200))

                    s.InspectorWidth = RightPanelContainer.Bounds.Width >= 200 ? RightPanelContainer.Bounds.Width : SymbolInspectorControl.Bounds.Width;
                s.ShowMethods = Graph.ShowMethods;
                s.ShowProperties = Graph.ShowProperties;
                s.ShowFields = Graph.ShowFields;
                s.ShowConstants = Graph.ShowConstants;
                s.ShowTypesAndEnums = Graph.ShowTypesAndEnums;
            });

            // Immediate synchronous saving of any pending node and cache snapshot layout changes
            FlushPendingLayoutChangesSync();
            if (!string.IsNullOrEmpty(_currentFolderPath) && _graph != null)

                ProjectCacheService.Instance.SaveSnapshot(_currentFolderPath, _graph, BlastRadiusGridControl.AllRows, _hasUserMovedNodes, _enabledAnalyticsTabs);
        };

        // Responsive Services: Localization & Theming with Auto-Save
        LocalizationService.Instance.LanguageChanged += (_, lang) =>
        {
            UpdateLocalizedUI();
            SettingsService.Instance.Update(s => s.Language = lang);
        };

        ThemeService.Instance.ThemeChanged += (_, theme) =>
        {
            ApplyTheme(theme);
            SettingsService.Instance.Update(s => s.Theme = theme.Theme);
        };

        SettingsService.Instance.SettingsChanged += (_, s) =>
        {
            Graph.EnableHoverSelection = s.EnableHoverSelection;
            HoverSelectionCheckIcon.IsVisible = s.EnableHoverSelection;
            ApplyTabLabelsVisibility(s.ShowTabLabels);
            ApplySavedTabOrder();
            ApplyTabsVisibility(s);
        };

        InitializeAnalyticsPlaceholders();

        // Initialize persisted size of side panels (Explorer and Inspector)
        if (settings.ExplorerWidth >= 160)
            _savedExplorerWidth = settings.ExplorerWidth;

        if (settings.InspectorWidth >= 200)
            _savedInspectorWidth = settings.InspectorWidth;

        // Track manual resizing of the right panel in real time via GridSplitter
        RightPanelContainer.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && _isInspectorVisible && RightPanelContainer.Bounds.Width >= 200)
            {
                _savedInspectorWidth = RightPanelContainer.Bounds.Width;
                SettingsService.Instance.Settings.InspectorWidth = _savedInspectorWidth;
            }
        };

        // Track manual resizing of the left panel in real time via GridSplitter
        ProjectExplorerControl.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && _isExplorerVisible && ProjectExplorerControl.Bounds.Width >= 160)
            {
                _savedExplorerWidth = ProjectExplorerControl.Bounds.Width;
                SettingsService.Instance.Settings.ExplorerWidth = _savedExplorerWidth;
            }
        };

        // Apply saved values before initial rendering
        ThemeService.Instance.ApplyTheme(settings.Theme);
        LocalizationService.Instance.SetLanguage(settings.Language);
        Graph.EnableHoverSelection = settings.EnableHoverSelection;
        HoverSelectionCheckIcon.IsVisible = settings.EnableHoverSelection;
        ApplyTabLabelsVisibility(settings.ShowTabLabels);
        ApplySavedTabOrder();
        ApplyTabsVisibility(settings);
        UpdateLocalizedUI();

        // Explicit connection of modal events and overlays
        LoadingOverlayControl.CancelRequested += OnLoadingCancelRequested;
        UpdateModalControl.UpdateNowRequested += OnUpdateModalNowRequested;
        UpdateModalControl.UpdateLaterRequested += OnUpdateModalLaterRequested;
        ExportSuccessModalControl.OpenFolderRequested += OnExportSuccessOpenFolderRequested;
        LayoutConfirmModalControl.Confirmed += OnLayoutConfirmModalConfirmed;

        // ------------------------------------------------------------------------------------------------
        // Project Loading at Startup: Intelligent Management based on StartupMode and Recent History
        // ------------------------------------------------------------------------------------------------
        var startupMode = settings.StartupMode;

        // Retrieves the most recent target in priority from the first RecentProjects history item.
        // If the history is empty, it falls to the LastOpenedFolder property stored in the settings.
        string? lastTarget = null;
        if (settings.RecentProjects.Count > 0)

            lastTarget = settings.RecentProjects[0].Paths.FirstOrDefault();
        if (string.IsNullOrEmpty(lastTarget))

            lastTarget = settings.LastOpenedFolder;

        // Determines the actual project folder (direct directory or parent directory if the target is a .csproj/.vbp/.sln file)
        var targetFolder = !string.IsNullOrEmpty(lastTarget)
            ? (Directory.Exists(lastTarget) ? lastTarget : (File.Exists(lastTarget) ? Path.GetDirectoryName(lastTarget) : null))
            : null;
        var hasValidTarget = !string.IsNullOrEmpty(lastTarget) && (Directory.Exists(lastTarget) || File.Exists(lastTarget));

        if (startupMode == StartupLoadMode.OnlyIfCached && hasValidTarget && !string.IsNullOrEmpty(targetFolder) && Directory.Exists(targetFolder))
        {
            var isCached = ProjectCacheService.Instance.HasCache(targetFolder);
            if (isCached)
            {
                // Instant loading of the project and graph directly from the cached snapshot
                var targetPaths = settings.RecentProjects.Count > 0 ? settings.RecentProjects[0].Paths : new List<string> { lastTarget! };
                _ = AnalyzeAndLoadPathsAsync(targetPaths);
            }
            else
            {
                // No cache available: prepares the welcome screen showing the card of the project pending for analysis
                _currentFolderPath = targetFolder;
                _currentPaths = new List<string> { lastTarget! };
                UpdateVcsBranchStatus(targetFolder);
                ShowWelcomeScreen(pendingFolder: lastTarget);
                StatusText.Text = LocalizationService.Instance.Get("Status_NoCachePending", Path.GetFileName(lastTarget!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            }
        }
        else if (startupMode == StartupLoadMode.AlwaysAnalyze && hasValidTarget)
        {
            // Always scan mode: Starts full scan at startup regardless of cache
            var targetPaths = settings.RecentProjects.Count > 0 ? settings.RecentProjects[0].Paths : new List<string> { lastTarget! };
            _ = AnalyzeAndLoadPathsAsync(targetPaths);
        }
        else
        {
            // EmptyWorkspace mode or no previous valid path: Shows the initial welcome screen
            ShowWelcomeScreen();
            StatusText.Text = LocalizationService.Instance["Status_StartupEmptyReady"];
        }

        // Automatically check for updates in the background at startup
        _ = CheckForUpdatesOnStartupAsync();
    }

    #endregion

    #region Window Management & Title Bar

    /// <summary>
    /// Allows you to drag the window from the free central area of the title bar.
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)

            BeginMoveDrag(e);
    }

    /// <summary>
    /// Double click on the free central area of the title bar to maximize or restore.
    /// </summary>
    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximize();
    }

    /// <summary>
    /// Toggles the state of the main window between maximized (<see cref="WindowState.Maximized"/>) and normal (<see cref="WindowState.Normal"/>).
    /// </summary>
    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    #endregion

    #region Loading & Progress Overlay

    /// <summary>
    /// Shows full-screen centered loading modal overlay with progress bar and statistics options.
    /// </summary>
    private void ShowLoading(
        string title,
        string subtitle,
        bool isIndeterminate = true,
        double progressValue = 0,
        double progressMax = 100,
        string? countText = null,
        string? statsText = null,
        string? timeText = null,
        bool canCancel = true)
    {
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = canCancel ? new CancellationTokenSource() : null;

        LoadingOverlayControl.Show(title, subtitle, canCancel, timeText);
        if (!isIndeterminate || !string.IsNullOrEmpty(countText) || !string.IsNullOrEmpty(statsText) || !string.IsNullOrEmpty(timeText))

            LoadingOverlayControl.UpdateProgress((int)progressValue, (int)progressMax, countText ?? string.Empty, statsText ?? string.Empty, timeText);
    }

    /// <summary>
    /// Updates the detailed progress and textual status of the loading overlay.
    /// </summary>
    private void UpdateLoadingProgress(
        double current,
        double total,
        string? subtitle = null,
        string? countText = null,
        string? statsText = null,
        string? timeText = null)
    {
        if (!LoadingOverlayControl.IsVisible || _loadingCts == null || _loadingCts.IsCancellationRequested)
            return;

        LoadingOverlayControl.UpdateProgress((int)current, (int)total, countText ?? string.Empty, statsText ?? string.Empty, timeText, subtitle);
    }

    /// <summary>
    /// Handles clicking on the Cancel button within the upload/analysis popup.
    /// </summary>
    private void OnLoadingCancelRequested(object? sender, EventArgs e)
    {
        if (_loadingCts != null && !_loadingCts.IsCancellationRequested)
        {
            _loadingCts.Cancel();
            StatusText.Text = LocalizationService.Instance["Status_AnalysisCancelled"];
        }
    }

    /// <summary>
    /// Hides the loading overlay and resets the progress bar state.
    /// </summary>
    private void HideLoading()
    {
        LoadingOverlayControl.Hide();
        _loadingCts?.Dispose();
        _loadingCts = null;
    }

    #endregion

    #region Data Loading, Workspace & Fixtures

    /// <summary>
    /// Generate and upload a synthetic graph of ~5000 nodes via FakeGraphGenerator for benchmarking and testing.
    /// </summary>
    private async void LoadSyntheticGraph()
    {
        var localizationService = LocalizationService.Instance;
        ShowLoading(
            localizationService["Status_FakeGraphGenerating"],
            "Creating ~5000 nodes and computing radial layout...",
            canCancel: true);
        var token = _loadingCts?.Token ?? CancellationToken.None;
        try
        {
            await Task.Delay(16, token);
            _currentFolderPath = null;
            UpdateVcsBranchStatus(null);
            _enabledAnalyticsTabs.Clear();
            if (SettingsService.Instance.Settings.LastEnabledAnalyticsTabs != null)
            {
                foreach (var tabKey in SettingsService.Instance.Settings.LastEnabledAnalyticsTabs)
                {
                    _enabledAnalyticsTabs.Add(tabKey);
                }
            }
            StatusText.Text = localizationService["Status_FakeGraphGenerating"];
            _graph = await Task.Run(() => Generation.FakeGraphGenerator.Generate(seed: Environment.TickCount), token);
            token.ThrowIfCancellationRequested();
            ApplyGraphToUI();
            StatusText.Text = localizationService["Status_FakeGraphGenerated"];
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = localizationService["Status_AnalysisCancelled"];
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("Status_ErrorGenerating", ex.Message);
        }
        finally
        {
            HideLoading();
        }
    }

    /// <summary>
    /// Load and analyze the suite of real VB6 fixtures integrated into the project with real-time progress updating.
    /// </summary>
    private async void LoadDefaultFixtures()
    {
        var localizationService = LocalizationService.Instance;
        ShowLoading(
            localizationService["Loading_FixturesTitle"],
            localizationService["Loading_FixturesSub"],
            isIndeterminate: false,
            progressValue: 0,
            progressMax: 100,
            canCancel: true);
        var token = _loadingCts?.Token ?? CancellationToken.None;

        var progress = new Progress<ProjectAnalysisProgress>(p =>
        {
            var countStr = localizationService.Get("Loading_FilesProgressFormat", p.CurrentFileIndex, p.TotalFiles, (int)p.Percentage);
            var statsStr = localizationService.Get("Loading_NodesExtractedFormat", p.ExtractedNodesCount);
            var timeStr = p.EstimatedRemainingSeconds.HasValue
                ? localizationService.FormatRemainingTime(p.EstimatedRemainingSeconds.Value)
                : (p.EstimatedTotalSeconds.HasValue ? localizationService.FormatEstimatedTime(p.EstimatedTotalSeconds.Value) : null);
            UpdateLoadingProgress(p.CurrentFileIndex, p.TotalFiles, p.PhaseMessage, countStr, statsStr, timeStr);
        });

        try
        {
            await Task.Delay(16, token);
            _currentFolderPath = null;
            UpdateVcsBranchStatus(null);
            _enabledAnalyticsTabs.Clear();
            if (SettingsService.Instance.Settings.LastEnabledAnalyticsTabs != null)
            {
                foreach (var tabKey in SettingsService.Instance.Settings.LastEnabledAnalyticsTabs)
                {
                    _enabledAnalyticsTabs.Add(tabKey);
                }
            }
            StatusText.Text = localizationService["Loading_FixturesSub"];
            _graph = await Task.Run(() => ProjectAnalysisService.LoadDefaultFixtures(progress, token), token);
            token.ThrowIfCancellationRequested();
            ApplyGraphToUI();
            StatusText.Text = localizationService["Status_FixturesLoaded"];

            // Flashing icon on the Windows bar and system notification
            WindowsNotificationService.NotifyAnalysisCompleted(
                this,
                localizationService["Notification_AnalysisCompleted_Title"],
                localizationService.Get("Notification_FixturesCompleted_Message", _graph.Nodes.Count));
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = localizationService["Status_AnalysisCancelled"];
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("Status_ErrorLoading", ex.Message);
        }
        finally
        {
            HideLoading();
        }
    }



    /// <summary>
    /// Drag & Drop Management: Accept files or folders dragged onto the window.
    /// </summary>
    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Drag & Drop Management: Receives dropped files/folders and starts immediate analysis.
    /// </summary>
    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            var items = e.Data.GetFiles();
            if (items != null)
            {
                var paths = items
                    .Select(i => i.Path.LocalPath)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
                if (paths.Count > 0)

                    await AnalyzeAndLoadPathsAsync(paths);
            }
        }
    }

    /// <summary>
    /// Opens a folder chooser to analyze one or more VB6 projects at once.
    /// </summary>
    private async Task OpenFolderAsync()
    {
        var localizationService = LocalizationService.Instance;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = localizationService["Open_FolderTitle"],
            AllowMultiple = true
        });

        if (folders.Count == 0) return;

        var folderPaths = folders.Select(f => f.Path.LocalPath).ToList();
        await AnalyzeAndLoadPathsAsync(folderPaths);
    }

    /// <summary>
    /// Opens a file chooser to directly open one or more project (.vbp) or group (.vbg) files.
    /// </summary>
    private async Task OpenProjectFilesAsync()
    {
        var localizationService = LocalizationService.Instance;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localizationService["Open_ProjectFilesTitle"],
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(localizationService["Open_FileTypeSupportedProjects"])
                {
                    Patterns = new[] { "*.vbp", "*.vbg", "*.csproj", "*.sln", "pom.xml", "build.gradle", "build.gradle.kts", "*.cs", "*.java", "*.kt", "*.kts" }
                },
                new FilePickerFileType(localizationService["Open_FileTypeJavaProjects"])
                {
                    Patterns = new[] { "pom.xml", "build.gradle", "build.gradle.kts" }
                },
                new FilePickerFileType(localizationService["Open_FileTypeJavaSources"])
                {
                    Patterns = new[] { "*.java" }
                },
                new FilePickerFileType(localizationService["Open_FileTypeKotlinSources"])
                {
                    Patterns = new[] { "*.kt", "*.kts" }
                },
                new FilePickerFileType(localizationService["Open_FileTypeCSharpProjects"])
                {
                    Patterns = new[] { "*.csproj", "*.sln" }
                },
                new FilePickerFileType(localizationService["Open_FileTypeCSharpSources"])
                {
                    Patterns = new[] { "*.cs" }
                },
                new FilePickerFileType(localizationService["Open_FileTypeVB6Projects"])
                {
                    Patterns = new[] { "*.vbp", "*.vbg" }
                },
                new FilePickerFileType(localizationService["Open_FileTypeAll"])
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count == 0) return;

        var filePaths = files.Select(f => f.Path.LocalPath).ToList();
        await AnalyzeAndLoadPathsAsync(filePaths);
    }

    /// <summary>
    /// Forcibly reloads the current project(s) from disk, re-parsing and analyzing.
    /// </summary>
    private async Task RefreshCurrentProjectAsync()
    {
        if (_currentPaths.Count > 0)
        {
            await AnalyzeAndLoadPathsAsync(_currentPaths, forceReanalyze: true);
            return;
        }

        var targetFolder = _currentFolderPath;
        if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder))

            targetFolder = SettingsService.Instance.Settings.LastOpenedFolder;

        if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder))
        {
            StatusText.Text = LocalizationService.Instance.CurrentLanguage == AppLanguage.English
                ? "No project open to reload. Use 'Open Folder...' (Ctrl+O)."
                : "Nessun progetto aperto da ricaricare. Usa 'Apri Cartella...' (Ctrl+O).";
            return;
        }

        await AnalyzeAndLoadPathsAsync(new[] { targetFolder }, forceReanalyze: true);
    }

    /// <summary>
    /// Runs or restores a folder scan from cache.
    /// </summary>
    private async Task AnalyzeAndLoadFolderAsync(string folderPath, bool forceReanalyze = false)
    {
        await AnalyzeAndLoadPathsAsync(new[] { folderPath }, forceReanalyze);
    }

    /// <summary>
    /// Performs or restores scanning of one or more paths (folders, .vbp, .vbg) creating an integrated workspace.
    /// </summary>
    private async Task AnalyzeAndLoadPathsAsync(IReadOnlyList<string> paths, bool forceReanalyze = false)
    {
        if (paths.Count == 0) return;

        FlushPendingLayoutChangesSync();
        var localizationService = LocalizationService.Instance;
        var firstPath = paths[0];
        var titleName = paths.Count == 1 
            ? Path.GetFileName(firstPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) 
            : $"Workspace ({paths.Count} percorsi)";

        // Show the loading overlay immediately to give immediate feedback to the user
        ShowLoading(localizationService["Loading_AnalyzeProjectTitle"], localizationService.Get("Loading_AnalyzeProjectSub", titleName), canCancel: true);
        var token = _loadingCts?.Token ?? CancellationToken.None;

        try
        {
            await Task.Delay(16, token);

            // Resolves the actual reference directory for both single folder and project files (.vbp, .csproj, .sln)
            var targetFolder = Directory.Exists(firstPath)
                ? firstPath
                : (File.Exists(firstPath) ? Path.GetDirectoryName(firstPath) : null);

            // 1. Attempt ultra-fast restore from cache only if not forced and if a valid snapshot exists on disk
            if (!forceReanalyze && paths.Count == 1 && !string.IsNullOrEmpty(targetFolder) && Directory.Exists(targetFolder))
            {
                var cachedSnapshot = await ProjectCacheService.Instance.TryLoadSnapshotAsync(targetFolder, token);
                token.ThrowIfCancellationRequested();
                if (cachedSnapshot != null)
                {
                    var (cachedGraph, cachedRows) = await Task.Run(() => ProjectCacheService.Instance.RestoreGraphAndRows(cachedSnapshot, token), token);
                    token.ThrowIfCancellationRequested();
                    _graph = cachedGraph;
                    _currentFolderPath = targetFolder;
                    _currentPaths = paths.ToList();
                    _enabledAnalyticsTabs.Clear();
                    if (SettingsService.Instance.Settings.LastEnabledAnalyticsTabs != null)
                    {
                        foreach (var tabKey in SettingsService.Instance.Settings.LastEnabledAnalyticsTabs)
                        {
                            _enabledAnalyticsTabs.Add(tabKey);
                        }
                    }
                    if (cachedSnapshot.EnabledAnalyticsTabs != null)
                    {
                        foreach (var tabKey in cachedSnapshot.EnabledAnalyticsTabs)
                        {
                            _enabledAnalyticsTabs.Add(tabKey);
                        }
                    }
                    SettingsService.Instance.Update(s => s.LastEnabledAnalyticsTabs = _enabledAnalyticsTabs.ToList());
                    UpdateVcsBranchStatus(targetFolder);
                    ApplyGraphToUI(cachedRows, applyAutomaticLayout: false);
                    _hasUserMovedNodes = cachedSnapshot.HasCustomUserLayout;

                    // Record the open in recent history and update LastOpenedFolder
                    AddToRecentProjects(paths);

                    // Detects any source changes that occurred after saving the cache
                    var isStale = ProjectCacheService.Instance.CheckStaleness(cachedSnapshot, targetFolder);
                    if (isStale)
                        StatusText.Text = localizationService.Get("Status_CacheStale", cachedSnapshot.ProjectName);
                    else
                    {
                        StatusText.Text = localizationService.Get("Status_LoadedFromCache", cachedSnapshot.ProjectName, cachedSnapshot.AnalyzedAtUtc.ToLocalTime().ToString("g"));
                    }
                    return;
                }
            }

            // 2. Complete analysis from disk sources with determined progress bar
            var progress = new Progress<ProjectAnalysisProgress>(p =>
            {
                var countStr = localizationService.Get("Loading_FilesProgressFormat", p.CurrentFileIndex, p.TotalFiles, (int)p.Percentage);
                var statsStr = localizationService.Get("Loading_NodesExtractedFormat", p.ExtractedNodesCount);
                var timeStr = p.EstimatedRemainingSeconds.HasValue
                    ? localizationService.FormatRemainingTime(p.EstimatedRemainingSeconds.Value)
                    : (p.EstimatedTotalSeconds.HasValue ? localizationService.FormatEstimatedTime(p.EstimatedTotalSeconds.Value) : null);
                UpdateLoadingProgress(p.CurrentFileIndex, p.TotalFiles, p.PhaseMessage, countStr, statsStr, timeStr);
            });

            StatusText.Text = localizationService.Get("Status_AnalyzingSources", paths.Count);
            _graph = await Task.Run(() => ProjectAnalysisService.AnalyzePaths(paths, progress, token), token);
            token.ThrowIfCancellationRequested();
            _currentFolderPath = targetFolder ?? firstPath;
            _currentPaths = paths.ToList();
            if (!string.IsNullOrEmpty(targetFolder) && Directory.Exists(targetFolder))

                UpdateVcsBranchStatus(targetFolder);

            _enabledAnalyticsTabs.Clear();
            if (SettingsService.Instance.Settings.LastEnabledAnalyticsTabs != null)
            {
                foreach (var tabKey in SettingsService.Instance.Settings.LastEnabledAnalyticsTabs)
                {
                    _enabledAnalyticsTabs.Add(tabKey);
                }
            }
            if (!string.IsNullOrEmpty(_currentFolderPath))
            {
                var prevSnapshot = await ProjectCacheService.Instance.TryLoadSnapshotAsync(_currentFolderPath);
                if (prevSnapshot?.EnabledAnalyticsTabs != null)
                {
                    foreach (var tabKey in prevSnapshot.EnabledAnalyticsTabs)
                    {
                        _enabledAnalyticsTabs.Add(tabKey);
                    }
                }
            }
            SettingsService.Instance.Update(s => s.LastEnabledAnalyticsTabs = _enabledAnalyticsTabs.ToList());

            // Building explorer tree on background thread to avoid UI Dispatcher crashes on large graphs (40k+ nodes)
            ExplorerTreeItem? precalculatedExplorerTree = null;
            if (_graph != null)
            {
                precalculatedExplorerTree = await Task.Run(
                    () => ExplorerTreeItem.BuildTree(_graph, _currentFolderPath, Graph.HiddenProjectIds),
                    token);
            }

            // Closes the loading modal before applying elements to the UI
            HideLoading();
            await Task.Yield();

            // The layout has already been calculated in the background during FromSemanticGraph in ProjectAnalysisService
            ApplyGraphToUI(precalculatedTree: precalculatedExplorerTree, applyAutomaticLayout: false); // Avvierà PopulateBlastRadiusGridAsync che poi aggiornerà la cache con i calcoli

            // Immediately saves the initial snapshot of the graph to cache on background threads
            if (!string.IsNullOrEmpty(_currentFolderPath) && _graph != null)
                _ = ProjectCacheService.Instance.SaveSnapshotAsync(_currentFolderPath, _graph, BlastRadiusGridControl.AllRows, _hasUserMovedNodes, _enabledAnalyticsTabs);

            var projectCount = _graph?.Nodes.Count(n => n.Kind == SymbolKind.Project) ?? 0;
            var crossCount = _graph?.Edges.Count(e => e.IsCrossProject) ?? 0;
            var nodeCount = _graph?.Nodes.Count ?? 0;
            if (projectCount > 1)

                StatusText.Text = localizationService.Get("Status_WorkspaceAnalyzed", projectCount, nodeCount, crossCount);

            else
            {
                StatusText.Text = localizationService.Get("Status_Refreshed", titleName, nodeCount);
            }

            // Record the open in recent history and update LastOpenedFolder
            AddToRecentProjects(paths);

            // Flashing icon on the Windows bar and system notification
            WindowsNotificationService.NotifyAnalysisCompleted(
                this,
                localizationService["Notification_AnalysisCompleted_Title"],
                localizationService.Get("Notification_AnalysisCompleted_Message", titleName, nodeCount));
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = localizationService["Status_AnalysisCancelled"];
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("Status_ErrorAnalyzing", ex.Message);
        }
        finally
        {
            HideLoading();
        }
    }

    /// <summary>
    /// Adds a project, file or folder to the recent projects list, updates the last opened project folder in settings, and rebuilds the menus and welcome screen.
    /// </summary>
    /// <param name="paths">List of open paths (folder, project file or multiple files).</param>
    private void AddToRecentProjects(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;

        var firstPath = paths[0];
        var isFolder = paths.Count == 1 && Directory.Exists(firstPath);
        var targetFolder = Directory.Exists(firstPath)
            ? firstPath
            : (File.Exists(firstPath) ? Path.GetDirectoryName(firstPath) : null);

        var displayName = paths.Count == 1
            ? Path.GetFileName(firstPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : $"Workspace ({paths.Count} percorsi)";

        if (string.IsNullOrEmpty(displayName))

            displayName = firstPath;

        SettingsService.Instance.Update(settings =>
        {
            // Atomically saves the last valid folder for startup and analysis card
            if (!string.IsNullOrEmpty(targetFolder) && Directory.Exists(targetFolder))

                settings.LastOpenedFolder = targetFolder;

            // Remove if already present to avoid duplicates
            settings.RecentProjects.RemoveAll(r => r.Paths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase));

            // Insert at top of history
            settings.RecentProjects.Insert(0, new RecentProjectItem
            {
                Paths = paths.ToList(),
                DisplayName = displayName,
                IsFolder = isFolder,
                LastOpenedUtc = DateTime.UtcNow
            });

            // Keep only the last 10 items
            if (settings.RecentProjects.Count > 10)

                settings.RecentProjects = settings.RecentProjects.Take(10).ToList();
        });

        UpdateRecentProjectsMenu();
        WelcomeControl.Refresh();
    }

    /// <summary>
    /// Dynamically rebuilds the "Recent" submenu in the File menu with the latest 10 items.
    /// </summary>
    private void UpdateRecentProjectsMenu() 
    {
        var localizationService = LocalizationService.Instance;
        RecentProjectsMenuItem.Header = localizationService["Menu_Recent"];
        RecentProjectsMenuItem.Items.Clear();

        var recentList = SettingsService.Instance.Settings.RecentProjects;
        if (recentList.Count == 0)
        {
            var emptyItem = new MenuItem
            {
                Header = localizationService["Menu_NoRecentProjects"],
                IsEnabled = false
            };
            RecentProjectsMenuItem.Items.Add(emptyItem);
            return;
        }

        foreach (var recent in recentList)
        {
            var iconKey = recent.IsFolder ? "IconFolder" : "IconFile";
            var iconColor = recent.IsFolder ? "#D4A72C" : "#6366F1";

            var item = new MenuItem
            {
                Header = recent.DisplayName,
                Tag = recent.Paths
            };
            ToolTip.SetTip(item, string.Join(Environment.NewLine, recent.Paths));

            if (this.TryFindResource(iconKey, out var res) && res is StreamGeometry geo)

                item.Icon = new PathIcon
                {
                    Data = geo,
                    Width = 14,
                    Height = 14,
                    Foreground = Color.TryParse(iconColor, out var c) ? new SolidColorBrush(c) : Brushes.DodgerBlue
                };

            var capturedPaths = recent.Paths.ToList();
            item.Click += async (_, _) =>
            {
                await AnalyzeAndLoadPathsAsync(capturedPaths);
            };

            RecentProjectsMenuItem.Items.Add(item);
        }

        RecentProjectsMenuItem.Items.Add(new Separator());
        var clearItem = new MenuItem
        {
            Header = localizationService["Menu_ClearRecentProjects"]
        };
        if (this.TryFindResource("IconClearFilter", out var clearRes) && clearRes is StreamGeometry clearGeo)

            clearItem.Icon = new PathIcon
            {
                Data = clearGeo,
                Width = 12,
                Height = 12
            };
        clearItem.Click += (_, _) =>
        {
            SettingsService.Instance.Update(s =>
            {
                s.RecentProjects.Clear();
                s.LastOpenedFolder = null;
            });
            UpdateRecentProjectsMenu();
            WelcomeControl.Refresh();
        };
        RecentProjectsMenuItem.Items.Add(clearItem);
    }

    /// <summary>
    /// Update all UI components (Graph, Explorer, Status) with the new CodeGraph.
    /// If precalculatedRows is provided (e.g. from cache), the Blast Radius grid is populated instantly
    /// and saved custom coordinates are not overwritten if applyAutomaticLayout is false.
    /// If precalculatedTree is provided, avoid rebuilding the explorer tree on the UI thread.
    /// </summary>
    private void ApplyGraphToUI(List<BlastRadiusGridRow>? precalculatedRows = null, bool applyAutomaticLayout = true, ExplorerTreeItem? precalculatedTree = null)
    {
        if (_graph is null) return;

        // Shows the central analysis tabs and hides the Welcome View
        WelcomeControl.IsVisible = false;
        CentralTabControl.IsVisible = true;

        if (applyAutomaticLayout)
            Layout.GraphLayoutManager.ApplyLayout(_graph, _currentLayoutMode);
        _hasUserMovedNodes = false;
        Graph.ClearHiddenProjects();

        // If there are multiple projects, pre-select only the first project (hiding all others)
        var projectNodes = _graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        if (projectNodes.Count > 1)
        {
            foreach (var otherProj in projectNodes.Skip(1))
            {
                Graph.SetProjectVisibility(otherProj.Id, false);
            }
        }

        Graph.Graph = _graph;
        ResetGraphView();
        Graph.SelectNode(null);

        var tree = precalculatedTree ?? ExplorerTreeItem.BuildTree(_graph, _currentFolderPath, Graph.HiddenProjectIds);
        ProjectExplorerControl.ItemsSource = new[] { tree };

        ShowEmptyInspector();
        UpdateProjectsFilterUI();
        UpdateStatsBar();
        SyncFilterUiState();

        if (precalculatedRows != null && precalculatedRows.Count > 0)
        {
            BlastRadiusGridControl.AllRows = precalculatedRows;
            BlastRadiusGridControl.SetHiddenProjects(Graph.HiddenProjectIds);
        }
        else
        {
            BlastRadiusGridControl.Clear();
            BlastRadiusGridControl.SetHiddenProjects(Graph.HiddenProjectIds);
        }

        InvalidateAnalyticsViews();
    }

    /// <summary>
    /// Expands or collapses all container nodes (classes, modules, forms) present in the visual graph.
    /// </summary>
    /// <param name="expanded"><c>true</c> to expand all containers; <c>false</c> to compress them.</param>
    private void SetAllExpanded(bool expanded)
    {
        if (_graph is null) return;
        foreach (var node in _graph.Nodes.Where(n => n.IsContainer))
        {
            node.IsExpanded = expanded;
        }
        Graph.InvalidateVisual();
        SyncFilterUiState();
    }

    #endregion

    #region Symbol Filters, Levels of Detail & Multiple Projects

    /// <summary>
    /// Initializes symbol filter controls, registering event handlers for checkboxes, menus, and legend buttons.
    /// It also restores the filter state saved in user settings.
    /// </summary>
    private void InitFilterControls()
    {
        var settings = SettingsService.Instance.Settings;
        Graph.ShowMethods = settings.ShowMethods;
        Graph.ShowProperties = settings.ShowProperties;
        Graph.ShowFields = settings.ShowFields;
        Graph.ShowConstants = settings.ShowConstants;
        Graph.ShowTypesAndEnums = settings.ShowTypesAndEnums;

        Graph.FilterChanged += (_, _) => SyncFilterUiState();
        Graph.ProjectVisibilityChanged += OnGraphProjectVisibilityChanged;

        DetailLevelComboBox.SelectionChanged += OnDetailLevelComboBoxSelectionChanged;

        FilterMethodsCheckBox.IsCheckedChanged += (_, _) => OnFilterCheckBoxChanged();
        FilterPropertiesCheckBox.IsCheckedChanged += (_, _) => OnFilterCheckBoxChanged();
        FilterFieldsCheckBox.IsCheckedChanged += (_, _) => OnFilterCheckBoxChanged();
        FilterConstantsCheckBox.IsCheckedChanged += (_, _) => OnFilterCheckBoxChanged();
        FilterTypesCheckBox.IsCheckedChanged += (_, _) => OnFilterCheckBoxChanged();

        LegendMethodButton.Click += (_, _) => ToggleFilterCategory(SymbolKind.Method);
        LegendPropertyButton.Click += (_, _) => ToggleFilterCategory(SymbolKind.Property);
        LegendFieldButton.Click += (_, _) => ToggleFilterCategory(SymbolKind.Field);
        LegendTypeButton.Click += (_, _) => ToggleFilterCategory(SymbolKind.Type);

        FilterAllMenuItem.Click += (_, _) => SetDetailPreset(0);
        FilterMethodsPropsMenuItem.Click += (_, _) => SetDetailPreset(1);
        FilterContainersOnlyMenuItem.Click += (_, _) => SetDetailPreset(2);

        FilterMethodsMenuItem.Click += (_, _) => ToggleFilterCategory(SymbolKind.Method);
        FilterPropertiesMenuItem.Click += (_, _) => ToggleFilterCategory(SymbolKind.Property);
        FilterFieldsMenuItem.Click += (_, _) => ToggleFilterCategory(SymbolKind.Field);
        FilterConstantsMenuItem.Click += (_, _) => ToggleFilterCategory(SymbolKind.Constant);
        FilterTypesMenuItem.Click += (_, _) => ToggleFilterCategory(SymbolKind.Type);

        SyncFilterUiState();
    }

    /// <summary>
    /// Applies a default level of detail preset (0 = Full, 1 = Methods & Properties, 2 = Forms Only).
    /// </summary>
    /// <param name="index">Index of the preset selected in the ComboBox or MenuItem.</param>
    private void SetDetailPreset(int index)
    {
        if (index == 0) // Completo: tutti i membri visibili

            Graph.SetFilters(true, true, true, true, true);

        else if (index == 1) // Metodi & Proprietà: nasconde variabili, costanti e tipi

    Graph.SetFilters(true, true, false, false, false);

else if (index == 2) // Solo Moduli: nasconde tutti i membri interni

    Graph.SetFilters(false, false, false, false, false);
        SaveFilterSettings();
    }

    /// <summary>
    /// Manages the change of selection in the symbol detail level ComboBox.
    /// </summary>
    private void OnDetailLevelComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingFilterUi) return;
        SetDetailPreset(DetailLevelComboBox.SelectedIndex);
    }

    /// <summary>
    /// Manages changing the check status of one of the symbol type filter CheckBoxes.
    /// </summary>
    private void OnFilterCheckBoxChanged()
    {
        if (_isSyncingFilterUi) return;
        Graph.ShowMethods = FilterMethodsCheckBox.IsChecked == true;
        Graph.ShowProperties = FilterPropertiesCheckBox.IsChecked == true;
        Graph.ShowFields = FilterFieldsCheckBox.IsChecked == true;
        Graph.ShowConstants = FilterConstantsCheckBox.IsChecked == true;
        Graph.ShowTypesAndEnums = FilterTypesCheckBox.IsChecked == true;
        SaveFilterSettings();
    }

    /// <summary>
    /// Reverses the visibility in the graph for the specific symbol category (<see cref="SymbolKind"/>).
    /// </summary>
    /// <param name="kind">The symbol type to toggle (Method, Property, Field, Constant, Type/Enum).</param>
    private void ToggleFilterCategory(SymbolKind kind)
    {
        switch (kind)
        {
            case SymbolKind.Method:
                Graph.ShowMethods = !Graph.ShowMethods;
                break;
            case SymbolKind.Property:
                Graph.ShowProperties = !Graph.ShowProperties;
                break;
            case SymbolKind.Field:
                Graph.ShowFields = !Graph.ShowFields;
                break;
            case SymbolKind.Constant:
                Graph.ShowConstants = !Graph.ShowConstants;
                break;
            case SymbolKind.Type or SymbolKind.Enum:
                Graph.ShowTypesAndEnums = !Graph.ShowTypesAndEnums;
                break;
        }
        SaveFilterSettings();
    }

    /// <summary>
    /// Saves the current symbol filter settings to the application's persistent configuration.
    /// </summary>
    private void SaveFilterSettings()
    {
        SettingsService.Instance.Update(s =>
        {
            s.ShowMethods = Graph.ShowMethods;
            s.ShowProperties = Graph.ShowProperties;
            s.ShowFields = Graph.ShowFields;
            s.ShowConstants = Graph.ShowConstants;
            s.ShowTypesAndEnums = Graph.ShowTypesAndEnums;
        });
    }

    /// <summary>
    /// Synchronize the visual state of all filter UI controls (checkboxes, legend element opacity, menu check icons, preset combos)
    /// with the actual state of the filter properties of the <see cref="Controls.GraphControl"/>.
    /// </summary>
    private void SyncFilterUiState()
    {
        _isSyncingFilterUi = true;
        try
        {
            FilterMethodsCheckBox.IsChecked = Graph.ShowMethods;
            FilterPropertiesCheckBox.IsChecked = Graph.ShowProperties;
            FilterFieldsCheckBox.IsChecked = Graph.ShowFields;
            FilterConstantsCheckBox.IsChecked = Graph.ShowConstants;
            FilterTypesCheckBox.IsChecked = Graph.ShowTypesAndEnums;

            LegendMethodPanel.Opacity = Graph.ShowMethods ? 1.0 : 0.35;
            LegendPropertyPanel.Opacity = Graph.ShowProperties ? 1.0 : 0.35;
            LegendFieldPanel.Opacity = Graph.ShowFields ? 1.0 : 0.35;
            LegendTypePanel.Opacity = Graph.ShowTypesAndEnums ? 1.0 : 0.35;

            // Update checkmarks in View menu items
            if (Application.Current != null && Application.Current.TryFindResource("IconCheck", out var res) && res is StreamGeometry checkGeo)
            {
                FilterMethodsMenuItem.Icon = Graph.ShowMethods ? new PathIcon { Data = checkGeo, Width = 12, Height = 12 } : null;
                FilterPropertiesMenuItem.Icon = Graph.ShowProperties ? new PathIcon { Data = checkGeo, Width = 12, Height = 12 } : null;
                FilterFieldsMenuItem.Icon = Graph.ShowFields ? new PathIcon { Data = checkGeo, Width = 12, Height = 12 } : null;
                FilterConstantsMenuItem.Icon = Graph.ShowConstants ? new PathIcon { Data = checkGeo, Width = 12, Height = 12 } : null;
                FilterTypesMenuItem.Icon = Graph.ShowTypesAndEnums ? new PathIcon { Data = checkGeo, Width = 12, Height = 12 } : null;
            }

            // Allinea indice ComboBox Preset
            if (Graph.ShowMethods && Graph.ShowProperties && Graph.ShowFields && Graph.ShowConstants && Graph.ShowTypesAndEnums)

                DetailLevelComboBox.SelectedIndex = 0; // Completo

            else if (Graph.ShowMethods && Graph.ShowProperties && !Graph.ShowFields && !Graph.ShowConstants && !Graph.ShowTypesAndEnums)

    DetailLevelComboBox.SelectedIndex = 1; // Metodi & Proprietà

else if (!Graph.ShowMethods && !Graph.ShowProperties && !Graph.ShowFields && !Graph.ShowConstants && !Graph.ShowTypesAndEnums)

    DetailLevelComboBox.SelectedIndex = 2; // Solo Moduli

else
            {
                DetailLevelComboBox.SelectedIndex = 3; // Personalizzato
            }

            if (_graph is not null)
            {
                var (vis, total) = Graph.GetNodeCounts();
                if (Graph.CurrentHighlightMode == HighlightMode.Normal && total > 0)

                    StatusText.Text = LocalizationService.Instance.Get("Status_NodesDisplayed", vis, total);
            }
        }
        finally
        {
            _isSyncingFilterUi = false;
        }
    }

    // Set of analytical tabs whose datasets require lazy or background recalculation
    private readonly HashSet<object> _dirtyAnalyticsTabs = new();
    private CancellationTokenSource? _analyticsCts;

    // Set of analysis cards launched or enabled by the user for the current workspace
    private readonly HashSet<string> _enabledAnalyticsTabs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the unique identifying key associated with each analysis card.
    /// </summary>
    private string? GetTabKey(object? tabItem)
    {
        if (tabItem == null) return null;
        if (tabItem == DsmTabItem) return "Dsm";
        if (tabItem == TreemapTabItem) return "Treemap";
        if (tabItem == HotspotTabItem) return "Hotspots";
        if (tabItem == ScreenFlowTabItem) return "ScreenFlow";
        if (tabItem == MartinTabItem) return "Martin";
        if (tabItem == DeadCodeTabItem) return "DeadCode";
        if (tabItem == SharedStateTabItem) return "SharedState";
        if (tabItem == TemporalCouplingTabItem) return "TemporalCoupling";
        if (tabItem == ArchitecturalFlowTabItem) return "ArchitecturalFlow";
        if (tabItem == CodeCityTabItem) return "CodeCity";
        if (tabItem == CohesionTabItem) return "Cohesion";
        if (tabItem == MaintainabilityTabItem) return "Maintainability";
        if (tabItem == ArchitecturalRulesTabItem) return "ArchitecturalRules";
        if (tabItem == CodeCloneTabItem) return "CodeClone";
        if (tabItem == SocialCodeTabItem) return "SocialCode";
        if (tabItem == DataFlowTabItem) return "DataFlow";
        if (tabItem == ApiDiffTabItem) return "ApiDiff";
        if (tabItem == CrudMatrixTabItem) return "CrudMatrix";
        if (tabItem == GridTabItem) return "BlastRadiusGrid";
        return null;
    }

    /// <summary>
    /// Marks an analysis tab as enabled for the current project and updates disk cache and global preferences.
    /// </summary>
    private void MarkAnalyticsTabEnabled(string tabKey)
    {
        if (_enabledAnalyticsTabs.Add(tabKey))
        {
            if (!string.IsNullOrEmpty(_currentFolderPath))

                _ = ProjectCacheService.Instance.UpdateEnabledAnalyticsTabsAsync(_currentFolderPath, _enabledAnalyticsTabs);
            SettingsService.Instance.Update(s => s.LastEnabledAnalyticsTabs = _enabledAnalyticsTabs.ToList());
        }
    }

    /// <summary>
    /// Mark all 19 analysis tabs as enabled for the current project and update disk cache and global preferences.
    /// </summary>
    private void MarkAllAnalyticsTabsEnabled()
    {
        var allKeys = new[]
        {
            "Dsm", "Treemap", "Hotspots", "ScreenFlow", "Martin", "DeadCode",
            "SharedState", "TemporalCoupling", "ArchitecturalFlow", "CodeCity",
            "Cohesion", "Maintainability", "ArchitecturalRules", "CodeClone",
            "SocialCode", "DataFlow", "ApiDiff", "CrudMatrix", "BlastRadiusGrid"
        };
        bool changed = false;
        foreach (var key in allKeys)
        {
            if (_enabledAnalyticsTabs.Add(key)) changed = true;
        }
        if (changed)
        {
            if (!string.IsNullOrEmpty(_currentFolderPath))

                _ = ProjectCacheService.Instance.UpdateEnabledAnalyticsTabsAsync(_currentFolderPath, _enabledAnalyticsTabs);
            SettingsService.Instance.Update(s => s.LastEnabledAnalyticsTabs = _enabledAnalyticsTabs.ToList());
        }
    }

    /// <summary>
    /// Returns the target semantic graph taking into account only the projects currently visible in the workspace.
    /// </summary>
    private CodeGraph GetTargetSemanticGraph()
    {
        if (_graph?.SemanticGraph is null) return new CodeGraph();

        var semGraph = _graph.SemanticGraph;
        var hiddenIds = Graph.HiddenProjectIds;

        if (hiddenIds.Count == 0)
            return semGraph;

        var hiddenSymbolIds = _graph.Nodes
            .Where(n => (n.ProjectId.HasValue && hiddenIds.Contains(n.ProjectId.Value)) ||
                        (n.Kind == SymbolKind.Project && hiddenIds.Contains(n.Id)))
            .Select(n => n.Id)
            .ToHashSet();

        var targetGraph = new CodeGraph();
        foreach (var s in semGraph.Symbols)
        {
            if (!hiddenSymbolIds.Contains(s.Id))
                targetGraph.Symbols.Add(s);
        }
        foreach (var r in semGraph.Relationships)
        {
            if (!hiddenSymbolIds.Contains(r.SourceId) && !hiddenSymbolIds.Contains(r.TargetId))
                targetGraph.Relationships.Add(r);
        }

        return targetGraph;
    }

    /// <summary>
    /// Mark all secondary analytic views as dirty and launch
    /// background preprocessing for all tabs if enabled globally,
    /// or only for tabs previously enabled/started for the current project.
    /// </summary>
    private void InvalidateAnalyticsViews()
    {
        // Clears any background processing still in progress for the previous workspace
        _analyticsCts?.Cancel();
        _analyticsCts?.Dispose();
        _analyticsCts = new CancellationTokenSource();

        _dirtyAnalyticsTabs.Clear();
        if (DsmTabItem != null) _dirtyAnalyticsTabs.Add(DsmTabItem);
        if (TreemapTabItem != null) _dirtyAnalyticsTabs.Add(TreemapTabItem);
        if (HotspotTabItem != null) _dirtyAnalyticsTabs.Add(HotspotTabItem);
        if (ScreenFlowTabItem != null) _dirtyAnalyticsTabs.Add(ScreenFlowTabItem);
        if (MartinTabItem != null) _dirtyAnalyticsTabs.Add(MartinTabItem);
        if (DeadCodeTabItem != null) _dirtyAnalyticsTabs.Add(DeadCodeTabItem);
        if (SharedStateTabItem != null) _dirtyAnalyticsTabs.Add(SharedStateTabItem);
        if (TemporalCouplingTabItem != null) _dirtyAnalyticsTabs.Add(TemporalCouplingTabItem);
        if (ArchitecturalFlowTabItem != null) _dirtyAnalyticsTabs.Add(ArchitecturalFlowTabItem);
        if (CodeCityTabItem != null) _dirtyAnalyticsTabs.Add(CodeCityTabItem);
        if (CohesionTabItem != null) _dirtyAnalyticsTabs.Add(CohesionTabItem);
        if (MaintainabilityTabItem != null) _dirtyAnalyticsTabs.Add(MaintainabilityTabItem);
        if (ArchitecturalRulesTabItem != null) _dirtyAnalyticsTabs.Add(ArchitecturalRulesTabItem);
        if (CodeCloneTabItem != null) _dirtyAnalyticsTabs.Add(CodeCloneTabItem);
        if (SocialCodeTabItem != null) _dirtyAnalyticsTabs.Add(SocialCodeTabItem);
        if (DataFlowTabItem != null) _dirtyAnalyticsTabs.Add(DataFlowTabItem);
        if (ApiDiffTabItem != null) _dirtyAnalyticsTabs.Add(ApiDiffTabItem);
        if (CrudMatrixTabItem != null) _dirtyAnalyticsTabs.Add(CrudMatrixTabItem);
        if (GridTabItem != null && BlastRadiusGridControl.AllRows.Count == 0) _dirtyAnalyticsTabs.Add(GridTabItem);

        SetAllAnalyticsPlaceholdersPending();

        var s = SettingsService.Instance.Settings;
        bool hasAnyToCompute = s.EnableBackgroundAnalytics || _enabledAnalyticsTabs.Count > 0;

        if (hasAnyToCompute)
        {
            // If the user is already on an enabled secondary analytic tab, calculate it now
            EnsureActiveTabAnalyticsLoaded();

            // Start the background precalculation pipeline for enabled tabs
            StartBackgroundAnalyticsPrecomputation(_analyticsCts.Token);
        }
        else
        {
            if (BackgroundAnalyticsPanel != null)
                BackgroundAnalyticsPanel.IsVisible = false;
        }
    }

    /// <summary>
    /// Sets the initial state on analytic card placeholders based on your settings
    /// global settings and tabs previously enabled/launched for the current project.
    /// </summary>
    private void SetAllAnalyticsPlaceholdersPending()
    {
        var s = SettingsService.Instance.Settings;
        void UpdateState(AnalyticsPlaceholderControl? placeholder, string key)
        {
            if (placeholder == null) return;
            bool isAuto = s.EnableBackgroundAnalytics || _enabledAnalyticsTabs.Contains(key);
            placeholder.SetState(isPending: !isAuto, isComputing: isAuto);
        }

        UpdateState(DsmPlaceholder, "Dsm");
        UpdateState(TreemapPlaceholder, "Treemap");
        UpdateState(HotspotPlaceholder, "Hotspots");
        UpdateState(ScreenFlowPlaceholder, "ScreenFlow");
        UpdateState(MartinPlaceholder, "Martin");
        UpdateState(DeadCodePlaceholder, "DeadCode");
        UpdateState(SharedStatePlaceholder, "SharedState");
        UpdateState(TemporalCouplingPlaceholder, "TemporalCoupling");
        UpdateState(ArchitecturalFlowPlaceholder, "ArchitecturalFlow");
        UpdateState(CodeCityPlaceholder, "CodeCity");
        UpdateState(CohesionPlaceholder, "Cohesion");
        UpdateState(MaintainabilityPlaceholder, "Maintainability");
        UpdateState(ArchitecturalRulesPlaceholder, "ArchitecturalRules");
        UpdateState(CodeClonePlaceholder, "CodeClone");
        UpdateState(SocialCodePlaceholder, "SocialCode");
        UpdateState(DataFlowPlaceholder, "DataFlow");
        UpdateState(ApiDiffPlaceholder, "ApiDiff");
        UpdateState(CrudMatrixPlaceholder, "CrudMatrix");
        if (BlastRadiusGridControl.AllRows.Count == 0)
            UpdateState(BlastRadiusGridPlaceholder, "BlastRadiusGrid");
        else
            BlastRadiusGridPlaceholder?.SetState(isPending: false, isComputing: false);
    }

    /// <summary>
    /// Background pipeline to pre-calculate the datasets of the enabled analytical tabs without blocking the interface,
    /// updating the progress bar visible in the status bar at the bottom right in real time.
    /// </summary>
    private void StartBackgroundAnalyticsPrecomputation(CancellationToken token)
    {
        if (_graph?.SemanticGraph is null)
        {
            if (BackgroundAnalyticsPanel != null) BackgroundAnalyticsPanel.IsVisible = false;
            return;
        }

        var targetGraph = GetTargetSemanticGraph();
        var folder = _currentFolderPath;
        var s = SettingsService.Instance.Settings;

        bool ShouldPrecompute(TabItem? tabItem, bool showTab, string tabKey)
        {
            if (tabItem == null || !showTab || !_dirtyAnalyticsTabs.Contains(tabItem))
                return false;
            return s.EnableBackgroundAnalytics || _enabledAnalyticsTabs.Contains(tabKey);
        }

        var nodeCount = targetGraph.Symbols.Count;
        double totalEstimatedSeconds = 0;
        var pendingTabs = new List<(TabItem tab, double estSeconds)>();

        void CheckAndQueue(TabItem? tabItem, bool showTab, string tabKey)
        {
            if (tabItem != null && ShouldPrecompute(tabItem, showTab, tabKey))
            {
                var est = GetEstimatedAnalysisSeconds(tabItem, nodeCount);
                pendingTabs.Add((tabItem, est));
                totalEstimatedSeconds += est;
            }
        }

        CheckAndQueue(DsmTabItem, s.ShowTabDsm, "Dsm");
        CheckAndQueue(TreemapTabItem, s.ShowTabTreemap, "Treemap");
        CheckAndQueue(HotspotTabItem, s.ShowTabHotspot, "Hotspots");
        CheckAndQueue(MartinTabItem, s.ShowTabMartin, "Martin");
        CheckAndQueue(DeadCodeTabItem, s.ShowTabDeadCode, "DeadCode");
        CheckAndQueue(ScreenFlowTabItem, s.ShowTabScreenFlow, "ScreenFlow");
        CheckAndQueue(SharedStateTabItem, s.ShowTabSharedState, "SharedState");
        CheckAndQueue(TemporalCouplingTabItem, s.ShowTabTemporalCoupling, "TemporalCoupling");
        CheckAndQueue(ArchitecturalFlowTabItem, s.ShowTabArchitecturalFlow, "ArchitecturalFlow");
        CheckAndQueue(CodeCityTabItem, s.ShowTabCodeCity, "CodeCity");
        CheckAndQueue(CohesionTabItem, s.ShowTabCohesion, "Cohesion");
        CheckAndQueue(MaintainabilityTabItem, s.ShowTabMaintainability, "Maintainability");
        CheckAndQueue(ArchitecturalRulesTabItem, s.ShowTabArchitecturalRules, "ArchitecturalRules");
        CheckAndQueue(CodeCloneTabItem, s.ShowTabCodeClone, "CodeClone");
        CheckAndQueue(SocialCodeTabItem, s.ShowTabSocialCode, "SocialCode");
        CheckAndQueue(DataFlowTabItem, s.ShowTabDataFlow, "DataFlow");
        CheckAndQueue(ApiDiffTabItem, s.ShowTabApiDiff, "ApiDiff");
        CheckAndQueue(CrudMatrixTabItem, s.ShowTabCrudMatrix, "CrudMatrix");
        CheckAndQueue(GridTabItem, s.ShowTabBlastRadiusGrid, "BlastRadiusGrid");

        int totalAnalyticsSteps = pendingTabs.Count;
        if (totalAnalyticsSteps == 0)
        {
            if (BackgroundAnalyticsPanel != null) BackgroundAnalyticsPanel.IsVisible = false;
            return;
        }

        // Initialize and show the progress bar in the status bar
        if (BackgroundAnalyticsPanel != null)
        {
            BackgroundAnalyticsPanel.IsVisible = true;
            BackgroundAnalyticsProgressBar.Minimum = 0;
            BackgroundAnalyticsProgressBar.Maximum = totalAnalyticsSteps;
            BackgroundAnalyticsProgressBar.Value = 0;
            BackgroundAnalyticsText.Text = LocalizationService.Instance.Get(
                "Status_BackgroundAnalyticsProgress", 0, totalAnalyticsSteps, Math.Max(0.5, Math.Round(totalEstimatedSeconds, 1)));
        }

        _ = Task.Run(async () =>
        {
            int completedCount = 0;
            double remainingSeconds = totalEstimatedSeconds;
            var lockObj = new object();

            void ReportStepCompletion(double stepDuration)
            {
                var done = Interlocked.Increment(ref completedCount);
                if (token.IsCancellationRequested) return;

                lock (lockObj)
                {
                    remainingSeconds = Math.Max(0.0, remainingSeconds - stepDuration);
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested || BackgroundAnalyticsPanel == null) return;

                    BackgroundAnalyticsProgressBar.Value = done;
                    var remEst = Math.Max(0.1, Math.Round(remainingSeconds, 1));
                    BackgroundAnalyticsText.Text = LocalizationService.Instance.Get(
                        "Status_BackgroundAnalyticsProgress", done, totalAnalyticsSteps, remEst);

                    // When all tabs are completed, it briefly displays the completed status and then hides the bar
                    if (done >= totalAnalyticsSteps)
                    {
                        BackgroundAnalyticsText.Text = LocalizationService.Instance["Status_BackgroundAnalyticsCompleted"];
                        _ = Task.Delay(1500, token).ContinueWith(_ =>
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                if (!token.IsCancellationRequested && BackgroundAnalyticsPanel != null)

                                    BackgroundAnalyticsPanel.IsVisible = false;
                            });
                        }, TaskScheduler.Default);
                    }
                });
            }

            // 1. Matrice DSM
            if (!token.IsCancellationRequested && ShouldPrecompute(DsmTabItem, s.ShowTabDsm, "Dsm"))
            {
                var matrix = DsmMatrixBuilder.Build(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(DsmTabItem))
                        {
                            Dsm.Matrix = matrix;
                            DsmPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(DsmTabItem, nodeCount));
            }

            // 2. Treemap
            if (!token.IsCancellationRequested && ShouldPrecompute(TreemapTabItem, s.ShowTabTreemap, "Treemap"))
            {
                var treemap = TreemapModelBuilder.Build(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(TreemapTabItem))
                        {
                            Treemap.RootItem = treemap;
                            TreemapPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(TreemapTabItem, nodeCount));
            }

            // 3. Risk Hotspots
            if (!token.IsCancellationRequested && ShouldPrecompute(HotspotTabItem, s.ShowTabHotspot, "Hotspots"))
            {
                var hotspot = HotspotAnalyzer.Analyze(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(HotspotTabItem))
                        {
                            Hotspot.Dataset = hotspot;
                            HotspotPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(HotspotTabItem, nodeCount));
            }

            // 4. Martin Metrics
            if (!token.IsCancellationRequested && ShouldPrecompute(MartinTabItem, s.ShowTabMartin, "Martin"))
            {
                var martin = MartinMetricsCalculator.Analyze(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(MartinTabItem))
                        {
                            MartinMetrics.Dataset = martin;
                            MartinPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(MartinTabItem, nodeCount));
            }

            // 5. Dead Code e Componenti Orfani
            if (!token.IsCancellationRequested && ShouldPrecompute(DeadCodeTabItem, s.ShowTabDeadCode, "DeadCode"))
            {
                var deadCode = DeadCodeAnalyzer.Analyze(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(DeadCodeTabItem))
                        {
                            DeadCode.Dataset = deadCode;
                            DeadCodePlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(DeadCodeTabItem, nodeCount));
            }

            // 6. Flusso Schermate (Screen Flow)
            if (!token.IsCancellationRequested && ShouldPrecompute(ScreenFlowTabItem, s.ShowTabScreenFlow, "ScreenFlow"))
            {
                var screenFlow = ScreenFlowBuilder.Build(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(ScreenFlowTabItem))
                        {
                            ScreenFlow.Graph = screenFlow;
                            ScreenFlowPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(ScreenFlowTabItem, nodeCount));
            }

            // 7. Shared Global State
            if (!token.IsCancellationRequested && ShouldPrecompute(SharedStateTabItem, s.ShowTabSharedState, "SharedState"))
            {
                var sharedState = SharedStateAnalyzer.Analyze(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(SharedStateTabItem))
                        {
                            SharedState.Dataset = sharedState;
                            SharedStatePlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(SharedStateTabItem, nodeCount));
            }

            // 8. Flussi Architetturali (Chord Diagram)
            if (!token.IsCancellationRequested && ShouldPrecompute(ArchitecturalFlowTabItem, s.ShowTabArchitecturalFlow, "ArchitecturalFlow"))
            {
                var archFlow = ArchitecturalFlowAnalyzer.Analyze(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(ArchitecturalFlowTabItem))
                        {
                            ArchitecturalFlow.Dataset = archFlow;
                            ArchitecturalFlowPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(ArchitecturalFlowTabItem, nodeCount));
            }

            // 9. CodeCity 3D / Isometrica
            if (!token.IsCancellationRequested && ShouldPrecompute(CodeCityTabItem, s.ShowTabCodeCity, "CodeCity"))
            {
                var codeCity = CodeCityBuilder.Build(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(CodeCityTabItem))
                        {
                            CodeCity.Dataset = codeCity;
                            CodeCityPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(CodeCityTabItem, nodeCount));
            }

            // 10. Temporal Coupling (Analyzing Git History on Disk)
            if (!token.IsCancellationRequested && ShouldPrecompute(TemporalCouplingTabItem, s.ShowTabTemporalCoupling, "TemporalCoupling"))
            {
                var commitChangelists = GitService.Instance.GetCommitFileChangelists(folder);
                var dataset = TemporalCouplingAnalyzer.Analyze(commitChangelists, targetGraph, folder);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(TemporalCouplingTabItem))
                        {
                            TemporalCoupling.Dataset = dataset;
                            TemporalCouplingPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(TemporalCouplingTabItem, nodeCount));
            }

            // 11. Cohesion & LCOM
            if (!token.IsCancellationRequested && ShouldPrecompute(CohesionTabItem, s.ShowTabCohesion, "Cohesion"))
            {
                var cohesion = CohesionCalculator.Analyze(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(CohesionTabItem))
                        {
                            Cohesion.Dataset = cohesion;
                            CohesionPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(CohesionTabItem, nodeCount));
            }

            // 12. Maintainability Index & Halstead
            if (!token.IsCancellationRequested && ShouldPrecompute(MaintainabilityTabItem, s.ShowTabMaintainability, "Maintainability"))
            {
                var maintainability = MaintainabilityCalculator.Analyze(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(MaintainabilityTabItem))
                        {
                            Maintainability.Dataset = maintainability;
                            MaintainabilityPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(MaintainabilityTabItem, nodeCount));
            }

            // 13. Regole Architetturali
            if (!token.IsCancellationRequested && ShouldPrecompute(ArchitecturalRulesTabItem, s.ShowTabArchitecturalRules, "ArchitecturalRules"))
            {
                var rulesReport = ArchitecturalRulesEngine.Evaluate(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(ArchitecturalRulesTabItem))
                        {
                            ArchitecturalRules.Report = rulesReport;
                            ArchitecturalRulesPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(ArchitecturalRulesTabItem, nodeCount));
            }

            // 14. Duplications and Clones (CPD)
            if (!token.IsCancellationRequested && ShouldPrecompute(CodeCloneTabItem, s.ShowTabCodeClone, "CodeClone"))
            {
                var cloneReport = CodeCloneDetector.DetectClones(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(CodeCloneTabItem))
                        {
                            CodeClone.Report = cloneReport;
                            CodeClonePlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(CodeCloneTabItem, nodeCount));
            }

            // 15. Social Code & Bus Factor
            if (!token.IsCancellationRequested && ShouldPrecompute(SocialCodeTabItem, s.ShowTabSocialCode, "SocialCode"))
            {
                var commitRecords = GitService.Instance.GetFileCommitHistory(folder);
                var socialDataset = SocialCodeAnalyzer.Analyze(targetGraph, commitRecords);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(SocialCodeTabItem))
                        {
                            SocialCode.Dataset = socialDataset;
                            SocialCodePlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(SocialCodeTabItem, nodeCount));
            }

            // 16. Data Flow & Taint Tracking
            if (!token.IsCancellationRequested && ShouldPrecompute(DataFlowTabItem, s.ShowTabDataFlow, "DataFlow"))
            {
                var dataFlowDataset = TaintAnalyzer.Analyze(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(DataFlowTabItem))
                        {
                            DataFlow.Dataset = dataFlowDataset;
                            DataFlowPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(DataFlowTabItem, nodeCount));
            }

            // 17. API Diff & SemVer
            if (!token.IsCancellationRequested && ShouldPrecompute(ApiDiffTabItem, s.ShowTabApiDiff, "ApiDiff"))
            {
                var baseline = _apiDiffBaselineGraph;
                var apiDiffReport = baseline != null ? ApiDiffAnalyzer.Compare(baseline, targetGraph) : null;
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(ApiDiffTabItem))
                        {
                            ApiDiff.Report = apiDiffReport;
                            ApiDiffPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(ApiDiffTabItem, nodeCount));
            }

            // 18. Matrice CRUD
            if (!token.IsCancellationRequested && ShouldPrecompute(CrudMatrixTabItem, s.ShowTabCrudMatrix, "CrudMatrix"))
            {
                var crudDataset = CrudMatrixAnalyzer.Analyze(targetGraph);
                if (!token.IsCancellationRequested)

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(CrudMatrixTabItem))
                        {
                            CrudMatrix.Dataset = crudDataset;
                            CrudMatrixPlaceholder?.SetState(isPending: false, isComputing: false);
                        }
                    });
                ReportStepCompletion(GetEstimatedAnalysisSeconds(CrudMatrixTabItem, nodeCount));
            }

            // 19. Griglia Risultati Blast Radius
            if (!token.IsCancellationRequested && ShouldPrecompute(GridTabItem, s.ShowTabBlastRadiusGrid, "BlastRadiusGrid"))
            {
                if (!token.IsCancellationRequested)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Contains(GridTabItem))
                        {
                            BlastRadiusGridPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(GridTabItem, nodeCount), GetComputingTabTitle(GridTabItem));
                            await PopulateBlastRadiusGridAsync();
                        }
                    });
                }
                ReportStepCompletion(GetEstimatedAnalysisSeconds(GridTabItem, nodeCount));
            }
        }, token);
    }

    /// <summary>
    /// Initialize event listeners for all analytic card placeholders.
    /// </summary>
    private void InitializeAnalyticsPlaceholders()
    {
        if (DsmPlaceholder != null && DsmTabItem != null) WirePlaceholder(DsmTabItem, DsmPlaceholder);
        if (TreemapPlaceholder != null && TreemapTabItem != null) WirePlaceholder(TreemapTabItem, TreemapPlaceholder);
        if (HotspotPlaceholder != null && HotspotTabItem != null) WirePlaceholder(HotspotTabItem, HotspotPlaceholder);
        if (ScreenFlowPlaceholder != null && ScreenFlowTabItem != null) WirePlaceholder(ScreenFlowTabItem, ScreenFlowPlaceholder);
        if (MartinPlaceholder != null && MartinTabItem != null) WirePlaceholder(MartinTabItem, MartinPlaceholder);
        if (DeadCodePlaceholder != null && DeadCodeTabItem != null) WirePlaceholder(DeadCodeTabItem, DeadCodePlaceholder);
        if (SharedStatePlaceholder != null && SharedStateTabItem != null) WirePlaceholder(SharedStateTabItem, SharedStatePlaceholder);
        if (TemporalCouplingPlaceholder != null && TemporalCouplingTabItem != null) WirePlaceholder(TemporalCouplingTabItem, TemporalCouplingPlaceholder);
        if (ArchitecturalFlowPlaceholder != null && ArchitecturalFlowTabItem != null) WirePlaceholder(ArchitecturalFlowTabItem, ArchitecturalFlowPlaceholder);
        if (CodeCityPlaceholder != null && CodeCityTabItem != null) WirePlaceholder(CodeCityTabItem, CodeCityPlaceholder);
        if (CohesionPlaceholder != null && CohesionTabItem != null) WirePlaceholder(CohesionTabItem, CohesionPlaceholder);
        if (MaintainabilityPlaceholder != null && MaintainabilityTabItem != null) WirePlaceholder(MaintainabilityTabItem, MaintainabilityPlaceholder);
        if (ArchitecturalRulesPlaceholder != null && ArchitecturalRulesTabItem != null) WirePlaceholder(ArchitecturalRulesTabItem, ArchitecturalRulesPlaceholder);
        if (CodeClonePlaceholder != null && CodeCloneTabItem != null) WirePlaceholder(CodeCloneTabItem, CodeClonePlaceholder);
        if (SocialCodePlaceholder != null && SocialCodeTabItem != null) WirePlaceholder(SocialCodeTabItem, SocialCodePlaceholder);
        if (DataFlowPlaceholder != null && DataFlowTabItem != null) WirePlaceholder(DataFlowTabItem, DataFlowPlaceholder);
        if (ApiDiffPlaceholder != null && ApiDiffTabItem != null) WirePlaceholder(ApiDiffTabItem, ApiDiffPlaceholder);
        if (CrudMatrixPlaceholder != null && CrudMatrixTabItem != null) WirePlaceholder(CrudMatrixTabItem, CrudMatrixPlaceholder);
        if (BlastRadiusGridPlaceholder != null && GridTabItem != null) WirePlaceholder(GridTabItem, BlastRadiusGridPlaceholder);
    }

    private void WirePlaceholder(TabItem tabItem, AnalyticsPlaceholderControl placeholder)
    {
        placeholder.StartAnalysisRequested += (_, _) =>
        {
            RunAnalyticsForTab(tabItem);
        };
        placeholder.StartAllRequested += (_, _) =>
        {
            StartAllPendingAnalytics();
        };
    }

    /// <summary>
    /// Start preprocessing for all currently pending analytics cards.
    /// </summary>
    private void StartAllPendingAnalytics()
    {
        if (_graph?.SemanticGraph is null) return;
        MarkAllAnalyticsTabsEnabled();
        _analyticsCts?.Cancel();
        _analyticsCts?.Dispose();
        _analyticsCts = new CancellationTokenSource();
        SetAllAnalyticsPlaceholdersPending();
        StartBackgroundAnalyticsPrecomputation(_analyticsCts.Token);
    }

    /// <summary>
    /// Handles clicking on the stop background scans button in the lower status bar.
    /// </summary>
    private void OnBackgroundAnalyticsStopButtonClicked(object? sender, RoutedEventArgs e)
    {
        StopBackgroundAnalytics();
    }

    /// <summary>
    /// Stops analysis processing in the background and resets the placeholders of pending uncompleted tabs.
    /// </summary>
    private void StopBackgroundAnalytics()
    {
        _analyticsCts?.Cancel();
        if (BackgroundAnalyticsPanel != null)

            BackgroundAnalyticsPanel.IsVisible = false;

        // Reset the state of placeholders for all tabs that are still dirty (not calculated)
        void ResetIfDirty(AnalyticsPlaceholderControl? placeholder, TabItem? tabItem)
        {
            if (placeholder != null && tabItem != null && _dirtyAnalyticsTabs.Contains(tabItem))

                placeholder.SetState(isPending: true, isComputing: false);
        }

        ResetIfDirty(DsmPlaceholder, DsmTabItem);
        ResetIfDirty(TreemapPlaceholder, TreemapTabItem);
        ResetIfDirty(HotspotPlaceholder, HotspotTabItem);
        ResetIfDirty(ScreenFlowPlaceholder, ScreenFlowTabItem);
        ResetIfDirty(MartinPlaceholder, MartinTabItem);
        ResetIfDirty(DeadCodePlaceholder, DeadCodeTabItem);
        ResetIfDirty(SharedStatePlaceholder, SharedStateTabItem);
        ResetIfDirty(TemporalCouplingPlaceholder, TemporalCouplingTabItem);
        ResetIfDirty(ArchitecturalFlowPlaceholder, ArchitecturalFlowTabItem);
        ResetIfDirty(CodeCityPlaceholder, CodeCityTabItem);
        ResetIfDirty(CohesionPlaceholder, CohesionTabItem);
        ResetIfDirty(MaintainabilityPlaceholder, MaintainabilityTabItem);
        ResetIfDirty(ArchitecturalRulesPlaceholder, ArchitecturalRulesTabItem);
        ResetIfDirty(CodeClonePlaceholder, CodeCloneTabItem);
        ResetIfDirty(SocialCodePlaceholder, SocialCodeTabItem);
        ResetIfDirty(DataFlowPlaceholder, DataFlowTabItem);
        ResetIfDirty(ApiDiffPlaceholder, ApiDiffTabItem);
        ResetIfDirty(CrudMatrixPlaceholder, CrudMatrixTabItem);
        ResetIfDirty(BlastRadiusGridPlaceholder, GridTabItem);

        StatusText.Text = LocalizationService.Instance["Status_BackgroundAnalyticsStopped"];
    }

    /// <summary>
    /// Performs on-demand asynchronous (Lazy) computation for the currently selected analytic tab
    /// if it is marked dirty and background processing is enabled.
    /// </summary>
    private void EnsureActiveTabAnalyticsLoaded()
    {
        if (_graph?.SemanticGraph is null || CentralTabControl == null) return;

        var selectedItem = CentralTabControl.SelectedItem;
        if (selectedItem == null || !_dirtyAnalyticsTabs.Contains(selectedItem))
            return;

        var s = SettingsService.Instance.Settings;
        var key = GetTabKey(selectedItem);
        if (!s.EnableBackgroundAnalytics && (key == null || !_enabledAnalyticsTabs.Contains(key)))

            return;

        RunAnalyticsForTab(selectedItem);
    }

    /// <summary>
    /// Performs on-demand analytical calculation for the specified single tab.
    /// </summary>
    private void RunAnalyticsForTab(object? selectedItem)
    {
        if (_graph?.SemanticGraph is null || selectedItem == null) return;
        if (!_dirtyAnalyticsTabs.Contains(selectedItem)) return;

        var key = GetTabKey(selectedItem);
        if (key != null)

            MarkAnalyticsTabEnabled(key);

        var targetGraph = GetTargetSemanticGraph();
        var nodeCount = targetGraph.Symbols.Count;

        if (selectedItem == DsmTabItem)
        {
            DsmPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(DsmTabItem, nodeCount), GetComputingTabTitle(DsmTabItem));
            _ = Task.Run(() =>
            {
                var matrix = DsmMatrixBuilder.Build(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(DsmTabItem);
                    Dsm.Matrix = matrix;
                    DsmPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == TreemapTabItem)
        {
            TreemapPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(TreemapTabItem, nodeCount), GetComputingTabTitle(TreemapTabItem));
            _ = Task.Run(() =>
            {
                var treemap = TreemapModelBuilder.Build(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(TreemapTabItem);
                    Treemap.RootItem = treemap;
                    TreemapPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == HotspotTabItem)
        {
            HotspotPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(HotspotTabItem, nodeCount), GetComputingTabTitle(HotspotTabItem));
            _ = Task.Run(() =>
            {
                var hotspot = HotspotAnalyzer.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(HotspotTabItem);
                    Hotspot.Dataset = hotspot;
                    HotspotPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == ScreenFlowTabItem)
        {
            ScreenFlowPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(ScreenFlowTabItem, nodeCount), GetComputingTabTitle(ScreenFlowTabItem));
            _ = Task.Run(() =>
            {
                var screenFlow = ScreenFlowBuilder.Build(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(ScreenFlowTabItem);
                    ScreenFlow.Graph = screenFlow;
                    ScreenFlowPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == MartinTabItem)
        {
            MartinPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(MartinTabItem, nodeCount), GetComputingTabTitle(MartinTabItem));
            _ = Task.Run(() =>
            {
                var martin = MartinMetricsCalculator.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(MartinTabItem);
                    MartinMetrics.Dataset = martin;
                    MartinPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == DeadCodeTabItem)
        {
            DeadCodePlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(DeadCodeTabItem, nodeCount), GetComputingTabTitle(DeadCodeTabItem));
            _ = Task.Run(() =>
            {
                var deadCode = DeadCodeAnalyzer.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(DeadCodeTabItem);
                    DeadCode.Dataset = deadCode;
                    DeadCodePlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == SharedStateTabItem)
        {
            SharedStatePlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(SharedStateTabItem, nodeCount), GetComputingTabTitle(SharedStateTabItem));
            _ = Task.Run(() =>
            {
                var sharedState = SharedStateAnalyzer.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(SharedStateTabItem);
                    SharedState.Dataset = sharedState;
                    SharedStatePlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == ArchitecturalFlowTabItem)
        {
            ArchitecturalFlowPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(ArchitecturalFlowTabItem, nodeCount), GetComputingTabTitle(ArchitecturalFlowTabItem));
            _ = Task.Run(() =>
            {
                var flow = ArchitecturalFlowAnalyzer.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(ArchitecturalFlowTabItem);
                    ArchitecturalFlow.Dataset = flow;
                    ArchitecturalFlowPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == CodeCityTabItem)
        {
            CodeCityPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(CodeCityTabItem, nodeCount), GetComputingTabTitle(CodeCityTabItem));
            _ = Task.Run(() =>
            {
                var city = CodeCityBuilder.Build(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(CodeCityTabItem);
                    CodeCity.Dataset = city;
                    CodeCityPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == TemporalCouplingTabItem)
        {
            TemporalCouplingPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(TemporalCouplingTabItem, nodeCount), GetComputingTabTitle(TemporalCouplingTabItem));
            var folder = _currentFolderPath;
            _ = Task.Run(() =>
            {
                var commitChangelists = GitService.Instance.GetCommitFileChangelists(folder);
                var dataset = TemporalCouplingAnalyzer.Analyze(commitChangelists, targetGraph, folder);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(TemporalCouplingTabItem);
                    TemporalCoupling.Dataset = dataset;
                    TemporalCouplingPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == CohesionTabItem)
        {
            CohesionPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(CohesionTabItem, nodeCount), GetComputingTabTitle(CohesionTabItem));
            _ = Task.Run(() =>
            {
                var cohesion = CohesionCalculator.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(CohesionTabItem);
                    Cohesion.Dataset = cohesion;
                    CohesionPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == MaintainabilityTabItem)
        {
            MaintainabilityPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(MaintainabilityTabItem, nodeCount), GetComputingTabTitle(MaintainabilityTabItem));
            _ = Task.Run(() =>
            {
                var maintainability = MaintainabilityCalculator.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(MaintainabilityTabItem);
                    Maintainability.Dataset = maintainability;
                    MaintainabilityPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == ArchitecturalRulesTabItem)
        {
            ArchitecturalRulesPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(ArchitecturalRulesTabItem, nodeCount), GetComputingTabTitle(ArchitecturalRulesTabItem));
            _ = Task.Run(() =>
            {
                var rules = ArchitecturalRulesEngine.Evaluate(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(ArchitecturalRulesTabItem);
                    ArchitecturalRules.Report = rules;
                    ArchitecturalRulesPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == CodeCloneTabItem)
        {
            CodeClonePlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(CodeCloneTabItem, nodeCount), GetComputingTabTitle(CodeCloneTabItem));
            _ = Task.Run(() =>
            {
                var clones = CodeCloneDetector.DetectClones(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(CodeCloneTabItem);
                    CodeClone.Report = clones;
                    CodeClonePlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == SocialCodeTabItem)
        {
            SocialCodePlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(SocialCodeTabItem, nodeCount), GetComputingTabTitle(SocialCodeTabItem));
            var folder = _currentFolderPath;
            _ = Task.Run(() =>
            {
                var commitRecords = GitService.Instance.GetFileCommitHistory(folder);
                var dataset = SocialCodeAnalyzer.Analyze(targetGraph, commitRecords);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(SocialCodeTabItem);
                    SocialCode.Dataset = dataset;
                    SocialCodePlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == DataFlowTabItem)
        {
            DataFlowPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(DataFlowTabItem, nodeCount), GetComputingTabTitle(DataFlowTabItem));
            _ = Task.Run(() =>
            {
                var dataset = TaintAnalyzer.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(DataFlowTabItem);
                    DataFlow.Dataset = dataset;
                    DataFlowPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == ApiDiffTabItem)
        {
            ApiDiffPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(ApiDiffTabItem, nodeCount), GetComputingTabTitle(ApiDiffTabItem));
            var baseline = _apiDiffBaselineGraph;
            _ = Task.Run(() =>
            {
                var report = baseline != null ? ApiDiffAnalyzer.Compare(baseline, targetGraph) : null;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(ApiDiffTabItem);
                    ApiDiff.Report = report;
                    ApiDiffPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == CrudMatrixTabItem)
        {
            CrudMatrixPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(CrudMatrixTabItem, nodeCount), GetComputingTabTitle(CrudMatrixTabItem));
            _ = Task.Run(() =>
            {
                var dataset = CrudMatrixAnalyzer.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dirtyAnalyticsTabs.Remove(CrudMatrixTabItem);
                    CrudMatrix.Dataset = dataset;
                    CrudMatrixPlaceholder?.SetState(isPending: false, isComputing: false);
                });
            });
        }
        else if (selectedItem == GridTabItem)
        {
            BlastRadiusGridPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(GridTabItem, nodeCount), GetComputingTabTitle(GridTabItem));
            _ = Task.Run(async () =>
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await PopulateBlastRadiusGridAsync();
                });
            });
        }
    }

    /// <summary>
    /// Calculates the estimated duration in seconds for scanning a specific tab based on the number of nodes in the workspace.
    /// </summary>
    private static double GetEstimatedAnalysisSeconds(TabItem tabItem, int nodeCount)
    {
        var nodeFactor = Math.Min(6.0, nodeCount / 350.0);

        if (tabItem.Name is "DsmTabItem" or "TreemapTabItem" or "MartinTabItem" or "ScreenFlowTabItem")
            return Math.Round(0.4 + 0.3 * nodeFactor, 1);

        if (tabItem.Name is "DeadCodeTabItem" or "SharedStateTabItem" or "ArchitecturalFlowTabItem" or "CohesionTabItem" or "MaintainabilityTabItem")
            return Math.Round(0.6 + 0.5 * nodeFactor, 1);

        if (tabItem.Name is "CodeCityTabItem" or "CrudMatrixTabItem" or "DataFlowTabItem" or "GridTabItem")
            return Math.Round(0.8 + 0.7 * nodeFactor, 1);

        // Algoritmi più complessi (CPD cloni, analisi VCS git history, linter regole)
        return Math.Round(1.2 + 1.2 * nodeFactor, 1);
    }

    /// <summary>
    /// Returns the localized dynamic title to display when calculating the analysis for the specified tab.
    /// </summary>
    private string GetComputingTabTitle(TabItem tabItem)
    {
        var title = GetTabTitle(tabItem);
        if (!string.IsNullOrWhiteSpace(title))

            return LocalizationService.Instance.Get("Analytics_ComputingNamedTitle", title);
        return LocalizationService.Instance["Analytics_ComputingTitle"];
    }

    /// <summary>
    /// Dynamically populate individual project visibility selectors (Project Explorer and View Menu)
    /// when there are multiple projects in the current workspace.
    /// </summary>
    private void UpdateProjectsFilterUI()
    {
        if (_graph is null)
        {
            ProjectExplorerControl.UpdateProjectFilterBar(false, "");
            FilterProjectsMenuItem.IsVisible = false;
            FilterProjectsMenuItem.Items.Clear();
            return;
        }

        var projectNodes = _graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        if (projectNodes.Count <= 1)
        {
            ProjectExplorerControl.UpdateProjectFilterBar(false, "");
            FilterProjectsMenuItem.IsVisible = false;
            FilterProjectsMenuItem.Items.Clear();
            return;
        }

        FilterProjectsMenuItem.IsVisible = true;

        // Populate the View -> Filter Projects drop-down menu items
        FilterProjectsMenuItem.Items.Clear();
        foreach (var projNode in projectNodes)
        {
            var pId = projNode.Id;
            var isVis = Graph.IsProjectVisible(pId);

            var mi = new MenuItem
            {
                Header = projNode.Name,
                Tag = pId
            };
            UpdateMenuItemCheck(mi, isVis);
            mi.Click += (_, _) =>
            {
                var next = !Graph.IsProjectVisible(pId);
                Graph.SetProjectVisibility(pId, next);
            };
            FilterProjectsMenuItem.Items.Add(mi);
        }

        UpdateProjectsBadgeText();
    }

    /// <summary>
    /// Updates the checkmark icon for a menu item (<see cref="MenuItem"/>) based on the specified Boolean value.
    /// </summary>
    /// <param name="mi">The menu item to update.</param>
    /// <param name="isChecked"><c>true</c> to show the check icon; <c>false</c> to remove it.</param>
    private void UpdateMenuItemCheck(MenuItem mi, bool isChecked)
    {
        if (isChecked)
        {
            if (this.TryFindResource("IconCheck", out var res) && res is StreamGeometry geo)

                mi.Icon = new PathIcon
                {
                    Data = geo,
                    Width = 12,
                    Height = 12,
                    Foreground = this.FindResource("ThemeAccentBrush") as IBrush ?? Brushes.DodgerBlue
                };
        }
        else
        {
            mi.Icon = null;
        }
    }

    /// <summary>
    /// Update the information text of the projects badge (e.g. "Projects: 2/3 visible") and enable/disable the "All" button.
    /// </summary>
    private void UpdateProjectsBadgeText()
    {
        var (vis, total) = Graph.GetProjectCounts();
        ProjectExplorerControl.UpdateProjectFilterBar(total > 1, LocalizationService.Instance.Get("Explorer_ProjectsVisible", vis, total));
    }

    /// <summary>
    /// Manages the selection request for all projects sent by the Project Explorer.
    /// </summary>
    private void OnExplorerSelectAllProjectsRequested(object? sender, EventArgs e)
    {
        OnProjectsSelectAllClicked(sender, new RoutedEventArgs());
    }

    /// <summary>
    /// Manages the visibility change of a project coming from the checkbox in the Project Explorer.
    /// </summary>
    private void OnExplorerProjectVisibilityChanged(object? sender, ExplorerTreeItem item)
    {
        if (item.ProjectId.HasValue)

            Graph.SetProjectVisibility(item.ProjectId.Value, item.IsProjectVisible);
    }

    /// <summary>
    /// Invoked when the visibility of a project changes in the graph.
    /// Synchronize the state of items in the Project Explorer, menu items, statistics and invalidate analytical views.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="projectId">The unique identifier of the project whose visibility has changed.</param>
    private void OnGraphProjectVisibilityChanged(object? sender, Guid projectId)
    {
        // Synchronize the state of nodes in the ExplorerTree
        if (ProjectExplorerControl.ItemsSource is IEnumerable<ExplorerTreeItem> rootItems)
        {
            void SyncTreeItemVisibility(ExplorerTreeItem item)
            {
                if (item.IsProjectRoot && item.ProjectId.HasValue)

                    item.IsProjectVisible = Graph.IsProjectVisible(item.ProjectId.Value);
                foreach (var child in item.Children)
                {
                    SyncTreeItemVisibility(child);
                }
            }

            foreach (var root in rootItems)
            {
                SyncTreeItemVisibility(root);
            }
        }

        // Synchronize check icons in MenuItems
        foreach (var item in FilterProjectsMenuItem.Items)
        {
            if (item is MenuItem mi && mi.Tag is Guid pId)
            {
                var isVis = Graph.IsProjectVisible(pId);
                UpdateMenuItemCheck(mi, isVis);
            }
        }

        UpdateProjectsBadgeText();
        UpdateStatsBar();
        BlastRadiusGridControl.SetHiddenProjects(Graph.HiddenProjectIds);
        InvalidateAnalyticsViews();
    }

    /// <summary>
    /// Manages clicking on the "All" button in the Explorer project bar, restoring the visibility of all projects in the workspace.
    /// </summary>
    private void OnProjectsSelectAllClicked(object? sender, RoutedEventArgs e)
    {
        Graph.ClearHiddenProjects();
    }

    #endregion

    #region Stock Graph & Impact Analysis

    /// <summary>
    /// Calculate and highlight the Blast Radius for the selected node asynchronously with loading overlay.
    /// If the result is already in cache, it is applied instantly without showing the overlay.
    /// </summary>
    private async void OnBlastRadiusClicked(object? sender = null, RoutedEventArgs? e = null)
    {
        var localizationService = LocalizationService.Instance;
        if (_selectedNode?.SemanticSymbol is null || _graph?.Queries is null)
        {
            StatusText.Text = localizationService["Status_SelectSymbolForBlast"];
            return;
        }

        var node = _selectedNode;
        var sym = node.SemanticSymbol;

        // If already calculated and present in cache, apply instantly (0 ms)
        if (_graph.Queries.TryGetCachedImpactReport(sym, 10, out var cachedReport) && cachedReport != null)
        {
            Graph.HighlightBlastRadius(cachedReport);
            SymbolInspectorControl.UpdateImpactSection(cachedReport);
            StatusText.Text = localizationService.Get("Status_BlastReportFormat", 
                node.QualifiedName, 
                cachedReport.DirectCallers.Count,
                cachedReport.IndirectCallers.Count, 
                cachedReport.AffectedForms.Count,
                localizationService.GetRiskLevel(cachedReport.RiskLevel));
            return;
        }

        ShowLoading(localizationService["Loading_BlastRadiusTitle"], localizationService.Get("Loading_BlastRadiusSub", node.Name));
        await Task.Delay(16);

        try
        {
            var report = await Task.Run(() => _graph.Queries.GetImpactReport(sym));

            if (_selectedNode?.Id == node.Id)
            {
                Graph.HighlightBlastRadius(report);
                SymbolInspectorControl.UpdateImpactSection(report);
                StatusText.Text = localizationService.Get("Status_BlastReportFormat", 
                    node.QualifiedName,
                    report.DirectCallers.Count,
                    report.IndirectCallers.Count, 
                    report.AffectedForms.Count, 
                    localizationService.GetRiskLevel(report.RiskLevel));
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("Status_BlastError", ex.Message);
        }
        finally
        {
            HideLoading();
        }
    }

    /// <summary>
    /// Highlights on the graph all the routines and classes called by the selected routine/container (callees).
    /// </summary>
    private void OnShowCalleesClicked(object? sender = null, RoutedEventArgs? e = null)
    {
        var localizationService = LocalizationService.Instance;
        if (_selectedNode is null)
        {
            StatusText.Text = localizationService["Status_SelectSymbolForCallees"];
            return;
        }

        var callees = Graph.HighlightCalleesForSelected();
        if (callees.Count > 0)
        {
            var names = string.Join(", ", callees.Take(4).Select(c => c.Name));
            if (callees.Count > 4) names += $" +{callees.Count - 4}...";
            StatusText.Text = localizationService.Get("Status_CalleesFound", _selectedNode.Name, callees.Count, names);
        }
        else
        {
            StatusText.Text = localizationService.Get("Status_CalleesNone", _selectedNode.Name);
        }
    }

    /// <summary>
    /// Highlights all the callers of the selected routine/container (callers) on the graph.
    /// </summary>
    private void OnShowCallersClicked(object? sender = null, RoutedEventArgs? e = null)
    {
        var localizationService = LocalizationService.Instance;
        if (_selectedNode is null)
        {
            StatusText.Text = localizationService["Status_SelectSymbolForCallers"];
            return;
        }

        var callers = Graph.HighlightCallersForSelected();
        if (callers.Count > 0)
        {
            var names = string.Join(", ", callers.Take(4).Select(c => c.Name));
            if (callers.Count > 4) names += $" +{callers.Count - 4}...";
            StatusText.Text = localizationService.Get("Status_CallersFound", _selectedNode.Name, callers.Count, names);
        }
        else
        {
            StatusText.Text = localizationService.Get("Status_CallersNone", _selectedNode.Name);
        }
    }

    /// <summary>
    /// Handles the selection change event of a node in the graph control (<see cref="Controls.GraphControl"/>).
    /// Updates the current selected node, synchronizes the row in the Blast Radius grid, and starts inspection in the Symbol Inspector.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="node">The node selected in the graph, or <c>null</c> if the selection has been reset.</param>
    private void OnGraphSelectionChanged(object? sender, GraphNode? node)
    {
        _selectedNode = node;

        if (!_isSyncingSelection && node != null)

            BlastRadiusGridControl.SelectNode(node.Id);

        if (node is null)
        {
            _selectionCts?.Cancel();
            ShowEmptyInspector();
            StatusText.Text = LocalizationService.Instance["Inspector_NoSelection"];
        }
        else
        {
            SelectAndInspectNodeAsync(node);
            StatusText.Text = LocalizationService.Instance.Get("Status_Selected", node.Kind, node.QualifiedName);
        }
    }

    /// <summary>
    /// Select a node, expand the parent container if necessary, center the graph view on it
    /// and synchronizes the ScreenFlow view if the node corresponds to a form.
    /// </summary>
    /// <param name="node">The graph node to select and focus on.</param>
    private void SelectAndFocusGraphNode(GraphNode node)
    {
        if (_graph is null) return;

        // If it is an internal member, make sure the parent container is expanded
        if (node.ParentId.HasValue)
        {
            var parent = _graph.GetNode(node.ParentId.Value);
            if (parent is not null)
                parent.IsExpanded = true;
        }

        Graph.FocusOnNode(node.Id);
        Graph.SelectNode(node);

        // Select and frame the screen if present in the ScreenFlow graph
        var screenId = node.Id;
        if (ScreenFlow.Graph?.FindScreen(screenId) is null && node.ParentId.HasValue)

            screenId = node.ParentId.Value;
        if (ScreenFlow.Graph?.FindScreen(screenId) is not null)
        {
            ScreenFlow.SelectScreen(screenId);
            ScreenFlow.FocusOnScreen(screenId);
        }

        StatusText.Text = LocalizationService.Instance.Get("Status_Selected", node.Kind, node.QualifiedName);
    }

    #endregion

    #region Project Explorer & File and Folder Interaction

    /// <summary>
    /// Manages the selection variation in the Project Explorer tree view, selecting and centering the corresponding node in the graph.
    /// </summary>
    private void OnExplorerSelectionChanged(object? sender, ExplorerTreeItem? item)
    {
        if (item is null || _graph is null) return;
        var node = _graph.GetNode(item.NodeId);
        if (node is not null)
            SelectAndFocusGraphNode(node);
    }

    /// <summary>
    /// Manages double clicking on an element of the Project Explorer tree.
    /// </summary>
    private void OnExplorerItemDoubleTapped(object? sender, ExplorerTreeItem item)
    {
        if (item.IsGroup) return; // double-clicking group expands/collapses tree

        if (!string.IsNullOrEmpty(item.FilePath))

            OpenOriginalFile(item.FilePath, item.LineStart);

        else if (_graph != null)
        {
            var node = _graph.GetNode(item.NodeId);
            if (node?.SemanticSymbol?.FilePath != null)

                OpenOriginalFile(node.SemanticSymbol.FilePath, node.SemanticSymbol.LineStart);

            else if (!string.IsNullOrEmpty(_currentFolderPath))

    OpenContainingFolder(_currentFolderPath);
        }
    }

    /// <summary>
    /// Handles the request to open the original file from Explorer.
    /// </summary>
    private void OnExplorerOpenFileRequested(object? sender, ExplorerTreeItem item)
    {
        if (!item.IsGroup && item.HasFile)

            OpenOriginalFile(item.FilePath, item.LineStart);
    }

    /// <summary>
    /// Manages the request to open the containing folder from the Explorer.
    /// </summary>
    private void OnExplorerOpenFolderRequested(object? sender, ExplorerTreeItem item)
    {
        if (!item.IsGroup && item.HasFile)

            OpenContainingFolder(item.FilePath);
    }

    /// <summary>
    /// Handles the request to copy file path to clipboard from Explorer.
    /// </summary>
    private void OnExplorerCopyPathRequested(object? sender, ExplorerTreeItem item)
    {
        if (!item.IsGroup && item.HasFile)

            CopyFilePathToClipboard(item.FilePath);
    }

    /// <summary>
    /// Handles the original file open request from the Inspector.
    /// </summary>
    private void OnInspectorOpenFileRequested(object? sender, EventArgs e)
    {
        var filePath = _selectedNode?.SemanticSymbol?.FilePath;
        var line = _selectedNode?.SemanticSymbol?.LineStart ?? 0;
        OpenOriginalFile(filePath, line);
    }

    /// <summary>
    /// Manages the request to open the containing folder from the Inspector.
    /// </summary>
    private void OnInspectorOpenFolderRequested(object? sender, EventArgs e)
    {
        var filePath = _selectedNode?.SemanticSymbol?.FilePath;
        OpenContainingFolder(filePath);
    }

    /// <summary>
    /// Handles the request to copy the Context Slice to the clipboard from the Inspector.
    /// </summary>
    private async void OnInspectorCopySliceRequested(object? sender, EventArgs e)
    {
        await CopyLlmSliceToClipboardAsync();
    }

    /// <summary>
    /// Handles the request to export the Context Slice in Markdown format from the Inspector.
    /// </summary>
    private async void OnInspectorSaveSliceRequested(object? sender, EventArgs e)
    {
        await ExportLlmSliceAsync();
    }

    /// <summary>
    /// Handles source code snippet copy notification from the Inspector.
    /// </summary>
    private void OnInspectorCopyCodeRequested(object? sender, string code)
    {
        if (_selectedNode != null)

            StatusText.Text = LocalizationService.Instance.Get("Status_CopiedCodeSnippet", _selectedNode.Name);
    }

    #endregion

    #region Graph Nodes Contextual Menu

    /// <summary>
    /// Manages the request to open the context menu on the graph canvas (<see cref="Controls.GraphControl"/>).
    /// Select the node below the cursor and show the related analysis and export actions.
    /// </summary>
    private void OnGraphContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var hitNode = e.TryGetPosition(Graph, out var screenPos)
            ? Graph.GetNodeAtScreenPos(screenPos)
            : Graph.SelectedNode ?? _selectedNode;

        // If no node is present under the mouse cursor, do not open the context menu
        if (hitNode == null)
        {
            e.Handled = true;
            return;
        }

        // Explicitly select the node that was right-clicked
        Graph.SelectNode(hitNode);

        if (GraphContainerGrid.Resources.TryGetResource("GraphNodeContextMenu", null, out var res) && res is ContextMenu menu)
        {
            menu.DataContext = hitNode;
            menu.PlacementTarget = Graph;
            menu.Open(Graph);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles the "Export Context Slice LLM (Markdown)" command from the graph node context menu.
    /// </summary>
    private async void OnNodeExportMarkdownClicked(object? sender, RoutedEventArgs e) => await ExportLlmSliceAsync();

    /// <summary>
    /// Handles the "Export Node Data (JSON)" command from the graph node context menu.
    /// </summary>
    private async void OnNodeExportJsonClicked(object? sender, RoutedEventArgs e) => await ExportNodeJsonAsync();

    /// <summary>
    /// Export the details of the node and its inbound/outbound relationships in JSON (.json) format.
    /// </summary>
    private async Task ExportNodeJsonAsync(GraphNode? node = null)
    {
        node ??= Graph.SelectedNode ?? _selectedNode;
        if (node == null)
        {
            StatusText.Text = LocalizationService.Instance["Status_NoSymbolSelected"];
            return;
        }

        var semGraph = _graph?.SemanticGraph;
        var symbol = node.SemanticSymbol;

        string json;
        if (semGraph != null && symbol != null)

            json = CodeGraphJsonExporter.ExportSliceToJson(semGraph, symbol, indented: true);

        else
        {
            var filePath = symbol?.FilePath;
            var lineStart = symbol?.LineStart ?? 0;
            var lineEnd = symbol?.LineEnd ?? 0;
            var childCount = _graph != null ? _graph.GetChildren(node.Id).Count() : 0;

            var nodeData = new
            {
                id = node.Id,
                name = node.Name,
                qualifiedName = node.QualifiedName,
                kind = node.Kind.ToString(),
                projectName = node.ProjectName,
                filePath = filePath,
                lineStart = lineStart,
                lineEnd = lineEnd,
                isContainer = node.IsContainer,
                childCount = childCount
            };
            json = System.Text.Json.JsonSerializer.Serialize(nodeData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        var defaultName = !string.IsNullOrEmpty(node.QualifiedName)
            ? $"{node.QualifiedName}_node.json"
            : $"{node.Name}_node.json";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Instance["Ctx_Node_ExportJson"],
            DefaultExtension = "json",
            SuggestedFileName = defaultName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON Node Data (*.json)") { Patterns = new[] { "*.json" } }
            }
        });

        if (file == null) return;

        var localizationService = LocalizationService.Instance;
        ShowLoading(localizationService["Loading_ExportTitle"], localizationService.Get("Loading_ExportSub", file.Name), canCancel: false);
        await Task.Delay(16);

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            await writer.WriteAsync(json);

            var fullPath = file.TryGetLocalPath() ?? file.Path?.LocalPath ?? file.Name;
            StatusText.Text = localizationService.Get("Status_ExportSuccess", file.Name);
            ShowExportSuccessModal(file.Name, fullPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("Status_ErrorExportJson", ex.Message);
        }
        finally
        {
            HideLoading();
        }
    }

    #endregion

    /// <summary>
    /// Opens the specified source file using the operating system's default application or IDE.
    /// </summary>
    /// <param name="filePath">The relative or absolute path of the source file to open.</param>
    /// <param name="line">The initial line to position at (optional).</param>
    private void OpenOriginalFile(string? filePath, int line = 0)
    {
        var localizationService = LocalizationService.Instance;
        var resolved = ResolveEffectiveFilePath(filePath);

        if (string.IsNullOrEmpty(resolved) || (!File.Exists(resolved) && !Directory.Exists(resolved)))
        {
            StatusText.Text = localizationService.Get("Explorer_FileNotFound", filePath ?? localizationService["Explorer_Title"]);
            return;
        }

        if (Directory.Exists(resolved) && !File.Exists(resolved))
        {
            OpenContainingFolder(resolved);
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = resolved,
                UseShellExecute = true
            };
            Process.Start(psi);
            StatusText.Text = localizationService.Get("Explorer_FileOpened", Path.GetFileName(resolved));
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("Status_ErrorOpenFile", ex.Message);
        }
    }

    /// <summary>
    /// Opens the folder containing the specified path in File Explorer (Windows Explorer).
    /// If <paramref name="filePath"/> is a file, the file is automatically pre-selected.
    /// </summary>
    /// <param name="filePath">The path to the file or folder to reveal in the file manager.</param>
    private void OpenContainingFolder(string? filePath)
    {
        var localizationService = LocalizationService.Instance;
        var resolved = ResolveEffectiveFilePath(filePath);
        if (string.IsNullOrEmpty(resolved))
        {
            if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
                resolved = _currentFolderPath;
            else return;
        }

        try
        {
            if (File.Exists(resolved))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{resolved}\"",
                    UseShellExecute = true
                });
                StatusText.Text = localizationService.Get("Explorer_FolderOpened", Path.GetFileName(resolved));
            }
            else if (Directory.Exists(resolved))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = resolved,
                    UseShellExecute = true
                });
                StatusText.Text = localizationService.Get("Explorer_FolderOpened", resolved);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("Status_ErrorOpenFolder", ex.Message);
        }
    }

    /// <summary>
    /// Copies the resolved absolute path of the specified file to the system clipboard.
    /// </summary>
    /// <param name="filePath">The relative or absolute path of the file to copy.</param>
    private async void CopyFilePathToClipboard(string? filePath)
    {
        var localizationService = LocalizationService.Instance;
        var resolved = ResolveEffectiveFilePath(filePath);
        if (string.IsNullOrEmpty(resolved)) return;

        try
        {
            if (Clipboard != null)
            {
                await Clipboard.SetTextAsync(resolved);
                StatusText.Text = localizationService.Get("Explorer_PathCopied", resolved);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("Status_ErrorCopyPath", ex.Message);
        }
    }

    /// <summary>
    /// Resolves a relative file path to the absolute path on disk, using the current project folder as the base.
    /// </summary>
    /// <param name="filePath">The relative or absolute path to resolve.</param>
    /// <returns>The normalized absolute path, or <c>null</c> if not resolvable.</returns>
    private string? ResolveEffectiveFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))

            return _currentFolderPath;

        if (Path.IsPathRooted(filePath) && (File.Exists(filePath) || Directory.Exists(filePath)))

            return filePath;

        if (!string.IsNullOrEmpty(_currentFolderPath))
        {
            var combined = Path.Combine(_currentFolderPath, filePath);
            if (File.Exists(combined) || Directory.Exists(combined))
                return combined;
            return combined;
        }

        return filePath;
    }

    #region Ricerca Globale & Command Palette

    private record AppSearchTarget(
        string Title,
        string Subtitle,
        string BadgeText,
        SearchResultCategory Category,
        string[] Keywords,
        Action Action);

    /// <summary>
    /// Constructs and returns the complete list of application search targets (Settings, Analytical Views, Help, Quick Actions)
    /// localized to the current language for the global Command Palette.
    /// </summary>
    /// <returns>The list of registered <see cref="AppSearchTarget"/>.</returns>
    private List<AppSearchTarget> GetAppSearchTargets()
    {
        var localizationService = LocalizationService.Instance;
        var isEn = localizationService.CurrentLanguage == AppLanguage.English;

        return new List<AppSearchTarget>
        {
            // --- SETTINGS SCREENS ---
            new AppSearchTarget(
                isEn ? "Settings" : "Impostazioni",
                isEn ? "Open application settings and preferences (Ctrl+,)" : "Apri il pannello delle impostazioni e preferenze (Ctrl+,)",
                isEn ? "Settings" : "Impostazioni",
                SearchResultCategory.Settings,
                new[] { "impostazioni", "settings", "preferenze", "configurazione", "opzioni", "config", "preferences" },
                () => OpenSettings(0)),

            new AppSearchTarget(
                isEn ? "Settings: Appearance & Themes" : "Impostazioni: Aspetto & Temi",
                isEn ? "Change visual theme (Dark Modern, Midnight Blue, Dracula, Light)" : "Personalizza il tema grafico (Dark Modern, Midnight Blue, Dracula, Light)",
                isEn ? "Settings" : "Impostazioni",
                SearchResultCategory.Settings,
                new[] { "tema", "temi", "aspetto", "colori", "theme", "dark", "light", "dracula", "midnight" },
                () => OpenSettings(0)),

            new AppSearchTarget(
                isEn ? "Settings: Language" : "Impostazioni: Lingua",
                isEn ? "Change application language (Italiano, English)" : "Cambia lingua dell'interfaccia (Italiano, English)",
                isEn ? "Settings" : "Impostazioni",
                SearchResultCategory.Settings,
                new[] { "lingua", "language", "italiano", "inglese", "english", "traduzione", "localizzazione" },
                () => OpenSettings(1)),

            new AppSearchTarget(
                isEn ? "Settings: Behavior" : "Impostazioni: Comportamento",
                isEn ? "Configure hover selection and UI interaction" : "Configura selezione al passaggio del mouse e interazioni UI",
                isEn ? "Settings" : "Impostazioni",
                SearchResultCategory.Settings,
                new[] { "comportamento", "behavior", "hover", "mouse", "interazione", "selezione" },
                () => OpenSettings(2)),

            new AppSearchTarget(
                isEn ? "Settings: Keyboard Shortcuts" : "Impostazioni: Scorciatoie da Tastiera",
                isEn ? "View all available keyboard shortcuts and keybindings" : "Elenco completo delle scorciatoie da tastiera e combinazioni di tasti",
                isEn ? "Settings" : "Impostazioni",
                SearchResultCategory.Settings,
                new[] { "scorciatoie", "shortcuts", "tasti", "tastiera", "keyboard", "hotkeys", "comandi" },
                () => OpenSettings(3)),

            // --- VISTE CENTRALI ---
            new AppSearchTarget(
                isEn ? "View: Dependency Graph" : "Vista: Grafo delle Dipendenze",
                isEn ? "Interactive node-link graph showing calls and relationships" : "Grafo interattivo delle relazioni e chiamate tra classi, moduli e form",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "grafo", "graph", "dipendenze", "relazioni", "chiamate", "nodi", "dependencies" },
                () => CentralTabControl.SelectedItem = GraphTabItem),

            new AppSearchTarget(
                isEn ? "View: DSM Matrix" : "Vista: Matrice DSM",
                isEn ? "Dependency Structure Matrix for coupling and cycle analysis" : "Matrice delle dipendenze per analisi di accoppiamento e cicli",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "dsm", "matrice", "matrix", "cicli", "accoppiamento", "struttura" },
                () => CentralTabControl.SelectedItem = DsmTabItem),

            new AppSearchTarget(
                isEn ? "View: Treemap" : "Vista: Treemap Gerarchica",
                isEn ? "Proportional metric treemap by LOC size and complexity" : "Mappa ad albero gerarchica proporzionale per righe di codice e complessità",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "treemap", "albero", "loc", "complessita", "dimensioni", "metriche", "size" },
                () => CentralTabControl.SelectedItem = TreemapTabItem),

            new AppSearchTarget(
                isEn ? "View: Hotspot Risk Matrix" : "Vista: Matrice Hotspot",
                isEn ? "4-Quadrant risk matrix (Complexity vs Churn / Fan-In)" : "Grafico cartesiano a 4 quadranti per individuare componenti ad alto rischio",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "hotspot", "rischio", "fan-in", "churn", "criticita", "risk" },
                () => CentralTabControl.SelectedItem = HotspotTabItem),

            new AppSearchTarget(
                isEn ? "View: Screen Flow" : "Vista: Flusso Schermate",
                isEn ? "UI form navigation diagram, modal dialogs and screen transitions" : "Diagramma di flusso e navigazione tra Form, finestre e dialoghi UI",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "flusso schermate", "screenflow", "schermate", "form", "finestre", "dialoghi", "ui", "screens", "flow" },
                () => CentralTabControl.SelectedItem = ScreenFlowTabItem),

            new AppSearchTarget(
                isEn ? "View: Control Flow Graph (CFG)" : "Vista: Control Flow Graph (CFG)",
                isEn ? "Control flow graph of routines, branching and basic blocks" : "Grafo del flusso di controllo per Sub, Function e Property",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "control flow", "cfg", "flusso controllo", "blocchi", "rami", "routine" },
                () => CentralTabControl.SelectedItem = ControlFlowTabItem),

            new AppSearchTarget(
                isEn ? "View: Martin Metrics" : "Vista: Metriche Martin",
                isEn ? "Abstractness vs Instability scatter plot and Main Sequence analysis" : "Diagramma di Martin (Astrattezza vs Instabilità e Sequenza Principale)",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "martin", "metriche martin", "abstractness", "instability", "astrattezza", "instabilita", "main sequence", "sofferenza", "pain" },
                () => CentralTabControl.SelectedItem = MartinTabItem),

            new AppSearchTarget(
                isEn ? "View: Dead Code & Orphans" : "Vista: Dead Code & Orfani",
                isEn ? "Dead code detection, unreachable routines and orphan components" : "Analisi codice morto, routine non raggiungibili e componenti orfani",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "dead code", "orfani", "orphans", "codice morto", "unreachable", "non raggiungibile", "inutilizzato" },
                () => CentralTabControl.SelectedItem = DeadCodeTabItem),

            new AppSearchTarget(
                isEn ? "View: Shared State" : "Vista: Stato Condiviso",
                isEn ? "Global and shared mutable state matrix, readers/writers and mutation risk" : "Matrice stato globale condiviso, lettori/scrittori e rischio mutazioni cross-module",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "shared state", "stato condiviso", "variabili globali", "static", "mutazioni", "global", "side effects" },
                () => CentralTabControl.SelectedItem = SharedStateTabItem),

            new AppSearchTarget(
                isEn ? "View: Temporal Coupling" : "Vista: Temporal Coupling",
                isEn ? "VCS commit co-change history, Jaccard similarity and hidden couplings" : "Analisi co-modifiche storiche da commit Git, indice Jaccard e accoppiamenti nascosti",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "temporal coupling", "co-change", "git", "commit", "jaccard", "hidden coupling", "accoppiamento temporale" },
                () => CentralTabControl.SelectedItem = TemporalCouplingTabItem),

            new AppSearchTarget(
                isEn ? "View: Architectural Flow" : "Vista: Flussi Architetturali",
                isEn ? "Module-to-module Chord diagram, call volumes and architectural layer boundaries" : "Diagramma Chord delle chiamate tra moduli, volumi di flusso e violazioni di layer",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "flussi", "flow", "chord", "sankey", "layer", "architettura", "volumi", "chiamate", "architectural flow" },
                () => CentralTabControl.SelectedItem = ArchitecturalFlowTabItem),

            new AppSearchTarget(
                isEn ? "View: CodeCity 3D" : "Vista: CodeCity 3D",
                isEn ? "3D / Isometric 2.5D polymetric city map (Building size = LOC, Height = Complexity)" : "Mappa urbana 3D/2.5D polimetrica (Dimensione base = LOC, Altezza = Complessità)",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "codecity", "city", "citta", "3d", "isometric", "isometria", "edifici", "polimetrica", "urban" },
                () => CentralTabControl.SelectedItem = CodeCityTabItem),

            new AppSearchTarget(
                isEn ? "View: Cohesion (LCOM4)" : "Vista: Coesione (LCOM4)",
                isEn ? "Class cohesion and separation of responsibilities analysis" : "Analisi di coesione interna dei moduli e classi (LCOM4, Chidamber & Kemerer)",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "coesione", "cohesion", "lcom", "lcom4", "responsabilita", "classi" },
                () => CentralTabControl.SelectedItem = CohesionTabItem),

            new AppSearchTarget(
                isEn ? "View: Maintainability Index" : "Vista: Indice di Manutenibilità",
                isEn ? "Maintainability Index (MI) and Halstead volume distribution" : "Indice di manutenibilità (MI), metriche Halstead e complessità",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "manutenibilita", "maintainability", "mi", "halstead", "qualita", "volume" },
                () => CentralTabControl.SelectedItem = MaintainabilityTabItem),

            new AppSearchTarget(
                isEn ? "View: Architectural Rules" : "Vista: Regole Architetturali",
                isEn ? "Architectural linting, layer enforcement and structural rule checks" : "Linter di conformità architetturale, regole di dipendenza tra layer e violazioni",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "regole", "rules", "architettura", "linter", "layer", "vincoli", "conformita" },
                () => CentralTabControl.SelectedItem = ArchitecturalRulesTabItem),

            new AppSearchTarget(
                isEn ? "View: Code Clones (CPD)" : "Vista: Cloni di Codice (CPD)",
                isEn ? "Duplicate code detection, copy-paste clone pairs and token similarities" : "Rilevamento duplicazioni di codice, token identici e refactoring di cloni",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "cloni", "duplicati", "clones", "cpd", "duplicazioni", "copia incolla", "duplication" },
                () => CentralTabControl.SelectedItem = CodeCloneTabItem),

            new AppSearchTarget(
                isEn ? "View: Social Code Analysis" : "Vista: Social Code Analysis",
                isEn ? "Developer bus factor, knowledge distribution and Conway's law analysis" : "Analisi bus factor sviluppatori, distribuzione della conoscenza e legge di Conway",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "social", "bus factor", "autori", "conoscenza", "conway", "team", "ownership" },
                () => CentralTabControl.SelectedItem = SocialCodeTabItem),

            new AppSearchTarget(
                isEn ? "View: Data Flow Tracking" : "Vista: Flusso Dati & Taint",
                isEn ? "Source-to-sink data flow propagation and taint analysis" : "Tracciamento flusso dei dati, sorgenti e punti di destinazione (Source-Sink)",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "data flow", "flusso dati", "taint", "source sink", "tracciamento", "dati" },
                () => CentralTabControl.SelectedItem = DataFlowTabItem),

            new AppSearchTarget(
                isEn ? "View: API Diff & SemVer" : "Vista: API Diff & SemVer",
                isEn ? "Public API surface comparison and breaking change detection" : "Confronto superficie pubblica API e rilevamento breaking change",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "api diff", "semver", "breaking changes", "baseline", "confronto api", "firme" },
                () => CentralTabControl.SelectedItem = ApiDiffTabItem),

            new AppSearchTarget(
                isEn ? "View: CRUD Matrix" : "Vista: Matrice CRUD",
                isEn ? "Data access matrix (Create, Read, Update, Delete) per entity" : "Matrice delle operazioni su dati ed entità database (Create, Read, Update, Delete)",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "crud", "matrice crud", "database", "tabelle", "entita", "sql", "dati" },
                () => CentralTabControl.SelectedItem = CrudMatrixTabItem),

            new AppSearchTarget(
                isEn ? "View: Blast Radius Grid" : "Vista: Griglia Blast Radius",
                isEn ? "Tabular impact assessment and blast radius details" : "Tabella di analisi impatto e raggio d'azione per le modifiche",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "blast radius", "impatto", "griglia", "tabella", "modifiche", "impact" },
                () => CentralTabControl.SelectedItem = GridTabItem),

            // --- GUIDE & INFORMATION ---
            new AppSearchTarget(
                isEn ? "Help: Concepts & Guide" : "Guida: Concetti & Documentazione",
                isEn ? "Open architecture and analysis concepts manual (F1)" : "Consulta la guida completa e i concetti architettonici (F1)",
                isEn ? "Help" : "Guida",
                SearchResultCategory.Help,
                new[] { "guida", "help", "aiuto", "info", "manuale", "documentazione", "concetti" },
                () => OpenInfo(0)),

            new AppSearchTarget(
                isEn ? "Help: Graph Layouts" : "Guida: Disposizioni del Grafo",
                isEn ? "Learn about Radial, Islands, and Force-Directed layout engines" : "Approfondimento sugli algoritmi di layout (Radiale, Isole, Force-Directed)",
                isEn ? "Help" : "Guida",
                SearchResultCategory.Help,
                new[] { "guida layout", "disposizioni", "algoritmi", "layout info" },
                () => OpenInfo(1)),

            new AppSearchTarget(
                isEn ? "Help: Metrics & Hotspots" : "Guida: Metriche & Hotspot",
                isEn ? "How to interpret cyclomatic complexity, churn, and hotspot risk" : "Come interpretare complessità ciclomantica, churn e rischio hotspot",
                isEn ? "Help" : "Guida",
                SearchResultCategory.Help,
                new[] { "guida metriche", "guida hotspot", "complessita spiegazione" },
                () => OpenInfo(5)),

            // --- QUICK ACTIONS AND TOOLS ---
            new AppSearchTarget(
                isEn ? "Action: Calculate Blast Radius" : "Azione: Calcola Blast Radius",
                isEn ? "Calculate change impact radius for selected symbol (F5)" : "Calcola il raggio d'impatto del simbolo attualmente selezionato (F5)",
                isEn ? "Action" : "Azione",
                SearchResultCategory.Action,
                new[] { "calcola blast radius", "analizza impatto", "blast radius", "calcola impatto" },
                () => OnBlastRadiusClicked()),

            new AppSearchTarget(
                isEn ? "Action: Show Callers" : "Azione: Mostra Chiamanti (Callers)",
                isEn ? "Highlight all callers of the selected routine or module" : "Evidenzia sul grafo tutti i chiamanti del simbolo selezionato",
                isEn ? "Action" : "Azione",
                SearchResultCategory.Action,
                new[] { "mostra callers", "chiamanti", "callers", "chi chiama" },
                () => OnShowCallersClicked()),

            new AppSearchTarget(
                isEn ? "Action: Show Callees" : "Azione: Mostra Chiamati (Callees)",
                isEn ? "Highlight all callees of the selected routine or module" : "Evidenzia sul grafo tutte le routine chiamate dal simbolo selezionato",
                isEn ? "Action" : "Azione",
                SearchResultCategory.Action,
                new[] { "mostra callees", "chiamati", "callees", "chi viene chiamato" },
                () => OnShowCalleesClicked()),

            new AppSearchTarget(
                isEn ? "Action: Clear Filters" : "Azione: Pulisci Filtri ed Evidenziazioni",
                isEn ? "Clear all active highlights and search filters" : "Rimuove tutti i filtri ed evidenziazioni attive",
                isEn ? "Action" : "Azione",
                SearchResultCategory.Action,
                new[] { "pulisci filtri", "clear filters", "reset filtri", "rimuovi evidenziazione" },
                () => { Graph.ClearHighlightMode(); ScreenFlow.ClearHighlight(); }),

            new AppSearchTarget(
                isEn ? "Action: Export to Markdown" : "Azione: Esporta in Markdown",
                isEn ? "Generate full architecture summary in Markdown" : "Esporta report architettonico e metriche in formato Markdown",
                isEn ? "Action" : "Azione",
                SearchResultCategory.Action,
                new[] { "esporta markdown", "export markdown", "report md", "salva markdown" },
                () => _ = ExportMarkdownAsync()),

            new AppSearchTarget(
                isEn ? "Action: Export to JSON" : "Azione: Esporta in JSON",
                isEn ? "Export semantic code graph to JSON format" : "Esporta l'intero grafo semantico in formato JSON strutturato",
                isEn ? "Action" : "Azione",
                SearchResultCategory.Action,
                new[] { "esporta json", "export json", "salva json", "grafo json" },
                () => _ = ExportJsonAsync()),

            new AppSearchTarget(
                isEn ? "Action: Check Updates" : "Azione: Verifica Aggiornamenti",
                isEn ? "Check for new releases of Macondo" : "Verifica la disponibilità di nuove versioni dell'applicazione",
                isEn ? "Action" : "Azione",
                SearchResultCategory.Action,
                new[] { "aggiornamenti", "updates", "nuova versione", "check updates" },
                () => _ = CheckForUpdatesManualAsync()),

            new AppSearchTarget(
                isEn ? "Action: Clear Project Cache" : "Azione: Pulisci Cache Progetti",
                isEn ? "Remove cached analysis snapshots from disk" : "Elimina gli snapshot di analisi memorizzati nella cache",
                isEn ? "Action" : "Azione",
                SearchResultCategory.Action,
                new[] { "pulisci cache", "clear cache", "elimina cache", "svuota cache" },
                () => ClearProjectCache())
        };
    }

    /// <summary>
    /// Manages the variation of the text typed in the global search box (SearchBox).
    /// Performs real-time searches on both commands/application views and symbols in the code graph.
    /// </summary>
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            SearchResultsPopup.IsOpen = false;
            SearchResultsList.ItemsSource = null;
            ScreenFlow.SetSearchFilter(null);
            return;
        }

        var results = new List<SearchResultItem>();

        // 1. Search the application screens and commands (Settings, Views, Help, Actions)
        var appTargets = GetAppSearchTargets();
        var matchingAppTargets = appTargets
            .Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        t.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        t.Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t => t.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(t => new SearchResultItem
            {
                Title = t.Title,
                Subtitle = t.Subtitle,
                BadgeText = t.BadgeText,
                Category = t.Category,
                ExecuteAction = t.Action
            });

        results.AddRange(matchingAppTargets);

        // 2. Search among the symbols of the code graph (if there is a loaded project)
        List<GraphNode> matchedNodes = new();
        if (_graph != null)
        {
            matchedNodes = _graph.Nodes
                .Where(n => n.Kind != SymbolKind.Project &&
                           (n.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            n.QualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(n => n.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n.Name)
                .Take(30)
                .ToList();

            var symbolItems = matchedNodes.Select(node => new SearchResultItem
            {
                Title = node.Name,
                Subtitle = node.QualifiedName,
                BadgeText = node.Kind.ToString(),
                Category = SearchResultCategory.Symbol,
                Node = node
            });

            results.AddRange(symbolItems);

            // Highlights matching screens in the ScreenFlow control
            var matchedScreenIds = matchedNodes
                .Select(m => m.Id)
                .Concat(matchedNodes.Where(m => m.ParentId.HasValue).Select(m => m.ParentId!.Value));
            ScreenFlow.SetSearchFilter(query, matchedScreenIds);
        }

        if (results.Count == 0)
        {
            SearchResultsHeader.Text = LocalizationService.Instance.Get("Search_NoMatches", query);
            SearchResultsList.ItemsSource = null;
            SearchResultsPopup.IsOpen = true;
            StatusText.Text = LocalizationService.Instance.Get("Search_NoMatches", query);
        }
        else
        {
            SearchResultsHeader.Text = LocalizationService.Instance.Get("Search_MultipleMatches", results.Count);
            SearchResultsList.ItemsSource = results;
            SearchResultsPopup.IsOpen = true;
            StatusText.Text = LocalizationService.Instance.Get("Search_MultipleMatches", results.Count);
        }
    }

    /// <summary>
    /// Performs the action associated with a search result (navigate to the symbol in the graph or execute the command/screen).
    /// </summary>
    /// <param name="item">The selected search item.</param>
    private void ExecuteSearchResult(SearchResultItem item)
    {
        SearchResultsPopup.IsOpen = false;
        if (item.Node != null)

            SelectAndFocusGraphNode(item.Node);

        else if (item.ExecuteAction != null)
        {
            item.ExecuteAction.Invoke();
            StatusText.Text = $"{item.Title}";
        }
    }

    /// <summary>
    /// Handles the event of selecting an item from the search results pop-up list.
    /// </summary>
    private void OnSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is SearchResultItem selected)
        {
            ExecuteSearchResult(selected);
            Dispatcher.UIThread.Post(() => SearchResultsList.SelectedItem = null);
        }
    }

    /// <summary>
    /// Handles special keys (Enter, Down Arrow, Esc) pressed in the global search text box.
    /// </summary>
    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (SearchResultsList.SelectedItem is SearchResultItem selected)
            {
                ExecuteSearchResult(selected);
                e.Handled = true;
            }
            else if (SearchResultsList.ItemsSource is IList<SearchResultItem> list && list.Count > 0)
            {
                ExecuteSearchResult(list[0]);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Down)
        {
            if (SearchResultsPopup.IsOpen && SearchResultsList.ItemsSource is IList<SearchResultItem> list && list.Count > 0)
            {
                SearchResultsList.Focus();
                if (SearchResultsList.SelectedIndex < 0)
                    SearchResultsList.SelectedIndex = 0;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            SearchResultsPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles special keys (Enter to execute, Esc to close) when the focus is in the search results list.
    /// </summary>
    private void OnSearchResultsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (SearchResultsList.SelectedItem is SearchResultItem selected)
            {
                ExecuteSearchResult(selected);
                SearchBox.Focus();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            SearchResultsPopup.IsOpen = false;
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    #endregion

    #region Symbol Inspector & Dettagli Semantici

    /// <summary>
    /// Show the Inspector placeholder information panel when no node or symbol is selected in the graph.
    /// </summary>
    private void ShowEmptyInspector()
    {
        SymbolInspectorControl.ShowEmpty(_graph != null);
        SourceCodePreviewControl.ShowEmpty(_graph != null);
        UpdateStatusLanguage();
    }

    /// <summary>
    /// Asynchronously inspects the selected node, updating both immediate static metadata (name, type, parameters, file)
    /// and heavy semantic computations in the background (CFG, Blast Radius, direct callers and instantiated classes).
    /// </summary>
    /// <param name="node">The node of the graph to be inspected.</param>
    private async void SelectAndInspectNodeAsync(GraphNode node)
    {
        // Cancels any previous analysis still in progress
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        _selectionCts = new CancellationTokenSource();
        var ct = _selectionCts.Token;

        // 1. Instantly show static symbol metadata and source code preview
        SymbolInspectorControl.ShowSymbolMetadata(node);
        SourceCodePreviewControl.ShowSourceCode(node);

        var sym = node.SemanticSymbol;

        // Build the Control Flow Graph (CFG) if it is routine
        if (sym != null && _graph?.SemanticGraph != null && (sym.Kind == SymbolKind.Method || sym.Kind == SymbolKind.Property))

            ControlFlow.Graph = ControlFlowBuilder.BuildForSymbol(sym, _graph.SemanticGraph);

        UpdateStatusLanguage(node.Language);

        // If we don't have any symbols or queries, clear the dynamic panels
        if (_graph?.Queries is null || sym is null)
        {
            SymbolInspectorControl.ClearDynamicSections();
            return;
        }

        // If the symbol is already present in cache, immediately update to 0ms (no overlay, no delayed tasks)
        if (_graph.Queries.TryGetCachedImpactReport(sym, 10, out var cachedImpact) && cachedImpact != null)
        {
            var cachedCallees = _graph.Queries.GetDirectCallees(sym);
            var cachedInstantiations = _graph.Queries.GetInstantiatedClasses(sym);
            SymbolInspectorControl.UpdateImpactDetails(cachedImpact, cachedCallees, cachedInstantiations);
            return;
        }

        // 2. Set the loading state in the Inspector for Blast Radius and callers
        var localizationService = LocalizationService.Instance;
        SymbolInspectorControl.SetCalculatingState();

        var targetId = node.Id;
        bool isCompleted = false;

        // If the calculation takes more than 80ms (typical for complex nodes), show the centered loading overlay
        _ = Task.Run(async () =>
        {
            await Task.Delay(80, ct);
            if (!ct.IsCancellationRequested && !isCompleted)

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested && !isCompleted && _selectedNode?.Id == targetId)

                        ShowLoading(localizationService["Loading_NodeAnalysisTitle"], localizationService.Get("Loading_NodeAnalysisSub", node.Name));
                });
        }, ct);

        try
        {
            // 3. Performs heavy calculations on a background thread
            var impact = await Task.Run(() => _graph.Queries.GetImpactReport(sym, cancellationToken: ct), ct);
            var callees = await Task.Run(() => _graph.Queries.GetDirectCallees(sym), ct);
            var instantiations = await Task.Run(() => _graph.Queries.GetInstantiatedClasses(sym), ct);

            if (ct.IsCancellationRequested || _selectedNode?.Id != targetId)
                return;

            // 4. Populate the Inspector with the complete results
            SymbolInspectorControl.UpdateImpactDetails(impact, callees, instantiations);
        }
        catch (OperationCanceledException)
        {
            // Analysis canceled by a new selection: normal operation
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested && _selectedNode?.Id == targetId)

                StatusText.Text = localizationService.Get("Status_NodeAnalysisError", ex.Message);
        }
        finally
        {
            isCompleted = true;
            if (_selectedNode?.Id == targetId)

                HideLoading();
        }
    }

    #endregion

    #region Blast Radius Grid, Tab Navigation & Statistics

    /// <summary>
    /// Cyclically switches the central display tab between those currently visible and enabled.
    /// </summary>
    private void CycleCentralTab(bool forward = true)
    {
        var visibleTabs = CentralTabControl.Items.OfType<TabItem>().Where(t => t.IsVisible).ToList();
        if (visibleTabs.Count <= 1) return;

        var current = CentralTabControl.SelectedItem as TabItem;
        var currentIndex = current != null ? visibleTabs.IndexOf(current) : -1;
        if (currentIndex < 0) currentIndex = 0;

        var nextIndex = forward
            ? (currentIndex + 1) % visibleTabs.Count
            : (currentIndex - 1 + visibleTabs.Count) % visibleTabs.Count;

        CentralTabControl.SelectedItem = visibleTabs[nextIndex];
    }

    /// <summary>
    /// Manages the variation of the selected tab in the central TabControl.
    /// Initiates Lazy analytical loading for the activated view and synchronizes the selection with the Blast Radius grid.
    /// </summary>
    private void OnCentralTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source != CentralTabControl) return;

        // Calculate and load tab data only when the user accesses it for the first time (Lazy Analytics)
        EnsureActiveTabAnalyticsLoaded();

        if (CentralTabControl.SelectedItem == GridTabItem)
        {
            if (_selectedNode != null)
                BlastRadiusGridControl.SelectNode(_selectedNode.Id);
        }
    }

    /// <summary>
    /// Calculate semantic impact and blast radius for all workspace routines in the background via <see cref="BlastRadiusGridView"/>.
    /// </summary>
    private async Task PopulateBlastRadiusGridAsync()
    {
        await BlastRadiusGridControl.PopulateBlastRadiusGridAsync(_graph, _currentFolderPath, _hasUserMovedNodes);
        _dirtyAnalyticsTabs.Remove(GridTabItem);
        BlastRadiusGridPlaceholder?.SetState(isPending: false, isComputing: false);
    }

    /// <summary>
    /// Manages the selection of a row within the Blast Radius grid, selecting and inspecting the relevant node in the graph.
    /// </summary>
    private void OnGridControlRowSelected(object? sender, BlastRadiusGridRow row)
    {
        if (_isSyncingSelection || _graph == null) return;
        var node = _graph.GetNode(row.NodeId);
        if (node != null && node != _selectedNode)
        {
            _isSyncingSelection = true;
            try
            {
                _selectedNode = node;
                if (node.ParentId.HasValue)
                {
                    var parent = _graph.GetNode(node.ParentId.Value);
                    if (parent is not null)
                        parent.IsExpanded = true;
                }
                Graph.SelectNode(node);
                SelectAndInspectNodeAsync(node);
                StatusText.Text = LocalizationService.Instance.Get("Status_SelectedFromGrid", node.Kind, node.QualifiedName);
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }

    /// <summary>
    /// Handles the navigation request to a graph node coming from the Blast Radius grid.
    /// </summary>
    private void OnGridControlNavigateToNodeRequested(object? sender, Guid nodeId)
    {
        NavigateToNodeInGraph(nodeId);
    }

    /// <summary>
    /// Handles the full audit recalculation request from the Blast Radius grid.
    /// </summary>
    private async void OnGridControlRecalculateAuditRequested(object? sender, EventArgs e)
    {
        _dirtyAnalyticsTabs.Add(GridTabItem);
        BlastRadiusGridPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(GridTabItem, _graph?.Nodes.Count ?? 0), GetComputingTabTitle(GridTabItem));
        await PopulateBlastRadiusGridAsync();
    }

    /// <summary>
    /// Switch to the Graph tab, expand the parent container, center the view, and graphically calculate the Blast Radius impact for the specified node.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the node to navigate to.</param>
    private void NavigateToNodeInGraph(Guid nodeId)
    {
        if (_graph == null) return;
        var node = _graph.GetNode(nodeId);
        if (node == null) return;

        // 1. Switch to the Graph View tab
        CentralTabControl.SelectedItem = GraphTabItem;

        // 2. Select, expand the parent container if necessary, and center the view on the node
        SelectAndFocusGraphNode(node);

        // 3. Calculate and highlight the Blast Radius graph
        OnBlastRadiusClicked();
    }

    /// <summary>
    /// Calculate and update overall workspace metrics (project count, nodes, relationships, calls, instantiations) displayed in the bottom status bar.
    /// </summary>
    private void UpdateStatsBar()
    {
        if (_graph is null) return;

        var localizationService = LocalizationService.Instance;
        var (visProj, projectCount) = Graph.GetProjectCounts();
        var (visNodes, nodeCount) = Graph.GetNodeCounts();
        var relCount = _graph.Edges.Count;
        var callCount = _graph.Edges.Count(e => e.Kind == RelationshipKind.Calls);
        var instCount = _graph.Edges.Count(e => e.Kind == RelationshipKind.Instantiates);
        var crossCount = _graph.Edges.Count(e => e.IsCrossProject);

        if (projectCount > 1)
        {
            var projText = visProj < projectCount 
                ? localizationService.Get("Stats_ProjectsRatioFormat", visProj, projectCount) 
                : localizationService.Get("Stats_ProjectsFormat", projectCount);
            var nodesText = visNodes < nodeCount 
                ? localizationService.Get("Stats_NodesRatioFormat", visNodes, nodeCount) 
                : localizationService.Get("Stats_NodesFormat", nodeCount);
            var relText = crossCount > 0 
                ? localizationService.Get("Stats_RelationsCrossFormat", relCount, crossCount) 
                : localizationService.Get("Stats_RelationsFormat", relCount);
            var callText = localizationService.Get("Stats_CallsFormat", callCount);
            var instText = localizationService.Get("Stats_InstantiatesFormat", instCount);

            StatusStats.Text = $"{projText} | {nodesText} | {relText} | {callText} | {instText}";
        }
        else
        {
            StatusStats.Text = localizationService.Get("Status_StatsFormat", nodeCount, relCount, callCount, instCount);
        }

        UpdateStatusLanguage();
    }

    /// <summary>
    /// Update the source language (VB6, C#, etc.) badge in the status bar at the bottom right,
    /// based on the currently selected node or overall languages detected in the workspace.
    /// </summary>
    /// <param name="selectedNodeLang">The language of the selected node, or <c>null</c> to use the overall graph languages.</param>
    private void UpdateStatusLanguage(SourceLanguage? selectedNodeLang = null)
    {
        if (selectedNodeLang.HasValue && selectedNodeLang.Value != SourceLanguage.Unknown)
        {
            var (badge, bg, fg, tip) = ExplorerTreeItem.GetLanguageBadgeInfo(selectedNodeLang.Value);
            if (!string.IsNullOrEmpty(badge))
            {
                StatusLanguagePanel.IsVisible = true;
                StatusLanguageText.Text = badge;
                StatusLanguageBadge.Background = Avalonia.Media.Brush.Parse(bg ?? "#2563EB");
                StatusLanguageText.Foreground = Avalonia.Media.Brush.Parse(fg ?? "#FFFFFF");
                ToolTip.SetTip(StatusLanguageBadge, tip);
                return;
            }
        }

        // Otherwise it shows the languages of the overall graph/workspace
        if (_graph?.Nodes != null && _graph.Nodes.Count > 0)
        {
            var realNodes = _graph.Nodes.Where(n => n.Kind != SymbolKind.Project).ToList();
            var nodesToInspect = realNodes.Count > 0 ? realNodes : _graph.Nodes;

            var distinctLangs = nodesToInspect
                .Select(n => n.Language)
                .Where(l => l != SourceLanguage.Unknown)
                .Distinct()
                .ToList();

            if (distinctLangs.Count == 1)
            {
                var (badge, bg, fg, tip) = ExplorerTreeItem.GetLanguageBadgeInfo(distinctLangs[0]);
                if (!string.IsNullOrEmpty(badge))
                {
                    StatusLanguagePanel.IsVisible = true;
                    StatusLanguageText.Text = badge;
                    StatusLanguageBadge.Background = Avalonia.Media.Brush.Parse(bg ?? "#2563EB");
                    StatusLanguageText.Foreground = Avalonia.Media.Brush.Parse(fg ?? "#FFFFFF");
                    ToolTip.SetTip(StatusLanguageBadge, tip);
                    return;
                }
            }
            else if (distinctLangs.Count > 1)
            {
                var badges = distinctLangs
                    .Select(l => ExplorerTreeItem.GetLanguageBadgeInfo(l).Text)
                    .Where(t => !string.IsNullOrEmpty(t));
                var text = string.Join(" + ", badges);
                StatusLanguagePanel.IsVisible = true;
                StatusLanguageText.Text = text;
                StatusLanguageBadge.Background = Avalonia.Media.Brush.Parse("#4F46E5");
                StatusLanguageText.Foreground = Avalonia.Media.Brush.Parse("#FFFFFF");
                var tips = distinctLangs
                    .Select(l => ExplorerTreeItem.GetLanguageBadgeInfo(l).Tooltip)
                    .Where(t => !string.IsNullOrEmpty(t));
                ToolTip.SetTip(StatusLanguageBadge, string.Join(", ", tips));
                return;
            }
        }

        StatusLanguagePanel.IsVisible = false;
    }

    #endregion

    #region Settings, About & Themes

    /// <summary>
    /// Opens the modal Settings dialog box (<see cref="SettingsWindow"/>) by hovering over the specified tab.
    /// </summary>
    /// <param name="initialTab">The index of the initial tab to select (0 = Appearance, 1 = Language, 2 = Behavior, 3 = Shortcuts).</param>
    private void OpenSettings(int initialTab = 0)
    {
        var settingsWin = new SettingsWindow(initialTab);
        settingsWin.ShowDialog(this);
    }

    /// <summary>
    /// Opens the modal Help and Information dialog box (<see cref="InfoWindow"/>) by hovering over the specified tab.
    /// </summary>
    /// <param name="initialTab">The index of the initial card to select.</param>
    private void OpenInfo(int initialTab = 0)
    {
        var infoWin = new Views.InfoWindow(initialTab);
        infoWin.ShowDialog(this);
    }

    /// <summary>
    /// Applies the theme definition's brushes and colors to all main window borders and analytic view controls.
    /// </summary>
    /// <param name="theme">The theme definition containing the hexadecimal color codes.</param>
    private void ApplyTheme(ThemeDefinition theme)
    {
        if (Color.TryParse(theme.BackgroundPrimary, out var bgPrimary))
            Background = new SolidColorBrush(bgPrimary);

        if (Color.TryParse(theme.BackgroundSecondary, out var bgSecondary))
        {
            var secondaryBrush = new SolidColorBrush(bgSecondary);
            TopBarBorder.Background = secondaryBrush;
            SearchRowBorder.Background = secondaryBrush;
            StatusBarBorder.Background = secondaryBrush;
            FloatingHudBorder.Background = secondaryBrush;
            GraphLegendBorder.Background = secondaryBrush;
        }

        if (Color.TryParse(theme.BackgroundSurface, out var bgSurface))
        {
            var surfaceBrush = new SolidColorBrush(bgSurface);
            ProjectExplorerControl.Background = surfaceBrush;
            SymbolInspectorControl.Background = surfaceBrush;
            RightPanelContainer.Background = surfaceBrush;
        }

        if (Color.TryParse(theme.BorderDefault, out var borderCol))
        {
            var borderBrush = new SolidColorBrush(borderCol);
            TopBarBorder.BorderBrush = borderBrush;
            SearchRowBorder.BorderBrush = borderBrush;
            StatusBarBorder.BorderBrush = borderBrush;
            FloatingHudBorder.BorderBrush = borderBrush;
            ProjectExplorerControl.BorderBrush = borderBrush;
            SymbolInspectorControl.BorderBrush = borderBrush;
            RightPanelContainer.BorderBrush = borderBrush;
            LeftGridSplitter.Background = borderBrush;
            RightGridSplitter.Background = borderBrush;
        }

        Graph.ApplyTheme(theme);
        Dsm.ApplyTheme(theme);
        Treemap.ApplyTheme(theme);
        Hotspot.ApplyTheme(theme);
        ScreenFlow.ApplyTheme(theme);
        ControlFlow.ApplyTheme(theme);
        MartinMetrics.ApplyTheme(theme);
        DeadCode.ApplyTheme(theme);
        SharedState.ApplyTheme(theme);
        TemporalCoupling.ApplyTheme(theme);
        ArchitecturalFlow.ApplyTheme(theme);
        CodeCity.ApplyTheme(theme);
        Cohesion.ApplyTheme(theme);
        Maintainability.ApplyTheme(theme);
        ArchitecturalRules.ApplyTheme(theme);
        CodeClone.ApplyTheme(theme);
        SocialCode.ApplyTheme(theme);
        DataFlow.ApplyTheme(theme);
        ApiDiff.ApplyTheme(theme);
        CrudMatrix.ApplyTheme(theme);
        BlastRadiusGridControl.ApplyTheme(theme);
    }

    #endregion

    #region Data Export & Import

    /// <summary>
    /// Export the entire graph in compact Markdown format (Repo Map) for LLM via file chooser.
    /// </summary>
    private async Task ExportMarkdownAsync()
    {
        var semGraph = _graph?.SemanticGraph;
        if (semGraph == null)
        {
            StatusText.Text = LocalizationService.Instance["Status_NoSemanticGraph"];
            return;
        }

        var defaultName = !string.IsNullOrEmpty(_currentFolderPath)
            ? $"{Path.GetFileName(_currentFolderPath)}_codemap.md"
            : "codegraph_map.md";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Instance["Menu_ExportMarkdown"],
            DefaultExtension = "md",
            SuggestedFileName = defaultName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Markdown Repo Map (*.md)") { Patterns = new[] { "*.md" } }
            }
        });

        if (file == null) return;

        var localizationService = LocalizationService.Instance;
        ShowLoading(localizationService["Loading_ExportTitle"], localizationService.Get("Loading_ExportSub", file.Name), canCancel: false);
        await Task.Delay(16);

        try
        {
            var markdown = await Task.Run(() => CodeGraphMarkdownExporter.Export(semGraph));
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            await writer.WriteAsync(markdown);

            var fullPath = file.TryGetLocalPath() ?? file.Path?.LocalPath ?? file.Name;
            StatusText.Text = localizationService.Get("Status_ExportSuccess", file.Name);
            ShowExportSuccessModal(file.Name, fullPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("Status_ErrorExportMarkdown", ex.Message);
        }
        finally
        {
            HideLoading();
        }
    }

    /// <summary>
    /// Export the entire graph in structured JSON format via file selector.
    /// </summary>
    private async Task ExportJsonAsync()
    {
        var semGraph = _graph?.SemanticGraph;
        if (semGraph == null)
        {
            StatusText.Text = LocalizationService.Instance["Status_NoSemanticGraph"];
            return;
        }

        var defaultName = !string.IsNullOrEmpty(_currentFolderPath)
            ? $"{Path.GetFileName(_currentFolderPath)}_codegraph.json"
            : "codegraph.json";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Instance["Menu_ExportJson"],
            DefaultExtension = "json",
            SuggestedFileName = defaultName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON CodeGraph (*.json)") { Patterns = new[] { "*.json" } }
            }
        });

        if (file == null) return;

        var localizationService = LocalizationService.Instance;
        ShowLoading(localizationService["Loading_ExportTitle"], localizationService.Get("Loading_ExportSub", file.Name), canCancel: false);
        await Task.Delay(16);

        try
        {
            var json = await Task.Run(() => CodeGraphJsonExporter.ExportToJson(semGraph, indented: true));
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            await writer.WriteAsync(json);

            var fullPath = file.TryGetLocalPath() ?? file.Path?.LocalPath ?? file.Name;
            StatusText.Text = localizationService.Get("Status_ExportSuccess", file.Name);
            ShowExportSuccessModal(file.Name, fullPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("Status_ErrorExportJson", ex.Message);
        }
        finally
        {
            HideLoading();
        }
    }

    /// <summary>
    /// Import a CodeGraph from previously saved JSON file and display it instantly.
    /// </summary>
    private async Task ImportJsonAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Instance["Menu_ImportJson"],
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("JSON CodeGraph (*.json)") { Patterns = new[] { "*.json" } }
            }
        });

        if (files.Count == 0) return;

        var file = files[0];
        ShowLoading("Importazione del grafo...", $"Caricamento di {file.Name} in corso...");
        await Task.Delay(16);

        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            var json = await reader.ReadToEndAsync();

            var importedGraph = await Task.Run(() => CodeGraphJsonExporter.ImportFromJson(json));
            var projectName = Path.GetFileNameWithoutExtension(file.Name);
            var visualGraph = await Task.Run(() => Layout.GraphVisualAdapter.FromSemanticGraph(importedGraph, projectName));

            _currentFolderPath = null;
            UpdateVcsBranchStatus(null);
            _graph = visualGraph;
            _enabledAnalyticsTabs.Clear();
            if (SettingsService.Instance.Settings.LastEnabledAnalyticsTabs != null)
            {
                foreach (var tabKey in SettingsService.Instance.Settings.LastEnabledAnalyticsTabs)
                {
                    _enabledAnalyticsTabs.Add(tabKey);
                }
            }
            ApplyGraphToUI();

            // Instantly calculate and export the Blast Radius grid for the imported graph
            await PopulateBlastRadiusGridAsync();

            StatusText.Text = LocalizationService.Instance.Get("Status_ImportSuccess", file.Name, importedGraph.Symbols.Count, importedGraph.Relationships.Count);
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationService.Instance.Get("Status_ErrorImportJson", ex.Message);
        }
        finally
        {
            HideLoading();
        }
    }

    /// <summary>
    /// Copies the semantic Context Slice for the selected symbol to the system clipboard.
    /// </summary>
    private async Task CopyLlmSliceToClipboardAsync()
    {
        var semGraph = _graph?.SemanticGraph;
        var symbol = _selectedNode?.SemanticSymbol;

        if (semGraph == null || symbol == null)
        {
            StatusText.Text = LocalizationService.Instance["Status_NoSymbolSelected"];
            return;
        }

        try
        {
            var slice = CodeGraphMarkdownExporter.ExportSlice(semGraph, symbol);
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(slice);
                StatusText.Text = LocalizationService.Instance.Get("Status_CopiedLlmSlice", symbol.Name);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationService.Instance.Get("Status_ErrorCopyClipboard", ex.Message);
        }
    }

    /// <summary>
    /// Save the Context Slice for the selected symbol/node to a Markdown (.md) file.
    /// </summary>
    private async Task ExportLlmSliceAsync(GraphNode? targetNode = null)
    {
        var node = targetNode ?? Graph.SelectedNode ?? _selectedNode;
        var semGraph = _graph?.SemanticGraph;
        var symbol = node?.SemanticSymbol;

        if (node == null)
        {
            StatusText.Text = LocalizationService.Instance["Status_NoSymbolSelected"];
            return;
        }

        var defaultName = !string.IsNullOrEmpty(node.QualifiedName)
            ? $"{node.QualifiedName}_slice.md"
            : $"{node.Name}_slice.md";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Instance["Menu_ExportSlice"],
            DefaultExtension = "md",
            SuggestedFileName = defaultName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Markdown Context Slice (*.md)") { Patterns = new[] { "*.md" } }
            }
        });

        if (file == null) return;

        var localizationService = LocalizationService.Instance;
        ShowLoading(localizationService["Loading_ExportTitle"], localizationService.Get("Loading_ExportSub", file.Name), canCancel: false);
        await Task.Delay(16);

        try
        {
            string markdown;
            if (semGraph != null && symbol != null)

                markdown = await Task.Run(() => CodeGraphMarkdownExporter.ExportSlice(semGraph, symbol));

            else
            {
                var filePath = symbol?.FilePath;
                var lineStart = symbol?.LineStart ?? 0;
                var lineEnd = symbol?.LineEnd ?? 0;
                var childCount = _graph != null ? _graph.GetChildren(node.Id).Count() : 0;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"# Symbol: {node.Name} ({node.Kind})");
                sb.AppendLine($"- **Qualified Name**: {node.QualifiedName}");
                if (!string.IsNullOrEmpty(node.ProjectName)) sb.AppendLine($"- **Project**: {node.ProjectName}");
                if (!string.IsNullOrEmpty(filePath)) sb.AppendLine($"- **File**: {filePath}");
                if (lineStart > 0) sb.AppendLine($"- **Lines**: {lineStart}-{lineEnd}");
                if (node.IsContainer) sb.AppendLine($"- **Container**: Yes ({childCount} members)");
                markdown = sb.ToString();
            }

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            await writer.WriteAsync(markdown);

            var fullPath = file.TryGetLocalPath() ?? file.Path?.LocalPath ?? file.Name;
            StatusText.Text = localizationService.Get("Status_ExportSuccess", file.Name);
            ShowExportSuccessModal(file.Name, fullPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("Status_ErrorSaveSlice", ex.Message);
        }
        finally
        {
            HideLoading();
        }
    }

    /// <summary>
    /// Show confirmation modal popup for successful export.
    /// </summary>
    private void ShowExportSuccessModal(string fileName, string fullPath)
    {
        ExportSuccessModalControl.Show(fileName, fullPath);
    }

    private void OnExportSuccessOpenFolderRequested(object? sender, string fullPath)
    {
        if (!string.IsNullOrEmpty(fullPath))

            OpenContainingFolder(fullPath);
    }

    #endregion

    #region Graph Layout Management & Hover Interaction

    private bool _hasUserMovedNodes;
    private Layout.GraphLayoutMode? _pendingLayoutMode;

    /// <summary>
    /// Invoked when the user manually moves/drag a node in the graph canvas with the mouse.
    /// </summary>
    private void OnGraphNodeMoved()
    {
        _hasUserMovedNodes = true;
        ScheduleLayoutAutoSave();
    }

    /// <summary>
    /// Requires changing graph layout. If the user moved at least one node manually,
    /// first shows a confirmation modal to warn that the positions of the moved nodes will be lost.
    /// </summary>
    private void RequestSetLayoutMode(Layout.GraphLayoutMode mode, bool resetView = true)
    {
        if (_graph is null || mode == _currentLayoutMode)
        {
            SetLayoutMode(mode, resetView);
            return;
        }

        if (_hasUserMovedNodes)
        {
            _pendingLayoutMode = mode;
            ShowLayoutConfirmModal();
            return;
        }

        SetLayoutMode(mode, resetView);
    }

    /// <summary>
    /// Show warning modal overlay before automatically rearranging the graph layout
    /// if the user manually moved nodes.
    /// </summary>
    private void ShowLayoutConfirmModal()
    {
        if (_pendingLayoutMode.HasValue)

            LayoutConfirmModalControl.Show(_pendingLayoutMode.Value);
    }

    private void OnLayoutConfirmModalConfirmed(object? sender, Layout.GraphLayoutMode mode)
    {
        _pendingLayoutMode = null;
        _hasUserMovedNodes = false;
        SetLayoutMode(mode, resetView: true);
    }

    /// <summary>
    /// Set the active layout mode, recalculate the coordinates of all nodes and update the graph canvas.
    /// </summary>
    private void SetLayoutMode(Layout.GraphLayoutMode mode, bool resetView = true)
    {
        _currentLayoutMode = mode;
        _hasUserMovedNodes = false;
        var targetIndex = (int)mode;

        if (LayoutComboBox.SelectedIndex != targetIndex)

            LayoutComboBox.SelectedIndex = targetIndex;

        if (_graph != null)
        {
            Layout.GraphLayoutManager.ApplyLayout(_graph, mode);
            Graph.InvalidateVisual();
            if (resetView)

                ResetGraphView();
            ScheduleLayoutAutoSave();
        }

        SettingsService.Instance.Update(s => s.PreferredLayout = mode);
        StatusText.Text = LocalizationService.Instance.Get("Status_LayoutApplied", GetLocalizedLayoutName(mode));
    }

    private DispatcherTimer? _layoutSaveDebounceTimer;
    private bool _hasUnsavedLayoutChanges;

    /// <summary>
    /// Schedule debounced asynchronous saving of node coordinates in cache (1.5s of inactivity).
    /// </summary>
    private void ScheduleLayoutAutoSave()
    {
        if (string.IsNullOrEmpty(_currentFolderPath) || _graph is null)
            return;

        _hasUnsavedLayoutChanges = true;

        if (_layoutSaveDebounceTimer is null)
        {
            _layoutSaveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _layoutSaveDebounceTimer.Tick += OnLayoutSaveDebounceTick;
        }

        _layoutSaveDebounceTimer.Stop();
        _layoutSaveDebounceTimer.Start();
    }

    /// <summary>
    /// Unleashed handler when debounce timer expires to persist updated node positions to disk.
    /// </summary>
    private void OnLayoutSaveDebounceTick(object? sender, EventArgs e)
    {
        _layoutSaveDebounceTimer?.Stop();
        FlushPendingLayoutChanges();
    }

    /// <summary>
    /// Performs asynchronous background saving of geometric layout changes to disk.
    /// </summary>
    private void FlushPendingLayoutChanges()
    {
        if (!_hasUnsavedLayoutChanges || string.IsNullOrEmpty(_currentFolderPath) || _graph is null)
            return;

        _hasUnsavedLayoutChanges = false;
        var folder = _currentFolderPath;
        var graph = _graph;
        var hasMoved = _hasUserMovedNodes;
        _ = ProjectCacheService.Instance.UpdateLayoutSnapshotAsync(folder, graph, hasMoved);
    }

    /// <summary>
    /// Save the geometric coordinates of the nodes on disk immediately and synchronously (e.g. when closing or changing the project).
    /// </summary>
    private void FlushPendingLayoutChangesSync()
    {
        _layoutSaveDebounceTimer?.Stop();
        if (!_hasUnsavedLayoutChanges || string.IsNullOrEmpty(_currentFolderPath) || _graph is null)
            return;

        _hasUnsavedLayoutChanges = false;
        ProjectCacheService.Instance.UpdateLayoutSnapshot(_currentFolderPath, _graph, _hasUserMovedNodes);
    }

    /// <summary>
    /// Restore the graph display by setting the zoom to 100% and centering the view
    /// on the first visible/selected subproject (or in the center of the canvas if not present).
    /// </summary>
    private void ResetGraphView()
    {
        Graph.ResetView();
        if (_graph != null)
        {
            var targetProject = _graph.Nodes.FirstOrDefault(n => n.Kind == SymbolKind.Project && Graph.IsProjectVisible(n.Id))
                                ?? _graph.Nodes.FirstOrDefault(n => n.Kind == SymbolKind.Project);
            if (targetProject != null)

                Graph.FocusOnNode(targetProject.Id);
        }
    }

    /// <summary>
    /// Returns the localized descriptive string corresponding to the specified layout algorithm.
    /// </summary>
    private string GetLocalizedLayoutName(Layout.GraphLayoutMode mode) => mode switch
    {
        Layout.GraphLayoutMode.StandardRadial => LocalizationService.Instance["Layout_StandardRadial"],
        Layout.GraphLayoutMode.AffinityRadial => LocalizationService.Instance["Layout_AffinityRadial"],
        Layout.GraphLayoutMode.ClusteredIslands => LocalizationService.Instance["Layout_ClusteredIslands"],
        Layout.GraphLayoutMode.ForceDirected => LocalizationService.Instance["Layout_ForceDirected"],
        _ => mode.ToString()
    };

    /// <summary>
    /// Manages the selection change in the layout ComboBox to request the application of the new algorithm.
    /// </summary>
    private void OnLayoutComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LayoutComboBox.SelectedIndex < 0) return;
        var mode = (Layout.GraphLayoutMode)LayoutComboBox.SelectedIndex;
        if (mode != _currentLayoutMode)

            RequestSetLayoutMode(mode, resetView: true);
    }

    /// <summary>
    /// Enable or disable node selection and inspection when hovering the mouse cursor (Hover).
    /// </summary>
    private void OnToggleHoverSelectionClicked(object? sender, RoutedEventArgs e)
    {
        var newValue = !Graph.EnableHoverSelection;
        Graph.EnableHoverSelection = newValue;
        HoverSelectionCheckIcon.IsVisible = newValue;
        SettingsService.Instance.Update(s => s.EnableHoverSelection = newValue);
    }

    /// <summary>
    /// Toggle the display of text labels in the central display tabs.
    /// </summary>
    private void OnToggleTabLabelsClicked(object? sender, RoutedEventArgs e)
    {
        ToggleTabLabels();
    }

    /// <summary>
    /// Toggle the visibility state of central tab labels and update user settings.
    /// </summary>
    private void ToggleTabLabels()
    {
        var newValue = !SettingsService.Instance.Settings.ShowTabLabels;
        SettingsService.Instance.Update(s => s.ShowTabLabels = newValue);
        ApplyTabLabelsVisibility(newValue);
    }

    /// <summary>
    /// Applies visual visibility to the text headings of all tabs in the central tabbed control.
    /// </summary>
    /// <param name="showLabels"><c>true</c> to show labels with text; <c>false</c> to show only the icons.</param>
    private void ApplyTabLabelsVisibility(bool showLabels)
    {
        GraphTabHeader.IsVisible = showLabels;
        DsmTabHeader.IsVisible = showLabels;
        TreemapTabHeader.IsVisible = showLabels;
        HotspotTabHeader.IsVisible = showLabels;
        ScreenFlowTabHeader.IsVisible = showLabels;
        ControlFlowTabHeader.IsVisible = showLabels;
        MartinTabHeader.IsVisible = showLabels;
        DeadCodeTabHeader.IsVisible = showLabels;
        SharedStateTabHeader.IsVisible = showLabels;
        TemporalCouplingTabHeader.IsVisible = showLabels;
        ArchitecturalFlowTabHeader.IsVisible = showLabels;
        CodeCityTabHeader.IsVisible = showLabels;
        GridTabHeader.IsVisible = showLabels;
        CohesionTabHeader.IsVisible = showLabels;
        MaintainabilityTabHeader.IsVisible = showLabels;
        ArchitecturalRulesTabHeader.IsVisible = showLabels;
        CodeCloneTabHeader.IsVisible = showLabels;
        SocialCodeTabHeader.IsVisible = showLabels;
        DataFlowTabHeader.IsVisible = showLabels;
        ApiDiffTabHeader.IsVisible = showLabels;
        CrudMatrixTabHeader.IsVisible = showLabels;
        if (TabLabelsCheckIcon != null)

            TabLabelsCheckIcon.IsVisible = showLabels;
    }

    /// <summary>
    /// Applies user-configured visibility for each analysis tab in the center bar
    /// and contextually updates the checkmarks in the View menu.
    /// </summary>
    private void ApplyTabsVisibility(AppSettings s)
    {
        if (GraphTabItem != null) GraphTabItem.IsVisible = s.ShowTabGraph;
        if (DsmTabItem != null) DsmTabItem.IsVisible = s.ShowTabDsm;
        if (TreemapTabItem != null) TreemapTabItem.IsVisible = s.ShowTabTreemap;
        if (HotspotTabItem != null) HotspotTabItem.IsVisible = s.ShowTabHotspot;
        if (ScreenFlowTabItem != null) ScreenFlowTabItem.IsVisible = s.ShowTabScreenFlow;
        if (ControlFlowTabItem != null) ControlFlowTabItem.IsVisible = s.ShowTabControlFlow;
        if (MartinTabItem != null) MartinTabItem.IsVisible = s.ShowTabMartin;
        if (DeadCodeTabItem != null) DeadCodeTabItem.IsVisible = s.ShowTabDeadCode;
        if (SharedStateTabItem != null) SharedStateTabItem.IsVisible = s.ShowTabSharedState;
        if (TemporalCouplingTabItem != null) TemporalCouplingTabItem.IsVisible = s.ShowTabTemporalCoupling;
        if (ArchitecturalFlowTabItem != null) ArchitecturalFlowTabItem.IsVisible = s.ShowTabArchitecturalFlow;
        if (CodeCityTabItem != null) CodeCityTabItem.IsVisible = s.ShowTabCodeCity;
        if (CohesionTabItem != null) CohesionTabItem.IsVisible = s.ShowTabCohesion;
        if (MaintainabilityTabItem != null) MaintainabilityTabItem.IsVisible = s.ShowTabMaintainability;
        if (ArchitecturalRulesTabItem != null) ArchitecturalRulesTabItem.IsVisible = s.ShowTabArchitecturalRules;
        if (CodeCloneTabItem != null) CodeCloneTabItem.IsVisible = s.ShowTabCodeClone;
        if (SocialCodeTabItem != null) SocialCodeTabItem.IsVisible = s.ShowTabSocialCode;
        if (DataFlowTabItem != null) DataFlowTabItem.IsVisible = s.ShowTabDataFlow;
        if (ApiDiffTabItem != null) ApiDiffTabItem.IsVisible = s.ShowTabApiDiff;
        if (CrudMatrixTabItem != null) CrudMatrixTabItem.IsVisible = s.ShowTabCrudMatrix;
        if (GridTabItem != null) GridTabItem.IsVisible = s.ShowTabBlastRadiusGrid;
        if (TabItemInspector != null) TabItemInspector.IsVisible = s.ShowTabInspector;
        if (TabItemSourceCode != null) TabItemSourceCode.IsVisible = s.ShowTabSourceCode;

        if (TabGraphCheckIcon != null) TabGraphCheckIcon.IsVisible = s.ShowTabGraph;
        if (TabDsmCheckIcon != null) TabDsmCheckIcon.IsVisible = s.ShowTabDsm;
        if (TabTreemapCheckIcon != null) TabTreemapCheckIcon.IsVisible = s.ShowTabTreemap;
        if (TabHotspotCheckIcon != null) TabHotspotCheckIcon.IsVisible = s.ShowTabHotspot;
        if (TabScreenFlowCheckIcon != null) TabScreenFlowCheckIcon.IsVisible = s.ShowTabScreenFlow;
        if (TabControlFlowCheckIcon != null) TabControlFlowCheckIcon.IsVisible = s.ShowTabControlFlow;
        if (TabMartinCheckIcon != null) TabMartinCheckIcon.IsVisible = s.ShowTabMartin;
        if (TabDeadCodeCheckIcon != null) TabDeadCodeCheckIcon.IsVisible = s.ShowTabDeadCode;
        if (TabSharedStateCheckIcon != null) TabSharedStateCheckIcon.IsVisible = s.ShowTabSharedState;
        if (TabTemporalCouplingCheckIcon != null) TabTemporalCouplingCheckIcon.IsVisible = s.ShowTabTemporalCoupling;
        if (TabArchitecturalFlowCheckIcon != null) TabArchitecturalFlowCheckIcon.IsVisible = s.ShowTabArchitecturalFlow;
        if (TabCodeCityCheckIcon != null) TabCodeCityCheckIcon.IsVisible = s.ShowTabCodeCity;
        if (TabCohesionCheckIcon != null) TabCohesionCheckIcon.IsVisible = s.ShowTabCohesion;
        if (TabMaintainabilityCheckIcon != null) TabMaintainabilityCheckIcon.IsVisible = s.ShowTabMaintainability;
        if (TabArchitecturalRulesCheckIcon != null) TabArchitecturalRulesCheckIcon.IsVisible = s.ShowTabArchitecturalRules;
        if (TabCodeCloneCheckIcon != null) TabCodeCloneCheckIcon.IsVisible = s.ShowTabCodeClone;
        if (TabSocialCodeCheckIcon != null) TabSocialCodeCheckIcon.IsVisible = s.ShowTabSocialCode;
        if (TabDataFlowCheckIcon != null) TabDataFlowCheckIcon.IsVisible = s.ShowTabDataFlow;
        if (TabApiDiffCheckIcon != null) TabApiDiffCheckIcon.IsVisible = s.ShowTabApiDiff;
        if (TabCrudMatrixCheckIcon != null) TabCrudMatrixCheckIcon.IsVisible = s.ShowTabCrudMatrix;
        if (TabBlastRadiusGridCheckIcon != null) TabBlastRadiusGridCheckIcon.IsVisible = s.ShowTabBlastRadiusGrid;
        if (TabInspectorCheckIcon != null) TabInspectorCheckIcon.IsVisible = s.ShowTabInspector;
        if (TabSourceCodeCheckIcon != null) TabSourceCodeCheckIcon.IsVisible = s.ShowTabSourceCode;
        if (BackgroundAnalyticsCheckIcon != null) BackgroundAnalyticsCheckIcon.IsVisible = s.EnableBackgroundAnalytics;

        // If the currently active tab becomes hidden, fall back safely to the first visible tab
        if (CentralTabControl?.SelectedItem is TabItem currentTab && !currentTab.IsVisible)
        {
            var firstVisible = CentralTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.IsVisible);
            if (firstVisible != null)

                CentralTabControl.SelectedItem = firstVisible;
        }

        // If the currently active right panel tab becomes hidden, it safely falls back to the first visible one
        if (RightTabControl?.SelectedItem is TabItem currentRightTab && !currentRightTab.IsVisible)
        {
            var firstVisibleRight = RightTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.IsVisible);
            if (firstVisibleRight != null)

                RightTabControl.SelectedItem = firstVisibleRight;
        }

        // If both tabs in the right panel are hidden, close and collapse the panel only if it is currently visible
        if (!s.ShowTabInspector && !s.ShowTabSourceCode)
        {
            if (_isInspectorVisible)
                SetInspectorVisibility(false, updateSettings: false);
        }
        else if (s.IsInspectorVisible)
        {
            if (!_isInspectorVisible)
                SetInspectorVisibility(true, updateSettings: false);
        }
        else
        {
            if (_isInspectorVisible)
                SetInspectorVisibility(false, updateSettings: false);
        }
    }

    /// <summary>Toggle whether background scans should run automatically in settings.</summary>
    private void OnToggleBackgroundAnalyticsMenuClicked(object? sender, RoutedEventArgs e)
    {
        var newValue = !SettingsService.Instance.Settings.EnableBackgroundAnalytics;
        SettingsService.Instance.Update(s => s.EnableBackgroundAnalytics = newValue);
        if (newValue && _graph != null && _dirtyAnalyticsTabs.Count > 0)

            StartAllPendingAnalytics();
    }

    /// <summary>Start the calculation of all pending analytical cards from the Analysis menu.</summary>
    private void OnRunAllAnalyticsMenuClicked(object? sender, RoutedEventArgs e)
    {
        StartAllPendingAnalytics();
    }

    /// <summary>Toggle the visibility of the Dependency Graph tab in settings.</summary>
    private void OnToggleTabGraphMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabGraph = !s.ShowTabGraph);

    /// <summary>Toggle the visibility of the DSM Matrix tab in settings.</summary>
    private void OnToggleTabDsmMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabDsm = !s.ShowTabDsm);

    /// <summary>Toggle the visibility of the Hierarchical Treemap tab in the settings.</summary>
    private void OnToggleTabTreemapMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabTreemap = !s.ShowTabTreemap);

    /// <summary>Toggle the visibility of the Hotspot Matrix tab in settings.</summary>
    private void OnToggleTabHotspotMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabHotspot = !s.ShowTabHotspot);

    /// <summary>Toggle the visibility of the Screen Flow tab in settings.</summary>
    private void OnToggleTabScreenFlowMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabScreenFlow = !s.ShowTabScreenFlow);

    /// <summary>Toggle the visibility of the Control Flow Graph (CFG) tab in settings.</summary>
    private void OnToggleTabControlFlowMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabControlFlow = !s.ShowTabControlFlow);

    /// <summary>Toggle the visibility of the Martin Metrics tab in settings.</summary>
    private void OnToggleTabMartinMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabMartin = !s.ShowTabMartin);

    /// <summary>Toggle the visibility of the Dead Code & Orphans tab in settings.</summary>
    private void OnToggleTabDeadCodeMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabDeadCode = !s.ShowTabDeadCode);

    /// <summary>Toggle the visibility of the Global Shared Status tab in settings.</summary>
    private void OnToggleTabSharedStateMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabSharedState = !s.ShowTabSharedState);

    /// <summary>Toggle the visibility of the Temporal Coupling (Git Co-change) tab in the settings.</summary>
    private void OnToggleTabTemporalCouplingMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabTemporalCoupling = !s.ShowTabTemporalCoupling);

    /// <summary>Toggle the visibility of the Architectural Flows (Chord) tab in the settings.</summary>
    private void OnToggleTabArchitecturalFlowMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabArchitecturalFlow = !s.ShowTabArchitecturalFlow);

    /// <summary>Toggle the visibility of the CodeCity 3D tab in the settings.</summary>
    private void OnToggleTabCodeCityMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabCodeCity = !s.ShowTabCodeCity);

    /// <summary>Toggle the visibility of the Cohesion & LCOM tab in settings.</summary>
    private void OnToggleTabCohesionMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabCohesion = !s.ShowTabCohesion);

    /// <summary>Toggle the visibility of the Maintainability Index tab in the settings.</summary>
    private void OnToggleTabMaintainabilityMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabMaintainability = !s.ShowTabMaintainability);

    /// <summary>Toggle the visibility of the Architectural Rules tab in the settings.</summary>
    private void OnToggleTabArchitecturalRulesMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabArchitecturalRules = !s.ShowTabArchitecturalRules);

    /// <summary>Toggle the visibility of the Clones and Duplications (CPD) tab in the settings.</summary>
    private void OnToggleTabCodeCloneMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabCodeClone = !s.ShowTabCodeClone);

    /// <summary>Toggle the visibility of the Social Code & Bus Factor tab in the settings.</summary>
    private void OnToggleTabSocialCodeMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabSocialCode = !s.ShowTabSocialCode);

    /// <summary>Toggle the visibility of the Data Flow & Taint Tracking tab in settings.</summary>
    private void OnToggleTabDataFlowMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabDataFlow = !s.ShowTabDataFlow);

    /// <summary>Toggle the visibility of the Diff & SemVer API tab in the settings.</summary>
    private void OnToggleTabApiDiffMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabApiDiff = !s.ShowTabApiDiff);

    /// <summary>Toggle the visibility of the CRUD Matrix tab in the settings.</summary>
    private void OnToggleTabCrudMatrixMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabCrudMatrix = !s.ShowTabCrudMatrix);

    /// <summary>Toggle the visibility of the Blast Radius Grid tab in settings.</summary>
    private void OnToggleTabBlastRadiusGridMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabBlastRadiusGrid = !s.ShowTabBlastRadiusGrid);

    /// <summary>Toggle the visibility of the Symbol Inspector tab in the settings.</summary>
    private void OnToggleTabInspectorMenuClicked(object? sender, RoutedEventArgs e)
    {
        var newValue = !SettingsService.Instance.Settings.ShowTabInspector;
        SettingsService.Instance.Update(s =>
        {
            s.ShowTabInspector = newValue;
            if (newValue) s.IsInspectorVisible = true;
        });
    }

    /// <summary>Toggle the visibility of the Source Code Preview tab in settings.</summary>
    private void OnToggleTabSourceCodeMenuClicked(object? sender, RoutedEventArgs e)
    {
        var newValue = !SettingsService.Instance.Settings.ShowTabSourceCode;
        SettingsService.Instance.Update(s =>
        {
            s.ShowTabSourceCode = newValue;
            if (newValue) s.IsInspectorVisible = true;
        });
    }

    #endregion

    #region Analysis Tabs Contextual Menu (TabControl)

    private TabItem? _contextMenuTargetTabItem;

    /// <summary>
    /// Manages the request to open the context menu on the headers of the analysis sheets.
    /// Show options to hide the specific tab, show all tabs, or open customization.
    /// </summary>
    private void OnCentralTabControlContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var visual = e.Source as Visual;
        var tabItem = FindTabItemFromHeaderVisual(visual, CentralTabControl);

        // If the click was not on a TabItem header belonging to the main TabControl, ignore
        if (tabItem == null || CentralTabControl == null)
            return;

        if (CentralTabControl.Resources.TryGetResource("AnalyticsTabContextMenu", null, out var res) && res is ContextMenu menu)
        {
            _contextMenuTargetTabItem = tabItem;
            menu.DataContext = tabItem;
            menu.PlacementTarget = tabItem;

            var tabTitle = GetTabTitle(tabItem);
            if (CtxTabHide != null)
                CtxTabHide.Header = !string.IsNullOrWhiteSpace(tabTitle)
                    ? LocalizationService.Instance.Get("TabContext_HideTabNamed", tabTitle)
                    : LocalizationService.Instance["TabContext_HideTab"];

            menu.Open(tabItem);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles the request to open the context menu on the right panel tab headers (Inspector / Source Code).
    /// Show options to hide the specific tab, show all tabs, or open customization.
    /// </summary>
    private void OnRightTabControlContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var visual = e.Source as Visual;
        var tabItem = FindTabItemFromHeaderVisual(visual, RightTabControl);

        // If the click was not on a TabItem header belonging to the right TabControl, ignore
        if (tabItem == null || RightTabControl == null)
            return;

        if (RightTabControl.Resources.TryGetResource("RightTabContextMenu", null, out var res) && res is ContextMenu menu)
        {
            _contextMenuTargetTabItem = tabItem;
            menu.DataContext = tabItem;
            menu.PlacementTarget = tabItem;

            var tabTitle = GetTabTitle(tabItem);
            if (CtxRightTabHide != null)
                CtxRightTabHide.Header = !string.IsNullOrWhiteSpace(tabTitle)
                    ? LocalizationService.Instance.Get("TabContext_HideTabNamed", tabTitle)
                    : LocalizationService.Instance["TabContext_HideTab"];

            menu.Open(tabItem);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Retrieves the header text of the specified TabItem for dynamic menu formatting.
    /// </summary>
    private static string GetTabTitle(TabItem tabItem)
    {
        if (tabItem.Header is StackPanel sp)
        {
            var tb = sp.Children.OfType<TextBlock>().FirstOrDefault();
            if (tb != null && !string.IsNullOrWhiteSpace(tb.Text)) return tb.Text;
        }
        if (tabItem.Header is TextBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text))

            return textBlock.Text;
        if (tabItem.Header is string str && !string.IsNullOrWhiteSpace(str))

            return str;
        return string.Empty;
    }

    /// <summary>
    /// Hides the specific analytic tab by saving the preference in the user configuration.
    /// </summary>
    private void OnTabContextMenuHideClicked(object? sender, RoutedEventArgs e)
    {
        var tabItem = _contextMenuTargetTabItem ?? GetTabItemFromSender(sender);
        if (tabItem != null)

            HideTab(tabItem);
    }

    /// <summary>
    /// Restore visibility of all analysis tabs by saving user configuration.
    /// </summary>
    private void OnTabContextMenuShowAllClicked(object? sender, RoutedEventArgs e)
    {
        SettingsService.Instance.Update(s =>
        {
            s.ShowTabGraph = true;
            s.ShowTabDsm = true;
            s.ShowTabTreemap = true;
            s.ShowTabHotspot = true;
            s.ShowTabScreenFlow = true;
            s.ShowTabControlFlow = true;
            s.ShowTabMartin = true;
            s.ShowTabDeadCode = true;
            s.ShowTabSharedState = true;
            s.ShowTabTemporalCoupling = true;
            s.ShowTabArchitecturalFlow = true;
            s.ShowTabCodeCity = true;
            s.ShowTabCohesion = true;
            s.ShowTabMaintainability = true;
            s.ShowTabArchitecturalRules = true;
            s.ShowTabCodeClone = true;
            s.ShowTabSocialCode = true;
            s.ShowTabDataFlow = true;
            s.ShowTabApiDiff = true;
            s.ShowTabCrudMatrix = true;
            s.ShowTabBlastRadiusGrid = true;
            s.ShowTabInspector = true;
            s.ShowTabSourceCode = true;
        });
    }

    /// <summary>
    /// Opens the Settings dialog box by positioning yourself directly on the "Analytical Tabs" section.
    /// </summary>
    private void OnTabContextMenuManageClicked(object? sender, RoutedEventArgs e)
    {
        OpenSettings(3);
    }

    /// <summary>
    /// Disables display of the TabItem specified in the persisted settings.
    /// </summary>
    private void HideTab(TabItem tabItem)
    {
        if (tabItem == TabItemInspector)
            SettingsService.Instance.Update(s => s.ShowTabInspector = false);
        else if (tabItem == TabItemSourceCode)
            SettingsService.Instance.Update(s => s.ShowTabSourceCode = false);
        else if (tabItem == GraphTabItem)
            SettingsService.Instance.Update(s => s.ShowTabGraph = false);
        else if (tabItem == DsmTabItem)
            SettingsService.Instance.Update(s => s.ShowTabDsm = false);
        else if (tabItem == TreemapTabItem)
            SettingsService.Instance.Update(s => s.ShowTabTreemap = false);
        else if (tabItem == HotspotTabItem)
            SettingsService.Instance.Update(s => s.ShowTabHotspot = false);
        else if (tabItem == ScreenFlowTabItem)
            SettingsService.Instance.Update(s => s.ShowTabScreenFlow = false);
        else if (tabItem == ControlFlowTabItem)
            SettingsService.Instance.Update(s => s.ShowTabControlFlow = false);
        else if (tabItem == MartinTabItem)
            SettingsService.Instance.Update(s => s.ShowTabMartin = false);
        else if (tabItem == DeadCodeTabItem)
            SettingsService.Instance.Update(s => s.ShowTabDeadCode = false);
        else if (tabItem == SharedStateTabItem)
            SettingsService.Instance.Update(s => s.ShowTabSharedState = false);
        else if (tabItem == TemporalCouplingTabItem)
            SettingsService.Instance.Update(s => s.ShowTabTemporalCoupling = false);
        else if (tabItem == ArchitecturalFlowTabItem)
            SettingsService.Instance.Update(s => s.ShowTabArchitecturalFlow = false);
        else if (tabItem == CodeCityTabItem)
            SettingsService.Instance.Update(s => s.ShowTabCodeCity = false);
        else if (tabItem == CohesionTabItem)
            SettingsService.Instance.Update(s => s.ShowTabCohesion = false);
        else if (tabItem == MaintainabilityTabItem)
            SettingsService.Instance.Update(s => s.ShowTabMaintainability = false);
        else if (tabItem == ArchitecturalRulesTabItem)
            SettingsService.Instance.Update(s => s.ShowTabArchitecturalRules = false);
        else if (tabItem == CodeCloneTabItem)
            SettingsService.Instance.Update(s => s.ShowTabCodeClone = false);
        else if (tabItem == SocialCodeTabItem)
            SettingsService.Instance.Update(s => s.ShowTabSocialCode = false);
        else if (tabItem == DataFlowTabItem)
            SettingsService.Instance.Update(s => s.ShowTabDataFlow = false);
        else if (tabItem == ApiDiffTabItem)
            SettingsService.Instance.Update(s => s.ShowTabApiDiff = false);
        else if (tabItem == CrudMatrixTabItem)
            SettingsService.Instance.Update(s => s.ShowTabCrudMatrix = false);
        else if (tabItem == GridTabItem)
            SettingsService.Instance.Update(s => s.ShowTabBlastRadiusGrid = false);
    }

    /// <summary>
    /// Manages the click or pointer pressed on the close button (X) present in each tab header.
    /// Hides the corresponding tab by saving the preference in the user settings.
    /// </summary>
    private void OnTabCloseButtonClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn)
            CloseTabFromButton(btn);
        else if (sender is Visual visual)
        {
            var button = visual as Button ?? visual.FindAncestorOfType<Button>();
            if (button != null)
                CloseTabFromButton(button);
        }
    }

    /// <summary>
    /// Locates the TabItem that owns the close button (X) and requests its deactivation.
    /// </summary>
    private void CloseTabFromButton(Button closeBtn)
    {
        // 1. Check if the Tag contains a direct reference to the TabItem
        if (closeBtn.Tag is TabItem tabItemTag)
        {
            HideTab(tabItemTag);
            return;
        }

        // 2. Search in the central TabControl
        if (CentralTabControl != null)
        {
            foreach (var tab in CentralTabControl.Items.OfType<TabItem>())
            {
                if (tab.Header is StackPanel sp && (sp == closeBtn.Parent || sp.Children.Contains(closeBtn) || closeBtn.FindAncestorOfType<TabItem>() == tab))
                {
                    HideTab(tab);
                    return;
                }
            }
        }

        // 3. Search in the right TabControl
        if (RightTabControl != null)
        {
            foreach (var tab in RightTabControl.Items.OfType<TabItem>())
            {
                if (tab.Header is StackPanel sp && (sp == closeBtn.Parent || sp.Children.Contains(closeBtn) || closeBtn.FindAncestorOfType<TabItem>() == tab))
                {
                    HideTab(tab);
                    return;
                }
            }
        }

        // 4. Fallback by moving up the visual tree
        var ancestorTab = closeBtn.FindAncestorOfType<TabItem>();
        if (ancestorTab != null)
            HideTab(ancestorTab);
    }

    /// <summary>
    /// Intercept PointerPressed events on the right panel to handle immediate closing of tabs (X).
    /// </summary>
    private void OnRightTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var currentPoint = e.GetCurrentPoint(this);
        if (!currentPoint.Properties.IsLeftButtonPressed)
            return;

        var visual = e.Source as Visual;
        var closeBtn = visual as Button ?? visual?.FindAncestorOfType<Button>();
        if (closeBtn != null && (closeBtn.Classes.Contains("tab-close-btn") || closeBtn.Name?.Contains("Close") == true))
        {
            e.Handled = true;
            CloseTabFromButton(closeBtn);
        }
    }

    private static TabItem? GetTabItemFromSender(object? sender)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.DataContext is TabItem tab1)
                return tab1;
            if (menuItem.Parent is ContextMenu ctx && ctx.PlacementTarget is TabItem tab2)
                return tab2;
            if (menuItem.Parent is ContextMenu ctx2 && ctx2.DataContext is TabItem tab3)
                return tab3;
        }
        return null;
    }

    /// <summary>
    /// Locate the TabItem belonging to a TabControl by going up the visual tree exclusively
    /// starting from the tab header (TabStrip / TabItem header), ignoring the internal content of the tab.
    /// 
    /// REGOLA ARCHITETTURALE CRITICA:
    /// CentralTabControl hosts both the tab headers and the body of the entire active view (Graph Canvas,
    /// HUD commands floating, Matrices, etc.). This function MUST return a TabItem ONLY IF the click point
    /// it is physically located within the visual header (title, icon or close button).
    /// If the click occurs within the tab content/canvas (e.g. HUD buttons such as Blast Radius or Export MD),
    /// MUST return null to not trap or block user events intended for view controls.
    /// </summary>
    private static TabItem? FindTabItemFromHeaderVisual(Visual? visual, TabControl? tabControl)
    {
        if (visual == null || tabControl == null)
            return null;

        var current = visual;
        while (current != null && current != tabControl)
        {
            if (current is TabItem tabItem && tabControl.Items.OfType<TabItem>().Contains(tabItem))
            {
                // Check that the click point is inside the visual header and not in the body of the tab
                if (tabItem.Header is Visual headerVisual && (visual == headerVisual || IsVisualDescendantOf(visual, headerVisual)))
                    return tabItem;

                // Fallback: if the header is not a Visual (e.g. string) or is wrapped in the ContentPresenter of the TabItem template
                var headerPresenter = tabItem.FindDescendantOfType<Avalonia.Controls.Presenters.ContentPresenter>();
                if (headerPresenter != null && (visual == headerPresenter || IsVisualDescendantOf(visual, headerPresenter)))
                    return tabItem;

                return null;
            }
            current = current.GetVisualParent();
        }

        return null;
    }

    private static bool IsVisualDescendantOf(Visual child, Visual parent)
    {
        var current = child.GetVisualParent();
        while (current != null)
        {
            if (current == parent)
                return true;
            current = current.GetVisualParent();
        }
        return false;
    }

    #endregion

    #region Sorting Analysis Tabs with Drag & Drop (CentralTabControl)

    /// <summary>
    /// Handles the pointer pressure (mouse down) event on analysis cards.
    /// Initialize the starting coordinates and detect the source TabItem without starting
    /// immediately drag, allowing normal selection changes with a single click.
    /// Intercepts the click on the close button (X) with absolute priority to close the tab instantly.
    /// 
    /// NOTE: Strictly ignore any click originating within the body of the tab (graph canvas,
    /// HUD buttons, matrix controls), letting the event flow to their respective handlers.
    /// </summary>
    /// <param name="sender">Sorgente dell'evento (CentralTabControl).</param>
    /// <param name="e">Pointer arguments containing coordinates and key states.</param>
    private void OnCentralTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Consider only the left mouse button
        var currentPoint = e.GetCurrentPoint(this);
        if (!currentPoint.Properties.IsLeftButtonPressed)
            return;

        // If the tab's close button (X) was clicked, close it immediately
        var visual = e.Source as Visual;
        var closeBtn = visual as Button ?? visual?.FindAncestorOfType<Button>();
        if (closeBtn != null && (closeBtn.Classes.Contains("tab-close-btn") || closeBtn.Name?.Contains("Close") == true))
        {
            e.Handled = true;
            CloseTabFromButton(closeBtn);
            return;
        }

        // Goes up the visual tree exclusively within the tab header (ignores the tab body/canvas)
        var tabItem = FindTabItemFromHeaderVisual(visual, CentralTabControl);

        // Ignores clicks outside the tab headers of the central TabControl
        if (tabItem == null || CentralTabControl == null)
            return;

        // Stores the initial data for calculating the movement threshold (deadzone)
        _draggedTabItem = tabItem;
        _tabDragStartPos = e.GetPosition(this);
        _tabDragOffsetWithinTab = e.GetPosition(tabItem);
        _isTabDragging = false;
        _targetDropIndex = -1;
    }

    /// <summary>
    /// Manages cursor movement when pressing the left button.
    /// Once past the 4px deadzone, activate Visual Studio style drag mode:
    /// 1. Display the ghost card floating under the mouse pointer.
    /// 2. Dim the transparency of the original card (35% opacity).
    /// 3. Calculates the insertion slot in real time and positions the vertical drop bar there.
    /// </summary>
    /// <param name="sender">Sorgente dell'evento (CentralTabControl).</param>
    /// <param name="e">Pointer arguments containing the current position.</param>
    private void OnCentralTabPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedTabItem == null || CentralTabControl == null || CentralTabPanel == null)
            return;

        // If the left button is no longer pressed (e.g. release outside the window), the drag ends
        var currentPoint = e.GetCurrentPoint(this);
        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            EndCentralTabDrag(null);
            return;
        }

        var currentPos = e.GetPosition(this);

        // Phase 1: Checking that the minimum movement threshold has been exceeded (4px deadzone)
        if (!_isTabDragging)
        {
            var diffX = Math.Abs(currentPos.X - _tabDragStartPos.X);
            var diffY = Math.Abs(currentPos.Y - _tabDragStartPos.Y);
            if (diffX > 4 || diffY > 4)
            {
                _isTabDragging = true;
                e.Pointer.Capture(CentralTabControl);
                CentralTabControl.SelectedItem = _draggedTabItem;
                _draggedTabItem.Opacity = 0.35;

                // Configure and view the floating ghost tab by cloning the tab's icon and text
                if (TabDragGhostBorder != null && TabDragGhostText != null)
                {
                    if (_draggedTabItem.Header is StackPanel sp)
                    {
                        var pathIcon = sp.Children.OfType<PathIcon>().FirstOrDefault();
                        if (pathIcon != null && TabDragGhostIcon != null)
                        {
                            TabDragGhostIcon.Data = pathIcon.Data;
                            TabDragGhostIcon.IsVisible = true;
                        }
                        else if (TabDragGhostIcon != null)
                        {
                            TabDragGhostIcon.IsVisible = false;
                        }
                        var tb = sp.Children.OfType<TextBlock>().FirstOrDefault();
                        TabDragGhostText.Text = tb?.Text ?? GetTabTitle(_draggedTabItem);
                    }
                    else
                    {
                        if (TabDragGhostIcon != null)
                            TabDragGhostIcon.IsVisible = false;
                        TabDragGhostText.Text = GetTabTitle(_draggedTabItem);
                    }

                    TabDragGhostBorder.IsVisible = true;
                }
            }
        }

        // Phase 2: Continuously updating the ghost position and calculating the insertion indicator
        if (_isTabDragging)
        {
            var posInPanel = e.GetPosition(CentralTabPanel);

            // 1. Move the floating ghost board in real time freely following the pointer (X and Y)
            var ghostX = posInPanel.X - _tabDragOffsetWithinTab.X;
            var ghostY = posInPanel.Y - _tabDragOffsetWithinTab.Y;
            _tabGhostTransform.X = ghostX;
            _tabGhostTransform.Y = ghostY;

            // 2. Detect all visible tabs with their coordinates to support multi-row layout
            var tabBoundsList = new List<(TabItem Tab, Rect Bounds, int Index)>();
            foreach (var tab in CentralTabControl.Items.OfType<TabItem>())
            {
                if (!tab.IsVisible)
                    continue;

                var origin = tab.TranslatePoint(new Point(0, 0), CentralTabPanel);
                if (!origin.HasValue)
                    continue;

                var bounds = new Rect(origin.Value, tab.Bounds.Size);
                tabBoundsList.Add((tab, bounds, CentralTabControl.Items.IndexOf(tab)));
            }

            if (tabBoundsList.Count > 0)
            {
                // Group tabs into visual rows based on vertical proximity of the Top (8px tolerance)
                var rows = new List<List<(TabItem Tab, Rect Bounds, int Index)>>();
                foreach (var item in tabBoundsList.OrderBy(t => t.Bounds.Top).ThenBy(t => t.Bounds.Left))
                {
                    var existingRow = rows.FirstOrDefault(r => Math.Abs(r[0].Bounds.Top - item.Bounds.Top) < 8);
                    if (existingRow != null)
                    {
                        existingRow.Add(item);
                    }
                    else
                    {
                        rows.Add(new List<(TabItem Tab, Rect Bounds, int Index)> { item });
                    }
                }

                // Sort each row's tabs from left to right and rows from top to bottom
                foreach (var row in rows)
                {
                    row.Sort((a, b) => a.Bounds.Left.CompareTo(b.Bounds.Left));
                }
                rows.Sort((a, b) => a[0].Bounds.Top.CompareTo(b[0].Bounds.Top));

                // Locates the target line of sight where the mouse pointer is located (Y coordinates)
                List<(TabItem Tab, Rect Bounds, int Index)> targetRow;
                if (posInPanel.Y <= rows[0].Min(t => t.Bounds.Bottom))
                {
                    targetRow = rows[0];
                }
                else if (posInPanel.Y >= rows[^1].Max(t => t.Bounds.Top))
                {
                    targetRow = rows[^1];
                }
                else
                {
                    // Find the row with the smallest vertical distance from the cursor or that contains posInPanel.Y
                    targetRow = rows.OrderBy(r =>
                    {
                        var top = r.Min(t => t.Bounds.Top);
                        var bottom = r.Max(t => t.Bounds.Bottom);
                        if (posInPanel.Y >= top && posInPanel.Y <= bottom)
                            return 0.0;
                        return Math.Min(Math.Abs(posInPanel.Y - top), Math.Abs(posInPanel.Y - bottom));
                    }).First();
                }

                // Within the identified row, calculate the horizontal drop slot based on the midpoint
                _targetDropIndex = -1;
                double indicatorX = 0;
                double indicatorY = targetRow[0].Bounds.Top + 2;
                double indicatorHeight = Math.Max(20, targetRow[0].Bounds.Height - 4);

                var firstTab = targetRow[0];
                var lastTab = targetRow[^1];

                if (posInPanel.X < (firstTab.Bounds.Left + (firstTab.Bounds.Width / 2.0)))
                {
                    // Before the first element of the line
                    _targetDropIndex = firstTab.Index;
                    indicatorX = firstTab.Bounds.Left - 1;
                    indicatorY = firstTab.Bounds.Top + 2;
                    indicatorHeight = Math.Max(20, firstTab.Bounds.Height - 4);
                }
                else if (posInPanel.X >= (lastTab.Bounds.Left + (lastTab.Bounds.Width / 2.0)))
                {
                    // After the last element of the line
                    _targetDropIndex = lastTab.Index + 1;
                    indicatorX = lastTab.Bounds.Right + 1;
                    indicatorY = lastTab.Bounds.Top + 2;
                    indicatorHeight = Math.Max(20, lastTab.Bounds.Height - 4);
                }
                else
                {
                    // Between two tabs within the row
                    for (int i = 0; i < targetRow.Count; i++)
                    {
                        var item = targetRow[i];
                        var midX = item.Bounds.Left + (item.Bounds.Width / 2.0);
                        if (posInPanel.X < midX)
                        {
                            _targetDropIndex = item.Index;
                            indicatorX = item.Bounds.Left - 1;
                            indicatorY = item.Bounds.Top + 2;
                            indicatorHeight = Math.Max(20, item.Bounds.Height - 4);
                            break;
                        }
                    }
                }

                // Positions and makes visible the vertical drop bar on the correct row and column
                if (_targetDropIndex >= 0 && TabDropIndicator != null)
                {
                    _tabDropIndicatorTransform.X = indicatorX;
                    _tabDropIndicatorTransform.Y = indicatorY;
                    TabDropIndicator.Height = indicatorHeight;
                    TabDropIndicator.IsVisible = true;
                }
            }

            e.Handled = true;
        }
    }

    /// <summary>
    /// Concludes the drag operation when the mouse button is released:
    /// moves the card to the calculated destination, restores its normal display
    /// and save the new sorting in persistent preferences.
    /// 
    /// CRITICAL ATTENTION (CLICK & POINTER CAPTURE REGRESSION IN AVALONIA):
    /// This method is registered as Tunnel Handler on CentralTabControl. Since CentralTabControl
    /// roots all displayed content (including FloatingHudBorder, GraphControl, Button, etc.),
    /// ANY release of the pointer inside the central area triggers this handler BEFORE the child controls.
    /// In Avalonia, when a button is pressed, it takes over pointer capture (pointer.Capture).
    /// If this handler unconditionally invoked EndCentralTabDrag() and pointer.Capture(null) on every PointerReleased,
    /// would take the mouse capture away from the button while it waits for release, generating the event
    /// PointerCaptureLost on the button and CANCELING its Click event completely!
    /// Therefore the guard clause (_draggedTabItem == null && !_isTabDragging) is ESSENTIAL and must NEVER be removed.
    /// </summary>
    /// <param name="sender">Sorgente dell'evento.</param>
    /// <param name="e">Pointer release arguments.</param>
    private void OnCentralTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // If there is no tab drag operation or preparation in progress, please ignore
        // so as not to interfere with the life cycle of the click events of the child controls (e.g. HUD buttons)
        if (_draggedTabItem == null && !_isTabDragging)
            return;

        if (_isTabDragging && _draggedTabItem != null && CentralTabControl != null)
        {
            var fromIndex = CentralTabControl.Items.IndexOf(_draggedTabItem);
            var toIndex = _targetDropIndex;

            if (fromIndex >= 0 && toIndex >= 0)
            {
                // If the destination is later than the starting index, it compensates for the removal of the element
                if (toIndex > fromIndex)
                    toIndex--;

                // Performs atomic shift in the collection and selects the tab
                if (toIndex >= 0 && toIndex < CentralTabControl.Items.Count && toIndex != fromIndex)
                {
                    CentralTabControl.Items.RemoveAt(fromIndex);
                    CentralTabControl.Items.Insert(toIndex, _draggedTabItem);
                    CentralTabControl.SelectedItem = _draggedTabItem;
                    SaveCurrentTabOrder();
                }
            }

            e.Handled = true;
        }

        EndCentralTabDrag(e.Pointer);
    }

    /// <summary>
    /// Handles sudden loss of pointer capture (e.g. window change or modal popup)
    /// saving the state if applicable and cleaning up the visual elements of the drag.
    /// As with PointerReleased, ignore the event if it doesn't involve a dragged tab.
    /// </summary>
    /// <param name="sender">Sorgente dell'evento.</param>
    /// <param name="e">Capture loss topics.</param>
    private void OnCentralTabPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Ignore if no tab drag operation is in progress
        if (_draggedTabItem == null && !_isTabDragging)
            return;

        if (_isTabDragging && _draggedTabItem != null && CentralTabControl != null)
        {
            var fromIndex = CentralTabControl.Items.IndexOf(_draggedTabItem);
            var toIndex = _targetDropIndex;

            if (fromIndex >= 0 && toIndex >= 0)
            {
                if (toIndex > fromIndex)
                    toIndex--;

                if (toIndex >= 0 && toIndex < CentralTabControl.Items.Count && toIndex != fromIndex)
                {
                    CentralTabControl.Items.RemoveAt(fromIndex);
                    CentralTabControl.Items.Insert(toIndex, _draggedTabItem);
                    CentralTabControl.SelectedItem = _draggedTabItem;
                    SaveCurrentTabOrder();
                }
            }
        }

        EndCentralTabDrag(null);
    }

    /// <summary>
    /// Release pointer capture, hide ghost tab and drop bar,
    /// and restores the full opacity of the source card.
    /// 
    /// SECURITY NOTE: Release pointer capture (pointer.Capture(null)) ONLY
    /// if _isTabDragging was true (i.e. if the capture was taken by CentralTabControl during the drag).
    /// Never release the capture if _isTabDragging is false to avoid resetting the capture of third-party controls.
    /// </summary>
    /// <param name="pointer">Pointer to release if available.</param>
    private void EndCentralTabDrag(IPointer? pointer)
    {
        // Release the capture only if it was actually captured by CentralTabControl
        if (_isTabDragging)
            pointer?.Capture(null);

        if (_draggedTabItem != null)
            _draggedTabItem.Opacity = 1.0;

        _draggedTabItem = null;
        _isTabDragging = false;
        _targetDropIndex = -1;

        if (TabDragGhostBorder != null)
            TabDragGhostBorder.IsVisible = false;

        if (TabDropIndicator != null)
            TabDropIndicator.IsVisible = false;
    }

    /// <summary>
    /// Saves the current sequence of central panel TabItem names in the user settings.
    /// Check in advance whether the ordering has actually changed to avoid redundant writing.
    /// </summary>
    private void SaveCurrentTabOrder()
    {
        if (CentralTabControl == null)
            return;

        var order = CentralTabControl.Items
            .OfType<TabItem>()
            .Select(t => t.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();

        var currentOrder = SettingsService.Instance.Settings.AnalyticsTabOrder;
        if (currentOrder != null && currentOrder.SequenceEqual(order))
            return;

        SettingsService.Instance.Update(s => s.AnalyticsTabOrder = order);
    }

    /// <summary>
    /// Applies the saved custom sorting to the tabs of the central TabControl.
    /// Securely manages the possible presence of new cards introduced in subsequent versions
    /// of the application, adding them to the end of the stored ordering.
    /// </summary>
    private void ApplySavedTabOrder()
    {
        if (CentralTabControl == null)
            return;

        var savedOrder = SettingsService.Instance.Settings.AnalyticsTabOrder;
        if (savedOrder == null || savedOrder.Count == 0)
            return;

        var allTabs = CentralTabControl.Items.OfType<TabItem>().ToList();
        if (allTabs.Count == 0)
            return;

        var tabMap = allTabs
            .Where(t => !string.IsNullOrEmpty(t.Name))
            .ToDictionary(t => t.Name!, t => t);

        var newOrder = new List<TabItem>();
        foreach (var name in savedOrder)
        {
            if (tabMap.TryGetValue(name, out var tab))
            {
                newOrder.Add(tab);
                tabMap.Remove(name);
            }
        }

        // Add any tabs not present in the saved sequence to the queue (e.g. new features)
        foreach (var tab in allTabs)
        {
            if (tab.Name != null && tabMap.ContainsKey(tab.Name))
                newOrder.Add(tab);
        }

        // If the sorting already matches perfectly, do not alter the collection
        if (newOrder.Count != allTabs.Count || newOrder.SequenceEqual(allTabs))
            return;

        var selected = CentralTabControl.SelectedItem;
        CentralTabControl.Items.Clear();
        foreach (var tab in newOrder)
            CentralTabControl.Items.Add(tab);

        if (selected != null && CentralTabControl.Items.Contains(selected))
            CentralTabControl.SelectedItem = selected;
    }

    /// <summary>
    /// Restores the original factory sorting of the analysis cards defined in the XAML file,
    /// resetting the sequence stored in the user settings.
    /// </summary>
    private void RestoreDefaultTabOrder()
    {
        if (CentralTabControl == null || _defaultCentralTabOrder.Count == 0)
            return;

        var selected = CentralTabControl.SelectedItem;
        CentralTabControl.Items.Clear();
        foreach (var tab in _defaultCentralTabOrder)
            CentralTabControl.Items.Add(tab);

        if (selected != null && CentralTabControl.Items.Contains(selected))
            CentralTabControl.SelectedItem = selected;

        SettingsService.Instance.Update(s => s.AnalyticsTabOrder = new List<string>());
    }

    #endregion

    #region Diff & SemVer API Baseline Management

    /// <summary>
    /// Sets the current workspace graph snapshot as the baseline for API and SemVer comparison.
    /// </summary>
    private void OnApiDiffSetCurrentAsBaseline()
    {
        if (_graph?.SemanticGraph == null)
        {
            StatusText.Text = LocalizationService.Instance["ApiDiff_Msg_NoProjectOpen"];
            return;
        }

        var targetGraph = GetTargetSemanticGraph();
        _apiDiffBaselineGraph = targetGraph;
        _apiDiffBaselineName = !string.IsNullOrEmpty(_currentFolderPath)
            ? Path.GetFileName(_currentFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : LocalizationService.Instance["ApiDiff_CurrentWorkspace"];

        var publicCount = CountPublicSymbols(_apiDiffBaselineGraph);
        ApiDiff.SetBaselineInfo(_apiDiffBaselineName, publicCount);

        var report = ApiDiffAnalyzer.Compare(_apiDiffBaselineGraph, targetGraph);
        ApiDiff.Report = report;

        StatusText.Text = LocalizationService.Instance.Get("ApiDiff_Msg_BaselineSet", publicCount);
    }

    /// <summary>
    /// Opens a folder selector to load a previous version or release as the API comparison baseline.
    /// </summary>
    private async Task OnApiDiffLoadBaselineAsync()
    {
        var localizationService = LocalizationService.Instance;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = localizationService["ApiDiff_PickBaselineTitle"],
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var baselineFolder = folders[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(baselineFolder) || !Directory.Exists(baselineFolder)) return;

        StatusText.Text = localizationService.Get("Loading_AnalyzeProjectSub", Path.GetFileName(baselineFolder));

        try
        {
            var visualGraph = await Task.Run(() => ProjectAnalysisService.AnalyzePaths(new[] { baselineFolder }));
            var baselineGraph = visualGraph.SemanticGraph;
            if (baselineGraph == null) return;

            _apiDiffBaselineGraph = baselineGraph;
            _apiDiffBaselineName = Path.GetFileName(baselineFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var publicCount = CountPublicSymbols(baselineGraph);
            ApiDiff.SetBaselineInfo(_apiDiffBaselineName, publicCount);

            if (_graph?.SemanticGraph != null)
            {
                var targetGraph = GetTargetSemanticGraph();
                var report = ApiDiffAnalyzer.Compare(baselineGraph, targetGraph);
                ApiDiff.Report = report;
            }

            StatusText.Text = localizationService.Get("ApiDiff_Msg_BaselineLoaded", _apiDiffBaselineName, publicCount);
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("ApiDiff_Msg_ErrorLoadingBaseline", ex.Message);
        }
    }

    /// <summary>
    /// Removes the current reference baseline restoring the initial wait state of the Diff API tab.
    /// </summary>
    private void OnApiDiffClearBaseline()
    {
        _apiDiffBaselineGraph = null;
        _apiDiffBaselineName = null;
        ApiDiff.SetBaselineInfo(null, null);
        ApiDiff.Report = null;
        StatusText.Text = LocalizationService.Instance["ApiDiff_Status_NoBaseline"];
    }

    /// <summary>
    /// Counts public symbols exported from a graph for the baseline.
    /// </summary>
    private static int CountPublicSymbols(CodeGraph? graph)
    {
        if (graph == null) return 0;
        return graph.Symbols.Count(s =>
            s.Kind is not (SymbolKind.Project or SymbolKind.Namespace) &&
            (s.Accessibility is Accessibility.Public or Accessibility.Global or Accessibility.Protected or Accessibility.ProtectedInternal or Accessibility.Default));
    }

    #endregion

    #region VCS integration (Git & SVN)

    /// <summary>
    /// Updates the visibility, icon, and text of the VCS (Git or SVN) branch indicator in the status bar.
    /// </summary>
    private void UpdateVcsBranchStatus(string? folderPath)
    {
        if (!string.IsNullOrEmpty(folderPath))
        {
            var branch = VcsService.Instance.GetCurrentBranch(folderPath, out var vcsType);
            if (vcsType != VcsType.None && !string.IsNullOrEmpty(branch))
            {
                GitBranchText.Text = branch;
                GitBranchPanel.IsVisible = true;

                // Updates vector icon based on VCS provider
                var iconKey = vcsType == VcsType.Svn ? "IconSvn" : "IconGitBranch";
                if (this.TryFindResource(iconKey, out var res) && res is Geometry geo)

                    GitBranchIcon.Data = geo;

                var tooltipKey = vcsType == VcsType.Svn ? "Svn_BranchTooltip" : "Git_BranchTooltip";
                ToolTip.SetTip(GitBranchButton, LocalizationService.Instance.Get(tooltipKey, branch));
                return;
            }
        }

        GitBranchPanel.IsVisible = false;
    }

    /// <summary>
    /// Opens the branch selection and switching popup for the active Git or SVN repository.
    /// </summary>
    private void OpenGitBranchPopup()
    {
        if (string.IsNullOrEmpty(_currentFolderPath) || !VcsService.Instance.IsVcsRepository(_currentFolderPath))
        {
            GitBranchPopup.IsOpen = false;
            return;
        }

        var vcsType = VcsService.Instance.DetectVcsType(_currentFolderPath);
        var localizationService = LocalizationService.Instance;

        GitBranchPopupTitle.Text = vcsType == VcsType.Svn
            ? localizationService["Svn_SwitchBranchTitle"]
            : localizationService["Git_SwitchBranchTitle"];

        GitBranchSearchBox.Watermark = vcsType == VcsType.Svn
            ? localizationService["Svn_SearchBranchWatermark"]
            : localizationService["Git_SearchBranchWatermark"];

        _allVcsBranches = VcsService.Instance.GetBranches(_currentFolderPath);
        GitBranchSearchBox.Text = string.Empty;
        GitBranchListBox.ItemsSource = _allVcsBranches;
        GitBranchPopup.IsOpen = true;
        GitBranchSearchBox.Focus();
    }

    /// <summary>
    /// Filter the branches displayed in the popup based on the text you type in the search box.
    /// </summary>
    private void OnGitBranchSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = GitBranchSearchBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query))

            GitBranchListBox.ItemsSource = _allVcsBranches;

        else
        {
            GitBranchListBox.ItemsSource = _allVcsBranches
                .Where(b => b.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            b.Target.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>
    /// Manages user-selected branch switching (Git checkout or SVN switch),
    /// invoking the background operation and re-analyzing the entire project.
    /// </summary>
    private async void OnGitBranchSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (GitBranchListBox.SelectedItem is not VcsBranchInfo selectedBranch) return;

        // Reset selection to avoid multiple triggers
        GitBranchListBox.SelectedItem = null;
        GitBranchPopup.IsOpen = false;

        var current = VcsService.Instance.GetCurrentBranch(_currentFolderPath, out var vcsType);
        if (string.Equals(current, selectedBranch.Name, StringComparison.OrdinalIgnoreCase))

            return;

        if (string.IsNullOrEmpty(_currentFolderPath)) return;

        var localizationService = LocalizationService.Instance;
        var switchingMessageKey = selectedBranch.VcsType == VcsType.Svn ? "Svn_SwitchingBranch" : "Git_SwitchingBranch";
        var successMessageKey = selectedBranch.VcsType == VcsType.Svn ? "Svn_SwitchSuccess" : "Git_SwitchSuccess";
        var errorMessageKey = selectedBranch.VcsType == VcsType.Svn ? "Svn_SwitchError" : "Git_SwitchError";

        ShowLoading(localizationService.Get(switchingMessageKey, selectedBranch.Name), localizationService["Status_Ready"]);
        await Task.Delay(16);

        try
        {
            var (success, error) = await VcsService.Instance.SwitchBranchAsync(_currentFolderPath, selectedBranch);
            if (success)
            {
                UpdateVcsBranchStatus(_currentFolderPath);
                await AnalyzeAndLoadFolderAsync(_currentFolderPath, forceReanalyze: true);
                StatusText.Text = localizationService.Get(successMessageKey, selectedBranch.Name);
            }
            else
            {
                StatusText.Text = localizationService.Get(errorMessageKey, error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get(errorMessageKey, ex.Message);
        }
        finally
        {
            HideLoading();
        }
    }

    #endregion

    #region Software Update System

    /// <summary>
    /// Automatically checks for software updates in the background when the application starts.
    /// </summary>
    private async Task CheckForUpdatesOnStartupAsync()
    {
        // Initial delay to allow project rendering and loading to complete
        await Task.Delay(2500);

        try
        {
            var update = await UpdateService.Instance.CheckForUpdatesAsync();
            if (update != null)

                ShowUpdateModal(update);
        }
        catch
        {
            // Silent on startup in case of network errors or missing endpoint
        }
    }

    /// <summary>
    /// Performs manual checks for updates (from the Help -> Check for Updates menu).
    /// </summary>
    private async Task CheckForUpdatesManualAsync()
    {
        var localizationService = LocalizationService.Instance;
        StatusText.Text = localizationService["Update_Checking"];

        try
        {
            var update = await UpdateService.Instance.CheckForUpdatesAsync(forceCheck: true);
            if (update != null)

                ShowUpdateModal(update);

            else
            {
                StatusText.Text = localizationService.Get("Update_NoUpdates", UpdateService.CurrentVersionString);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = localizationService.Get("Status_ErrorCheckUpdates", ex.Message);
        }
    }

    /// <summary>
    /// Shows the update notification modal overlay with "Download later" and "Update now" options.
    /// </summary>
    private void ShowUpdateModal(AppUpdateInfo update)
    {
        _availableUpdate = update;
        UpdateModalControl.Show(update);
    }

    /// <summary>
    /// Closes the update modal upon user request ("Download after").
    /// </summary>
    private void DismissUpdateModal()
    {
        UpdateModalControl.Hide();
    }

    private void OnUpdateModalNowRequested(object? sender, AppUpdateInfo update)
    {
        var localizationService = LocalizationService.Instance;
        StatusText.Text = localizationService["Update_OpeningDownload"];
        UpdateModalControl.Hide();
        UpdateService.Instance.OpenDownloadPage(update?.DownloadUrl);
    }

    private void OnUpdateModalLaterRequested(object? sender, EventArgs e)
    {
        DismissUpdateModal();
    }

    #endregion

    #region Panel Visibility, Keyboard Shortcuts & Maintenance

    /// <summary>
    /// Manages global keyboard shortcuts of the main window (e.g. Ctrl+B for Project Explorer, Ctrl+I for Symbol Inspector).
    /// </summary>
    /// <param name="sender">Source of key press event.</param>
    /// <param name="e">Event arguments containing modifiers and the pressed key.</param>
    private void OnMainWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isTabDragging)
        {
            EndCentralTabDrag(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.B)
            {
                ToggleExplorer();
                e.Handled = true;
            }
            else if (e.Key == Key.I)
            {
                ToggleInspector();
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Toggle the visibility of the Project Explorer left side panel or set a forced state.
    /// </summary>
    /// <param name="forceState">Desired visibility state (true to show, false to hide, null to toggle).</param>
    public void ToggleExplorer(bool? forceState = null)
    {
        SetExplorerVisibility(forceState ?? !_isExplorerVisible);
    }

    /// <summary>
    /// Sets the visibility and size of the Project Explorer layout column in the central grid, saving the state if required.
    /// Accurately preserves the custom width set by the user via GridSplitter.
    /// </summary>
    /// <param name="isVisible">Indicates whether the Project Explorer panel should be visible.</param>
    /// <param name="updateSettings">Indicates whether to persist the preference in user settings.</param>
    public void SetExplorerVisibility(bool isVisible, bool updateSettings = true)
    {
        _isExplorerVisible = isVisible;
        if (isVisible)
        {
            // If the panel is already visible, update only the stored width without overwriting the layout
            if (ProjectExplorerControl.IsVisible && CentralGrid.ColumnDefinitions[0].ActualWidth >= 160)
            {
                _savedExplorerWidth = CentralGrid.ColumnDefinitions[0].ActualWidth;
            }
            else
            {
                // Determine optimal width from saved preferences or default value (280px)
                var w = _savedExplorerWidth >= 160
                    ? _savedExplorerWidth
                    : (SettingsService.Instance.Settings.ExplorerWidth >= 160 ? SettingsService.Instance.Settings.ExplorerWidth : 280);
                _savedExplorerWidth = w;
                CentralGrid.ColumnDefinitions[0].MinWidth = 180;
                CentralGrid.ColumnDefinitions[0].MaxWidth = 600;
                CentralGrid.ColumnDefinitions[0].Width = new GridLength(w, GridUnitType.Pixel);
                CentralGrid.ColumnDefinitions[1].Width = new GridLength(4, GridUnitType.Pixel);
                ProjectExplorerControl.IsVisible = true;
                LeftGridSplitter.IsVisible = true;
            }
        }
        else
        {
            // Before hiding, save the current effective width of the panel for later restoration
            if (ProjectExplorerControl.Bounds.Width >= 160)
                _savedExplorerWidth = ProjectExplorerControl.Bounds.Width;

            CentralGrid.ColumnDefinitions[0].MinWidth = 0;
            CentralGrid.ColumnDefinitions[0].MaxWidth = 0;
            CentralGrid.ColumnDefinitions[0].Width = new GridLength(0, GridUnitType.Pixel);
            CentralGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
            ProjectExplorerControl.IsVisible = false;
            LeftGridSplitter.IsVisible = false;
        }

        if (updateSettings)
        {
            SettingsService.Instance.Update(s =>
            {
                s.IsExplorerVisible = _isExplorerVisible;
                if (_isExplorerVisible) s.ExplorerWidth = _savedExplorerWidth;
            });
        }

        UpdatePanelToggleVisuals();
    }

    /// <summary>
    /// Toggle the visibility of the Symbol Inspector right side panel or set a forced state.
    /// </summary>
    /// <param name="forceState">Desired visibility state (true to show, false to hide, null to toggle).</param>
    public void ToggleInspector(bool? forceState = null)
    {
        SetInspectorVisibility(forceState ?? !_isInspectorVisible);
    }

    /// <summary>
    /// Sets the visibility and size of the Symbol Inspector layout column in the central grid, saving the state if required.
    /// Accurately preserves the custom width set by the user via GridSplitter without restoring default values.
    /// </summary>
    /// <param name="isVisible">Indicates whether the Symbol Inspector panel should be visible.</param>
    /// <param name="updateSettings">Indicates whether to persist the preference in user settings.</param>
    public void SetInspectorVisibility(bool isVisible, bool updateSettings = true)
    {
        _isInspectorVisible = isVisible;
        if (isVisible)
        {
            // Automatically reactivate tabs if both were deactivated
            if (!SettingsService.Instance.Settings.ShowTabInspector && !SettingsService.Instance.Settings.ShowTabSourceCode)
            {
                SettingsService.Instance.Update(s =>
                {
                    s.ShowTabInspector = true;
                    s.ShowTabSourceCode = true;
                });
            }

            // If the panel is already visible, it stores the current width without forcing a reset of the columns
            if (RightPanelContainer.IsVisible && CentralGrid.ColumnDefinitions[4].ActualWidth >= 200)
            {
                _savedInspectorWidth = CentralGrid.ColumnDefinitions[4].ActualWidth;
            }
            else
            {
                // Determine optimal width from saved preferences or default value (360px)
                var w = _savedInspectorWidth >= 200
                    ? _savedInspectorWidth
                    : (SettingsService.Instance.Settings.InspectorWidth >= 200 ? SettingsService.Instance.Settings.InspectorWidth : 360);
                _savedInspectorWidth = w;
                CentralGrid.ColumnDefinitions[4].MinWidth = 240;
                CentralGrid.ColumnDefinitions[4].MaxWidth = 800;
                CentralGrid.ColumnDefinitions[4].Width = new GridLength(w, GridUnitType.Pixel);
                CentralGrid.ColumnDefinitions[3].Width = new GridLength(4, GridUnitType.Pixel);
                RightPanelContainer.IsVisible = true;
                RightGridSplitter.IsVisible = true;
            }
        }
        else
        {
            // Before hiding, save the current effective width of the panel for later restoration
            if (RightPanelContainer.Bounds.Width >= 200)
                _savedInspectorWidth = RightPanelContainer.Bounds.Width;
            else if (SymbolInspectorControl.Bounds.Width >= 200)
                _savedInspectorWidth = SymbolInspectorControl.Bounds.Width;

            CentralGrid.ColumnDefinitions[4].MinWidth = 0;
            CentralGrid.ColumnDefinitions[4].MaxWidth = 0;
            CentralGrid.ColumnDefinitions[4].Width = new GridLength(0, GridUnitType.Pixel);
            CentralGrid.ColumnDefinitions[3].Width = new GridLength(0, GridUnitType.Pixel);
            RightPanelContainer.IsVisible = false;
            RightGridSplitter.IsVisible = false;
        }

        if (updateSettings)
        {
            SettingsService.Instance.Update(s =>
            {
                s.IsInspectorVisible = _isInspectorVisible;
                if (_isInspectorVisible) s.InspectorWidth = _savedInspectorWidth;
            });
        }

        UpdatePanelToggleVisuals();
    }

    /// <summary>
    /// Updates the active/inactive visual style of the Explorer and Inspector panel toggle buttons in the top bar.
    /// </summary>
    private void UpdatePanelToggleVisuals()
    {
        ToggleExplorerTopButton.Classes.Set("active", _isExplorerVisible);
        ToggleInspectorTopButton.Classes.Set("active", _isInspectorVisible);
    }

    /// <summary>
    /// Resets all application settings to default values, updating the theme, language, layout and visibility of panels.
    /// </summary>
    private void ResetSettingsToDefaults()
    {
        var defaultSettings = SettingsService.Instance.Reset();
        ThemeService.Instance.ApplyTheme(defaultSettings.Theme);
        LocalizationService.Instance.SetLanguage(defaultSettings.Language);
        SetLayoutMode(defaultSettings.PreferredLayout);
        RestoreDefaultTabOrder();
        SetExplorerVisibility(true);
        SetInspectorVisibility(true);
        UpdateRecentProjectsMenu();
        WelcomeControl.Refresh();
        StatusText.Text = LocalizationService.Instance["Status_SettingsReset"];
    }

    /// <summary>
    /// Completely clears the local project cache (%APPDATA%\Macondo\cache\) and invalidates in-memory graph queries.
    /// </summary>
    private void ClearProjectCache()
    {
        var count = ProjectCacheService.Instance.ClearAllCache();
        _graph?.Queries?.ClearCache();
        StatusText.Text = LocalizationService.Instance.Get("Status_CacheCleared", count);
        WelcomeControl.Refresh();
    }

    #endregion

    #region Welcome Screen (Welcome View)

    /// <summary>
    /// Show the initial welcome screen by hiding the graph analysis tabs.
    /// </summary>
    private void ShowWelcomeScreen(string? pendingFolder = null)
    {
        _graph = null;
        CentralTabControl.IsVisible = false;
        WelcomeControl.IsVisible = true;
        ProjectExplorerControl.ItemsSource = null;
        ShowEmptyInspector();
        UpdateStatsBar();
        WelcomeControl.Refresh(pendingFolder);
    }

    /// <summary>
    /// Handles the quick analysis request from the pending project card of the welcome view.
    /// </summary>
    private async void OnWelcomePendingAnalyzeRequested(object? sender, string folderPath)
    {
        await AnalyzeAndLoadFolderAsync(folderPath, forceReanalyze: true);
    }

    /// <summary>
    /// Handles the project file open request from the welcome view.
    /// </summary>
    private async void OnWelcomeOpenProjectRequested(object? sender, EventArgs e)
    {
        await OpenProjectFilesAsync();
    }

    /// <summary>
    /// Handles the folder open request from the welcome view.
    /// </summary>
    private async void OnWelcomeOpenFolderRequested(object? sender, EventArgs e)
    {
        await OpenFolderAsync();
    }

    /// <summary>
    /// Manages the demo fixture loading request from the welcome view.
    /// </summary>
    private void OnWelcomeLoadFixturesRequested(object? sender, EventArgs e)
    {
        LoadDefaultFixtures();
    }

    /// <summary>
    /// Handles the selection of a recent item from the welcome view.
    /// </summary>
    private async void OnWelcomeRecentItemClicked(object? sender, IReadOnlyList<string> paths)
    {
        await AnalyzeAndLoadPathsAsync(paths);
    }

    #endregion

    #region Localization & UI Update

    /// <summary>
    /// Updates all labels, tooltips, menu headers, columns and user interface texts to the language currently selected in the <see cref="LocalizationService"/>.
    /// </summary>
    private void UpdateLocalizedUI()
    {
        var localizationService = LocalizationService.Instance;

        // Welcome Screen
        WelcomeControl.UpdateLocalizedUI();

        // Menu File
        FileMenu.Header = localizationService["Menu_File"];
        OpenSubMenu.Header = localizationService["Menu_Open"];
        OpenProjectFilesMenuItem.Header = localizationService["Menu_OpenProjectFiles"];
        OpenFolderMenuItem.Header = localizationService["Menu_OpenFolder"];
        UpdateRecentProjectsMenu();
        RefreshProjectMenuItem.Header = localizationService["Menu_RefreshProject"];
        LoadFixturesMenuItem.Header = localizationService["Menu_LoadFixtures"];
        GenerateFakeMenuItem.Header = localizationService["Menu_GenerateFake"];
        ExportMarkdownMenuItem.Header = localizationService["Menu_ExportMarkdown"];
        ExportJsonMenuItem.Header = localizationService["Menu_ExportJson"];
        ImportJsonMenuItem.Header = localizationService["Menu_ImportJson"];
        ExportSliceMenuItem.Header = localizationService["Menu_ExportSlice"];
        SettingsMenuItem.Header = localizationService["Menu_Settings"];
        ExitMenuItem.Header = localizationService["Menu_Exit"];

        // Menu Visualizza
        ViewMenu.Header = localizationService["Menu_View"];
        ToggleExplorerMenuItem.Header = localizationService["Menu_ToggleExplorer"];
        ToggleInspectorMenuItem.Header = localizationService["Menu_ToggleInspector"];
        ResetViewMenuItem.Header = localizationService["Menu_ResetView"];
        ExpandAllMenuItem.Header = localizationService["Menu_ExpandAll"];
        CollapseAllMenuItem.Header = localizationService["Menu_CollapseAll"];
        FilterDetailLevelMenuItem.Header = localizationService["Menu_FilterDetailLevel"];
        FilterProjectsMenuItem.Header = localizationService["Menu_FilterProjects"];
        FilterAllMenuItem.Header = localizationService["Filter_AllMembers"];
        FilterMethodsPropsMenuItem.Header = localizationService["Filter_MethodsAndProperties"];
        FilterContainersOnlyMenuItem.Header = localizationService["Filter_ContainersOnly"];
        FilterMethodsMenuItem.Header = localizationService["Filter_ShowMethods"];
        FilterPropertiesMenuItem.Header = localizationService["Filter_ShowProperties"];
        FilterFieldsMenuItem.Header = localizationService["Filter_ShowFields"];
        FilterConstantsMenuItem.Header = localizationService["Filter_ShowConstants"];
        FilterTypesMenuItem.Header = localizationService["Filter_ShowTypesEnums"];
        GraphLayoutMenuItem.Header = localizationService["Menu_GraphLayout"];
        LayoutStandardMenuItem.Header = localizationService["Layout_StandardRadial"];
        LayoutAffinityMenuItem.Header = localizationService["Layout_AffinityRadial"];
        LayoutIslandsMenuItem.Header = localizationService["Layout_ClusteredIslands"];
        LayoutForceMenuItem.Header = localizationService["Layout_ForceDirected"];
        ToggleHoverSelectionMenuItem.Header = localizationService["Menu_HoverSelection"];
        ToggleTabLabelsMenuItem.Header = localizationService["Menu_ToggleTabLabels"];
        VisibleTabsMenuItem.Header = localizationService["Menu_VisibleTabs"];
        ToggleTabGraphMenuItem.Header = localizationService["Tab_GraphView"];
        ToggleTabDsmMenuItem.Header = localizationService["Tab_DsmMatrix"];
        ToggleTabTreemapMenuItem.Header = localizationService["Tab_TreemapMetrics"];
        ToggleTabHotspotMenuItem.Header = localizationService["Tab_RiskHotspots"];
        ToggleTabScreenFlowMenuItem.Header = localizationService["Tab_ScreenFlow"];
        ToggleTabControlFlowMenuItem.Header = localizationService["Tab_ControlFlow"];
        ToggleTabMartinMenuItem.Header = localizationService["Tab_MartinMetrics"];
        ToggleTabDeadCodeMenuItem.Header = localizationService["Tab_DeadCode"];
        ToggleTabSharedStateMenuItem.Header = localizationService["Tab_SharedState"];
        ToggleTabTemporalCouplingMenuItem.Header = localizationService["Tab_TemporalCoupling"];
        ToggleTabArchitecturalFlowMenuItem.Header = localizationService["Tab_ArchitecturalFlow"];
        ToggleTabCodeCityMenuItem.Header = localizationService["Tab_CodeCity"];
        ToggleTabCohesionMenuItem.Header = localizationService["Tab_Cohesion"];
        ToggleTabMaintainabilityMenuItem.Header = localizationService["Tab_Maintainability"];
        ToggleTabArchitecturalRulesMenuItem.Header = localizationService["Tab_ArchitecturalRules"];
        ToggleTabCodeCloneMenuItem.Header = localizationService["Tab_CodeClone"];
        ToggleTabSocialCodeMenuItem.Header = localizationService["Tab_SocialCode"];
        ToggleTabDataFlowMenuItem.Header = localizationService["Tab_DataFlow"];
        ToggleTabApiDiffMenuItem.Header = localizationService["Tab_ApiDiff"];
        ToggleTabCrudMatrixMenuItem.Header = localizationService["Tab_CrudMatrix"];
        ToggleTabBlastRadiusGridMenuItem.Header = localizationService["Tab_BlastRadiusGrid"];

        // Menu Analisi
        AnalysisMenu.Header = localizationService["Menu_Analysis"];
        BlastRadiusMenuItem.Header = localizationService["Menu_BlastRadius"];
        ShowCallersMenuItem.Header = localizationService["Menu_ShowCallers"];
        ShowCalleesMenuItem.Header = localizationService["Menu_ShowCallees"];
        ClearHighlightMenuItem.Header = localizationService["Menu_ClearHighlight"];
        RunAllAnalyticsMenuItem.Header = localizationService["Menu_RunAllAnalytics"];
        ToggleBackgroundAnalyticsMenuItem.Header = localizationService["Menu_BackgroundAnalytics"];

        // Menu Aiuto
        HelpMenu.Header = localizationService["Menu_Help"];
        InfoMenuItem.Header = localizationService["Menu_HelpInfo"];
        CheckUpdatesMenuItem.Header = localizationService["Menu_CheckUpdates"];
        ResetSettingsMenuItem.Header = localizationService["Menu_ResetSettings"];
        ToolTip.SetTip(ResetSettingsMenuItem, localizationService["Tooltip_ResetSettings"]);
        ClearCacheMenuItem.Header = localizationService["Menu_ClearCache"];
        ToolTip.SetTip(ClearCacheMenuItem, localizationService["Tooltip_ClearCache"]);

        // Caption Buttons Tooltips
        ToolTip.SetTip(ToggleExplorerTopButton, localizationService["Tooltip_ToggleExplorer"]);
        ToolTip.SetTip(ToggleInspectorTopButton, localizationService["Tooltip_ToggleInspector"]);
        ToolTip.SetTip(SettingsButton, localizationService["Tooltip_Settings"]);
        ToolTip.SetTip(MinimizeButton, localizationService["Tooltip_Minimize"]);
        ToolTip.SetTip(MaximizeButton, localizationService["Tooltip_Maximize"]);
        ToolTip.SetTip(CloseButton, localizationService["Tooltip_Close"]);

        // Floating HUD Tooltips
        ToolTip.SetTip(ResetViewButton, localizationService["Tooltip_ResetView"]);
        ToolTip.SetTip(ExpandAllButton, localizationService["Tooltip_ExpandAll"]);
        ToolTip.SetTip(CollapseAllButton, localizationService["Tooltip_CollapseAll"]);
        ToolTip.SetTip(RefreshProjectButton, localizationService["Tooltip_RefreshProject"]);
        ToolTip.SetTip(BlastRadiusButton, localizationService["Tooltip_BlastRadius"]);
        ToolTip.SetTip(ShowCallersButton, localizationService["Tooltip_ShowCallers"]);
        ToolTip.SetTip(ShowCalleesButton, localizationService["Tooltip_ShowCallees"]);
        ToolTip.SetTip(ClearHighlightButton, localizationService["Tooltip_ClearHighlight"]);
        ToolTip.SetTip(ExportMarkdownButton, localizationService["Tooltip_ExportMarkdown"]);
        ToolTip.SetTip(ExportJsonButton, localizationService["Tooltip_ExportJson"]);
        ToolTip.SetTip(ImportJsonButton, localizationService["Tooltip_ImportJson"]);

        // Ricerca
        SearchBox.Watermark = localizationService["Search_Watermark"];

        // Context Menus & Dynamic Explorer
        CtxNodeExportMarkdown.Header = localizationService["Ctx_Node_ExportMarkdown"];
        CtxNodeExportJson.Header = localizationService["Ctx_Node_ExportJson"];
        CtxNodeBlastRadius.Header = localizationService["Ctx_Node_BlastRadius"];
        CtxNodeShowCallers.Header = localizationService["Ctx_Node_ShowCallers"];
        CtxNodeShowCallees.Header = localizationService["Ctx_Node_ShowCallees"];
        if (CtxTabHide != null) CtxTabHide.Header = localizationService["TabContext_HideTab"];
        if (CtxTabShowAll != null) CtxTabShowAll.Header = localizationService["TabContext_ShowAllTabs"];
        if (CtxTabManage != null) CtxTabManage.Header = localizationService["TabContext_ManageTabs"];
        if (CtxRightTabHide != null) CtxRightTabHide.Header = localizationService["TabContext_HideTab"];
        if (CtxRightTabShowAll != null) CtxRightTabShowAll.Header = localizationService["TabContext_ShowAllTabs"];
        if (CtxRightTabManage != null) CtxRightTabManage.Header = localizationService["TabContext_ManageTabs"];
        if (ToggleTabInspectorMenuItem != null) ToggleTabInspectorMenuItem.Header = localizationService["Tab_Inspector"];
        if (ToggleTabSourceCodeMenuItem != null) ToggleTabSourceCodeMenuItem.Header = localizationService["Tab_SourceCode"];
        if (_graph is not null)

            ProjectExplorerControl.ItemsSource = new[] { ExplorerTreeItem.BuildTree(_graph, _currentFolderPath, Graph.HiddenProjectIds) };

        // Tab Centrali
        GraphTabHeader.Text = localizationService["Tab_GraphView"];
        DsmTabHeader.Text = localizationService["Tab_DsmMatrix"];
        TreemapTabHeader.Text = localizationService["Tab_TreemapMetrics"];
        HotspotTabHeader.Text = localizationService["Tab_RiskHotspots"];
        ScreenFlowTabHeader.Text = localizationService["Tab_ScreenFlow"];
        ControlFlowTabHeader.Text = localizationService["Tab_ControlFlow"];
        MartinTabHeader.Text = localizationService["Tab_MartinMetrics"];
        DeadCodeTabHeader.Text = localizationService["Tab_DeadCode"];
        SharedStateTabHeader.Text = localizationService["Tab_SharedState"];
        TemporalCouplingTabHeader.Text = localizationService["Tab_TemporalCoupling"];
        ArchitecturalFlowTabHeader.Text = localizationService["Tab_ArchitecturalFlow"];
        CodeCityTabHeader.Text = localizationService["Tab_CodeCity"];
        CohesionTabHeader.Text = localizationService["Tab_Cohesion"];
        MaintainabilityTabHeader.Text = localizationService["Tab_Maintainability"];
        ArchitecturalRulesTabHeader.Text = localizationService["Tab_ArchitecturalRules"];
        CodeCloneTabHeader.Text = localizationService["Tab_CodeClone"];
        SocialCodeTabHeader.Text = localizationService["Tab_SocialCode"];
        DataFlowTabHeader.Text = localizationService["Tab_DataFlow"];
        ApiDiffTabHeader.Text = localizationService["Tab_ApiDiff"];
        CrudMatrixTabHeader.Text = localizationService["Tab_CrudMatrix"];
        GridTabHeader.Text = localizationService["Tab_BlastRadiusGrid"];

        ToolTip.SetTip(GraphTabItem, localizationService["Tab_GraphView_Tip"]);
        ToolTip.SetTip(DsmTabItem, localizationService["Tab_DsmMatrix_Tip"]);
        ToolTip.SetTip(TreemapTabItem, localizationService["Tab_TreemapMetrics_Tip"]);
        ToolTip.SetTip(HotspotTabItem, localizationService["Tab_RiskHotspots_Tip"]);
        ToolTip.SetTip(ScreenFlowTabItem, localizationService["Tab_ScreenFlow_Tip"]);
        ToolTip.SetTip(ControlFlowTabItem, localizationService["Tab_ControlFlow_Tip"]);
        ToolTip.SetTip(MartinTabItem, localizationService["Tab_MartinMetrics_Tip"]);
        ToolTip.SetTip(DeadCodeTabItem, localizationService["Tab_DeadCode_Tip"]);
        ToolTip.SetTip(SharedStateTabItem, localizationService["Tab_SharedState_Tip"]);
        ToolTip.SetTip(TemporalCouplingTabItem, localizationService["Tab_TemporalCoupling_Tip"]);
        ToolTip.SetTip(ArchitecturalFlowTabItem, localizationService["Tab_ArchitecturalFlow_Tip"]);
        ToolTip.SetTip(CodeCityTabItem, localizationService["Tab_CodeCity_Tip"]);
        ToolTip.SetTip(CohesionTabItem, localizationService["Tab_Cohesion_Tip"]);
        ToolTip.SetTip(MaintainabilityTabItem, localizationService["Tab_Maintainability_Tip"]);
        ToolTip.SetTip(ArchitecturalRulesTabItem, localizationService["Tab_ArchitecturalRules_Tip"]);
        ToolTip.SetTip(CodeCloneTabItem, localizationService["Tab_CodeClone_Tip"]);
        ToolTip.SetTip(SocialCodeTabItem, localizationService["Tab_SocialCode_Tip"]);
        ToolTip.SetTip(DataFlowTabItem, localizationService["Tab_DataFlow_Tip"]);
        ToolTip.SetTip(ApiDiffTabItem, localizationService["Tab_ApiDiff_Tip"]);
        ToolTip.SetTip(CrudMatrixTabItem, localizationService["Tab_CrudMatrix_Tip"]);
        ToolTip.SetTip(GridTabItem, localizationService["Tab_BlastRadiusGrid_Tip"]);

        // Tab Close Buttons Tooltip (X)
        void UpdateTabCloseButtonsTips(TabControl? tc)
        {
            if (tc == null) return;
            foreach (var tab in tc.Items.OfType<TabItem>())
            {
                if (tab.Header is StackPanel sp)
                {
                    var closeBtn = sp.Children.OfType<Button>().FirstOrDefault(b => b.Classes.Contains("tab-close-btn"));
                    if (closeBtn != null)
                        ToolTip.SetTip(closeBtn, localizationService["Tab_CloseTip"]);
                }
            }
        }
        UpdateTabCloseButtonsTips(CentralTabControl);
        UpdateTabCloseButtonsTips(RightTabControl);

        Dsm.InvalidateVisual();
        Treemap.InvalidateVisual();
        Hotspot.InvalidateVisual();
        ScreenFlow.InvalidateVisual();
        ControlFlow.InvalidateVisual();
        MartinMetrics.InvalidateVisual();
        ArchitecturalFlow.InvalidateVisual();
        CodeCity.InvalidateVisual();

        // Floating HUD
        HudLayoutLabel.Text = localizationService["HUD_LayoutLabel"];
        LayoutItemStandard.Content = localizationService["Layout_StandardRadial"];
        LayoutItemAffinity.Content = localizationService["Layout_AffinityRadial"];
        LayoutItemIslands.Content = localizationService["Layout_ClusteredIslands"];
        LayoutItemForce.Content = localizationService["Layout_ForceDirected"];
        HudFilterLabel.Text = localizationService["HUD_FilterLabel"];
        HudFilterButtonText.Text = localizationService["HUD_FilterButton"];
        DetailItemAll.Content = localizationService["Filter_AllMembers"];
        DetailItemMethodsProps.Content = localizationService["Filter_MethodsAndProperties"];
        DetailItemContainers.Content = localizationService["Filter_ContainersOnly"];
        DetailItemCustom.Content = localizationService["Filter_Custom"];
        FilterMethodsCheckBox.Content = localizationService["Filter_ShowMethods"];
        FilterPropertiesCheckBox.Content = localizationService["Filter_ShowProperties"];
        FilterFieldsCheckBox.Content = localizationService["Filter_ShowFields"];
        FilterConstantsCheckBox.Content = localizationService["Filter_ShowConstants"];
        FilterTypesCheckBox.Content = localizationService["Filter_ShowTypesEnums"];
        ToolTip.SetTip(FilterOptionsButton, localizationService["Tooltip_FilterMethods"]);
        ToolTip.SetTip(LegendMethodButton, localizationService["Tooltip_LegendToggle"]);
        ToolTip.SetTip(LegendPropertyButton, localizationService["Tooltip_LegendToggle"]);
        ToolTip.SetTip(LegendFieldButton, localizationService["Tooltip_LegendToggle"]);
        ToolTip.SetTip(LegendTypeButton, localizationService["Tooltip_LegendToggle"]);

        // Graph legend
        LegendTitleText.Text = localizationService["Legend_Title"];
        LegendFormText.Text = localizationService["Legend_Forms"];
        LegendModuleText.Text = localizationService["Legend_Modules"];
        LegendClassText.Text = localizationService["Legend_Classes"];
        LegendControlText.Text = localizationService["Legend_Controls"];
        LegendMethodText.Text = localizationService["Legend_Methods"];
        LegendPropertyText.Text = localizationService["Legend_Properties"];
        LegendFieldText.Text = localizationService["Legend_Fields"];
        LegendTypeText.Text = localizationService["Legend_Types"];
        LegendCallsText.Text = localizationService["Legend_Calls"];
        LegendInstantiatesText.Text = localizationService["Legend_Instantiates"];
        HudResetViewText.Text = localizationService["HUD_ResetView"];
        HudExpandAllText.Text = localizationService["HUD_ExpandAll"];
        HudCollapseAllText.Text = localizationService["HUD_CollapseAll"];
        HudRefreshText.Text = localizationService["HUD_Refresh"];
        HudBlastRadiusText.Text = localizationService["HUD_BlastRadius"];
        HudShowCallersText.Text = localizationService["HUD_ShowCallers"];
        HudShowCalleesText.Text = localizationService["HUD_ShowCallees"];
        HudClearHighlightText.Text = localizationService["HUD_ClearFilter"];
        HudExportMarkdownText.Text = localizationService["HUD_ExportMarkdown"];
        HudExportJsonText.Text = localizationService["HUD_ExportJson"];
        HudImportJsonText.Text = localizationService["HUD_ImportJson"];

        // VCS (Git & SVN) Popup & Tooltip
        if (!string.IsNullOrEmpty(_currentFolderPath))
        {
            var currentBranch = VcsService.Instance.GetCurrentBranch(_currentFolderPath, out var vcsType);
            if (vcsType != VcsType.None && !string.IsNullOrEmpty(currentBranch))
            {
                var tooltipKey = vcsType == VcsType.Svn ? "Svn_BranchTooltip" : "Git_BranchTooltip";
                ToolTip.SetTip(GitBranchButton, localizationService.Get(tooltipKey, currentBranch));
                GitBranchPopupTitle.Text = vcsType == VcsType.Svn ? localizationService["Svn_SwitchBranchTitle"] : localizationService["Git_SwitchBranchTitle"];
                GitBranchSearchBox.Watermark = vcsType == VcsType.Svn ? localizationService["Svn_SearchBranchWatermark"] : localizationService["Git_SearchBranchWatermark"];
            }
        }

        // Right panel tabs and sub-views localizations
        TabHeaderInspectorText.Text = localizationService["Tab_Inspector"];
        TabHeaderSourceCodeText.Text = localizationService["Tab_SourceCode"];
        ProjectExplorerControl?.UpdateLocalizedUI();
        SymbolInspectorControl?.UpdateLocalizedUI();
        SourceCodePreviewControl?.UpdateLocalizedUI();
        BlastRadiusGridControl?.UpdateLocalizedUI();
        LoadingOverlayControl?.UpdateLocalizedUI();
        UpdateModalControl?.UpdateLocalizedUI();
        ExportSuccessModalControl?.UpdateLocalizedUI();
        LayoutConfirmModalControl?.UpdateLocalizedUI();

        // Placeholders localizations
        DsmPlaceholder?.UpdateLocalizedUI();
        TreemapPlaceholder?.UpdateLocalizedUI();
        HotspotPlaceholder?.UpdateLocalizedUI();
        ScreenFlowPlaceholder?.UpdateLocalizedUI();
        MartinPlaceholder?.UpdateLocalizedUI();
        DeadCodePlaceholder?.UpdateLocalizedUI();
        SharedStatePlaceholder?.UpdateLocalizedUI();
        TemporalCouplingPlaceholder?.UpdateLocalizedUI();
        ArchitecturalFlowPlaceholder?.UpdateLocalizedUI();
        CodeCityPlaceholder?.UpdateLocalizedUI();
        CohesionPlaceholder?.UpdateLocalizedUI();
        MaintainabilityPlaceholder?.UpdateLocalizedUI();
        ArchitecturalRulesPlaceholder?.UpdateLocalizedUI();
        CodeClonePlaceholder?.UpdateLocalizedUI();
        SocialCodePlaceholder?.UpdateLocalizedUI();
        DataFlowPlaceholder?.UpdateLocalizedUI();
        ApiDiffPlaceholder?.UpdateLocalizedUI();
        CrudMatrixPlaceholder?.UpdateLocalizedUI();
        BlastRadiusGridPlaceholder?.UpdateLocalizedUI();

        // Refresh dynamic strings in Grid and Status
        UpdateStatsBar();
        ToolTip.SetTip(BackgroundAnalyticsStopButton, localizationService["Tooltip_StopBackgroundAnalytics"]);
        if (_selectedNode == null)

            StatusText.Text = localizationService["Status_Ready"];

        else
        {
            StatusText.Text = localizationService.Get("Status_Selected", _selectedNode.Kind, _selectedNode.QualifiedName);
            if (_selectedNode.SemanticSymbol is { } sym && _graph?.Queries is { } q && q.TryGetCachedImpactReport(sym, 10, out var cachedReport) && cachedReport != null)

                SymbolInspectorControl?.UpdateImpactDetails(cachedReport, q.GetDirectCallees(sym), q.GetInstantiatedClasses(sym));
        }
    }

    #endregion
}

/// <summary>
/// Compact implementation of ICommand for performing delegated actions from menus and UI controls.
/// </summary>
public sealed class ActionCommand : System.Windows.Input.ICommand
{
    private readonly Action _action;
    public ActionCommand(Action action) => _action = action;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _action();
}
