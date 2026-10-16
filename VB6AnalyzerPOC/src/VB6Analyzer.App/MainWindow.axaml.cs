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
/// Finestra principale dell'applicazione VB6 Analyzer.
/// Gestisce l'interazione tra Toolbar, Project Explorer (TreeView),
/// Canvas del Grafo (<see cref="Controls.GraphControl"/>), Symbol Inspector e Blast Radius Engine.
/// </summary>
public partial class MainWindow : Window
{
    #region Campi e Stato

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
    // --- Gestione Drag & Drop e Ordinamento Schede di Analisi (CentralTabControl) ---
    /// <summary>Elenco ordinato immutabile dei TabItem nell'ordine predefinito originario definito nel file XAML.</summary>
    private readonly List<TabItem> _defaultCentralTabOrder = new();

    /// <summary>Riferimento al TabItem attualmente afferrato e trascinato dall'utente.</summary>
    private TabItem? _draggedTabItem;

    /// <summary>Coordinate relative alla finestra al momento del PointerPressed iniziale.</summary>
    private Point _tabDragStartPos;

    /// <summary>Offset relativo tra il punto di clic del mouse e l'angolo superiore sinistro dell'intestazione del tab trascinato.</summary>
    private Point _tabDragOffsetWithinTab;

    /// <summary>Flag booleano che indica se è attualmente in corso un'operazione di trascinamento attivo (superata la deadzone).</summary>
    private bool _isTabDragging;

    /// <summary>Indice di destinazione calcolato nel CentralTabControl dove inserire la scheda al rilascio del mouse.</summary>
    private int _targetDropIndex = -1;

    /// <summary>Trasformazione di traslazione applicata alla scheda fluttuante ghost per seguirne il movimento sotto al cursore.</summary>
    private readonly TranslateTransform _tabGhostTransform = new();

    /// <summary>Trasformazione di traslazione applicata all'indicatore verticale di drop per posizionarlo dinamicamente tra i tab.</summary>
    private readonly TranslateTransform _tabDropIndicatorTransform = new();

    #endregion

    #region Costruttore & Inizializzazione

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

        // Canvas del Grafo: menu contestuale su clic destro sopra i nodi e tracking movimenti
        Graph.AddHandler(Control.ContextRequestedEvent, OnGraphContextRequested, RoutingStrategies.Tunnel);
        Graph.NodeMoved += (_, _) => OnGraphNodeMoved();

        // Schede di Analisi e Pannello Destro: menu contestuale su clic destro per nascondere o gestire i tab
        CentralTabControl.AddHandler(Control.ContextRequestedEvent, OnCentralTabControlContextRequested, RoutingStrategies.Tunnel);
        RightTabControl.AddHandler(Control.ContextRequestedEvent, OnRightTabControlContextRequested, RoutingStrategies.Tunnel);

        // Click sui pulsanti di chiusura schede e Drag & Drop per riordinare le schede di analisi con il mouse.
        // NOTA CRITICA: Gli handler registrati con RoutingStrategies.Tunnel su CentralTabControl intercettano TUTTI
        // gli eventi puntatore dell'intera area centrale (inclusi i canvas di analisi, i pulsanti dell'HUD, controlli e form).
        // Per evitare regressioni in cui i pulsanti interni (es. Blast Radius, Esporta MD) perdono il click,
        // gli handler sottostanti (OnCentralTabPointerPressed/Released) NON devono mai azzerare la cattura del puntatore
        // né gestire gli eventi se il clic non proviene specificamente dall'header di un TabItem o se non è in corso un drag.
        RightTabControl.AddHandler(InputElement.PointerPressedEvent, OnRightTabPointerPressed, RoutingStrategies.Tunnel);
        CentralTabControl.AddHandler(InputElement.PointerPressedEvent, OnCentralTabPointerPressed, RoutingStrategies.Tunnel);
        CentralTabControl.AddHandler(InputElement.PointerMovedEvent, OnCentralTabPointerMoved, RoutingStrategies.Tunnel);
        CentralTabControl.AddHandler(InputElement.PointerReleasedEvent, OnCentralTabPointerReleased, RoutingStrategies.Tunnel);
        CentralTabControl.AddHandler(InputElement.PointerCaptureLostEvent, OnCentralTabPointerCaptureLost, RoutingStrategies.Tunnel);

        // Memorizza l'ordine predefinito originale e applica l'ordinamento personalizzato
        _defaultCentralTabOrder.AddRange(CentralTabControl.Items.OfType<TabItem>());
        ApplySavedTabOrder();

        // Menu Bar Superiore - File (Command + Click per massima affidabilità)
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

        // Drag & Drop di file e cartelle direttamente sull'intera finestra
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

        // Pulsante Mostra/Nascondi Contenuto Legenda Grafo
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

        // Controlli Finestra Stile IDE (Impostazioni, Riduci a icona, Ingrandisci/Ripristina, Chiudi)
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

        // Scorciatoie da tastiera globali (Ctrl + F, Ctrl + O, Ctrl + ,, F5)
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

        // Sincronizzazione selezione tra Tab Grafo e Tab Griglia Blast Radius / Analytics
        CentralTabControl.SelectionChanged += OnCentralTabSelectionChanged;

        // Eventi di selezione e sincronizzazione per le nuove visualizzazioni analitiche
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

        // Eventi di gestione Baseline per il tab API Diff & SemVer
        ApiDiff.SetCurrentAsBaselineRequested += (_, _) => OnApiDiffSetCurrentAsBaseline();
        ApiDiff.LoadBaselineRequested += async (_, _) => await OnApiDiffLoadBaselineAsync();
        ApiDiff.ClearBaselineRequested += (_, _) => OnApiDiffClearBaseline();

        // Inizializzazione Impostazioni e Preferenze Utente (%APPDATA%\Macondo\settings.json)
        var settings = SettingsService.Instance.Settings;

        // Ripristino Geometria Finestra e Larghezza Pannelli
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

        // Inizializzazione Filtri per Livello e Categoria di Simbolo
        InitFilterControls();

        // Salvataggio Geometria Finestra, Larghezza e Visibilità Pannelli alla chiusura
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

            // Salvataggio sincrono immediato di eventuali modifiche pendenti al layout dei nodi e dello snapshot di cache
            FlushPendingLayoutChangesSync();
            if (!string.IsNullOrEmpty(_currentFolderPath) && _graph != null)

                ProjectCacheService.Instance.SaveSnapshot(_currentFolderPath, _graph, BlastRadiusGridControl.AllRows, _hasUserMovedNodes, _enabledAnalyticsTabs);
        };

        // Servizi Reattivi: Localizzazione & Temi con Auto-Save
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

        // Inizializza le dimensioni persistite dei pannelli laterali (Explorer e Inspector)
        if (settings.ExplorerWidth >= 160)
            _savedExplorerWidth = settings.ExplorerWidth;

        if (settings.InspectorWidth >= 200)
            _savedInspectorWidth = settings.InspectorWidth;

        // Traccia in tempo reale il ridimensionamento manuale del pannello destro tramite GridSplitter
        RightPanelContainer.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && _isInspectorVisible && RightPanelContainer.Bounds.Width >= 200)
            {
                _savedInspectorWidth = RightPanelContainer.Bounds.Width;
                SettingsService.Instance.Settings.InspectorWidth = _savedInspectorWidth;
            }
        };

        // Traccia in tempo reale il ridimensionamento manuale del pannello sinistro tramite GridSplitter
        ProjectExplorerControl.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && _isExplorerVisible && ProjectExplorerControl.Bounds.Width >= 160)
            {
                _savedExplorerWidth = ProjectExplorerControl.Bounds.Width;
                SettingsService.Instance.Settings.ExplorerWidth = _savedExplorerWidth;
            }
        };

        // Applica i valori salvati prima del rendering iniziale
        ThemeService.Instance.ApplyTheme(settings.Theme);
        LocalizationService.Instance.SetLanguage(settings.Language);
        Graph.EnableHoverSelection = settings.EnableHoverSelection;
        HoverSelectionCheckIcon.IsVisible = settings.EnableHoverSelection;
        ApplyTabLabelsVisibility(settings.ShowTabLabels);
        ApplySavedTabOrder();
        ApplyTabsVisibility(settings);
        UpdateLocalizedUI();

        // Connessione esplicita eventi modali e overlay
        LoadingOverlayControl.CancelRequested += OnLoadingCancelRequested;
        UpdateModalControl.UpdateNowRequested += OnUpdateModalNowRequested;
        UpdateModalControl.UpdateLaterRequested += OnUpdateModalLaterRequested;
        ExportSuccessModalControl.OpenFolderRequested += OnExportSuccessOpenFolderRequested;
        LayoutConfirmModalControl.Confirmed += OnLayoutConfirmModalConfirmed;

        // ------------------------------------------------------------------------------------------------
        // Caricamento del Progetto all'Avvio: Gestione Intelligente in base a StartupMode e Cronologia Recenti
        // ------------------------------------------------------------------------------------------------
        var startupMode = settings.StartupMode;

        // Recupera il target più recente prioritariamente dal primo elemento della cronologia RecentProjects.
        // Se la cronologia è vuota, ricade sulla proprietà LastOpenedFolder memorizzata nelle impostazioni.
        string? lastTarget = null;
        if (settings.RecentProjects.Count > 0)

            lastTarget = settings.RecentProjects[0].Paths.FirstOrDefault();
        if (string.IsNullOrEmpty(lastTarget))

            lastTarget = settings.LastOpenedFolder;

        // Determina la cartella effettiva del progetto (directory diretta oppure directory padre se il target è un file .csproj/.vbp/.sln)
        var targetFolder = !string.IsNullOrEmpty(lastTarget)
            ? (Directory.Exists(lastTarget) ? lastTarget : (File.Exists(lastTarget) ? Path.GetDirectoryName(lastTarget) : null))
            : null;
        var hasValidTarget = !string.IsNullOrEmpty(lastTarget) && (Directory.Exists(lastTarget) || File.Exists(lastTarget));

        if (startupMode == StartupLoadMode.OnlyIfCached && hasValidTarget && !string.IsNullOrEmpty(targetFolder) && Directory.Exists(targetFolder))
        {
            var isCached = ProjectCacheService.Instance.HasCache(targetFolder);
            if (isCached)
            {
                // Caricamento istantaneo del progetto e del grafo direttamente dallo snapshot in cache
                var targetPaths = settings.RecentProjects.Count > 0 ? settings.RecentProjects[0].Paths : new List<string> { lastTarget! };
                _ = AnalyzeAndLoadPathsAsync(targetPaths);
            }
            else
            {
                // Nessuna cache disponibile: predispone la schermata di benvenuto mostrando la card del progetto in sospeso per l'analisi
                _currentFolderPath = targetFolder;
                _currentPaths = new List<string> { lastTarget! };
                UpdateVcsBranchStatus(targetFolder);
                ShowWelcomeScreen(pendingFolder: lastTarget);
                StatusText.Text = LocalizationService.Instance.Get("Status_NoCachePending", Path.GetFileName(lastTarget!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            }
        }
        else if (startupMode == StartupLoadMode.AlwaysAnalyze && hasValidTarget)
        {
            // Modalità sempre analizza: avvia l'analisi completa all'avvio indipendentemente dalla cache
            var targetPaths = settings.RecentProjects.Count > 0 ? settings.RecentProjects[0].Paths : new List<string> { lastTarget! };
            _ = AnalyzeAndLoadPathsAsync(targetPaths);
        }
        else
        {
            // Modalità EmptyWorkspace o nessun percorso valido precedente: mostra la schermata iniziale di benvenuto
            ShowWelcomeScreen();
            StatusText.Text = LocalizationService.Instance["Status_StartupEmptyReady"];
        }

        // Controllo automatico aggiornamenti in background all'avvio
        _ = CheckForUpdatesOnStartupAsync();
    }

    #endregion

    #region Gestione Finestra & Barra del Titolo

    /// <summary>
    /// Consente il trascinamento della finestra dall'area centrale libera della barra del titolo.
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)

            BeginMoveDrag(e);
    }

    /// <summary>
    /// Doppio click sull'area centrale libera della barra del titolo per massimizzare o ripristinare.
    /// </summary>
    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximize();
    }

    /// <summary>
    /// Alterna lo stato della finestra principale tra massimizzato (<see cref="WindowState.Maximized"/>) e normale (<see cref="WindowState.Normal"/>).
    /// </summary>
    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    #endregion

    #region Overlay di Caricamento & Progresso

    /// <summary>
    /// Mostra l'overlay modale di caricamento centrato a tutto schermo con opzioni per la barra di avanzamento e statistiche.
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
    /// Aggiorna l'avanzamento dettagliato e lo stato testuale dell'overlay di caricamento.
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
    /// Gestisce il click sul pulsante Annulla all'interno del popup di caricamento / analisi.
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
    /// Nasconde l'overlay di caricamento e reimposta lo stato della barra di avanzamento.
    /// </summary>
    private void HideLoading()
    {
        LoadingOverlayControl.Hide();
        _loadingCts?.Dispose();
        _loadingCts = null;
    }

    #endregion

    #region Caricamento Dati, Workspace & Fixture

    /// <summary>
    /// Genera e carica un grafo sintetico di ~500 nodi tramite FakeGraphGenerator per benchmark e test.
    /// </summary>
    private async void LoadSyntheticGraph()
    {
        var localizationService = LocalizationService.Instance;
        ShowLoading(
            "Generazione del grafo sintetico...",
            "Creazione di ~500 nodi e calcolo del layout radiale...",
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
    /// Carica e analizza la suite di fixture VB6 reali integrate nel progetto con aggiornamento del progresso in tempo reale.
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

            // Lampeggio icona sulla barra di Windows e notifica di sistema
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
    /// Gestione Drag & Drop: Accetta file o cartelle trascinate sulla finestra.
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
    /// Gestione Drag & Drop: Riceve i file/cartelle rilasciati ed avvia l'analisi immediata.
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
    /// Apre un selettore di cartelle per analizzare uno o più progetti VB6 contemporaneamente.
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
    /// Apre un selettore di file per aprire direttamente uno o più file progetto (.vbp) o gruppi (.vbg).
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
    /// Ricarica forzatamente il progetto o i progetti correnti da disco, ri-eseguendo parsing e analisi.
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
    /// Esegue o ripristina da cache l'analisi di una cartella.
    /// </summary>
    private async Task AnalyzeAndLoadFolderAsync(string folderPath, bool forceReanalyze = false)
    {
        await AnalyzeAndLoadPathsAsync(new[] { folderPath }, forceReanalyze);
    }

    /// <summary>
    /// Esegue o ripristina l'analisi di uno o più percorsi (cartelle, .vbp, .vbg) creando un workspace integrato.
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

        // Mostra subito l'overlay di caricamento per dare feedback immediato all'utente
        ShowLoading(localizationService["Loading_AnalyzeProjectTitle"], localizationService.Get("Loading_AnalyzeProjectSub", titleName), canCancel: true);
        var token = _loadingCts?.Token ?? CancellationToken.None;

        try
        {
            await Task.Delay(16, token);

            // Risolve la directory effettiva di riferimento sia per singola cartella che per file di progetto (.vbp, .csproj, .sln)
            var targetFolder = Directory.Exists(firstPath)
                ? firstPath
                : (File.Exists(firstPath) ? Path.GetDirectoryName(firstPath) : null);

            // 1. Tenta il ripristino ultra-rapido dalla cache solo se non forzato e se esiste uno snapshot valido su disco
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

                    // Registra l'apertura nella cronologia recenti e aggiorna LastOpenedFolder
                    AddToRecentProjects(paths);

                    // Rileva eventuali modifiche ai sorgenti intervenute dopo il salvataggio della cache
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

            // 2. Analisi completa da sorgenti su disco con barra di avanzamento determinata
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

            // Costruzione dell'albero dell'esploratore su thread in background per evitare blocchi dell'UI Dispatcher su grafi estesi (40k+ nodi)
            ExplorerTreeItem? precalculatedExplorerTree = null;
            if (_graph != null)
            {
                precalculatedExplorerTree = await Task.Run(
                    () => ExplorerTreeItem.BuildTree(_graph, _currentFolderPath, Graph.HiddenProjectIds),
                    token);
            }

            // Chiude il modal di caricamento prima di applicare gli elementi alla UI
            HideLoading();
            await Task.Yield();

            // Il layout è già stato calcolato in background durante FromSemanticGraph in ProjectAnalysisService
            ApplyGraphToUI(precalculatedTree: precalculatedExplorerTree, applyAutomaticLayout: false); // Avvierà PopulateBlastRadiusGridAsync che poi aggiornerà la cache con i calcoli

            // Salva immediatamente lo snapshot iniziale del grafo su cache su thread in background
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

            // Registra l'apertura nella cronologia recenti e aggiorna LastOpenedFolder
            AddToRecentProjects(paths);

            // Lampeggio icona sulla barra di Windows e notifica di sistema
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
    /// Aggiunge un progetto, file o cartella alla lista dei progetti recenti, aggiorna la cartella dell'ultimo progetto aperto nelle impostazioni e ricostruisce i menu e la schermata di benvenuto.
    /// </summary>
    /// <param name="paths">Elenco dei percorsi aperti (cartella, file di progetto o file multipli).</param>
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
            // Salva atomicamente l'ultima cartella valida per l'avvio e la card di analisi
            if (!string.IsNullOrEmpty(targetFolder) && Directory.Exists(targetFolder))

                settings.LastOpenedFolder = targetFolder;

            // Rimuovi se già presente per evitare duplicati
            settings.RecentProjects.RemoveAll(r => r.Paths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase));

            // Inserisci in cima alla cronologia
            settings.RecentProjects.Insert(0, new RecentProjectItem
            {
                Paths = paths.ToList(),
                DisplayName = displayName,
                IsFolder = isFolder,
                LastOpenedUtc = DateTime.UtcNow
            });

            // Tieni solo gli ultimi 10 elementi
            if (settings.RecentProjects.Count > 10)

                settings.RecentProjects = settings.RecentProjects.Take(10).ToList();
        });

        UpdateRecentProjectsMenu();
        WelcomeControl.Refresh();
    }

    /// <summary>
    /// Ricostruisce dinamicamente il sottomenu "Recenti" nel menu File con gli ultimi 10 elementi.
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
    /// Aggiorna tutti i componenti della UI (Grafo, Explorer, Status) con il nuovo CodeGraph.
    /// Se precalculatedRows è fornito (es. da cache), la griglia Blast Radius viene popolata istantaneamente
    /// e le coordinate personalizzate salvate non vengono sovrascritte se applyAutomaticLayout è false.
    /// Se precalculatedTree è fornito, evita di ricostruire l'albero dell'esploratore sul thread UI.
    /// </summary>
    private void ApplyGraphToUI(List<BlastRadiusGridRow>? precalculatedRows = null, bool applyAutomaticLayout = true, ExplorerTreeItem? precalculatedTree = null)
    {
        if (_graph is null) return;

        // Mostra i Tab centrali di analisi e nasconde la Welcome View
        WelcomeControl.IsVisible = false;
        CentralTabControl.IsVisible = true;

        if (applyAutomaticLayout)
            Layout.GraphLayoutManager.ApplyLayout(_graph, _currentLayoutMode);
        _hasUserMovedNodes = false;
        Graph.ClearHiddenProjects();

        // Se sono presenti più progetti, pre-seleziona solo il primo progetto (nascondendo tutti gli altri)
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
    /// Espande o comprime tutti i nodi container (classi, moduli, form) presenti nel grafo visuale.
    /// </summary>
    /// <param name="expanded"><c>true</c> per espandere tutti i container; <c>false</c> per comprimerli.</param>
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

    #region Filtri Simboli, Livelli di Dettaglio & Progetti Multipli

    /// <summary>
    /// Inizializza i controlli di filtro dei simboli, registrando i gestori di eventi per checkbox, menu e pulsanti della legenda.
    /// Ripristina inoltre lo stato dei filtri salvato nelle impostazioni utente.
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
    /// Applica un preset di livello di dettaglio predefinito (0 = Completo, 1 = Metodi &amp; Proprietà, 2 = Solo Moduli).
    /// </summary>
    /// <param name="index">Indice del preset selezionato nel ComboBox o MenuItem.</param>
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
    /// Gestisce il cambio di selezione nel ComboBox del livello di dettaglio dei simboli.
    /// </summary>
    private void OnDetailLevelComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingFilterUi) return;
        SetDetailPreset(DetailLevelComboBox.SelectedIndex);
    }

    /// <summary>
    /// Gestisce la variazione dello stato di spunta di uno dei CheckBox dei filtri di tipo simbolo.
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
    /// Inverte la visibilità nel grafo per la specifica categoria di simboli (<see cref="SymbolKind"/>).
    /// </summary>
    /// <param name="kind">Il tipo di simbolo da alternare (Method, Property, Field, Constant, Type/Enum).</param>
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
    /// Salva le impostazioni correnti di filtro dei simboli nella configurazione persistente dell'applicazione.
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
    /// Sincronizza lo stato visivo di tutti i controlli UI di filtro (checkbox, opacità elementi legenda, icone di spunta menu, combo preset)
    /// con lo stato effettivo delle proprietà di filtro del <see cref="Controls.GraphControl"/>.
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

            // Aggiorna indicatori di spunta nelle voci del menu Visualizza
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

    // Insieme dei Tab analitici i cui dataset necessitano di ricalcolo lazy o in background
    private readonly HashSet<object> _dirtyAnalyticsTabs = new();
    private CancellationTokenSource? _analyticsCts;

    // Insieme delle schede di analisi avviate o abilitate dall'utente per il workspace corrente
    private readonly HashSet<string> _enabledAnalyticsTabs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Restituisce la chiave identificativa univoca associata a ciascuna scheda di analisi.
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
    /// Marca una scheda di analisi come abilitata per il progetto corrente e aggiorna la cache su disco e le preferenze globali.
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
    /// Marca tutte le 19 schede di analisi come abilitate per il progetto corrente e aggiorna la cache su disco e le preferenze globali.
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
    /// Restituisce il grafo semantico target tenendo conto dei soli progetti attualmente visibili nel workspace.
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
    /// Marca tutte le viste analitiche secondarie come non aggiornate (dirty) e avvia
    /// la pre-elaborazione in background per tutte le schede se abilitata globalmente,
    /// oppure solo per le schede precedentemente abilitate/avviate per il progetto corrente.
    /// </summary>
    private void InvalidateAnalyticsViews()
    {
        // Cancella eventuali elaborazioni in background ancora in corso per il workspace precedente
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
            // Se l'utente si trova già su un tab analitico secondario abilitato, calcolalo subito
            EnsureActiveTabAnalyticsLoaded();

            // Avvia la pipeline di pre-calcolo in background per i tab abilitati
            StartBackgroundAnalyticsPrecomputation(_analyticsCts.Token);
        }
        else
        {
            if (BackgroundAnalyticsPanel != null)
                BackgroundAnalyticsPanel.IsVisible = false;
        }
    }

    /// <summary>
    /// Imposta lo stato iniziale sui placeholder delle schede analitiche in base alle impostazioni
    /// globali e alle schede precedentemente abilitate/avviate per il progetto corrente.
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
    /// Pipeline in background per pre-calcolare i dataset dei tab analitici abilitati senza bloccare l'interfaccia,
    /// aggiornando in tempo reale la barra di avanzamento visibile nella barra di stato in basso a destra.
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

        // Inizializza e mostra la barra di avanzamento nella status bar
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

                    // Al completamento di tutti i tab, visualizza brevemente lo stato completato e poi nasconde la barra
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

            // 3. Hotspots di Rischio
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

            // 7. Stato Globale Condiviso
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

            // 10. Temporal Coupling (Analisi Cronologia Git su Disco)
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

            // 11. Coesione & LCOM
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

            // 14. Duplicazioni e Cloni (CPD)
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
    /// Inizializza gli ascoltatori degli eventi per tutti i placeholder delle schede analitiche.
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
    /// Avvia la pre-elaborazione per tutte le schede analitiche attualmente pendenti.
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
    /// Gestisce il click sul pulsante di arresto delle analisi in background nella barra di stato inferiore.
    /// </summary>
    private void OnBackgroundAnalyticsStopButtonClicked(object? sender, RoutedEventArgs e)
    {
        StopBackgroundAnalytics();
    }

    /// <summary>
    /// Interrompe l'elaborazione delle analisi in background e reimposta i placeholder delle schede non ancora completate in attesa.
    /// </summary>
    private void StopBackgroundAnalytics()
    {
        _analyticsCts?.Cancel();
        if (BackgroundAnalyticsPanel != null)

            BackgroundAnalyticsPanel.IsVisible = false;

        // Reimposta lo stato dei placeholder per tutte le schede ancora dirty (non calcolate)
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
    /// Esegue il calcolo asincrono on-demand (Lazy) per il tab analitico correntemente selezionato
    /// qualora sia contrassegnato come dirty e l'elaborazione in background sia abilitata.
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
    /// Esegue il calcolo analitico on-demand per il singolo tab specificato.
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
    /// Calcola la durata stimata in secondi per l'analisi di una specifica scheda in base al numero di nodi del workspace.
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
    /// Restituisce il titolo dinamico localizzato da visualizzare durante il calcolo dell'analisi per la scheda specificata.
    /// </summary>
    private string GetComputingTabTitle(TabItem tabItem)
    {
        var title = GetTabTitle(tabItem);
        if (!string.IsNullOrWhiteSpace(title))

            return LocalizationService.Instance.Get("Analytics_ComputingNamedTitle", title);
        return LocalizationService.Instance["Analytics_ComputingTitle"];
    }

    /// <summary>
    /// Popola dinamicamente i selettori di visibilità dei singoli progetti (Project Explorer e Menu Visualizza)
    /// quando sono presenti progetti multipli nel workspace corrente.
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

        // Popola le voci del menu a discesa Visualizza -> Filtra Progetti
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
    /// Aggiorna l'icona di spunta (checkmark) per una voce di menu (<see cref="MenuItem"/>) in base al valore booleano specificato.
    /// </summary>
    /// <param name="mi">L'elemento di menu da aggiornare.</param>
    /// <param name="isChecked"><c>true</c> per mostrare l'icona di spunta; <c>false</c> per rimuoverla.</param>
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
    /// Aggiorna il testo informativo del badge dei progetti (es. "Progetti: 2/3 visibili") e abilita/disabilita il pulsante "Tutti".
    /// </summary>
    private void UpdateProjectsBadgeText()
    {
        var (vis, total) = Graph.GetProjectCounts();
        ProjectExplorerControl.UpdateProjectFilterBar(total > 1, LocalizationService.Instance.Get("Explorer_ProjectsVisible", vis, total));
    }

    /// <summary>
    /// Gestisce la richiesta di selezione di tutti i progetti inviata dal Project Explorer.
    /// </summary>
    private void OnExplorerSelectAllProjectsRequested(object? sender, EventArgs e)
    {
        OnProjectsSelectAllClicked(sender, new RoutedEventArgs());
    }

    /// <summary>
    /// Gestisce la variazione di visibilità di un progetto proveniente dalla casella di controllo nel Project Explorer.
    /// </summary>
    private void OnExplorerProjectVisibilityChanged(object? sender, ExplorerTreeItem item)
    {
        if (item.ProjectId.HasValue)

            Graph.SetProjectVisibility(item.ProjectId.Value, item.IsProjectVisible);
    }

    /// <summary>
    /// Invocato quando la visibilità di un progetto cambia nel grafo.
    /// Sincronizza lo stato degli elementi nel Project Explorer, le voci di menu, le statistiche e invalida le viste analitiche.
    /// </summary>
    /// <param name="sender">La sorgente dell'evento.</param>
    /// <param name="projectId">L'identificatore univoco del progetto la cui visibilità è cambiata.</param>
    private void OnGraphProjectVisibilityChanged(object? sender, Guid projectId)
    {
        // Sincronizza lo stato dei nodi nell'ExplorerTree
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

        // Sincronizza le icone di spunta nei MenuItem
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
    /// Gestisce il click sul pulsante "Tutti" nella barra progetti dell'Explorer, ripristinando la visibilità di tutti i progetti nel workspace.
    /// </summary>
    private void OnProjectsSelectAllClicked(object? sender, RoutedEventArgs e)
    {
        Graph.ClearHiddenProjects();
    }

    #endregion

    #region Azioni Grafo & Analisi Impatto

    /// <summary>
    /// Calcola ed evidenzia il Blast Radius per il nodo selezionato in modo asincrono con loading overlay.
    /// Se il risultato è già in cache, viene applicato all'istante senza mostrare l'overlay.
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

        // Se già calcolato e presente in cache, applica all'istante (0 ms)
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
    /// Evidenzia sul grafo tutte le routine e classi chiamate dalla routine/container selezionato (callees).
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
    /// Evidenzia sul grafo tutti i chiamanti della routine/container selezionato (callers).
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
    /// Gestisce l'evento di cambio selezione di un nodo nel controllo del grafo (<see cref="Controls.GraphControl"/>).
    /// Aggiorna il nodo selezionato corrente, sincronizza la riga nella griglia Blast Radius e avvia l'ispezione nel Symbol Inspector.
    /// </summary>
    /// <param name="sender">La sorgente dell'evento.</param>
    /// <param name="node">Il nodo selezionato nel grafo, oppure <c>null</c> se la selezione è stata azzerata.</param>
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
    /// Seleziona un nodo, espande il container padre se necessario, centra la vista del grafo su di esso
    /// e sincronizza la vista ScreenFlow se il nodo corrisponde a un form.
    /// </summary>
    /// <param name="node">Il nodo del grafo da selezionare e mettere a fuoco.</param>
    private void SelectAndFocusGraphNode(GraphNode node)
    {
        if (_graph is null) return;

        // Se è un membro interno, assicurati che il container padre sia espanso
        if (node.ParentId.HasValue)
        {
            var parent = _graph.GetNode(node.ParentId.Value);
            if (parent is not null)
                parent.IsExpanded = true;
        }

        Graph.FocusOnNode(node.Id);
        Graph.SelectNode(node);

        // Seleziona e inquadra la schermata se presente nel grafo ScreenFlow
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

    #region Project Explorer & Interazione File e Cartelle

    /// <summary>
    /// Gestisce la variazione di selezione nella vista ad albero del Project Explorer, selezionando e centrando il nodo corrispondente nel grafo.
    /// </summary>
    private void OnExplorerSelectionChanged(object? sender, ExplorerTreeItem? item)
    {
        if (item is null || _graph is null) return;
        var node = _graph.GetNode(item.NodeId);
        if (node is not null)
            SelectAndFocusGraphNode(node);
    }

    /// <summary>
    /// Gestisce il doppio click su un elemento dell'albero Project Explorer.
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
    /// Gestisce la richiesta di apertura del file originale dall'Explorer.
    /// </summary>
    private void OnExplorerOpenFileRequested(object? sender, ExplorerTreeItem item)
    {
        if (!item.IsGroup && item.HasFile)

            OpenOriginalFile(item.FilePath, item.LineStart);
    }

    /// <summary>
    /// Gestisce la richiesta di apertura della cartella contenitrice dall'Explorer.
    /// </summary>
    private void OnExplorerOpenFolderRequested(object? sender, ExplorerTreeItem item)
    {
        if (!item.IsGroup && item.HasFile)

            OpenContainingFolder(item.FilePath);
    }

    /// <summary>
    /// Gestisce la richiesta di copia percorso file negli appunti dall'Explorer.
    /// </summary>
    private void OnExplorerCopyPathRequested(object? sender, ExplorerTreeItem item)
    {
        if (!item.IsGroup && item.HasFile)

            CopyFilePathToClipboard(item.FilePath);
    }

    /// <summary>
    /// Gestisce la richiesta di apertura file originale dall'Inspector.
    /// </summary>
    private void OnInspectorOpenFileRequested(object? sender, EventArgs e)
    {
        var filePath = _selectedNode?.SemanticSymbol?.FilePath;
        var line = _selectedNode?.SemanticSymbol?.LineStart ?? 0;
        OpenOriginalFile(filePath, line);
    }

    /// <summary>
    /// Gestisce la richiesta di apertura cartella contenitrice dall'Inspector.
    /// </summary>
    private void OnInspectorOpenFolderRequested(object? sender, EventArgs e)
    {
        var filePath = _selectedNode?.SemanticSymbol?.FilePath;
        OpenContainingFolder(filePath);
    }

    /// <summary>
    /// Gestisce la richiesta di copia del Context Slice negli appunti dall'Inspector.
    /// </summary>
    private async void OnInspectorCopySliceRequested(object? sender, EventArgs e)
    {
        await CopyLlmSliceToClipboardAsync();
    }

    /// <summary>
    /// Gestisce la richiesta di esportazione del Context Slice in formato Markdown dall'Inspector.
    /// </summary>
    private async void OnInspectorSaveSliceRequested(object? sender, EventArgs e)
    {
        await ExportLlmSliceAsync();
    }

    /// <summary>
    /// Gestisce la notifica di copia del frammento di codice sorgente dall'Inspector.
    /// </summary>
    private void OnInspectorCopyCodeRequested(object? sender, string code)
    {
        if (_selectedNode != null)

            StatusText.Text = LocalizationService.Instance.Get("Status_CopiedCodeSnippet", _selectedNode.Name);
    }

    #endregion

    #region Menu Contestuale Nodi del Grafo

    /// <summary>
    /// Gestisce la richiesta di apertura del menu contestuale sul canvas del grafo (<see cref="Controls.GraphControl"/>).
    /// Seleziona il nodo sottostante al cursore e mostra le relative azioni di analisi ed esportazione.
    /// </summary>
    private void OnGraphContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var hitNode = e.TryGetPosition(Graph, out var screenPos)
            ? Graph.GetNodeAtScreenPos(screenPos)
            : Graph.SelectedNode ?? _selectedNode;

        // Se nessun nodo è presente sotto il cursore del mouse, non aprire il menu contestuale
        if (hitNode == null)
        {
            e.Handled = true;
            return;
        }

        // Seleziona esplicitamente il nodo su cui è stato fatto clic destro
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
    /// Gestisce il comando "Esporta Context Slice LLM (Markdown)" dal menu contestuale del nodo del grafo.
    /// </summary>
    private async void OnNodeExportMarkdownClicked(object? sender, RoutedEventArgs e) => await ExportLlmSliceAsync();

    /// <summary>
    /// Gestisce il comando "Esporta Dati Nodo (JSON)" dal menu contestuale del nodo del grafo.
    /// </summary>
    private async void OnNodeExportJsonClicked(object? sender, RoutedEventArgs e) => await ExportNodeJsonAsync();

    /// <summary>
    /// Esporta i dettagli del nodo e delle sue relazioni inbound/outbound in formato JSON (.json).
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
    /// Apre il file sorgente specificato utilizzando l'applicazione o l'IDE predefinito del sistema operativo.
    /// </summary>
    /// <param name="filePath">Il percorso relativo o assoluto del file sorgente da aprire.</param>
    /// <param name="line">La riga iniziale a cui posizionarsi (opzionale).</param>
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
    /// Apre la cartella contenitrice del percorso specificato in Esplora File (Windows Explorer).
    /// Se <paramref name="filePath"/> è un file, il file viene pre-selezionato automaticamente.
    /// </summary>
    /// <param name="filePath">Il percorso del file o della cartella da rivelare nel file manager.</param>
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
    /// Copia il percorso assoluto risolto del file specificato negli Appunti di sistema.
    /// </summary>
    /// <param name="filePath">Il percorso relativo o assoluto del file da copiare.</param>
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
    /// Risolve un percorso file relativo trasformandolo nel percorso assoluto su disco, utilizzando la cartella di progetto corrente come base.
    /// </summary>
    /// <param name="filePath">Il percorso relativo o assoluto da risolvere.</param>
    /// <returns>Il percorso assoluto normalizzato, oppure <c>null</c> se non risolvibile.</returns>
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
    /// Costruisce e restituisce l'elenco completo delle destinazioni di ricerca applicative (Impostazioni, Viste analitiche, Guida, Azioni rapide)
    /// localizzate nella lingua corrente per la Command Palette globale.
    /// </summary>
    /// <returns>La lista degli <see cref="AppSearchTarget"/> registrati.</returns>
    private List<AppSearchTarget> GetAppSearchTargets()
    {
        var localizationService = LocalizationService.Instance;
        var isEn = localizationService.CurrentLanguage == AppLanguage.English;

        return new List<AppSearchTarget>
        {
            // --- SCHERMATE DI IMPOSTAZIONI ---
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

            // --- GUIDA & INFORMAZIONI ---
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

            // --- AZIONI E STRUMENTI RAPIDI ---
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
    /// Gestisce la variazione del testo digitato nella casella di ricerca globale (SearchBox).
    /// Esegue la ricerca in tempo reale sia sui comandi/viste applicative sia sui simboli del grafo di codice.
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

        // 1. Cerca tra le schermate e comandi dell'applicazione (Impostazioni, Viste, Guida, Azioni)
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

        // 2. Cerca tra i simboli del grafo di codice (se presente un progetto caricato)
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

            // Evidenzia le schermate corrispondenti nel controllo ScreenFlow
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
    /// Esegue l'azione associata a un risultato di ricerca (navigazione al simbolo nel grafo o esecuzione del comando/schermata).
    /// </summary>
    /// <param name="item">L'elemento di ricerca selezionato.</param>
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
    /// Gestisce l'evento di selezione di un elemento dall'elenco a comparsa dei risultati di ricerca.
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
    /// Gestisce i tasti speciali (Invio, Freccia Giù, Esc) premuti nella casella di testo della ricerca globale.
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
    /// Gestisce i tasti speciali (Invio per eseguire, Esc per chiudere) quando il focus si trova nell'elenco dei risultati di ricerca.
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
    /// Mostra il pannello informativo segnaposto dell'Inspector quando nessun nodo o simbolo è selezionato nel grafo.
    /// </summary>
    private void ShowEmptyInspector()
    {
        SymbolInspectorControl.ShowEmpty(_graph != null);
        SourceCodePreviewControl.ShowEmpty(_graph != null);
        UpdateStatusLanguage();
    }

    /// <summary>
    /// Ispeziona asincronamente il nodo selezionato, aggiornando sia i metadati statici immediati (nome, tipo, parametri, file)
    /// sia i calcoli semantici pesanti in background (CFG, Blast Radius, chiamanti diretti e classi istanziate).
    /// </summary>
    /// <param name="node">Il nodo del grafo da ispezionare.</param>
    private async void SelectAndInspectNodeAsync(GraphNode node)
    {
        // Annulla ogni analisi precedente ancora in corso
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        _selectionCts = new CancellationTokenSource();
        var ct = _selectionCts.Token;

        // 1. Mostra istantaneamente i metadati statici del simbolo e l'anteprima del codice sorgente
        SymbolInspectorControl.ShowSymbolMetadata(node);
        SourceCodePreviewControl.ShowSourceCode(node);

        var sym = node.SemanticSymbol;

        // Costruisci il Control Flow Graph (CFG) se è una routine
        if (sym != null && _graph?.SemanticGraph != null && (sym.Kind == SymbolKind.Method || sym.Kind == SymbolKind.Property))

            ControlFlow.Graph = ControlFlowBuilder.BuildForSymbol(sym, _graph.SemanticGraph);

        UpdateStatusLanguage(node.Language);

        // Se non abbiamo simboli o query, svuota i pannelli dinamici
        if (_graph?.Queries is null || sym is null)
        {
            SymbolInspectorControl.ClearDynamicSections();
            return;
        }

        // Se il simbolo è già presente in cache, aggiorna immediatamente a 0ms (nessun overlay, nessun task ritardato)
        if (_graph.Queries.TryGetCachedImpactReport(sym, 10, out var cachedImpact) && cachedImpact != null)
        {
            var cachedCallees = _graph.Queries.GetDirectCallees(sym);
            var cachedInstantiations = _graph.Queries.GetInstantiatedClasses(sym);
            SymbolInspectorControl.UpdateImpactDetails(cachedImpact, cachedCallees, cachedInstantiations);
            return;
        }

        // 2. Imposta lo stato di caricamento nell'Inspector per Blast Radius e chiamanti
        var localizationService = LocalizationService.Instance;
        SymbolInspectorControl.SetCalculatingState();

        var targetId = node.Id;
        bool isCompleted = false;

        // Se il calcolo richiede più di 80ms (tipico per nodi complessi), mostra il loading overlay centrato
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
            // 3. Esegue i calcoli pesanti su un thread in background
            var impact = await Task.Run(() => _graph.Queries.GetImpactReport(sym, cancellationToken: ct), ct);
            var callees = await Task.Run(() => _graph.Queries.GetDirectCallees(sym), ct);
            var instantiations = await Task.Run(() => _graph.Queries.GetInstantiatedClasses(sym), ct);

            if (ct.IsCancellationRequested || _selectedNode?.Id != targetId)
                return;

            // 4. Popola l'Inspector con i risultati completi
            SymbolInspectorControl.UpdateImpactDetails(impact, callees, instantiations);
        }
        catch (OperationCanceledException)
        {
            // Analisi annullata da una nuova selezione: operazione normale
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

    #region Griglia Blast Radius, Navigazione Tab & Statistiche

    /// <summary>
    /// Cambia ciclicamente la scheda di visualizzazione centrale tra quelle attualmente visibili e abilitate.
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
    /// Gestisce la variazione della scheda selezionata nel TabControl centrale.
    /// Avvia il caricamento analitico Lazy per la vista attivata e sincronizza la selezione con la griglia Blast Radius.
    /// </summary>
    private void OnCentralTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source != CentralTabControl) return;

        // Calcola e carica i dati del tab solo quando l'utente vi accede per la prima volta (Lazy Analytics)
        EnsureActiveTabAnalyticsLoaded();

        if (CentralTabControl.SelectedItem == GridTabItem)
        {
            if (_selectedNode != null)
                BlastRadiusGridControl.SelectNode(_selectedNode.Id);
        }
    }

    /// <summary>
    /// Calcola in background l'impatto semantico e il Blast Radius per tutte le routine del workspace tramite <see cref="BlastRadiusGridView"/>.
    /// </summary>
    private async Task PopulateBlastRadiusGridAsync()
    {
        await BlastRadiusGridControl.PopulateBlastRadiusGridAsync(_graph, _currentFolderPath, _hasUserMovedNodes);
        _dirtyAnalyticsTabs.Remove(GridTabItem);
        BlastRadiusGridPlaceholder?.SetState(isPending: false, isComputing: false);
    }

    /// <summary>
    /// Gestisce la selezione di una riga all'interno della griglia Blast Radius, selezionando e ispezionando il relativo nodo nel grafo.
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
    /// Gestisce la richiesta di navigazione verso un nodo del grafo proveniente dalla griglia Blast Radius.
    /// </summary>
    private void OnGridControlNavigateToNodeRequested(object? sender, Guid nodeId)
    {
        NavigateToNodeInGraph(nodeId);
    }

    /// <summary>
    /// Gestisce la richiesta di ricalcolo dell'audit completo dalla griglia Blast Radius.
    /// </summary>
    private async void OnGridControlRecalculateAuditRequested(object? sender, EventArgs e)
    {
        _dirtyAnalyticsTabs.Add(GridTabItem);
        BlastRadiusGridPlaceholder?.SetState(isPending: false, isComputing: true, GetEstimatedAnalysisSeconds(GridTabItem, _graph?.Nodes.Count ?? 0), GetComputingTabTitle(GridTabItem));
        await PopulateBlastRadiusGridAsync();
    }

    /// <summary>
    /// Passa alla scheda del Grafo, espande il container genitore, centra la vista e calcola graficamente l'impatto Blast Radius per il nodo specificato.
    /// </summary>
    /// <param name="nodeId">L'identificatore univoco del nodo a cui navigare.</param>
    private void NavigateToNodeInGraph(Guid nodeId)
    {
        if (_graph == null) return;
        var node = _graph.GetNode(nodeId);
        if (node == null) return;

        // 1. Passa alla scheda Vista Grafo
        CentralTabControl.SelectedItem = GraphTabItem;

        // 2. Seleziona, espande il container genitore se necessario e centra la visuale sul nodo
        SelectAndFocusGraphNode(node);

        // 3. Calcola ed evidenzia il Blast Radius grafico
        OnBlastRadiusClicked();
    }

    /// <summary>
    /// Calcola e aggiorna le metriche generali del workspace (conteggio progetti, nodi, relazioni, chiamate, istanziazioni) visualizzate nella barra di stato inferiore.
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
    /// Aggiorna il badge del linguaggio sorgente (VB6, C#, ecc.) nella barra di stato in basso a destra,
    /// basandosi sul nodo correntemente selezionato o sui linguaggi complessivi rilevati nel workspace.
    /// </summary>
    /// <param name="selectedNodeLang">Il linguaggio del nodo selezionato, o <c>null</c> per usare i linguaggi complessivi del grafo.</param>
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

        // Altrimenti mostra i linguaggi del grafo/workspace complessivo
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

    #region Impostazioni, Informazioni & Temi

    /// <summary>
    /// Apre la finestra di dialogo modale delle Impostazioni (<see cref="SettingsWindow"/>) posizionandosi sulla scheda specificata.
    /// </summary>
    /// <param name="initialTab">L'indice della scheda iniziale da selezionare (0 = Aspetto, 1 = Lingua, 2 = Comportamento, 3 = Scorciatoie).</param>
    private void OpenSettings(int initialTab = 0)
    {
        var settingsWin = new SettingsWindow(initialTab);
        settingsWin.ShowDialog(this);
    }

    /// <summary>
    /// Apre la finestra di dialogo modale della Guida e Informazioni (<see cref="InfoWindow"/>) posizionandosi sulla scheda specificata.
    /// </summary>
    /// <param name="initialTab">L'indice della scheda iniziale da selezionare.</param>
    private void OpenInfo(int initialTab = 0)
    {
        var infoWin = new Views.InfoWindow(initialTab);
        infoWin.ShowDialog(this);
    }

    /// <summary>
    /// Applica i pennelli e colori della definizione del tema a tutti i bordi della finestra principale e ai controlli di visualizzazione analitica.
    /// </summary>
    /// <param name="theme">La definizione del tema contenente i codici esadecimali dei colori.</param>
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

    #region Esportazione & Importazione Dati

    /// <summary>
    /// Esporta l'intero grafo in formato Markdown compatto (Repo Map) per LLM tramite selettore di file.
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
    /// Esporta l'intero grafo in formato JSON strutturato tramite selettore di file.
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
    /// Importa un CodeGraph da file JSON precedentemente salvato e lo visualizza istantaneamente.
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

            // Calcola ed esporta istantaneamente la griglia di Blast Radius per il grafo importato
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
    /// Copia negli appunti di sistema il Context Slice semantico per il simbolo selezionato.
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
    /// Salva su file Markdown (.md) il Context Slice per il simbolo/nodo selezionato.
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
    /// Mostra il popup modale di conferma per l'esportazione completata con successo.
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

    #region Gestione Layout Grafo & Interazione Hover

    private bool _hasUserMovedNodes;
    private Layout.GraphLayoutMode? _pendingLayoutMode;

    /// <summary>
    /// Invocato quando l'utente sposta/trascina manualmente un nodo nel canvas del grafo con il mouse.
    /// </summary>
    private void OnGraphNodeMoved()
    {
        _hasUserMovedNodes = true;
        ScheduleLayoutAutoSave();
    }

    /// <summary>
    /// Richiede il cambio di layout del grafo. Se l'utente ha spostato almeno un nodo manualmente,
    /// mostra prima una modale di conferma per avvertire che le posizioni dei nodi spostati andranno perse.
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
    /// Mostra l'overlay modale di avviso prima di riorganizzare automaticamente il layout del grafo
    /// se l'utente ha spostato manualmente dei nodi.
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
    /// Imposta la modalità di layout attiva, ricalcola le coordinate di tutti i nodi e aggiorna il canvas del grafo.
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
    /// Pianifica il salvataggio asincrono debounced delle coordinate dei nodi in cache (1.5s di inattività).
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
    /// Gestore scatenato allo scadere del timer di debounce per persistere su disco le posizioni aggiornate dei nodi.
    /// </summary>
    private void OnLayoutSaveDebounceTick(object? sender, EventArgs e)
    {
        _layoutSaveDebounceTimer?.Stop();
        FlushPendingLayoutChanges();
    }

    /// <summary>
    /// Esegue il salvataggio asincrono in background delle modifiche geometriche al layout su disco.
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
    /// Salva immediatamente e in modo sincrono le coordinate geometriche dei nodi su disco (es. alla chiusura o cambio progetto).
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
    /// Ripristina la visualizzazione del grafo impostando lo zoom al 100% e centrando la vista
    /// sul primo sottoprogetto visibile/selezionato (oppure al centro del canvas se non presente).
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
    /// Restituisce la stringa descrittiva localizzata corrispondente all'algoritmo di layout specificato.
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
    /// Gestisce il cambio di selezione nel ComboBox del layout per richiedere l'applicazione del nuovo algoritmo.
    /// </summary>
    private void OnLayoutComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LayoutComboBox.SelectedIndex < 0) return;
        var mode = (Layout.GraphLayoutMode)LayoutComboBox.SelectedIndex;
        if (mode != _currentLayoutMode)

            RequestSetLayoutMode(mode, resetView: true);
    }

    /// <summary>
    /// Attiva o disattiva la selezione e ispezione dei nodi al passaggio del cursore del mouse (Hover).
    /// </summary>
    private void OnToggleHoverSelectionClicked(object? sender, RoutedEventArgs e)
    {
        var newValue = !Graph.EnableHoverSelection;
        Graph.EnableHoverSelection = newValue;
        HoverSelectionCheckIcon.IsVisible = newValue;
        SettingsService.Instance.Update(s => s.EnableHoverSelection = newValue);
    }

    /// <summary>
    /// Attiva o disattiva la visualizzazione delle etichette testuali nelle schede di visualizzazione centrali.
    /// </summary>
    private void OnToggleTabLabelsClicked(object? sender, RoutedEventArgs e)
    {
        ToggleTabLabels();
    }

    /// <summary>
    /// Alterna lo stato di visibilità delle etichette dei tab centrali e aggiorna le impostazioni utente.
    /// </summary>
    private void ToggleTabLabels()
    {
        var newValue = !SettingsService.Instance.Settings.ShowTabLabels;
        SettingsService.Instance.Update(s => s.ShowTabLabels = newValue);
        ApplyTabLabelsVisibility(newValue);
    }

    /// <summary>
    /// Applica la visibilità visiva alle intestazioni testuali di tutte le schede del controllo a schede centrale.
    /// </summary>
    /// <param name="showLabels"><c>true</c> per mostrare le etichette con il testo; <c>false</c> per mostrare solo le icone.</param>
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
    /// Applica la visibilità configurata dall'utente per ciascuna scheda di analisi nella barra centrale
    /// e aggiorna contestualmente i segni di spunta nel menu Visualizza.
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

        // Se la scheda attualmente attiva viene nascosta, ripiega in sicurezza sulla prima scheda visibile
        if (CentralTabControl?.SelectedItem is TabItem currentTab && !currentTab.IsVisible)
        {
            var firstVisible = CentralTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.IsVisible);
            if (firstVisible != null)

                CentralTabControl.SelectedItem = firstVisible;
        }

        // Se la scheda del pannello destro attualmente attiva viene nascosta, ripiega in sicurezza sulla prima visibile
        if (RightTabControl?.SelectedItem is TabItem currentRightTab && !currentRightTab.IsVisible)
        {
            var firstVisibleRight = RightTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.IsVisible);
            if (firstVisibleRight != null)

                RightTabControl.SelectedItem = firstVisibleRight;
        }

        // Se entrambe le schede del pannello destro sono nascoste, chiudi e comprimi il pannello solo se attualmente visibile
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

    /// <summary>Alterna l'esecuzione automatica delle analisi in background nelle impostazioni.</summary>
    private void OnToggleBackgroundAnalyticsMenuClicked(object? sender, RoutedEventArgs e)
    {
        var newValue = !SettingsService.Instance.Settings.EnableBackgroundAnalytics;
        SettingsService.Instance.Update(s => s.EnableBackgroundAnalytics = newValue);
        if (newValue && _graph != null && _dirtyAnalyticsTabs.Count > 0)

            StartAllPendingAnalytics();
    }

    /// <summary>Avvia il calcolo di tutte le schede analitiche in sospeso dal menu Analisi.</summary>
    private void OnRunAllAnalyticsMenuClicked(object? sender, RoutedEventArgs e)
    {
        StartAllPendingAnalytics();
    }

    /// <summary>Alterna la visibilità del tab Grafo delle Dipendenze nelle impostazioni.</summary>
    private void OnToggleTabGraphMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabGraph = !s.ShowTabGraph);

    /// <summary>Alterna la visibilità del tab Matrice DSM nelle impostazioni.</summary>
    private void OnToggleTabDsmMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabDsm = !s.ShowTabDsm);

    /// <summary>Alterna la visibilità del tab Treemap Gerarchica nelle impostazioni.</summary>
    private void OnToggleTabTreemapMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabTreemap = !s.ShowTabTreemap);

    /// <summary>Alterna la visibilità del tab Matrice Hotspot nelle impostazioni.</summary>
    private void OnToggleTabHotspotMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabHotspot = !s.ShowTabHotspot);

    /// <summary>Alterna la visibilità del tab Flusso Schermate nelle impostazioni.</summary>
    private void OnToggleTabScreenFlowMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabScreenFlow = !s.ShowTabScreenFlow);

    /// <summary>Alterna la visibilità del tab Control Flow Graph (CFG) nelle impostazioni.</summary>
    private void OnToggleTabControlFlowMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabControlFlow = !s.ShowTabControlFlow);

    /// <summary>Alterna la visibilità del tab Metriche Martin nelle impostazioni.</summary>
    private void OnToggleTabMartinMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabMartin = !s.ShowTabMartin);

    /// <summary>Alterna la visibilità del tab Dead Code &amp; Orfani nelle impostazioni.</summary>
    private void OnToggleTabDeadCodeMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabDeadCode = !s.ShowTabDeadCode);

    /// <summary>Alterna la visibilità del tab Stato Globale Condiviso nelle impostazioni.</summary>
    private void OnToggleTabSharedStateMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabSharedState = !s.ShowTabSharedState);

    /// <summary>Alterna la visibilità del tab Temporal Coupling (Git Co-change) nelle impostazioni.</summary>
    private void OnToggleTabTemporalCouplingMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabTemporalCoupling = !s.ShowTabTemporalCoupling);

    /// <summary>Alterna la visibilità del tab Flussi Architetturali (Chord) nelle impostazioni.</summary>
    private void OnToggleTabArchitecturalFlowMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabArchitecturalFlow = !s.ShowTabArchitecturalFlow);

    /// <summary>Alterna la visibilità del tab CodeCity 3D nelle impostazioni.</summary>
    private void OnToggleTabCodeCityMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabCodeCity = !s.ShowTabCodeCity);

    /// <summary>Alterna la visibilità del tab Coesione &amp; LCOM nelle impostazioni.</summary>
    private void OnToggleTabCohesionMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabCohesion = !s.ShowTabCohesion);

    /// <summary>Alterna la visibilità del tab Maintainability Index nelle impostazioni.</summary>
    private void OnToggleTabMaintainabilityMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabMaintainability = !s.ShowTabMaintainability);

    /// <summary>Alterna la visibilità del tab Regole Architetturali nelle impostazioni.</summary>
    private void OnToggleTabArchitecturalRulesMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabArchitecturalRules = !s.ShowTabArchitecturalRules);

    /// <summary>Alterna la visibilità del tab Cloni e Duplicazioni (CPD) nelle impostazioni.</summary>
    private void OnToggleTabCodeCloneMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabCodeClone = !s.ShowTabCodeClone);

    /// <summary>Alterna la visibilità del tab Social Code &amp; Bus Factor nelle impostazioni.</summary>
    private void OnToggleTabSocialCodeMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabSocialCode = !s.ShowTabSocialCode);

    /// <summary>Alterna la visibilità del tab Data Flow &amp; Taint Tracking nelle impostazioni.</summary>
    private void OnToggleTabDataFlowMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabDataFlow = !s.ShowTabDataFlow);

    /// <summary>Alterna la visibilità del tab API Diff &amp; SemVer nelle impostazioni.</summary>
    private void OnToggleTabApiDiffMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabApiDiff = !s.ShowTabApiDiff);

    /// <summary>Alterna la visibilità del tab Matrice CRUD nelle impostazioni.</summary>
    private void OnToggleTabCrudMatrixMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabCrudMatrix = !s.ShowTabCrudMatrix);

    /// <summary>Alterna la visibilità del tab Griglia Blast Radius nelle impostazioni.</summary>
    private void OnToggleTabBlastRadiusGridMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabBlastRadiusGrid = !s.ShowTabBlastRadiusGrid);

    /// <summary>Alterna la visibilità del tab Symbol Inspector nelle impostazioni.</summary>
    private void OnToggleTabInspectorMenuClicked(object? sender, RoutedEventArgs e)
    {
        var newValue = !SettingsService.Instance.Settings.ShowTabInspector;
        SettingsService.Instance.Update(s =>
        {
            s.ShowTabInspector = newValue;
            if (newValue) s.IsInspectorVisible = true;
        });
    }

    /// <summary>Alterna la visibilità del tab Anteprima Codice Sorgente nelle impostazioni.</summary>
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

    #region Menu Contestuale Schede di Analisi (TabControl)

    private TabItem? _contextMenuTargetTabItem;

    /// <summary>
    /// Gestisce la richiesta di apertura del menu contestuale sulle intestazioni delle schede di analisi.
    /// Mostra le opzioni per nascondere la scheda specifica, mostrare tutte le schede o aprire la personalizzazione.
    /// </summary>
    private void OnCentralTabControlContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var visual = e.Source as Visual;
        var tabItem = FindTabItemFromHeaderVisual(visual, CentralTabControl);

        // Se il clic non è avvenuto su un'intestazione TabItem appartenente al TabControl principale, ignora
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
    /// Gestisce la richiesta di apertura del menu contestuale sulle intestazioni delle schede del pannello destro (Inspector / Codice Sorgente).
    /// Mostra le opzioni per nascondere la scheda specifica, mostrare tutte le schede o aprire la personalizzazione.
    /// </summary>
    private void OnRightTabControlContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var visual = e.Source as Visual;
        var tabItem = FindTabItemFromHeaderVisual(visual, RightTabControl);

        // Se il clic non è avvenuto su un'intestazione TabItem appartenente al TabControl destro, ignora
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
    /// Recupera il testo dell'intestazione del TabItem specificato per la formattazione dinamica del menu.
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
    /// Nasconde la scheda analitica specifica salvando la preferenza nella configurazione utente.
    /// </summary>
    private void OnTabContextMenuHideClicked(object? sender, RoutedEventArgs e)
    {
        var tabItem = _contextMenuTargetTabItem ?? GetTabItemFromSender(sender);
        if (tabItem != null)

            HideTab(tabItem);
    }

    /// <summary>
    /// Ripristina la visibilità di tutte le schede di analisi salvando la configurazione utente.
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
    /// Apre la finestra di dialogo Impostazioni posizionandosi direttamente sulla sezione "Schede Analitiche".
    /// </summary>
    private void OnTabContextMenuManageClicked(object? sender, RoutedEventArgs e)
    {
        OpenSettings(3);
    }

    /// <summary>
    /// Disabilita la visualizzazione del TabItem specificato nelle impostazioni persistite.
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
    /// Gestisce il click o pointer pressed sul pulsante di chiusura (X) presente in ciascuna intestazione di scheda.
    /// Nasconde la scheda corrispondente salvando la preferenza nelle impostazioni utente.
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
    /// Individua il TabItem proprietario del pulsante di chiusura (X) e ne richiede la disattivazione.
    /// </summary>
    private void CloseTabFromButton(Button closeBtn)
    {
        // 1. Verifica se il Tag contiene un riferimento diretto al TabItem
        if (closeBtn.Tag is TabItem tabItemTag)
        {
            HideTab(tabItemTag);
            return;
        }

        // 2. Ricerca nel TabControl centrale
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

        // 3. Ricerca nel TabControl destro
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

        // 4. Fallback tramite risalita dell'albero visuale
        var ancestorTab = closeBtn.FindAncestorOfType<TabItem>();
        if (ancestorTab != null)
            HideTab(ancestorTab);
    }

    /// <summary>
    /// Intercetta gli eventi PointerPressed sul pannello destro per gestire la chiusura immediata delle schede (X).
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
    /// Individua il TabItem appartenente a un TabControl risalendo l'albero visuale esclusivamente
    /// a partire dall'intestazione della scheda (TabStrip / TabItem header), ignorando il contenuto interno del tab.
    /// 
    /// REGOLA ARCHITETTURALE CRITICA:
    /// CentralTabControl ospita sia gli header delle schede sia il corpo dell'intera vista attiva (Canvas del Grafo,
    /// HUD comandi flottante, Matrici, ecc.). Questa funzione DEVE restituire un TabItem SOLO SE il punto di click
    /// si trova fisicamente all'interno dell'header visuale (titolo, icona o pulsante di chiusura).
    /// Se il clic avviene all'interno del contenuto/canvas del tab (es. pulsanti dell'HUD come Blast Radius o Esporta MD),
    /// DEVE restituire null per non intercettare o bloccare gli eventi utente destinati ai controlli della vista.
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
                // Verifica che il punto di click sia all'interno dell'header visuale e non nel corpo del tab
                if (tabItem.Header is Visual headerVisual && (visual == headerVisual || IsVisualDescendantOf(visual, headerVisual)))
                    return tabItem;

                // Fallback: se l'header non è un Visual (es. stringa) o è avvolto nel ContentPresenter di template del TabItem
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

    #region Ordinamento Schede di Analisi con Drag & Drop (CentralTabControl)

    /// <summary>
    /// Gestisce l'evento di pressione del puntatore (mouse down) sulle schede di analisi.
    /// Inizializza le coordinate di partenza e rileva il TabItem di origine senza avviare
    /// immediatamente il trascinamento, consentendo il normale cambio di selezione al clic singolo.
    /// Intercetta con priorità assoluta il click sul pulsante di chiusura (X) per chiudere la scheda all'istante.
    /// 
    /// NOTA: Ignora tassativamente qualsiasi clic originato all'interno del corpo del tab (canvas del grafo,
    /// pulsanti dell'HUD, controlli delle matrici), lasciando fluire l'evento verso i rispettivi gestori.
    /// </summary>
    /// <param name="sender">Sorgente dell'evento (CentralTabControl).</param>
    /// <param name="e">Argomenti del puntatore contenenti coordinate e stato dei tasti.</param>
    private void OnCentralTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Considera esclusivamente il tasto sinistro del mouse
        var currentPoint = e.GetCurrentPoint(this);
        if (!currentPoint.Properties.IsLeftButtonPressed)
            return;

        // Se il clic è avvenuto sul pulsante di chiusura della scheda (X), esegui la chiusura immediata
        var visual = e.Source as Visual;
        var closeBtn = visual as Button ?? visual?.FindAncestorOfType<Button>();
        if (closeBtn != null && (closeBtn.Classes.Contains("tab-close-btn") || closeBtn.Name?.Contains("Close") == true))
        {
            e.Handled = true;
            CloseTabFromButton(closeBtn);
            return;
        }

        // Risale l'albero visuale esclusivamente all'interno dell'intestazione della scheda (ignora il corpo/canvas del tab)
        var tabItem = FindTabItemFromHeaderVisual(visual, CentralTabControl);

        // Ignora clic al di fuori delle intestazioni delle schede del TabControl centrale
        if (tabItem == null || CentralTabControl == null)
            return;

        // Memorizza i dati iniziali per il calcolo della soglia di movimento (deadzone)
        _draggedTabItem = tabItem;
        _tabDragStartPos = e.GetPosition(this);
        _tabDragOffsetWithinTab = e.GetPosition(tabItem);
        _isTabDragging = false;
        _targetDropIndex = -1;
    }

    /// <summary>
    /// Gestisce il movimento del cursore durante la pressione del tasto sinistro.
    /// Superata la deadzone di 4px, attiva la modalità di trascinamento stile Visual Studio:
    /// 1. Visualizza la scheda ghost fluttuante sotto al puntatore del mouse.
    /// 2. Attenua la trasparenza della scheda originale (35% opacity).
    /// 3. Calcola in tempo reale lo slot di inserimento e vi posiziona la barra verticale di drop.
    /// </summary>
    /// <param name="sender">Sorgente dell'evento (CentralTabControl).</param>
    /// <param name="e">Argomenti del puntatore contenenti la posizione corrente.</param>
    private void OnCentralTabPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedTabItem == null || CentralTabControl == null || CentralTabPanel == null)
            return;

        // Se il tasto sinistro non è più premuto (es. rilascio fuori finestra), termina il drag
        var currentPoint = e.GetCurrentPoint(this);
        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            EndCentralTabDrag(null);
            return;
        }

        var currentPos = e.GetPosition(this);

        // Fase 1: Verifica del superamento della soglia di movimento minima (deadzone 4px)
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

                // Configura e visualizza la scheda ghost fluttuante clonando icona e testo del tab
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

        // Fase 2: Aggiornamento continuo della posizione ghost e calcolo dell'indicatore di inserimento
        if (_isTabDragging)
        {
            var posInPanel = e.GetPosition(CentralTabPanel);

            // 1. Sposta la scheda ghost fluttuante in tempo reale seguendo liberamente il puntatore (X e Y)
            var ghostX = posInPanel.X - _tabDragOffsetWithinTab.X;
            var ghostY = posInPanel.Y - _tabDragOffsetWithinTab.Y;
            _tabGhostTransform.X = ghostX;
            _tabGhostTransform.Y = ghostY;

            // 2. Rileva tutte le schede visibili con relative coordinate per supportare il layout multi-riga
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
                // Raggruppa i tab in righe visive basandosi sulla vicinanza verticale del Top (tolleranza 8px)
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

                // Ordina i tab di ciascuna riga da sinistra a destra e le righe dall'alto verso il basso
                foreach (var row in rows)
                {
                    row.Sort((a, b) => a.Bounds.Left.CompareTo(b.Bounds.Left));
                }
                rows.Sort((a, b) => a[0].Bounds.Top.CompareTo(b[0].Bounds.Top));

                // Individua la riga visiva bersaglio su cui si trova il puntatore del mouse (coordinate Y)
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
                    // Trova la riga con la distanza verticale minima dal cursore o che contiene posInPanel.Y
                    targetRow = rows.OrderBy(r =>
                    {
                        var top = r.Min(t => t.Bounds.Top);
                        var bottom = r.Max(t => t.Bounds.Bottom);
                        if (posInPanel.Y >= top && posInPanel.Y <= bottom)
                            return 0.0;
                        return Math.Min(Math.Abs(posInPanel.Y - top), Math.Abs(posInPanel.Y - bottom));
                    }).First();
                }

                // All'interno della riga individuata, calcola lo slot di drop orizzontale in base al punto medio (midpoint)
                _targetDropIndex = -1;
                double indicatorX = 0;
                double indicatorY = targetRow[0].Bounds.Top + 2;
                double indicatorHeight = Math.Max(20, targetRow[0].Bounds.Height - 4);

                var firstTab = targetRow[0];
                var lastTab = targetRow[^1];

                if (posInPanel.X < (firstTab.Bounds.Left + (firstTab.Bounds.Width / 2.0)))
                {
                    // Prima del primo elemento della riga
                    _targetDropIndex = firstTab.Index;
                    indicatorX = firstTab.Bounds.Left - 1;
                    indicatorY = firstTab.Bounds.Top + 2;
                    indicatorHeight = Math.Max(20, firstTab.Bounds.Height - 4);
                }
                else if (posInPanel.X >= (lastTab.Bounds.Left + (lastTab.Bounds.Width / 2.0)))
                {
                    // Dopo l'ultimo elemento della riga
                    _targetDropIndex = lastTab.Index + 1;
                    indicatorX = lastTab.Bounds.Right + 1;
                    indicatorY = lastTab.Bounds.Top + 2;
                    indicatorHeight = Math.Max(20, lastTab.Bounds.Height - 4);
                }
                else
                {
                    // Tra due schede all'interno della riga
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

                // Posiziona e rende visibile la barra verticale di drop sulla riga e colonna corrette
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
    /// Conclude l'operazione di trascinamento al rilascio del tasto del mouse:
    /// sposta la scheda nella destinazione calcolata, ne ripristina la visualizzazione normale
    /// e salva il nuovo ordinamento nelle preferenze persistenti.
    /// 
    /// ATTENZIONE CRITICA (REGRESSIONE CLICK & POINTER CAPTURE IN AVALONIA):
    /// Questo metodo è registrato come Tunnel Handler su CentralTabControl. Poiché CentralTabControl
    /// fa da radice a tutti i contenuti visualizzati (tra cui FloatingHudBorder, GraphControl, Button, ecc.),
    /// QUALSIASI rilascio del puntatore all'interno dell'area centrale scatena questo handler PRIMA dei controlli figli.
    /// In Avalonia, quando un pulsante viene premuto, assume la cattura del puntatore (pointer.Capture).
    /// Se questo handler invocasse incondizionatamente EndCentralTabDrag() e pointer.Capture(null) ad ogni PointerReleased,
    /// sottrarrebbe la cattura del mouse al pulsante mentre questo attende il rilascio, generando l'evento
    /// PointerCaptureLost sul pulsante e ANNULLANDO completamente il suo evento Click!
    /// Pertanto la guard clause (_draggedTabItem == null && !_isTabDragging) è INDISPENSABILE e non deve MAI essere rimossa.
    /// </summary>
    /// <param name="sender">Sorgente dell'evento.</param>
    /// <param name="e">Argomenti del rilascio del puntatore.</param>
    private void OnCentralTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Se non è in corso alcuna operazione o preparazione di trascinamento tab, ignora tassativamente
        // per non interferire con il ciclo di vita degli eventi di click dei controlli figli (es. pulsanti HUD)
        if (_draggedTabItem == null && !_isTabDragging)
            return;

        if (_isTabDragging && _draggedTabItem != null && CentralTabControl != null)
        {
            var fromIndex = CentralTabControl.Items.IndexOf(_draggedTabItem);
            var toIndex = _targetDropIndex;

            if (fromIndex >= 0 && toIndex >= 0)
            {
                // Se la destinazione è successiva all'indice di partenza, compensa la rimozione dell'elemento
                if (toIndex > fromIndex)
                    toIndex--;

                // Esegue lo spostamento atomico nella collezione e seleziona il tab
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
    /// Gestisce la perdita improvvisa di cattura del puntatore (es. cambio finestra o popup modale)
    /// salvando lo stato se applicabile e ripulendo gli elementi visuali del drag.
    /// Come per PointerReleased, ignora l'evento se non riguarda una scheda trascinata.
    /// </summary>
    /// <param name="sender">Sorgente dell'evento.</param>
    /// <param name="e">Argomenti di perdita cattura.</param>
    private void OnCentralTabPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Ignora se non è in corso alcuna operazione di trascinamento tab
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
    /// Rilascia la cattura del puntatore, nasconde la scheda ghost e la barra di drop,
    /// e ripristina la piena opacità della scheda di origine.
    /// 
    /// NOTA DI SICUREZZA: Rilascia la cattura del puntatore (pointer.Capture(null)) ESCLUSIVAMENTE
    /// se _isTabDragging era true (ovvero se la cattura era stata acquisita da CentralTabControl durante il drag).
    /// Non rilasciare mai la cattura se _isTabDragging è false per non azzerare la cattura di controlli terzi.
    /// </summary>
    /// <param name="pointer">Puntatore da rilasciare se disponibile.</param>
    private void EndCentralTabDrag(IPointer? pointer)
    {
        // Rilascia la cattura solo se era stata effettivamente acquisita da CentralTabControl
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
    /// Salva la sequenza corrente dei nomi dei TabItem del pannello centrale nelle impostazioni dell'utente.
    /// Verifica preventivamente se l'ordinamento è effettivamente cambiato per evitare scritture ridondanti.
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
    /// Applica l'ordinamento personalizzato salvato alle schede del TabControl centrale.
    /// Gestisce in modo sicuro l'eventuale presenza di nuove schede introdotte in versioni successive
    /// dell'applicazione, aggiungendole in coda all'ordinamento memorizzato.
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

        // Aggiungi in coda eventuali tab non presenti nella sequenza salvata (es. nuove feature)
        foreach (var tab in allTabs)
        {
            if (tab.Name != null && tabMap.ContainsKey(tab.Name))
                newOrder.Add(tab);
        }

        // Se l'ordinamento coincide già perfettamente, non alterare la collezione
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
    /// Ripristina l'ordinamento originale di fabbrica delle schede di analisi definito nel file XAML,
    /// azzerando la sequenza memorizzata nelle impostazioni utente.
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

    #region Gestione Baseline API Diff & SemVer

    /// <summary>
    /// Fissa lo snapshot del grafo corrente del workspace come baseline per il confronto API e SemVer.
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
    /// Apre un selettore di cartelle per caricare una versione o release precedente come baseline di confronto API.
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
    /// Rimuove la baseline di riferimento corrente ripristinando lo stato di attesa iniziale del tab API Diff.
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
    /// Conta i simboli pubblici esportati da un grafo per la baseline.
    /// </summary>
    private static int CountPublicSymbols(CodeGraph? graph)
    {
        if (graph == null) return 0;
        return graph.Symbols.Count(s =>
            s.Kind is not (SymbolKind.Project or SymbolKind.Namespace) &&
            (s.Accessibility is Accessibility.Public or Accessibility.Global or Accessibility.Protected or Accessibility.ProtectedInternal or Accessibility.Default));
    }

    #endregion

    #region Integrazione VCS (Git & SVN)

    /// <summary>
    /// Aggiorna la visibilità, icona e testo dell'indicatore del branch VCS (Git o SVN) nella barra di stato.
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

                // Aggiorna l'icona vettoriale in base al provider VCS
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
    /// Apre il popup di selezione e cambio branch per il repository Git o SVN attivo.
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
    /// Filtra i branch visualizzati nel popup in base al testo digitato nella casella di ricerca.
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
    /// Gestisce il cambio di branch selezionato dall'utente (Git checkout o SVN switch),
    /// invocando l'operazione in background e re-analizzando l'intero progetto.
    /// </summary>
    private async void OnGitBranchSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (GitBranchListBox.SelectedItem is not VcsBranchInfo selectedBranch) return;

        // Reset della selezione per evitare trigger multipli
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

    #region Sistema di Aggiornamento Software

    /// <summary>
    /// Esegue il controllo automatico degli aggiornamenti software in background all'avvio dell'applicazione.
    /// </summary>
    private async Task CheckForUpdatesOnStartupAsync()
    {
        // Ritardo iniziale per consentire il completamento del rendering e del caricamento del progetto
        await Task.Delay(2500);

        try
        {
            var update = await UpdateService.Instance.CheckForUpdatesAsync();
            if (update != null)

                ShowUpdateModal(update);
        }
        catch
        {
            // Silenzioso all'avvio in caso di errori di rete o endpoint assente
        }
    }

    /// <summary>
    /// Esegue il controllo manuale degli aggiornamenti (da menu Aiuto -> Verifica Aggiornamenti).
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
    /// Mostra l'overlay modale di notifica aggiornamento con le opzioni "Scarica dopo" e "Aggiorna ora".
    /// </summary>
    private void ShowUpdateModal(AppUpdateInfo update)
    {
        _availableUpdate = update;
        UpdateModalControl.Show(update);
    }

    /// <summary>
    /// Chiude il modal di aggiornamento su richiesta dell'utente ("Scarica dopo").
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

    #region Visibilità Pannelli, Scorciatoie da Tastiera & Manutenzione

    /// <summary>
    /// Gestisce le scorciatoie da tastiera globali della finestra principale (es. Ctrl+B per Project Explorer, Ctrl+I per Symbol Inspector).
    /// </summary>
    /// <param name="sender">Sorgente dell'evento di pressione tasto.</param>
    /// <param name="e">Argomenti dell'evento contenenti i modificatori e il tasto premuto.</param>
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
    /// Alterna la visibilità del pannello laterale sinistro Project Explorer o imposta uno stato forzato.
    /// </summary>
    /// <param name="forceState">Stato di visibilità desiderato (true per mostrare, false per nascondere, null per alternare).</param>
    public void ToggleExplorer(bool? forceState = null)
    {
        SetExplorerVisibility(forceState ?? !_isExplorerVisible);
    }

    /// <summary>
    /// Imposta la visibilità e le dimensioni della colonna di layout del Project Explorer nella griglia centrale, salvando lo stato se richiesto.
    /// Preserva accuratamente la larghezza personalizzata impostata dall'utente tramite GridSplitter.
    /// </summary>
    /// <param name="isVisible">Indica se il pannello Project Explorer deve essere visibile.</param>
    /// <param name="updateSettings">Indica se persistere la preferenza nelle impostazioni utente.</param>
    public void SetExplorerVisibility(bool isVisible, bool updateSettings = true)
    {
        _isExplorerVisible = isVisible;
        if (isVisible)
        {
            // Se il pannello è già visibile, aggiorna solo la larghezza memorizzata senza sovrascrivere il layout
            if (ProjectExplorerControl.IsVisible && CentralGrid.ColumnDefinitions[0].ActualWidth >= 160)
            {
                _savedExplorerWidth = CentralGrid.ColumnDefinitions[0].ActualWidth;
            }
            else
            {
                // Determina la larghezza ottimale dalle preferenze salvate o dal valore di default (280px)
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
            // Prima di nascondere, salva la larghezza effettiva corrente del pannello per il successivo ripristino
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
    /// Alterna la visibilità del pannello laterale destro Symbol Inspector o imposta uno stato forzato.
    /// </summary>
    /// <param name="forceState">Stato di visibilità desiderato (true per mostrare, false per nascondere, null per alternare).</param>
    public void ToggleInspector(bool? forceState = null)
    {
        SetInspectorVisibility(forceState ?? !_isInspectorVisible);
    }

    /// <summary>
    /// Imposta la visibilità e le dimensioni della colonna di layout del Symbol Inspector nella griglia centrale, salvando lo stato se richiesto.
    /// Preserva accuratamente la larghezza personalizzata impostata dall'utente tramite GridSplitter senza ripristinare valori di default.
    /// </summary>
    /// <param name="isVisible">Indica se il pannello Symbol Inspector deve essere visibile.</param>
    /// <param name="updateSettings">Indica se persistere la preferenza nelle impostazioni utente.</param>
    public void SetInspectorVisibility(bool isVisible, bool updateSettings = true)
    {
        _isInspectorVisible = isVisible;
        if (isVisible)
        {
            // Riattiva automaticamente le schede se entrambe erano state disattivate
            if (!SettingsService.Instance.Settings.ShowTabInspector && !SettingsService.Instance.Settings.ShowTabSourceCode)
            {
                SettingsService.Instance.Update(s =>
                {
                    s.ShowTabInspector = true;
                    s.ShowTabSourceCode = true;
                });
            }

            // Se il pannello è già visibile, memorizza la larghezza attuale senza forzare un reset delle colonne
            if (RightPanelContainer.IsVisible && CentralGrid.ColumnDefinitions[4].ActualWidth >= 200)
            {
                _savedInspectorWidth = CentralGrid.ColumnDefinitions[4].ActualWidth;
            }
            else
            {
                // Determina la larghezza ottimale dalle preferenze salvate o dal valore di default (360px)
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
            // Prima di nascondere, salva la larghezza effettiva corrente del pannello per il successivo ripristino
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
    /// Aggiorna lo stile visivo attivo/inattivo dei pulsanti di toggle dei pannelli Explorer e Inspector nella barra superiore.
    /// </summary>
    private void UpdatePanelToggleVisuals()
    {
        ToggleExplorerTopButton.Classes.Set("active", _isExplorerVisible);
        ToggleInspectorTopButton.Classes.Set("active", _isInspectorVisible);
    }

    /// <summary>
    /// Ripristina tutte le impostazioni dell'applicazione ai valori predefiniti, aggiornando tema, lingua, layout e visibilità dei pannelli.
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
    /// Svuota completamente la cache locale dei progetti (%APPDATA%\Macondo\cache\) e invalida le query del grafo in memoria.
    /// </summary>
    private void ClearProjectCache()
    {
        var count = ProjectCacheService.Instance.ClearAllCache();
        _graph?.Queries?.ClearCache();
        StatusText.Text = LocalizationService.Instance.Get("Status_CacheCleared", count);
        WelcomeControl.Refresh();
    }

    #endregion

    #region Schermata di Benvenuto (Welcome View)

    /// <summary>
    /// Mostra la schermata iniziale di benvenuto nascondendo i tab di analisi del grafo.
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
    /// Gestisce la richiesta di analisi rapida dalla card di progetto in sospeso della vista di benvenuto.
    /// </summary>
    private async void OnWelcomePendingAnalyzeRequested(object? sender, string folderPath)
    {
        await AnalyzeAndLoadFolderAsync(folderPath, forceReanalyze: true);
    }

    /// <summary>
    /// Gestisce la richiesta di apertura file progetto dalla vista di benvenuto.
    /// </summary>
    private async void OnWelcomeOpenProjectRequested(object? sender, EventArgs e)
    {
        await OpenProjectFilesAsync();
    }

    /// <summary>
    /// Gestisce la richiesta di apertura cartella dalla vista di benvenuto.
    /// </summary>
    private async void OnWelcomeOpenFolderRequested(object? sender, EventArgs e)
    {
        await OpenFolderAsync();
    }

    /// <summary>
    /// Gestisce la richiesta di caricamento fixture demo dalla vista di benvenuto.
    /// </summary>
    private void OnWelcomeLoadFixturesRequested(object? sender, EventArgs e)
    {
        LoadDefaultFixtures();
    }

    /// <summary>
    /// Gestisce la selezione di un elemento recente dalla vista di benvenuto.
    /// </summary>
    private async void OnWelcomeRecentItemClicked(object? sender, IReadOnlyList<string> paths)
    {
        await AnalyzeAndLoadPathsAsync(paths);
    }

    #endregion

    #region Localizzazione & Aggiornamento UI

    /// <summary>
    /// Aggiorna tutte le etichette, tooltip, intestazioni di menu, colonne e testi dell'interfaccia utente in base alla lingua correntemente selezionata nel <see cref="LocalizationService"/>.
    /// </summary>
    private void UpdateLocalizedUI()
    {
        var localizationService = LocalizationService.Instance;

        // Schermata di Benvenuto
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

        // Tooltip pulsanti di chiusura schede (X)
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

        // Legenda Grafo
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
/// Implementazione compatta di ICommand per eseguire azioni delegate dai menu e controlli UI.
/// </summary>
public sealed class ActionCommand : System.Windows.Input.ICommand
{
    private readonly Action _action;
    public ActionCommand(Action action) => _action = action;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _action();
}
