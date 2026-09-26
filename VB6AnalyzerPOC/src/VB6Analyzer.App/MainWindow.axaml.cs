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

    #endregion

    #region Costruttore & Inizializzazione

    public MainWindow()
    {
        InitializeComponent();

        ToggleExplorerTopButton.Click += (_, _) => ToggleExplorer();
        ToggleInspectorTopButton.Click += (_, _) => ToggleInspector();
        ToggleExplorerMenuItem.Command = new ActionCommand(() => ToggleExplorer());
        ToggleInspectorMenuItem.Command = new ActionCommand(() => ToggleInspector());
        ToggleExplorerMenuItem.Click += (_, _) => ToggleExplorer();
        ToggleInspectorMenuItem.Click += (_, _) => ToggleInspector();

        KeyDown += OnMainWindowKeyDown;

        // VCS (Git & SVN) Branch Button
        GitBranchButton.Click += (_, _) => OpenGitBranchPopup();

        // Project Explorer: selezione automatica su clic destro e gestione menu contestuale sui file
        ExplorerTree.AddHandler(InputElement.PointerPressedEvent, OnExplorerTreePointerPressed, RoutingStrategies.Tunnel);
        ExplorerTree.AddHandler(Control.ContextRequestedEvent, OnExplorerTreeContextRequested, RoutingStrategies.Tunnel);

        // Canvas del Grafo: menu contestuale su clic destro sopra i nodi e tracking movimenti
        Graph.AddHandler(Control.ContextRequestedEvent, OnGraphContextRequested, RoutingStrategies.Tunnel);
        Graph.NodeMoved += (_, _) => OnGraphNodeMoved();

        // Pulsanti Dialog Aggiornamento Software
        UpdateLaterButton.Click += (_, _) => DismissUpdateModal();
        UpdateNowButton.Click += (_, _) => ApplyUpdateAndOpenDownload();

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

        // Inspector AI Context Slice buttons
        CopyLlmSliceButton.Click += async (_, _) => await CopyLlmSliceToClipboardAsync();
        SaveLlmSliceButton.Click += async (_, _) => await ExportLlmSliceAsync();

        // Pulsante Ricalcola Audit nella Grid Toolbar
        RefreshGridButton.Click += async (_, _) => await PopulateBlastRadiusGridAsync();

        // Pulsante Mostra/Nascondi Contenuto Legenda Grafo
        ToggleLegendButton.Click += (_, _) =>
        {
            LegendContentPanel.IsVisible = !LegendContentPanel.IsVisible;
            if (Application.Current != null)
            {
                var iconKey = LegendContentPanel.IsVisible ? "IconChevronLeft" : "IconChevronRight";
                if (Application.Current.TryFindResource(iconKey, out var res) && res is StreamGeometry geo)
                {
                    LegendToggleIcon.Data = geo;
                }
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
                {
                    MaximizeIcon.Data = geo;
                }
            }
        };

        SearchBox.GotFocus += (_, _) =>
        {
            var query = SearchBox.Text?.Trim();
            if (!string.IsNullOrEmpty(query) && SearchResultsList.ItemsSource is IList<SearchResultItem> { Count: > 0 })
            {
                SearchResultsPopup.IsOpen = true;
            }
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
            {
                StatusText.Text = LocalizationService.Instance.Get("Status_CfgBlock", block.BlockKind, block.Title);
            }
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
            {
                OpenOriginalFile(item.FilePath, item.LineStart);
            }
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
            {
                OpenOriginalFile(acc.FilePath, acc.Line);
            }
        };

        TemporalCoupling.PairDoubleTapped += (_, pair) =>
        {
            if (pair != null && !string.IsNullOrEmpty(pair.FileA))
            {
                OpenOriginalFile(pair.FileA, 1);
            }
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
            {
                OpenOriginalFile(b.FilePath, 1);
            }
        };

        // Inizializzazione Impostazioni e Preferenze Utente (%APPDATA%\Macondo\settings.json)
        var settings = SettingsService.Instance.Settings;

        // Ripristino Geometria Finestra e Larghezza Pannelli
        if (settings.WindowWidth >= 600 && settings.WindowHeight >= 400)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }

        if (settings.WindowX.HasValue && settings.WindowY.HasValue)
        {
            Position = new PixelPoint(settings.WindowX.Value, settings.WindowY.Value);
        }

        if (settings.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        _savedExplorerWidth = (settings.ExplorerWidth >= 160 && settings.ExplorerWidth <= 700) ? settings.ExplorerWidth : 280;
        _savedInspectorWidth = (settings.InspectorWidth >= 200 && settings.InspectorWidth <= 800) ? settings.InspectorWidth : 360;

        if (settings.IsExplorerVisible)
        {
            CentralGrid.ColumnDefinitions[0].Width = new GridLength(_savedExplorerWidth);
        }
        else
        {
            SetExplorerVisibility(false, updateSettings: false);
        }

        if (settings.IsInspectorVisible)
        {
            CentralGrid.ColumnDefinitions[4].Width = new GridLength(_savedInspectorWidth);
        }
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
                if (_isExplorerVisible && ExplorerBorder.Bounds.Width >= 160)
                {
                    s.ExplorerWidth = ExplorerBorder.Bounds.Width;
                }
                if (_isInspectorVisible && InspectorBorder.Bounds.Width >= 200)
                {
                    s.InspectorWidth = InspectorBorder.Bounds.Width;
                }
                s.ShowMethods = Graph.ShowMethods;
                s.ShowProperties = Graph.ShowProperties;
                s.ShowFields = Graph.ShowFields;
                s.ShowConstants = Graph.ShowConstants;
                s.ShowTypesAndEnums = Graph.ShowTypesAndEnums;
            });

            // Salvataggio sincrono immediato di eventuali modifiche pendenti al layout dei nodi
            FlushPendingLayoutChangesSync();
        };

        // Servizi Reattivi: Localizzazione & Temi con Auto-Save
        LocalizationService.Instance.LanguageChanged += (_, lang) =>
        {
            UpdateLocalizedUI();
            ApplyGridFilters();
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
            ApplyTabsVisibility(s);
        };

        // Applica i valori salvati prima del rendering iniziale
        ThemeService.Instance.ApplyTheme(settings.Theme);
        LocalizationService.Instance.SetLanguage(settings.Language);
        Graph.EnableHoverSelection = settings.EnableHoverSelection;
        HoverSelectionCheckIcon.IsVisible = settings.EnableHoverSelection;
        ApplyTabLabelsVisibility(settings.ShowTabLabels);
        ApplyTabsVisibility(settings);
        UpdateLocalizedUI();

        // Ripristino Filtro di Rischio salvato
        RiskFilterComboBox.SelectedIndex = Math.Clamp(settings.DefaultRiskFilterIndex, 0, 4);

        // Caricamento del Progetto: Gestione Intelligente in base a StartupMode
        var startupMode = settings.StartupMode;
        var lastFolder = settings.LastOpenedFolder;
        var hasValidLastFolder = !string.IsNullOrEmpty(lastFolder) && Directory.Exists(lastFolder);

        if (startupMode == StartupLoadMode.OnlyIfCached && hasValidLastFolder)
        {
            var isCached = ProjectCacheService.Instance.HasCache(lastFolder!);
            if (isCached)
            {
                // Caricamento istantaneo da cache
                _ = AnalyzeAndLoadFolderAsync(lastFolder!);
            }
            else
            {
                // Nessuna cache: mostra la schermata iniziale predisponendo il progetto in sospeso
                _currentFolderPath = lastFolder;
                _currentPaths = new List<string> { lastFolder! };
                UpdateVcsBranchStatus(lastFolder);
                ShowWelcomeScreen(pendingFolder: lastFolder);
                StatusText.Text = LocalizationService.Instance.Get("Status_NoCachePending", Path.GetFileName(lastFolder!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            }
        }
        else if (startupMode == StartupLoadMode.AlwaysAnalyze && hasValidLastFolder)
        {
            _ = AnalyzeAndLoadFolderAsync(lastFolder!);
        }
        else
        {
            // EmptyWorkspace o nessun percorso recente salvato
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
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>
    /// Doppio click sull'area centrale libera della barra del titolo per massimizzare o ripristinare.
    /// </summary>
    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximize();
    }

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
        bool canCancel = true)
    {
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = canCancel ? new CancellationTokenSource() : null;

        LoadingTitle.Text = title;
        LoadingSubtitle.Text = subtitle;
        LoadingProgressBar.IsIndeterminate = isIndeterminate;
        LoadingProgressBar.Minimum = 0;
        LoadingProgressBar.Maximum = progressMax > 0 ? progressMax : 100;
        LoadingProgressBar.Value = progressValue;

        LoadingCancelButton.IsVisible = canCancel;
        LoadingCancelButton.IsEnabled = true;
        LoadingCancelButton.Content = LocalizationService.Instance["Loading_Cancel"];

        if (!string.IsNullOrEmpty(countText))
        {
            LoadingProgressCount.Text = countText;
            LoadingProgressCount.IsVisible = true;
        }
        else
        {
            LoadingProgressCount.Text = string.Empty;
            LoadingProgressCount.IsVisible = false;
        }

        if (!string.IsNullOrEmpty(statsText))
        {
            LoadingProgressStats.Text = statsText;
            LoadingProgressStats.IsVisible = true;
        }
        else
        {
            LoadingProgressStats.Text = string.Empty;
            LoadingProgressStats.IsVisible = false;
        }

        LoadingOverlay.IsVisible = true;
    }

    /// <summary>
    /// Aggiorna l'avanzamento dettagliato e lo stato testuale dell'overlay di caricamento.
    /// </summary>
    private void UpdateLoadingProgress(
        double current,
        double total,
        string? subtitle = null,
        string? countText = null,
        string? statsText = null)
    {
        LoadingProgressBar.IsIndeterminate = false;
        LoadingProgressBar.Minimum = 0;
        LoadingProgressBar.Maximum = total > 0 ? total : 100;
        LoadingProgressBar.Value = current;

        if (subtitle != null)
        {
            LoadingSubtitle.Text = subtitle;
        }

        if (!string.IsNullOrEmpty(countText))
        {
            LoadingProgressCount.Text = countText;
            LoadingProgressCount.IsVisible = true;
        }
        else
        {
            LoadingProgressCount.IsVisible = false;
        }

        if (!string.IsNullOrEmpty(statsText))
        {
            LoadingProgressStats.Text = statsText;
            LoadingProgressStats.IsVisible = true;
        }
        else
        {
            LoadingProgressStats.IsVisible = false;
        }
    }

    /// <summary>
    /// Gestisce il click sul pulsante Annulla all'interno del popup di caricamento / analisi.
    /// </summary>
    private void LoadingCancelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_loadingCts != null && !_loadingCts.IsCancellationRequested)
        {
            LoadingCancelButton.IsEnabled = false;
            LoadingSubtitle.Text = LocalizationService.Instance["Loading_Cancelling"];
            _loadingCts.Cancel();
        }
    }

    /// <summary>
    /// Nasconde l'overlay di caricamento e reimposta lo stato della barra di avanzamento.
    /// </summary>
    private void HideLoading()
    {
        LoadingOverlay.IsVisible = false;
        LoadingProgressBar.IsIndeterminate = true;
        LoadingProgressBar.Value = 0;
        LoadingProgressCount.IsVisible = false;
        LoadingProgressStats.IsVisible = false;
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
            StatusText.Text = localizationService["Status_FakeGraphGenerating"];
            _graph = await Task.Run(() => Generation.FakeGraphGenerator.Generate(seed: Environment.TickCount), token);
#if !DEBUG
            token.ThrowIfCancellationRequested();
#endif
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
            UpdateLoadingProgress(p.CurrentFileIndex, p.TotalFiles, p.PhaseMessage, countStr, statsStr);
        });

        try
        {
            await Task.Delay(16, token);
            _currentFolderPath = null;
            UpdateVcsBranchStatus(null);
            StatusText.Text = localizationService["Loading_FixturesSub"];
            _graph = await Task.Run(() => ProjectAnalysisService.LoadDefaultFixtures(progress, token), token);
#if !DEBUG
            token.ThrowIfCancellationRequested();
#endif
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
                {
                    await AnalyzeAndLoadPathsAsync(paths);
                }
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
                    Patterns = new[] { "*.vbp", "*.vbg", "*.csproj", "*.sln", "*.cs" }
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
        {
            targetFolder = SettingsService.Instance.Settings.LastOpenedFolder;
        }

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

            // 1. Tenta il ripristino ultra-rapido dalla cache solo se singola cartella e non forzato
            if (!forceReanalyze && paths.Count == 1 && Directory.Exists(firstPath))
            {
                var cachedSnapshot = await ProjectCacheService.Instance.TryLoadSnapshotAsync(firstPath);
#if !DEBUG
                token.ThrowIfCancellationRequested();
#endif
                if (cachedSnapshot != null)
                {
                    var (cachedGraph, cachedRows) = await Task.Run(() => ProjectCacheService.Instance.RestoreGraphAndRows(cachedSnapshot), token);
#if !DEBUG
                    token.ThrowIfCancellationRequested();
#endif
                    _graph = cachedGraph;
                    _currentFolderPath = firstPath;
                    _currentPaths = paths.ToList();
                    UpdateVcsBranchStatus(firstPath);
                    ApplyGraphToUI(cachedRows, applyAutomaticLayout: false);
                    _hasUserMovedNodes = cachedSnapshot.HasCustomUserLayout;

                    // Salva l'ultima cartella aperta nelle impostazioni e aggiunge ai recenti
                    SettingsService.Instance.Update(s => s.LastOpenedFolder = firstPath);
                    AddToRecentProjects(paths);

                    // Rileva eventuali modifiche ai sorgenti intervenute dopo il salvataggio della cache
                    var isStale = ProjectCacheService.Instance.CheckStaleness(cachedSnapshot, firstPath);
                    if (isStale)
                    {
                        StatusText.Text = LocalizationService.Instance.Get("Status_CacheStale", cachedSnapshot.ProjectName);
                    }
                    else
                    {
                        StatusText.Text = LocalizationService.Instance.Get("Status_LoadedFromCache", cachedSnapshot.ProjectName, cachedSnapshot.AnalyzedAtUtc.ToLocalTime().ToString("g"));
                    }
                    return;
                }
            }

            // 2. Analisi completa da sorgenti su disco con barra di avanzamento determinata
            var progress = new Progress<ProjectAnalysisProgress>(p =>
            {
                var countStr = localizationService.Get("Loading_FilesProgressFormat", p.CurrentFileIndex, p.TotalFiles, (int)p.Percentage);
                var statsStr = localizationService.Get("Loading_NodesExtractedFormat", p.ExtractedNodesCount);
                UpdateLoadingProgress(p.CurrentFileIndex, p.TotalFiles, p.PhaseMessage, countStr, statsStr);
            });

            StatusText.Text = localizationService.Get("Status_AnalyzingSources", paths.Count);
            _graph = await Task.Run(() => ProjectAnalysisService.AnalyzePaths(paths, progress, token), token);
#if !DEBUG
            token.ThrowIfCancellationRequested();
#endif
            _currentFolderPath = firstPath;
            _currentPaths = paths.ToList();
            if (Directory.Exists(firstPath)) UpdateVcsBranchStatus(firstPath);
            ApplyGraphToUI(); // Avvierà PopulateBlastRadiusGridAsync che poi salverà la cache aggiornata

            var projectCount = _graph.Nodes.Count(n => n.Kind == SymbolKind.Project);
            var crossCount = _graph.Edges.Count(e => e.IsCrossProject);
            if (projectCount > 1)
            {
                StatusText.Text = localizationService.Get("Status_WorkspaceAnalyzed", projectCount, _graph.Nodes.Count, crossCount);
            }
            else
            {
                StatusText.Text = localizationService.Get("Status_Refreshed", titleName, _graph.Nodes.Count);
            }

            // Salva l'ultima cartella aperta nelle impostazioni se singola cartella e aggiunge ai recenti
            if (paths.Count == 1 && Directory.Exists(firstPath))
            {
                SettingsService.Instance.Update(s => s.LastOpenedFolder = firstPath);
            }
            AddToRecentProjects(paths);

            // Lampeggio icona sulla barra di Windows e notifica di sistema
            WindowsNotificationService.NotifyAnalysisCompleted(
                this,
                localizationService["Notification_AnalysisCompleted_Title"],
                localizationService.Get("Notification_AnalysisCompleted_Message", titleName, _graph.Nodes.Count));
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
    /// Aggiunge un progetto, file o cartella alla lista dei progetti recenti e aggiorna il menu.
    /// </summary>
    private void AddToRecentProjects(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;

        SettingsService.Instance.Update(settings =>
        {
            var isFolder = paths.Count == 1 && Directory.Exists(paths[0]);
            var firstPath = paths[0];
            var displayName = paths.Count == 1
                ? Path.GetFileName(firstPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : $"Workspace ({paths.Count} percorsi)";

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = firstPath;
            }

            // Rimuovi se già presente
            settings.RecentProjects.RemoveAll(r => r.Paths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase));

            // Inserisci in cima
            settings.RecentProjects.Insert(0, new RecentProjectItem
            {
                Paths = paths.ToList(),
                DisplayName = displayName,
                IsFolder = isFolder,
                LastOpenedUtc = DateTime.UtcNow
            });

            // Tieni solo gli ultimi 10
            if (settings.RecentProjects.Count > 10)
            {
                settings.RecentProjects = settings.RecentProjects.Take(10).ToList();
            }
        });

        UpdateRecentProjectsMenu();
        UpdateWelcomeView();
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
            {
                item.Icon = new PathIcon
                {
                    Data = geo,
                    Width = 14,
                    Height = 14,
                    Foreground = Color.TryParse(iconColor, out var c) ? new SolidColorBrush(c) : Brushes.DodgerBlue
                };
            }

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
        {
            clearItem.Icon = new PathIcon
            {
                Data = clearGeo,
                Width = 12,
                Height = 12
            };
        }
        clearItem.Click += (_, _) =>
        {
            SettingsService.Instance.Update(s => s.RecentProjects.Clear());
            UpdateRecentProjectsMenu();
        };
        RecentProjectsMenuItem.Items.Add(clearItem);
    }

    /// <summary>
    /// Aggiorna tutti i componenti della UI (Grafo, Explorer, Status) con il nuovo CodeGraph.
    /// Se precalculatedRows è fornito (es. da cache), la griglia Blast Radius viene popolata istantaneamente
    /// e le coordinate personalizzate salvate non vengono sovrascritte se applyAutomaticLayout è false.
    /// </summary>
    private void ApplyGraphToUI(List<BlastRadiusGridRow>? precalculatedRows = null, bool applyAutomaticLayout = true)
    {
        if (_graph is null) return;

        // Mostra i Tab centrali di analisi e nasconde la Welcome View
        WelcomePanel.IsVisible = false;
        CentralTabControl.IsVisible = true;

        if (applyAutomaticLayout)
        {
            Layout.GraphLayoutManager.ApplyLayout(_graph, _currentLayoutMode);
        }
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

        ExplorerTree.ItemsSource = new[] { ExplorerTreeItem.BuildTree(_graph, _currentFolderPath, Graph.HiddenProjectIds) };

        ShowEmptyInspector();
        UpdateProjectsFilterUI();
        UpdateStatsBar();
        SyncFilterUiState();

        InvalidateAnalyticsViews();

        if (precalculatedRows != null && precalculatedRows.Count > 0)
        {
            _allBlastRadiusRows = precalculatedRows;
            ApplyGridFilters();
        }
        else
        {
            _allBlastRadiusRows = new();
            ApplyGridFilters();
            _ = PopulateBlastRadiusGridAsync();
        }
    }

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

    private void SetDetailPreset(int index)
    {
        if (index == 0) // Completo: tutti i membri visibili
        {
            Graph.SetFilters(true, true, true, true, true);
        }
        else if (index == 1) // Metodi & Proprietà: nasconde variabili, costanti e tipi
        {
            Graph.SetFilters(true, true, false, false, false);
        }
        else if (index == 2) // Solo Moduli: nasconde tutti i membri interni
        {
            Graph.SetFilters(false, false, false, false, false);
        }
        SaveFilterSettings();
    }

    private void OnDetailLevelComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingFilterUi) return;
        SetDetailPreset(DetailLevelComboBox.SelectedIndex);
    }

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
            {
                DetailLevelComboBox.SelectedIndex = 0; // Completo
            }
            else if (Graph.ShowMethods && Graph.ShowProperties && !Graph.ShowFields && !Graph.ShowConstants && !Graph.ShowTypesAndEnums)
            {
                DetailLevelComboBox.SelectedIndex = 1; // Metodi & Proprietà
            }
            else if (!Graph.ShowMethods && !Graph.ShowProperties && !Graph.ShowFields && !Graph.ShowConstants && !Graph.ShowTypesAndEnums)
            {
                DetailLevelComboBox.SelectedIndex = 2; // Solo Moduli
            }
            else
            {
                DetailLevelComboBox.SelectedIndex = 3; // Personalizzato
            }

            if (_graph is not null)
            {
                var (vis, total) = Graph.GetNodeCounts();
                if (Graph.CurrentHighlightMode == HighlightMode.Normal && total > 0)
                {
                    StatusText.Text = LocalizationService.Instance.Get("Status_NodesDisplayed", vis, total);
                }
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
    /// immediatamente la pre-elaborazione in background (su thread pool a bassa priorità).
    /// In questo modo:
    /// 1. Il thread UI mostra il grafo visuale e l'albero file a 0 ms di ritardo (nessun blocco o freeze).
    /// 2. I vari tab analitici (DSM, Treemap, Martin, Dead Code, Screen Flow, ecc.) vengono pre-calcolati
    ///    silenziosamente in background nei secondi successivi.
    /// 3. Se l'utente clicca su un tab già completato dal worker in background, l'apertura è istantanea (0 ms).
    /// 4. Se l'utente clicca immediatamente su un tab prima che il worker vi sia arrivato, il calcolo viene eseguito
    ///    just-in-time su quel singolo tab.
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

        // Se l'utente si trova già su un tab analitico secondario (es. non la vista grafo), calcolalo subito
        EnsureActiveTabAnalyticsLoaded();

        // Avvia la pipeline di pre-calcolo in background per tutti i rimanenti tab
        StartBackgroundAnalyticsPrecomputation(_analyticsCts.Token);
    }

    /// <summary>
    /// Pipeline in background per pre-calcolare i dataset di tutti i tab analitici senza bloccare l'interfaccia,
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
        const int totalAnalyticsSteps = 18;

        // Inizializza e mostra la barra di avanzamento nella status bar
        if (BackgroundAnalyticsPanel != null)
        {
            BackgroundAnalyticsPanel.IsVisible = true;
            BackgroundAnalyticsProgressBar.Minimum = 0;
            BackgroundAnalyticsProgressBar.Maximum = totalAnalyticsSteps;
            BackgroundAnalyticsProgressBar.Value = 0;
            BackgroundAnalyticsText.Text = LocalizationService.Instance.Get("Status_BackgroundAnalyticsProgress", 0, totalAnalyticsSteps);
        }

        _ = Task.Run(async () =>
        {
            int completedCount = 0;

            void ReportStepCompletion()
            {
                var done = Interlocked.Increment(ref completedCount);
                if (token.IsCancellationRequested) return;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested || BackgroundAnalyticsPanel == null) return;

                    BackgroundAnalyticsProgressBar.Value = done;
                    BackgroundAnalyticsText.Text = LocalizationService.Instance.Get("Status_BackgroundAnalyticsProgress", done, totalAnalyticsSteps);

                    // Al completamento di tutti i tab, visualizza brevemente lo stato completato e poi nasconde la barra
                    if (done >= totalAnalyticsSteps)
                    {
                        BackgroundAnalyticsText.Text = LocalizationService.Instance["Status_BackgroundAnalyticsCompleted"];
                        _ = Task.Delay(1500, token).ContinueWith(_ =>
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                if (!token.IsCancellationRequested && BackgroundAnalyticsPanel != null)
                                {
                                    BackgroundAnalyticsPanel.IsVisible = false;
                                }
                            });
                        }, TaskScheduler.Default);
                    }
                });
            }

            var s = SettingsService.Instance.Settings;

            // 1. Matrice DSM
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabDsm && _dirtyAnalyticsTabs.Contains(DsmTabItem))
                {
                    var matrix = DsmMatrixBuilder.Build(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(DsmTabItem))
                            {
                                Dsm.Matrix = matrix;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 2. Treemap
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabTreemap && _dirtyAnalyticsTabs.Contains(TreemapTabItem))
                {
                    var treemap = TreemapModelBuilder.Build(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(TreemapTabItem))
                            {
                                Treemap.RootItem = treemap;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 3. Hotspots di Rischio
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabHotspot && _dirtyAnalyticsTabs.Contains(HotspotTabItem))
                {
                    var hotspot = HotspotAnalyzer.Analyze(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(HotspotTabItem))
                            {
                                Hotspot.Dataset = hotspot;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 4. Martin Metrics
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabMartin && _dirtyAnalyticsTabs.Contains(MartinTabItem))
                {
                    var martin = MartinMetricsCalculator.Analyze(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(MartinTabItem))
                            {
                                MartinMetrics.Dataset = martin;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 5. Dead Code e Componenti Orfani
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabDeadCode && _dirtyAnalyticsTabs.Contains(DeadCodeTabItem))
                {
                    var deadCode = DeadCodeAnalyzer.Analyze(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(DeadCodeTabItem))
                            {
                                DeadCode.Dataset = deadCode;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 6. Flusso Schermate (Screen Flow)
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabScreenFlow && _dirtyAnalyticsTabs.Contains(ScreenFlowTabItem))
                {
                    var screenFlow = ScreenFlowBuilder.Build(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(ScreenFlowTabItem))
                            {
                                ScreenFlow.Graph = screenFlow;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 7. Stato Globale Condiviso
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabSharedState && _dirtyAnalyticsTabs.Contains(SharedStateTabItem))
                {
                    var sharedState = SharedStateAnalyzer.Analyze(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(SharedStateTabItem))
                            {
                                SharedState.Dataset = sharedState;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 8. Flussi Architetturali (Chord Diagram)
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabArchitecturalFlow && _dirtyAnalyticsTabs.Contains(ArchitecturalFlowTabItem))
                {
                    var archFlow = ArchitecturalFlowAnalyzer.Analyze(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(ArchitecturalFlowTabItem))
                            {
                                ArchitecturalFlow.Dataset = archFlow;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 9. CodeCity 3D / Isometrica
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabCodeCity && _dirtyAnalyticsTabs.Contains(CodeCityTabItem))
                {
                    var codeCity = CodeCityBuilder.Build(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(CodeCityTabItem))
                            {
                                CodeCity.Dataset = codeCity;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 10. Temporal Coupling (Analisi Cronologia Git su Disco)
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabTemporalCoupling && _dirtyAnalyticsTabs.Contains(TemporalCouplingTabItem))
                {
                    var commitChangelists = GitService.Instance.GetCommitFileChangelists(folder);
                    var dataset = TemporalCouplingAnalyzer.Analyze(commitChangelists, targetGraph, folder);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(TemporalCouplingTabItem))
                            {
                                TemporalCoupling.Dataset = dataset;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 11. Coesione & LCOM
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabCohesion && _dirtyAnalyticsTabs.Contains(CohesionTabItem))
                {
                    var cohesion = CohesionCalculator.Analyze(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(CohesionTabItem))
                            {
                                Cohesion.Dataset = cohesion;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 12. Maintainability Index & Halstead
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabMaintainability && _dirtyAnalyticsTabs.Contains(MaintainabilityTabItem))
                {
                    var maintainability = MaintainabilityCalculator.Analyze(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(MaintainabilityTabItem))
                            {
                                Maintainability.Dataset = maintainability;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 13. Regole Architetturali
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabArchitecturalRules && _dirtyAnalyticsTabs.Contains(ArchitecturalRulesTabItem))
                {
                    var rulesReport = ArchitecturalRulesEngine.Evaluate(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(ArchitecturalRulesTabItem))
                            {
                                ArchitecturalRules.Report = rulesReport;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 14. Duplicazioni e Cloni (CPD)
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabCodeClone && _dirtyAnalyticsTabs.Contains(CodeCloneTabItem))
                {
                    var cloneReport = CodeCloneDetector.DetectClones(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(CodeCloneTabItem))
                            {
                                CodeClone.Report = cloneReport;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 15. Social Code & Bus Factor
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabSocialCode && _dirtyAnalyticsTabs.Contains(SocialCodeTabItem))
                {
                    var commitRecords = GitService.Instance.GetFileCommitHistory(folder);
                    var socialDataset = SocialCodeAnalyzer.Analyze(targetGraph, commitRecords);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(SocialCodeTabItem))
                            {
                                SocialCode.Dataset = socialDataset;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 16. Data Flow & Taint Tracking
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabDataFlow && _dirtyAnalyticsTabs.Contains(DataFlowTabItem))
                {
                    var dataFlowDataset = TaintAnalyzer.Analyze(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(DataFlowTabItem))
                            {
                                DataFlow.Dataset = dataFlowDataset;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 17. API Diff & SemVer
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabApiDiff && _dirtyAnalyticsTabs.Contains(ApiDiffTabItem))
                {
                    var apiDiffReport = ApiDiffAnalyzer.Compare(new CodeGraph(), targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(ApiDiffTabItem))
                            {
                                ApiDiff.Report = apiDiffReport;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }

            // 18. Matrice CRUD
            if (!token.IsCancellationRequested)
            {
                if (s.ShowTabCrudMatrix && _dirtyAnalyticsTabs.Contains(CrudMatrixTabItem))
                {
                    var crudDataset = CrudMatrixAnalyzer.Analyze(targetGraph);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && _dirtyAnalyticsTabs.Remove(CrudMatrixTabItem))
                            {
                                CrudMatrix.Dataset = crudDataset;
                            }
                        });
                    }
                }
                ReportStepCompletion();
            }
        }, token);
    }

    /// <summary>
    /// Esegue il calcolo asincrono on-demand (Lazy) per il tab analitico correntemente selezionato
    /// qualora sia contrassegnato come dirty e non ancora pre-elaborato in background.
    /// Esegue il lavoro su Task.Run in background per garantire che il thread UI rimanga sempre reattivo a 60 FPS.
    /// </summary>
    private void EnsureActiveTabAnalyticsLoaded()
    {
        if (_graph?.SemanticGraph is null || CentralTabControl == null) return;

        var selectedItem = CentralTabControl.SelectedItem;
        if (selectedItem == null || !_dirtyAnalyticsTabs.Contains(selectedItem))
            return;

        var targetGraph = GetTargetSemanticGraph();
        _dirtyAnalyticsTabs.Remove(selectedItem);

        if (selectedItem == DsmTabItem)
        {
            _ = Task.Run(() =>
            {
                var matrix = DsmMatrixBuilder.Build(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Dsm.Matrix = matrix;
                });
            });
        }
        else if (selectedItem == TreemapTabItem)
        {
            _ = Task.Run(() =>
            {
                var treemap = TreemapModelBuilder.Build(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Treemap.RootItem = treemap;
                });
            });
        }
        else if (selectedItem == HotspotTabItem)
        {
            _ = Task.Run(() =>
            {
                var hotspot = HotspotAnalyzer.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Hotspot.Dataset = hotspot;
                });
            });
        }
        else if (selectedItem == ScreenFlowTabItem)
        {
            _ = Task.Run(() =>
            {
                var screenFlow = ScreenFlowBuilder.Build(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ScreenFlow.Graph = screenFlow;
                });
            });
        }
        else if (selectedItem == MartinTabItem)
        {
            _ = Task.Run(() =>
            {
                var martin = MartinMetricsCalculator.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    MartinMetrics.Dataset = martin;
                });
            });
        }
        else if (selectedItem == DeadCodeTabItem)
        {
            _ = Task.Run(() =>
            {
                var deadCode = DeadCodeAnalyzer.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    DeadCode.Dataset = deadCode;
                });
            });
        }
        else if (selectedItem == SharedStateTabItem)
        {
            _ = Task.Run(() =>
            {
                var sharedState = SharedStateAnalyzer.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    SharedState.Dataset = sharedState;
                });
            });
        }
        else if (selectedItem == ArchitecturalFlowTabItem)
        {
            _ = Task.Run(() =>
            {
                var flow = ArchitecturalFlowAnalyzer.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ArchitecturalFlow.Dataset = flow;
                });
            });
        }
        else if (selectedItem == CodeCityTabItem)
        {
            _ = Task.Run(() =>
            {
                var city = CodeCityBuilder.Build(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    CodeCity.Dataset = city;
                });
            });
        }
        else if (selectedItem == TemporalCouplingTabItem)
        {
            var folder = _currentFolderPath;
            _ = Task.Run(() =>
            {
                var commitChangelists = GitService.Instance.GetCommitFileChangelists(folder);
                var dataset = TemporalCouplingAnalyzer.Analyze(commitChangelists, targetGraph, folder);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    TemporalCoupling.Dataset = dataset;
                });
            });
        }
        else if (selectedItem == CohesionTabItem)
        {
            _ = Task.Run(() =>
            {
                var cohesion = CohesionCalculator.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Cohesion.Dataset = cohesion;
                });
            });
        }
        else if (selectedItem == MaintainabilityTabItem)
        {
            _ = Task.Run(() =>
            {
                var maintainability = MaintainabilityCalculator.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Maintainability.Dataset = maintainability;
                });
            });
        }
        else if (selectedItem == ArchitecturalRulesTabItem)
        {
            _ = Task.Run(() =>
            {
                var rules = ArchitecturalRulesEngine.Evaluate(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ArchitecturalRules.Report = rules;
                });
            });
        }
        else if (selectedItem == CodeCloneTabItem)
        {
            _ = Task.Run(() =>
            {
                var clones = CodeCloneDetector.DetectClones(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    CodeClone.Report = clones;
                });
            });
        }
        else if (selectedItem == SocialCodeTabItem)
        {
            var folder = _currentFolderPath;
            _ = Task.Run(() =>
            {
                var commitRecords = GitService.Instance.GetFileCommitHistory(folder);
                var dataset = SocialCodeAnalyzer.Analyze(targetGraph, commitRecords);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    SocialCode.Dataset = dataset;
                });
            });
        }
        else if (selectedItem == DataFlowTabItem)
        {
            _ = Task.Run(() =>
            {
                var dataset = TaintAnalyzer.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    DataFlow.Dataset = dataset;
                });
            });
        }
        else if (selectedItem == ApiDiffTabItem)
        {
            _ = Task.Run(() =>
            {
                var report = ApiDiffAnalyzer.Compare(new CodeGraph(), targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ApiDiff.Report = report;
                });
            });
        }
        else if (selectedItem == CrudMatrixTabItem)
        {
            _ = Task.Run(() =>
            {
                var dataset = CrudMatrixAnalyzer.Analyze(targetGraph);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    CrudMatrix.Dataset = dataset;
                });
            });
        }
    }

    /// <summary>
    /// Popola dinamicamente i selettori di visibilità dei singoli progetti (Project Explorer e Menu Visualizza)
    /// quando sono presenti progetti multipli nel workspace corrente.
    /// </summary>
    private void UpdateProjectsFilterUI()
    {
        if (_graph is null)
        {
            ExplorerProjectFilterBar.IsVisible = false;
            FilterProjectsMenuItem.IsVisible = false;
            FilterProjectsMenuItem.Items.Clear();
            return;
        }

        var projectNodes = _graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        if (projectNodes.Count <= 1)
        {
            ExplorerProjectFilterBar.IsVisible = false;
            FilterProjectsMenuItem.IsVisible = false;
            FilterProjectsMenuItem.Items.Clear();
            return;
        }

        ExplorerProjectFilterBar.IsVisible = true;
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

    private void UpdateMenuItemCheck(MenuItem mi, bool isChecked)
    {
        if (isChecked)
        {
            if (this.TryFindResource("IconCheck", out var res) && res is StreamGeometry geo)
            {
                mi.Icon = new PathIcon
                {
                    Data = geo,
                    Width = 12,
                    Height = 12,
                    Foreground = this.FindResource("ThemeAccentBrush") as IBrush ?? Brushes.DodgerBlue
                };
            }
        }
        else
        {
            mi.Icon = null;
        }
    }

    private void UpdateProjectsBadgeText()
    {
        var (vis, total) = Graph.GetProjectCounts();
        ExplorerProjectsCountText.Text = LocalizationService.Instance.Get("Explorer_ProjectsVisible", vis, total);
        ExplorerProjectsSelectAllButton.IsEnabled = (vis < total);
    }

    private void OnExplorerProjectCheckBoxClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is ExplorerTreeItem item && item.ProjectId.HasValue)
        {
            var isVis = cb.IsChecked == true;
            Graph.SetProjectVisibility(item.ProjectId.Value, isVis);
        }
    }

    private void OnGraphProjectVisibilityChanged(object? sender, Guid projectId)
    {
        // Sincronizza lo stato dei nodi nell'ExplorerTree
        if (ExplorerTree.ItemsSource is IEnumerable<ExplorerTreeItem> rootItems)
        {
            void SyncTreeItemVisibility(ExplorerTreeItem item)
            {
                if (item.IsProjectRoot && item.ProjectId.HasValue)
                {
                    item.IsProjectVisible = Graph.IsProjectVisible(item.ProjectId.Value);
                }
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
        ApplyGridFilters();
        InvalidateAnalyticsViews();
    }

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
            UpdateInspectorImpactSection(cachedReport);
            StatusText.Text = localizationService.Get("Status_BlastReportFormat", node.QualifiedName, cachedReport.DirectCallers.Count, cachedReport.IndirectCallers.Count, cachedReport.AffectedForms.Count, localizationService.GetRiskLevel(cachedReport.RiskLevel));
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
                UpdateInspectorImpactSection(report);
                StatusText.Text = localizationService.Get("Status_BlastReportFormat", node.QualifiedName, report.DirectCallers.Count, report.IndirectCallers.Count, report.AffectedForms.Count, localizationService.GetRiskLevel(report.RiskLevel));
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

    private void OnGraphSelectionChanged(object? sender, GraphNode? node)
    {
        _selectedNode = node;

        if (!_isSyncingSelection && CentralTabControl?.SelectedIndex == 6 && node != null)
        {
            var matchingRow = _allBlastRadiusRows.FirstOrDefault(r => r.NodeId == node.Id);
            if (matchingRow != null && BlastRadiusDataGrid.SelectedItem != matchingRow)
            {
                _isSyncingSelection = true;
                try
                {
                    BlastRadiusDataGrid.SelectedItem = matchingRow;
                    BlastRadiusDataGrid.ScrollIntoView(matchingRow, null);
                }
                finally
                {
                    _isSyncingSelection = false;
                }
            }
        }

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
        {
            screenId = node.ParentId.Value;
        }
        if (ScreenFlow.Graph?.FindScreen(screenId) is not null)
        {
            ScreenFlow.SelectScreen(screenId);
            ScreenFlow.FocusOnScreen(screenId);
        }

        StatusText.Text = LocalizationService.Instance.Get("Status_Selected", node.Kind, node.QualifiedName);
    }

    #endregion

    #region Project Explorer & Interazione File e Cartelle

    private void OnExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not ExplorerTreeItem item) return;
        if (_graph is null) return;

        var node = _graph.GetNode(item.NodeId);
        if (node is not null)
            SelectAndFocusGraphNode(node);
    }

    public void OnExplorerDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ExplorerTree.SelectedItem is not ExplorerTreeItem item) return;
        if (item.IsGroup) return; // double-clicking group expands/collapses tree

        if (!string.IsNullOrEmpty(item.FilePath))
        {
            OpenOriginalFile(item.FilePath, item.LineStart);
        }
        else if (_graph != null)
        {
            var node = _graph.GetNode(item.NodeId);
            if (node?.SemanticSymbol?.FilePath != null)
            {
                OpenOriginalFile(node.SemanticSymbol.FilePath, node.SemanticSymbol.LineStart);
            }
            else if (!string.IsNullOrEmpty(_currentFolderPath))
            {
                OpenContainingFolder(_currentFolderPath);
            }
        }
    }

    private void OnExplorerOpenItemButtonClick(object? sender, RoutedEventArgs e)
    {
        var item = GetExplorerItemFromSender(sender);
        if (item != null && !string.IsNullOrEmpty(item.FilePath))
        {
            OpenOriginalFile(item.FilePath, item.LineStart);
        }
    }

    private void OnExplorerTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(ExplorerTree);
        if (point.Properties.IsRightButtonPressed)
        {
            var visual = e.Source as Visual;
            var tvi = visual?.FindAncestorOfType<TreeViewItem>();
            if (tvi?.DataContext is ExplorerTreeItem item)
            {
                ExplorerTree.SelectedItem = item;
            }
        }
    }

    private void OnExplorerTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var visual = e.Source as Visual;
        var tvi = visual?.FindAncestorOfType<TreeViewItem>();
        var item = tvi?.DataContext as ExplorerTreeItem ?? ExplorerTree.SelectedItem as ExplorerTreeItem;

        // Se è un gruppo o non ha un file associato, impedisci categoricamente l'apertura del menu
        if (item == null || item.IsGroup || !item.HasFile)
        {
            e.Handled = true;
            return;
        }

        // È un file valido: apri il menu contestuale dedicato sui file
        if (ExplorerBorder.Resources.TryGetResource("ExplorerFileContextMenu", null, out var res) && res is ContextMenu menu)
        {
            menu.DataContext = item;
            menu.PlacementTarget = tvi ?? (Control)e.Source!;
            menu.Open(tvi ?? (Control)e.Source!);
            e.Handled = true;
        }
    }

    private void OnExplorerContextMenuOpenFile(object? sender, RoutedEventArgs e)
    {
        var item = GetExplorerItemFromSender(sender);
        if (item != null && !item.IsGroup && item.HasFile)
        {
            OpenOriginalFile(item.FilePath, item.LineStart);
        }
    }

    private void OnExplorerContextMenuOpenFolder(object? sender, RoutedEventArgs e)
    {
        var item = GetExplorerItemFromSender(sender);
        if (item != null && !item.IsGroup && item.HasFile)
        {
            OpenContainingFolder(item.FilePath);
        }
    }

    private void OnExplorerContextMenuCopyPath(object? sender, RoutedEventArgs e)
    {
        var item = GetExplorerItemFromSender(sender);
        if (item != null && !item.IsGroup && item.HasFile)
        {
            CopyFilePathToClipboard(item.FilePath);
        }
    }

    private void OnInspectorOpenFileClicked(object? sender, RoutedEventArgs e)
    {
        var filePath = _selectedNode?.SemanticSymbol?.FilePath;
        var line = _selectedNode?.SemanticSymbol?.LineStart ?? 0;
        OpenOriginalFile(filePath, line);
    }

    private void OnInspectorOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        var filePath = _selectedNode?.SemanticSymbol?.FilePath;
        OpenContainingFolder(filePath);
    }

    private ExplorerTreeItem? GetExplorerItemFromSender(object? sender)
    {
        if (sender is Button btn && btn.CommandParameter is ExplorerTreeItem itemParam)
            return itemParam;

        if (sender is MenuItem menuItem)
        {
            if (menuItem.DataContext is ExplorerTreeItem item1)
                return item1;
            if (menuItem.Parent is ContextMenu ctx && ctx.DataContext is ExplorerTreeItem itemCtx)
                return itemCtx;
            if (menuItem.Parent is ContextMenu ctx2 && ctx2.PlacementTarget?.DataContext is ExplorerTreeItem itemTarget)
                return itemTarget;
            if (menuItem.Parent is ContextMenu ctxTvi && ctxTvi.PlacementTarget is TreeViewItem tvi && tvi.DataContext is ExplorerTreeItem itemTvi)
                return itemTvi;
        }

        if (sender is ContextMenu ctxMenu)
        {
            if (ctxMenu.DataContext is ExplorerTreeItem itemCtx)
                return itemCtx;
            if (ctxMenu.PlacementTarget?.DataContext is ExplorerTreeItem itemTarget)
                return itemTarget;
            if (ctxMenu.PlacementTarget is TreeViewItem tvi2 && tvi2.DataContext is ExplorerTreeItem itemTvi2)
                return itemTvi2;
        }

        if (sender is Control ctrl && ctrl.DataContext is ExplorerTreeItem item3)
            return item3;

        return ExplorerTree.SelectedItem as ExplorerTreeItem;
    }

    #region Menu Contestuale Nodi del Grafo

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

    private async void OnNodeExportMarkdownClicked(object? sender, RoutedEventArgs e) => await ExportLlmSliceAsync();

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
        {
            json = CodeGraphJsonExporter.ExportSliceToJson(semGraph, symbol, indented: true);
        }
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

    private string? ResolveEffectiveFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return _currentFolderPath;
        }

        if (Path.IsPathRooted(filePath) && (File.Exists(filePath) || Directory.Exists(filePath)))
        {
            return filePath;
        }

        if (!string.IsNullOrEmpty(_currentFolderPath))
        {
            var combined = Path.Combine(_currentFolderPath, filePath);
            if (File.Exists(combined) || Directory.Exists(combined))
                return combined;
            return combined;
        }

        return filePath;
    }

    #endregion

    #region Ricerca Globale & Command Palette

    private record AppSearchTarget(
        string Title,
        string Subtitle,
        string BadgeText,
        SearchResultCategory Category,
        string[] Keywords,
        Action Action);

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
                () => CentralTabControl.SelectedIndex = 0),

            new AppSearchTarget(
                isEn ? "View: DSM Matrix" : "Vista: Matrice DSM",
                isEn ? "Dependency Structure Matrix for coupling and cycle analysis" : "Matrice delle dipendenze per analisi di accoppiamento e cicli",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "dsm", "matrice", "matrix", "cicli", "accoppiamento", "struttura" },
                () => CentralTabControl.SelectedIndex = 1),

            new AppSearchTarget(
                isEn ? "View: Treemap" : "Vista: Treemap Gerarchica",
                isEn ? "Proportional metric treemap by LOC size and complexity" : "Mappa ad albero gerarchica proporzionale per righe di codice e complessità",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "treemap", "albero", "loc", "complessita", "dimensioni", "metriche", "size" },
                () => CentralTabControl.SelectedIndex = 2),

            new AppSearchTarget(
                isEn ? "View: Hotspot Risk Matrix" : "Vista: Matrice Hotspot",
                isEn ? "4-Quadrant risk matrix (Complexity vs Churn / Fan-In)" : "Grafico cartesiano a 4 quadranti per individuare componenti ad alto rischio",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "hotspot", "rischio", "fan-in", "churn", "criticita", "risk" },
                () => CentralTabControl.SelectedIndex = 3),

            new AppSearchTarget(
                isEn ? "View: Screen Flow" : "Vista: Flusso Schermate",
                isEn ? "UI form navigation diagram, modal dialogs and screen transitions" : "Diagramma di flusso e navigazione tra Form, finestre e dialoghi UI",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "flusso schermate", "screenflow", "schermate", "form", "finestre", "dialoghi", "ui", "screens", "flow" },
                () => CentralTabControl.SelectedIndex = 4),

            new AppSearchTarget(
                isEn ? "View: Control Flow Graph (CFG)" : "Vista: Control Flow Graph (CFG)",
                isEn ? "Control flow graph of routines, branching and basic blocks" : "Grafo del flusso di controllo per Sub, Function e Property",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "control flow", "cfg", "flusso controllo", "blocchi", "rami", "routine" },
                () => CentralTabControl.SelectedIndex = 5),

            new AppSearchTarget(
                isEn ? "View: Martin Metrics" : "Vista: Metriche Martin",
                isEn ? "Abstractness vs Instability scatter plot and Main Sequence analysis" : "Diagramma di Martin (Astrattezza vs Instabilità e Sequenza Principale)",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "martin", "metriche martin", "abstractness", "instability", "astrattezza", "instabilita", "main sequence", "sofferenza", "pain" },
                () => CentralTabControl.SelectedIndex = 6),

            new AppSearchTarget(
                isEn ? "View: Dead Code & Orphans" : "Vista: Dead Code & Orfani",
                isEn ? "Dead code detection, unreachable routines and orphan components" : "Analisi codice morto, routine non raggiungibili e componenti orfani",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "dead code", "orfani", "orphans", "codice morto", "unreachable", "non raggiungibile", "inutilizzato" },
                () => CentralTabControl.SelectedIndex = 7),

            new AppSearchTarget(
                isEn ? "View: Shared State" : "Vista: Stato Condiviso",
                isEn ? "Global and shared mutable state matrix, readers/writers and mutation risk" : "Matrice stato globale condiviso, lettori/scrittori e rischio mutazioni cross-module",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "shared state", "stato condiviso", "variabili globali", "static", "mutazioni", "global", "side effects" },
                () => CentralTabControl.SelectedIndex = 8),

            new AppSearchTarget(
                isEn ? "View: Temporal Coupling" : "Vista: Temporal Coupling",
                isEn ? "VCS commit co-change history, Jaccard similarity and hidden couplings" : "Analisi co-modifiche storiche da commit Git, indice Jaccard e accoppiamenti nascosti",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "temporal coupling", "co-change", "git", "commit", "jaccard", "hidden coupling", "accoppiamento temporale" },
                () => CentralTabControl.SelectedIndex = 9),

            new AppSearchTarget(
                isEn ? "View: Architectural Flow" : "Vista: Flussi Architetturali",
                isEn ? "Module-to-module Chord diagram, call volumes and architectural layer boundaries" : "Diagramma Chord delle chiamate tra moduli, volumi di flusso e violazioni di layer",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "flussi", "flow", "chord", "sankey", "layer", "architettura", "volumi", "chiamate", "architectural flow" },
                () => CentralTabControl.SelectedIndex = 10),

            new AppSearchTarget(
                isEn ? "View: CodeCity 3D" : "Vista: CodeCity 3D",
                isEn ? "3D / Isometric 2.5D polymetric city map (Building size = LOC, Height = Complexity)" : "Mappa urbana 3D/2.5D polimetrica (Dimensione base = LOC, Altezza = Complessità)",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "codecity", "city", "citta", "3d", "isometric", "isometria", "edifici", "polimetrica", "urban" },
                () => CentralTabControl.SelectedIndex = 11),

            new AppSearchTarget(
                isEn ? "View: Blast Radius Grid" : "Vista: Griglia Blast Radius",
                isEn ? "Tabular impact assessment and blast radius details" : "Tabella di analisi impatto e raggio d'azione per le modifiche",
                isEn ? "View" : "Vista",
                SearchResultCategory.View,
                new[] { "blast radius", "impatto", "griglia", "tabella", "modifiche", "impact" },
                () => CentralTabControl.SelectedIndex = 12),

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

    private void ExecuteSearchResult(SearchResultItem item)
    {
        SearchResultsPopup.IsOpen = false;
        if (item.Node != null)
        {
            SelectAndFocusGraphNode(item.Node);
        }
        else if (item.ExecuteAction != null)
        {
            item.ExecuteAction.Invoke();
            StatusText.Text = $"{item.Title}";
        }
    }

    private void OnSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is SearchResultItem selected)
        {
            ExecuteSearchResult(selected);
            Dispatcher.UIThread.Post(() => SearchResultsList.SelectedItem = null);
        }
    }

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

    private void ShowEmptyInspector()
    {
        InspectorEmptyPanel.IsVisible = true;
        InspectorDetailsPanel.IsVisible = false;
        UpdateStatusLanguage();
    }

    private async void SelectAndInspectNodeAsync(GraphNode node)
    {
        // Annulla ogni analisi precedente ancora in corso
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        _selectionCts = new CancellationTokenSource();
        var ct = _selectionCts.Token;

        // 1. Mostra istantaneamente i metadati statici del simbolo
        InspectorEmptyPanel.IsVisible = false;
        InspectorDetailsPanel.IsVisible = true;

        InspectorName.Text = node.Name;
        InspectorQualifiedName.Text = node.QualifiedName;
        InspectorKind.Text = node.Kind.ToString();

        var sym = node.SemanticSymbol;

        // Costruisci il Control Flow Graph (CFG) se è una routine
        if (sym != null && _graph?.SemanticGraph != null && (sym.Kind == SymbolKind.Method || sym.Kind == SymbolKind.Property))
        {
            ControlFlow.Graph = ControlFlowBuilder.BuildForSymbol(sym, _graph.SemanticGraph);
        }

        // Visibilità
        InspectorAccessibility.Text = sym != null ? sym.Accessibility.ToString() : "N/A";

        // Linguaggio
        var (langBadge, langBg, langFg, langTip) = ExplorerTreeItem.GetLanguageBadgeInfo(node.Language);
        if (!string.IsNullOrEmpty(langBadge))
        {
            InspectorLanguageBadge.IsVisible = true;
            InspectorLanguage.Text = langBadge;
            InspectorLanguageBadge.Background = Avalonia.Media.Brush.Parse(langBg ?? "#2563EB");
            InspectorLanguage.Foreground = Avalonia.Media.Brush.Parse(langFg ?? "#FFFFFF");
            ToolTip.SetTip(InspectorLanguageBadge, langTip);
        }
        else
        {
            InspectorLanguageBadge.IsVisible = false;
        }

        UpdateStatusLanguage(node.Language);

        // Tipi
        if (sym != null && (sym.DeclaredTypeName != null || sym.ReturnType != null))
        {
            InspectorTypeSection.IsVisible = true;
            var declared = sym.DeclaredTypeName ?? sym.ReturnType;
            InspectorType.Text = sym.ResolvedTypeSymbolId.HasValue || sym.ResolvedReturnTypeSymbolId.HasValue
                ? $"As {declared} (Risolto)"
                : $"As {declared}";
        }
        else
        {
            InspectorTypeSection.IsVisible = false;
        }

        // Parametri formali
        if (sym != null && sym.Parameters.Count > 0)
        {
            InspectorParamsSection.IsVisible = true;
            var paramList = sym.Parameters.Select(p =>
            {
                var mode = p.IsByVal ? "ByVal" : "ByRef";
                var opt = p.IsOptional ? "Optional " : "";
                var type = p.DeclaredTypeName != null ? $" As {p.DeclaredTypeName}" : "";
                return $"{opt}{mode} {p.Name}{type}";
            });
            InspectorParams.Text = string.Join("\n", paramList);
        }
        else
        {
            InspectorParamsSection.IsVisible = false;
        }

        // File e coordinate di riga
        if (sym != null && sym.FilePath != null)
        {
            InspectorFileSection.IsVisible = true;
            InspectorFile.Text = $"{Path.GetFileName(sym.FilePath)} (Righe {sym.LineStart}-{sym.LineEnd})";
        }
        else
        {
            InspectorFileSection.IsVisible = false;
        }

        // Se non abbiamo simboli o query, svuota i pannelli dinamici
        if (_graph?.Queries is null || sym is null)
        {
            ClearDynamicInspectorSections();
            return;
        }

        // Se il simbolo è già presente in cache, aggiorna immediatamente a 0ms (nessun overlay, nessun task ritardato)
        if (_graph.Queries.TryGetCachedImpactReport(sym, 10, out var cachedImpact) && cachedImpact != null)
        {
            var cachedCallees = _graph.Queries.GetDirectCallees(sym);
            var cachedInstantiations = _graph.Queries.GetInstantiatedClasses(sym);
            UpdateInspectorImpactDetails(cachedImpact, cachedCallees, cachedInstantiations);
            return;
        }

        // 2. Imposta lo stato di caricamento nell'Inspector per Blast Radius e chiamanti
        var localizationService = LocalizationService.Instance;
        RiskLevelText.Text = localizationService["Inspector_Calculating"];
        RiskBadge.Background = new SolidColorBrush(Color.Parse("#FF6633"));
        BlastDirectCallersCount.Text = "...";
        BlastIndirectCallersCount.Text = "...";
        BlastMaxDepth.Text = "...";
        InspectorCallers.Text = localizationService["Inspector_CalculatingCallers"];
        InspectorCallees.Text = localizationService["Inspector_CalculatingCallees"];
        BlastFormsSection.IsVisible = false;
        BlastClassesSection.IsVisible = false;
        BlastPathsSection.IsVisible = false;
        InspectorInstantiatesSection.IsVisible = false;

        var targetId = node.Id;
        bool isCompleted = false;

        // Se il calcolo richiede più di 80ms (tipico per nodi complessi), mostra il loading overlay centrato
        _ = Task.Run(async () =>
        {
            await Task.Delay(80, ct);
            if (!ct.IsCancellationRequested && !isCompleted)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested && !isCompleted && _selectedNode?.Id == targetId)
                    {
                        ShowLoading(localizationService["Loading_NodeAnalysisTitle"], localizationService.Get("Loading_NodeAnalysisSub", node.Name));
                    }
                });
            }
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
            UpdateInspectorImpactDetails(impact, callees, instantiations);
        }
        catch (OperationCanceledException)
        {
            // Analisi annullata da una nuova selezione: operazione normale
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested && _selectedNode?.Id == targetId)
            {
                StatusText.Text = localizationService.Get("Status_NodeAnalysisError", ex.Message);
            }
        }
        finally
        {
            isCompleted = true;
            if (_selectedNode?.Id == targetId)
            {
                HideLoading();
            }
        }
    }

    private void ClearDynamicInspectorSections()
    {
        var localizationService = LocalizationService.Instance;
        InspectorCallers.Text = localizationService["Inspector_NoItems"];
        InspectorCallees.Text = localizationService["Inspector_NoItems"];
        InspectorInstantiatesSection.IsVisible = false;
        BlastFormsSection.IsVisible = false;
        BlastClassesSection.IsVisible = false;
        BlastPathsSection.IsVisible = false;
    }

    private void UpdateInspectorImpactSection(ImpactReport impact)
    {
        var localizationService = LocalizationService.Instance;

        // Risk Badge
        RiskLevelText.Text = localizationService.Get("Risk_Format", localizationService.GetRiskLevel(impact.RiskLevel));
        RiskBadge.Background = impact.RiskLevel switch
        {
            ImpactRiskLevel.Low => new SolidColorBrush(Color.Parse("#2E7D32")),
            ImpactRiskLevel.Medium => new SolidColorBrush(Color.Parse("#F57C00")),
            ImpactRiskLevel.High => new SolidColorBrush(Color.Parse("#D32F2F")),
            ImpactRiskLevel.Critical => new SolidColorBrush(Color.Parse("#880E4F")),
            _ => new SolidColorBrush(Color.Parse("#454555"))
        };

        BlastDirectCallersCount.Text = impact.DirectCallers.Count.ToString();
        BlastIndirectCallersCount.Text = impact.IndirectCallers.Count.ToString();
        BlastMaxDepth.Text = impact.MaxDependencyDepth.ToString();

        // Form UI impattate
        if (impact.AffectedForms.Count > 0)
        {
            BlastFormsSection.IsVisible = true;
            BlastFormsList.Text = string.Join(", ", impact.AffectedForms.Select(f => f.Name));
        }
        else
        {
            BlastFormsSection.IsVisible = false;
        }

        // Classi e Moduli toccati
        var touchedContainers = impact.AffectedClasses.Concat(impact.AffectedModules).ToList();
        if (touchedContainers.Count > 0)
        {
            BlastClassesSection.IsVisible = true;
            BlastClassesList.Text = string.Join(", ", touchedContainers.Select(c => c.Name));
        }
        else
        {
            BlastClassesSection.IsVisible = false;
        }

        // Percorsi di esecuzione (call chains)
        if (impact.AffectedExecutionPaths.Count > 0)
        {
            BlastPathsSection.IsVisible = true;
            var paths = impact.AffectedExecutionPaths.Take(5).Select(p => $"• {p}");
            BlastPathsList.Text = string.Join("\n", paths) + (impact.AffectedExecutionPaths.Count > 5 ? $"\n... (+ altri {impact.AffectedExecutionPaths.Count - 5})" : "");
        }
        else
        {
            BlastPathsSection.IsVisible = false;
        }
    }

    private void UpdateInspectorImpactDetails(
        ImpactReport impact,
        IReadOnlyList<Symbol> callees,
        IReadOnlyList<Symbol> instantiations)
    {
        UpdateInspectorImpactSection(impact);

        var localizationService = LocalizationService.Instance;

        // Callers diretti standard
        InspectorCallers.Text = impact.DirectCallers.Count > 0
            ? string.Join("\n", impact.DirectCallers.Select(c => $"• {c.QualifiedName}"))
            : localizationService["Inspector_NoCallers"];

        // Callees diretti
        InspectorCallees.Text = callees.Count > 0
            ? string.Join("\n", callees.Select(c => $"• {c.QualifiedName}"))
            : localizationService["Inspector_NoCallees"];

        // Classi istanziate
        if (instantiations.Count > 0)
        {
            InspectorInstantiatesSection.IsVisible = true;
            InspectorInstantiates.Text = string.Join("\n", instantiations.Select(i => $"• {i.QualifiedName}"));
        }
        else
        {
            InspectorInstantiatesSection.IsVisible = false;
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

    private void OnCentralTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source != CentralTabControl) return;

        // Calcola e carica i dati del tab solo quando l'utente vi accede per la prima volta (Lazy Analytics)
        EnsureActiveTabAnalyticsLoaded();

        if (CentralTabControl.SelectedItem == GridTabItem && _selectedNode != null)
        {
            var matchingRow = _allBlastRadiusRows.FirstOrDefault(r => r.NodeId == _selectedNode.Id);
            if (matchingRow != null && BlastRadiusDataGrid.SelectedItem != matchingRow)
            {
                _isSyncingSelection = true;
                try
                {
                    BlastRadiusDataGrid.SelectedItem = matchingRow;
                    BlastRadiusDataGrid.ScrollIntoView(matchingRow, null);
                }
                finally
                {
                    _isSyncingSelection = false;
                }
            }
        }
    }

    private async Task PopulateBlastRadiusGridAsync()
    {
        if (_graph is null)
        {
            _allBlastRadiusRows = new();
            if (BlastRadiusDataGrid != null) BlastRadiusDataGrid.ItemsSource = null;
            if (GridStatsTextBlock != null) GridStatsTextBlock.Text = LocalizationService.Instance.Get("Grid_StatsFormat", 0, 0);
            if (GridLoadingProgressBar != null) GridLoadingProgressBar.IsVisible = false;
            return;
        }

        var graph = _graph;
        var queries = graph.Queries;
        if (queries == null) return;

        // Raccogliamo tutti i nodi di tipo routine (Method e Property)
        var routineNodes = graph.Nodes
            .Where(n => n.SemanticSymbol is not null &&
                        (n.SemanticSymbol.Kind == SymbolKind.Method || n.SemanticSymbol.Kind == SymbolKind.Property))
            .ToList();

        if (routineNodes.Count == 0)
        {
            _allBlastRadiusRows = new();
            if (BlastRadiusDataGrid != null) BlastRadiusDataGrid.ItemsSource = null;
            if (GridStatsTextBlock != null) GridStatsTextBlock.Text = LocalizationService.Instance.Get("Grid_StatsFormat", 0, 0);
            if (GridLoadingProgressBar != null) GridLoadingProgressBar.IsVisible = false;
            return;
        }

        // Mostra e configura la barra di avanzamento del calcolo
        if (GridLoadingProgressBar != null)
        {
            GridLoadingProgressBar.Minimum = 0;
            GridLoadingProgressBar.Maximum = routineNodes.Count;
            GridLoadingProgressBar.Value = 0;
            GridLoadingProgressBar.IsVisible = true;
        }

        if (GridStatsTextBlock != null)
            GridStatsTextBlock.Text = LocalizationService.Instance.Get("Grid_Calculating", routineNodes.Count);

        try
        {
            var totalCount = routineNodes.Count;
            var processedCount = 0;
            var lastReportTime = DateTime.UtcNow;

            var rows = await Task.Run(() =>
            {
                var result = new List<BlastRadiusGridRow>(totalCount);
                for (int i = 0; i < totalCount; i++)
                {
                    var node = routineNodes[i];
                    var sym = node.SemanticSymbol!;
                    var report = queries.GetImpactReport(sym);
                    string containerName = "(globale)";
                    if (node.ParentId.HasValue && graph.GetNode(node.ParentId.Value) is { } parentNode)
                    {
                        containerName = parentNode.Name;
                    }
                    else if (!string.IsNullOrEmpty(sym.FilePath))
                    {
                        containerName = Path.GetFileName(sym.FilePath);
                    }
                    var affectedForms = report.AffectedForms.Select(f => f.Name).ToList();

                    result.Add(new BlastRadiusGridRow
                    {
                        TargetSymbol = sym,
                        NodeId = node.Id,
                        ProjectId = node.ProjectId ?? (node.Kind == SymbolKind.Project ? node.Id : null),
                        ProjectName = node.ProjectName ?? "(predefinito)",
                        Name = sym.Name,
                        QualifiedName = sym.QualifiedName,
                        ContainerName = containerName,
                        Kind = sym.Kind.ToString(),
                        DirectCallersCount = report.DirectCallers.Count,
                        IndirectCallersCount = report.IndirectCallers.Count,
                        MaxDepth = report.MaxDependencyDepth,
                        RiskLevel = report.RiskLevel,
                        AffectedFormsCount = report.AffectedForms.Count,
                        AffectedFormsSummary = affectedForms.Count > 0 ? string.Join(", ", affectedForms) : "-",
                        TotalImpactedCount = report.DirectCallers.Count + report.IndirectCallers.Count + report.AffectedForms.Count + report.AffectedClasses.Count + report.AffectedModules.Count
                    });

                    processedCount++;

                    // Notifica periodica dell'avanzamento sulla UI
                    var now = DateTime.UtcNow;
                    if (processedCount % 25 == 0 || (now - lastReportTime).TotalMilliseconds >= 100 || processedCount == totalCount)
                    {
                        lastReportTime = now;
                        var current = processedCount;
                        var percent = (int)((double)current / totalCount * 100);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (GridLoadingProgressBar != null && GridLoadingProgressBar.IsVisible)
                            {
                                GridLoadingProgressBar.Value = current;
                            }
                            if (GridStatsTextBlock != null)
                            {
                                GridStatsTextBlock.Text = LocalizationService.Instance.Get("Grid_CalculatingProgress", current, totalCount, percent);
                            }
                        });
                    }
                }

                // Ordinamento predefinito: Rischio decrescente (Critical -> High -> Medium -> Low), poi Impatto Totale decrescente
                return result
                    .OrderByDescending(r => r.RiskLevel)
                    .ThenByDescending(r => r.TotalImpactedCount)
                    .ToList();
            });

            _allBlastRadiusRows = rows;
            ApplyGridFilters();

            // Salva in background nella cache persistente se stiamo analizzando una cartella su disco
            if (!string.IsNullOrEmpty(_currentFolderPath) && _graph != null)
            {
                _ = ProjectCacheService.Instance.SaveSnapshotAsync(_currentFolderPath, _graph, _allBlastRadiusRows, _hasUserMovedNodes);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationService.Instance.Get("Status_ErrorGridCalc", ex.Message);
        }
        finally
        {
            if (GridLoadingProgressBar != null)
            {
                GridLoadingProgressBar.IsVisible = false;
            }
        }
    }

    private void ApplyGridFilters()
    {
        if (BlastRadiusDataGrid == null || GridStatsTextBlock == null) return;

        var filterText = GridFilterTextBox?.Text?.Trim() ?? string.Empty;
        var selectedRiskIndex = RiskFilterComboBox?.SelectedIndex ?? 0;

        IEnumerable<BlastRadiusGridRow> query = _allBlastRadiusRows;

        if (Graph.HiddenProjectIds.Count > 0)
        {
            query = query.Where(r => !r.ProjectId.HasValue || !Graph.HiddenProjectIds.Contains(r.ProjectId.Value));
        }

        if (!string.IsNullOrEmpty(filterText))
        {
            query = query.Where(r =>
                r.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                r.ContainerName.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                r.ProjectName.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                r.QualifiedName.Contains(filterText, StringComparison.OrdinalIgnoreCase));
        }

        if (selectedRiskIndex > 0)
        {
            var targetRisk = selectedRiskIndex switch
            {
                1 => ImpactRiskLevel.Critical,
                2 => ImpactRiskLevel.High,
                3 => ImpactRiskLevel.Medium,
                4 => ImpactRiskLevel.Low,
                _ => (ImpactRiskLevel?)null
            };

            if (targetRisk.HasValue)
            {
                query = query.Where(r => r.RiskLevel == targetRisk.Value);
            }
        }

        var filteredList = query.ToList();
        BlastRadiusDataGrid.ItemsSource = filteredList;
        GridStatsTextBlock.Text = LocalizationService.Instance.Get("Grid_StatsFormat", filteredList.Count, _allBlastRadiusRows.Count);

        // Se c'è un nodo selezionato, sincronizza la riga corrispondente
        if (_selectedNode != null)
        {
            var matchingRow = filteredList.FirstOrDefault(r => r.NodeId == _selectedNode.Id);
            if (matchingRow != null && BlastRadiusDataGrid.SelectedItem != matchingRow)
            {
                _isSyncingSelection = true;
                try
                {
                    BlastRadiusDataGrid.SelectedItem = matchingRow;
                    BlastRadiusDataGrid.ScrollIntoView(matchingRow, null);
                }
                finally
                {
                    _isSyncingSelection = false;
                }
            }
        }
    }

    private void OnGridFilterTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyGridFilters();
    }

    private void OnRiskFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        ApplyGridFilters();
        if (RiskFilterComboBox != null)
        {
            SettingsService.Instance.Update(s => s.DefaultRiskFilterIndex = RiskFilterComboBox.SelectedIndex);
        }
    }

    private void OnRefreshGridClicked(object? sender, RoutedEventArgs e)
    {
        _ = PopulateBlastRadiusGridAsync();
    }

    private void OnGridRowSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection || BlastRadiusDataGrid.SelectedItem is not BlastRadiusGridRow row)
            return;

        if (_graph == null) return;
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

    private void OnGridRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (BlastRadiusDataGrid.SelectedItem is BlastRadiusGridRow row)
        {
            NavigateToNodeInGraph(row.NodeId);
        }
    }

    private void OnShowInGraphButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BlastRadiusGridRow row })
        {
            NavigateToNodeInGraph(row.NodeId);
        }
    }

    private void NavigateToNodeInGraph(Guid nodeId)
    {
        if (_graph == null) return;
        var node = _graph.GetNode(nodeId);
        if (node == null) return;

        // 1. Passa alla scheda Vista Grafo
        CentralTabControl.SelectedIndex = 0;

        // 2. Seleziona, espande il container genitore se necessario e centra la visuale sul nodo
        SelectAndFocusGraphNode(node);

        // 3. Calcola ed evidenzia il Blast Radius grafico
        OnBlastRadiusClicked();
    }

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
            var distinctLangs = _graph.Nodes
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

    private void OpenSettings(int initialTab = 0)
    {
        var settingsWin = new SettingsWindow(initialTab);
        settingsWin.ShowDialog(this);
    }

    private void OpenInfo(int initialTab = 0)
    {
        var infoWin = new Views.InfoWindow(initialTab);
        infoWin.ShowDialog(this);
    }

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
            ExplorerBorder.Background = surfaceBrush;
            GridToolbarBorder.Background = surfaceBrush;
            InspectorBorder.Background = surfaceBrush;
            BlastRadiusDataGrid.Background = surfaceBrush;
            BlastRadiusDataGrid.RowBackground = surfaceBrush;
        }

        if (Color.TryParse(theme.BorderDefault, out var borderCol))
        {
            var borderBrush = new SolidColorBrush(borderCol);
            TopBarBorder.BorderBrush = borderBrush;
            SearchRowBorder.BorderBrush = borderBrush;
            StatusBarBorder.BorderBrush = borderBrush;
            FloatingHudBorder.BorderBrush = borderBrush;
            ExplorerBorder.BorderBrush = borderBrush;
            GridToolbarBorder.BorderBrush = borderBrush;
            InspectorBorder.BorderBrush = borderBrush;
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
            {
                markdown = await Task.Run(() => CodeGraphMarkdownExporter.ExportSlice(semGraph, symbol));
            }
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

    private string? _lastExportedFilePath;

    /// <summary>
    /// Mostra il popup modale di conferma per l'esportazione completata con successo.
    /// </summary>
    private void ShowExportSuccessModal(string fileName, string fullPath)
    {
        var localizationService = LocalizationService.Instance;
        _lastExportedFilePath = fullPath;

        ExportSuccessModalTitle.Text = localizationService["Export_ModalTitle"];
        ExportSuccessModalSubtitle.Text = localizationService["Export_ModalSubtitle"];
        ExportSuccessFileLabel.Text = localizationService["Export_ModalFileLabel"];
        ExportSuccessPathLabel.Text = localizationService["Export_ModalPathLabel"];
        ExportSuccessFileName.Text = fileName;
        ExportSuccessFilePath.Text = fullPath;
        ExportSuccessOpenFolderButton.Content = localizationService["Export_ModalOpenFolder"];
        ExportSuccessCloseButton.Content = localizationService["Export_ModalOk"];

        ExportSuccessOverlay.IsVisible = true;
    }

    private void OnExportSuccessCloseClicked(object? sender, RoutedEventArgs e)
    {
        ExportSuccessOverlay.IsVisible = false;
    }

    private void OnExportSuccessOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastExportedFilePath))
        {
            OpenContainingFolder(_lastExportedFilePath);
        }
        ExportSuccessOverlay.IsVisible = false;
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

    private void ShowLayoutConfirmModal()
    {
        var localizationService = LocalizationService.Instance;
        LayoutConfirmModalTitle.Text = localizationService["Layout_ConfirmTitle"];
        LayoutConfirmModalSubtitle.Text = localizationService["Layout_ConfirmMessage"];
        LayoutConfirmCancelButton.Content = localizationService["Layout_ConfirmCancel"];
        LayoutConfirmApplyButton.Content = localizationService["Layout_ConfirmApply"];
        LayoutConfirmOverlay.IsVisible = true;
    }

    private void OnLayoutConfirmCancelClicked(object? sender, RoutedEventArgs e)
    {
        LayoutConfirmOverlay.IsVisible = false;
        _pendingLayoutMode = null;
        // Ripristina l'indice selezionato nella ComboBox senza scatenare nuovo evento
        LayoutComboBox.SelectedIndex = (int)_currentLayoutMode;
    }

    private void OnLayoutConfirmApplyClicked(object? sender, RoutedEventArgs e)
    {
        LayoutConfirmOverlay.IsVisible = false;
        if (_pendingLayoutMode.HasValue)
        {
            var mode = _pendingLayoutMode.Value;
            _pendingLayoutMode = null;
            _hasUserMovedNodes = false;
            SetLayoutMode(mode, resetView: true);
        }
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
        {
            LayoutComboBox.SelectedIndex = targetIndex;
        }

        if (_graph != null)
        {
            Layout.GraphLayoutManager.ApplyLayout(_graph, mode);
            Graph.InvalidateVisual();
            if (resetView)
            {
                ResetGraphView();
            }
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
        _ = Task.Run(async () => await ProjectCacheService.Instance.UpdateLayoutSnapshotAsync(folder, graph, hasMoved));
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
        ProjectCacheService.Instance.UpdateLayoutSnapshotSync(_currentFolderPath, _graph, _hasUserMovedNodes);
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
            {
                Graph.FocusOnNode(targetProject.Id);
            }
        }
    }

    private string GetLocalizedLayoutName(Layout.GraphLayoutMode mode) => mode switch
    {
        Layout.GraphLayoutMode.StandardRadial => LocalizationService.Instance["Layout_StandardRadial"],
        Layout.GraphLayoutMode.AffinityRadial => LocalizationService.Instance["Layout_AffinityRadial"],
        Layout.GraphLayoutMode.ClusteredIslands => LocalizationService.Instance["Layout_ClusteredIslands"],
        Layout.GraphLayoutMode.ForceDirected => LocalizationService.Instance["Layout_ForceDirected"],
        _ => mode.ToString()
    };

    private void OnLayoutComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LayoutComboBox.SelectedIndex < 0) return;
        var mode = (Layout.GraphLayoutMode)LayoutComboBox.SelectedIndex;
        if (mode != _currentLayoutMode)
        {
            RequestSetLayoutMode(mode, resetView: true);
        }
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

    private void ToggleTabLabels()
    {
        var newValue = !SettingsService.Instance.Settings.ShowTabLabels;
        SettingsService.Instance.Update(s => s.ShowTabLabels = newValue);
        ApplyTabLabelsVisibility(newValue);
    }

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
        {
            TabLabelsCheckIcon.IsVisible = showLabels;
        }
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

        // Se la scheda attualmente attiva viene nascosta, ripiega in sicurezza sulla prima scheda visibile
        if (CentralTabControl?.SelectedItem is TabItem currentTab && !currentTab.IsVisible)
        {
            var firstVisible = CentralTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.IsVisible);
            if (firstVisible != null)
            {
                CentralTabControl.SelectedItem = firstVisible;
            }
        }
    }

    private void OnToggleTabGraphMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabGraph = !s.ShowTabGraph);

    private void OnToggleTabDsmMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabDsm = !s.ShowTabDsm);

    private void OnToggleTabTreemapMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabTreemap = !s.ShowTabTreemap);

    private void OnToggleTabHotspotMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabHotspot = !s.ShowTabHotspot);

    private void OnToggleTabScreenFlowMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabScreenFlow = !s.ShowTabScreenFlow);

    private void OnToggleTabControlFlowMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabControlFlow = !s.ShowTabControlFlow);

    private void OnToggleTabMartinMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabMartin = !s.ShowTabMartin);

    private void OnToggleTabDeadCodeMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabDeadCode = !s.ShowTabDeadCode);

    private void OnToggleTabSharedStateMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabSharedState = !s.ShowTabSharedState);

    private void OnToggleTabTemporalCouplingMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabTemporalCoupling = !s.ShowTabTemporalCoupling);

    private void OnToggleTabArchitecturalFlowMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabArchitecturalFlow = !s.ShowTabArchitecturalFlow);

    private void OnToggleTabCodeCityMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabCodeCity = !s.ShowTabCodeCity);

    private void OnToggleTabCohesionMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabCohesion = !s.ShowTabCohesion);

    private void OnToggleTabMaintainabilityMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabMaintainability = !s.ShowTabMaintainability);

    private void OnToggleTabArchitecturalRulesMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabArchitecturalRules = !s.ShowTabArchitecturalRules);

    private void OnToggleTabCodeCloneMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabCodeClone = !s.ShowTabCodeClone);

    private void OnToggleTabSocialCodeMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabSocialCode = !s.ShowTabSocialCode);

    private void OnToggleTabDataFlowMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabDataFlow = !s.ShowTabDataFlow);

    private void OnToggleTabApiDiffMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabApiDiff = !s.ShowTabApiDiff);

    private void OnToggleTabCrudMatrixMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabCrudMatrix = !s.ShowTabCrudMatrix);

    private void OnToggleTabBlastRadiusGridMenuClicked(object? sender, RoutedEventArgs e) =>
        SettingsService.Instance.Update(s => s.ShowTabBlastRadiusGrid = !s.ShowTabBlastRadiusGrid);

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
                {
                    GitBranchIcon.Data = geo;
                }

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
        {
            GitBranchListBox.ItemsSource = _allVcsBranches;
        }
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
        {
            return;
        }

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
            {
                ShowUpdateModal(update);
            }
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
            {
                ShowUpdateModal(update);
            }
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
        var localizationService = LocalizationService.Instance;

        UpdateModalTitle.Text = localizationService["Update_AvailableTitle"];
        UpdateModalSubtitle.Text = localizationService.Get("Update_AvailableSubtitle", update.VersionString, UpdateService.CurrentVersionString);
        UpdateModalNotesLabel.Text = localizationService["Update_NotesLabel"];
        UpdateModalNotes.Text = update.GetReleaseNotes(localizationService.CurrentLanguage == AppLanguage.Italian ? "it" : "en");
        UpdateLaterButton.Content = localizationService["Update_ButtonLater"];
        UpdateNowButton.Content = localizationService["Update_ButtonNow"];

        UpdateOverlay.IsVisible = true;
    }

    /// <summary>
    /// Chiude il modal di aggiornamento su richiesta dell'utente ("Scarica dopo").
    /// </summary>
    private void DismissUpdateModal()
    {
        UpdateOverlay.IsVisible = false;
    }

    /// <summary>
    /// Gestisce il click sul pulsante "Aggiorna ora", aprendo la pagina di download nel browser.
    /// </summary>
    private void ApplyUpdateAndOpenDownload()
    {
        var localizationService = LocalizationService.Instance;
        StatusText.Text = localizationService["Update_OpeningDownload"];
        UpdateOverlay.IsVisible = false;
        UpdateService.Instance.OpenDownloadPage(_availableUpdate?.DownloadUrl);
    }

    #endregion

    #region Visibilità Pannelli, Scorciatoie da Tastiera & Manutenzione

    private void OnMainWindowKeyDown(object? sender, KeyEventArgs e)
    {
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

    public void ToggleExplorer(bool? forceState = null)
    {
        SetExplorerVisibility(forceState ?? !_isExplorerVisible);
    }

    public void SetExplorerVisibility(bool isVisible, bool updateSettings = true)
    {
        _isExplorerVisible = isVisible;
        if (isVisible)
        {
            var w = _savedExplorerWidth >= 160 ? _savedExplorerWidth : 280;
            CentralGrid.ColumnDefinitions[0].MinWidth = 180;
            CentralGrid.ColumnDefinitions[0].MaxWidth = 600;
            CentralGrid.ColumnDefinitions[0].Width = new GridLength(w, GridUnitType.Pixel);
            CentralGrid.ColumnDefinitions[1].Width = new GridLength(4, GridUnitType.Pixel);
            ExplorerBorder.IsVisible = true;
            LeftGridSplitter.IsVisible = true;
        }
        else
        {
            if (ExplorerBorder.Bounds.Width >= 160)
            {
                _savedExplorerWidth = ExplorerBorder.Bounds.Width;
            }
            CentralGrid.ColumnDefinitions[0].MinWidth = 0;
            CentralGrid.ColumnDefinitions[0].MaxWidth = 0;
            CentralGrid.ColumnDefinitions[0].Width = new GridLength(0, GridUnitType.Pixel);
            CentralGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
            ExplorerBorder.IsVisible = false;
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

    public void ToggleInspector(bool? forceState = null)
    {
        SetInspectorVisibility(forceState ?? !_isInspectorVisible);
    }

    public void SetInspectorVisibility(bool isVisible, bool updateSettings = true)
    {
        _isInspectorVisible = isVisible;
        if (isVisible)
        {
            var w = _savedInspectorWidth >= 200 ? _savedInspectorWidth : 360;
            CentralGrid.ColumnDefinitions[4].MinWidth = 240;
            CentralGrid.ColumnDefinitions[4].MaxWidth = 800;
            CentralGrid.ColumnDefinitions[4].Width = new GridLength(w, GridUnitType.Pixel);
            CentralGrid.ColumnDefinitions[3].Width = new GridLength(4, GridUnitType.Pixel);
            InspectorBorder.IsVisible = true;
            RightGridSplitter.IsVisible = true;
        }
        else
        {
            if (InspectorBorder.Bounds.Width >= 200)
            {
                _savedInspectorWidth = InspectorBorder.Bounds.Width;
            }
            CentralGrid.ColumnDefinitions[4].MinWidth = 0;
            CentralGrid.ColumnDefinitions[4].MaxWidth = 0;
            CentralGrid.ColumnDefinitions[4].Width = new GridLength(0, GridUnitType.Pixel);
            CentralGrid.ColumnDefinitions[3].Width = new GridLength(0, GridUnitType.Pixel);
            InspectorBorder.IsVisible = false;
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

    private void UpdatePanelToggleVisuals()
    {
        ToggleExplorerTopButton.Classes.Set("active", _isExplorerVisible);
        ToggleInspectorTopButton.Classes.Set("active", _isInspectorVisible);
    }

    /// <summary>
    /// Svuota completamente la cache locale dei progetti (%APPDATA%\Macondo\cache\) e invalida le query in memoria.
    /// </summary>
    private void ResetSettingsToDefaults()
    {
        var defaultSettings = SettingsService.Instance.Reset();
        ThemeService.Instance.ApplyTheme(defaultSettings.Theme);
        LocalizationService.Instance.SetLanguage(defaultSettings.Language);
        SetLayoutMode(defaultSettings.PreferredLayout);
        SetExplorerVisibility(true);
        SetInspectorVisibility(true);
        UpdateRecentProjectsMenu();
        UpdateWelcomeView();
        StatusText.Text = LocalizationService.Instance["Status_SettingsReset"];
    }

    private void ClearProjectCache()
    {
        var count = ProjectCacheService.Instance.ClearAllCache();
        _graph?.Queries?.ClearCache();
        StatusText.Text = LocalizationService.Instance.Get("Status_CacheCleared", count);
        UpdateWelcomeView();
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
        WelcomePanel.IsVisible = true;
        ExplorerTree.ItemsSource = null;
        ShowEmptyInspector();
        UpdateStatsBar();
        UpdateWelcomeView(pendingFolder);
    }

    /// <summary>
    /// Aggiorna i contenuti dinamici della schermata di benvenuto (card di progetto in sospeso e lista recenti con badge cache).
    /// </summary>
    private void UpdateWelcomeView(string? pendingFolder = null)
    {
        var localizationService = LocalizationService.Instance;
        var settings = SettingsService.Instance.Settings;

        // 1. Gestione card progetto recente in sospeso (senza cache)
        var targetPending = pendingFolder ?? (_graph == null && !string.IsNullOrEmpty(settings.LastOpenedFolder) && Directory.Exists(settings.LastOpenedFolder) && !ProjectCacheService.Instance.HasCache(settings.LastOpenedFolder) ? settings.LastOpenedFolder : null);

        if (!string.IsNullOrEmpty(targetPending) && Directory.Exists(targetPending))
        {
            WelcomePendingCard.IsVisible = true;
            WelcomePendingPathText.Text = targetPending;
            WelcomePendingPathText.Tag = targetPending;
            WelcomePendingTitleText.Text = localizationService.Get("Welcome_PendingTitle");
            WelcomePendingBadgeText.Text = localizationService.Get("Welcome_CacheNeeded");
            WelcomePendingDescText.Text = localizationService.Get("Welcome_PendingNoCache");
            WelcomePendingAnalyzeButtonText.Text = localizationService.Get("Welcome_AnalyzeNow");
        }
        else
        {
            WelcomePendingCard.IsVisible = false;
        }

        // 2. Popolamento lista progetti recenti con calcolo dinamico dello stato cache
        var recentItems = new List<WelcomeRecentItem>();
        foreach (var recent in settings.RecentProjects)
        {
            var isFolder = recent.Paths.Count == 1 && Directory.Exists(recent.Paths[0]);
            var isCached = isFolder && ProjectCacheService.Instance.HasCache(recent.Paths[0]);

            var pathSummary = recent.Paths.Count == 1
                ? recent.Paths[0]
                : string.Join(", ", recent.Paths);

            var badgeText = isCached ? localizationService["Welcome_CacheReady"] : localizationService["Welcome_CacheNeeded"];
            var badgeBg = isCached ? "#1B4D3E" : "#3D2A10";
            var badgeFg = isCached ? "#10B981" : "#FFB74D";
            var badgeTooltip = isCached ? localizationService["Welcome_CacheReadyTooltip"] : localizationService["Welcome_CacheNeededTooltip"];

            recentItems.Add(new WelcomeRecentItem
            {
                DisplayName = recent.DisplayName,
                PathSummary = pathSummary,
                LastOpenedText = recent.LastOpenedUtc.ToLocalTime().ToString("g"),
                Paths = recent.Paths,
                IsFolder = isFolder,
                IsCached = isCached,
                CacheBadgeText = badgeText,
                CacheBadgeBackground = badgeBg,
                CacheBadgeForeground = badgeFg,
                CacheBadgeTooltip = badgeTooltip
            });
        }

        WelcomeRecentItemsList.ItemsSource = recentItems;
        WelcomeNoRecentText.IsVisible = recentItems.Count == 0;
    }

    private async void OnWelcomePendingAnalyzeClicked(object? sender, RoutedEventArgs e)
    {
        if (WelcomePendingPathText.Tag is string path && !string.IsNullOrEmpty(path) && Directory.Exists(path))
        {
            await AnalyzeAndLoadFolderAsync(path, forceReanalyze: true);
        }
    }

    private async void OnWelcomeOpenProjectFilesClicked(object? sender, PointerPressedEventArgs e)
    {
        await OpenProjectFilesAsync();
    }

    private async void OnWelcomeOpenFolderClicked(object? sender, PointerPressedEventArgs e)
    {
        await OpenFolderAsync();
    }

    private void OnWelcomeLoadFixturesClicked(object? sender, PointerPressedEventArgs e)
    {
        LoadDefaultFixtures();
    }

    private async void OnWelcomeRecentItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is WelcomeRecentItem item && item.Paths.Count > 0)
        {
            await AnalyzeAndLoadPathsAsync(item.Paths);
        }
    }

    #endregion

    #region Localizzazione & Aggiornamento UI

    private void UpdateLocalizedUI()
    {
        var localizationService = LocalizationService.Instance;

        // Schermata di Benvenuto
        WelcomeHeaderTitle.Text = localizationService["Welcome_Title"];
        WelcomeHeaderSub.Text = localizationService["Welcome_Subtitle"];
        WelcomeQuickStartTitle.Text = localizationService["Welcome_QuickStart"];
        WelcomeOpenProjectTitle.Text = localizationService["Welcome_OpenProject"];
        WelcomeOpenProjectSub.Text = localizationService["Welcome_OpenProjectDesc"];
        WelcomeOpenFolderTitle.Text = localizationService["Welcome_OpenFolder"];
        WelcomeOpenFolderSub.Text = localizationService["Welcome_OpenFolderDesc"];
        WelcomeLoadFixturesTitle.Text = localizationService["Welcome_LoadFixtures"];
        WelcomeLoadFixturesSub.Text = localizationService["Welcome_LoadFixturesDesc"];
        WelcomePendingTitleText.Text = localizationService["Welcome_PendingTitle"];
        WelcomePendingBadgeText.Text = localizationService["Welcome_CacheNeeded"];
        WelcomePendingDescText.Text = localizationService["Welcome_PendingNoCache"];
        WelcomePendingAnalyzeButtonText.Text = localizationService["Welcome_AnalyzeNow"];
        WelcomeRecentTitle.Text = localizationService["Welcome_RecentProjects"];
        WelcomeNoRecentText.Text = localizationService["Welcome_NoRecent"];
        UpdateWelcomeView();

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

        // Grid Toolbar Tooltips
        ToolTip.SetTip(RefreshGridButton, localizationService["Tooltip_RecalculateAudit"]);

        // Ricerca
        SearchBox.Watermark = localizationService["Search_Watermark"];

        // Explorer & Inspector & Context Menus
        ExplorerTitleText.Text = localizationService["Explorer_Title"];
        InspectorTitleText.Text = localizationService["Inspector_Title"];
        CtxExplorerOpenFile.Header = localizationService["Explorer_OpenOriginalFile"];
        CtxExplorerOpenFolder.Header = localizationService["Explorer_OpenContainingFolder"];
        CtxExplorerCopyPath.Header = localizationService["Explorer_CopyFilePath"];
        CtxNodeExportMarkdown.Header = localizationService["Ctx_Node_ExportMarkdown"];
        CtxNodeExportJson.Header = localizationService["Ctx_Node_ExportJson"];
        CtxNodeBlastRadius.Header = localizationService["Ctx_Node_BlastRadius"];
        CtxNodeShowCallers.Header = localizationService["Ctx_Node_ShowCallers"];
        CtxNodeShowCallees.Header = localizationService["Ctx_Node_ShowCallees"];
        ToolTip.SetTip(InspectorOpenFileButton, localizationService["Inspector_Tooltip_OpenFile"]);
        ToolTip.SetTip(InspectorOpenFolderButton, localizationService["Inspector_Tooltip_OpenFolder"]);
        if (_graph is not null)
        {
            ExplorerTree.ItemsSource = new[] { ExplorerTreeItem.BuildTree(_graph, _currentFolderPath, Graph.HiddenProjectIds) };
        }

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

        Dsm.InvalidateVisual();
        Treemap.InvalidateVisual();
        Hotspot.InvalidateVisual();
        ScreenFlow.InvalidateVisual();
        ControlFlow.InvalidateVisual();
        MartinMetrics.InvalidateVisual();
        ArchitecturalFlow.InvalidateVisual();
        CodeCity.InvalidateVisual();

        // Project Explorer Toolbar
        ExplorerProjectsSelectAllButton.Content = localizationService["HUD_SelectAll"];
        UpdateProjectsBadgeText();

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

        // Grid Toolbar
        GridFilterLabel.Text = localizationService["Grid_FilterLabel"];
        GridFilterTextBox.Watermark = localizationService["Grid_FilterWatermark"];
        GridRiskLabel.Text = localizationService["Grid_RiskLabel"];
        RiskItemAll.Content = localizationService["Grid_AllRisks"];
        RiskItemCritical.Content = localizationService["Risk_Critical"];
        RiskItemHigh.Content = localizationService["Risk_High"];
        RiskItemMedium.Content = localizationService["Risk_Medium"];
        RiskItemLow.Content = localizationService["Risk_Low"];
        RefreshGridButtonText.Text = localizationService["Grid_Recalculate"];

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

        // DataGrid Colonne
        if (BlastRadiusDataGrid?.Columns.Count >= 10)
        {
            BlastRadiusDataGrid.Columns[0].Header = localizationService["Col_Project"];
            BlastRadiusDataGrid.Columns[1].Header = localizationService["Col_Routine"];
            BlastRadiusDataGrid.Columns[2].Header = localizationService["Col_Module"];
            BlastRadiusDataGrid.Columns[3].Header = localizationService["Col_DirectCallers"];
            BlastRadiusDataGrid.Columns[4].Header = localizationService["Col_IndirectCallers"];
            BlastRadiusDataGrid.Columns[5].Header = localizationService["Col_Depth"];
            BlastRadiusDataGrid.Columns[6].Header = localizationService["Col_Risk"];
            BlastRadiusDataGrid.Columns[7].Header = localizationService["Col_Forms"];
            BlastRadiusDataGrid.Columns[8].Header = localizationService["Col_TotalImpact"];
            BlastRadiusDataGrid.Columns[9].Header = localizationService["Col_Action"];
        }

        // Inspector
        InspectorTitleText.Text = localizationService["Inspector_Title"];
        InspectorEmptyTitle.Text = localizationService["Inspector_NoSelection"];
        InspectorEmptyHint1.Text = localizationService["Inspector_Hint1"];
        InspectorEmptyHint2.Text = localizationService["Inspector_Hint2"];
        InspectorEmptyHint3.Text = localizationService["Inspector_Hint3"];
        InspectorSecSymbol.Text = localizationService["Inspector_Sec_Symbol"];
        InspectorSecKind.Text = localizationService["Inspector_Sec_Kind"];
        InspectorSecVisibility.Text = localizationService["Inspector_Sec_Visibility"];
        InspectorSecLanguage.Text = localizationService["Inspector_Sec_Language"];
        InspectorSecType.Text = localizationService["Inspector_Sec_Type"];
        InspectorSecParams.Text = localizationService["Inspector_Sec_Params"];
        InspectorSecFile.Text = localizationService["Inspector_Sec_File"];
        InspectorSecAiContext.Text = localizationService["Inspector_Sec_AIContext"];
        InspectorAiContextDesc.Text = localizationService["Inspector_AiContextDesc"];
        CopyLlmSliceButtonText.Text = localizationService["Inspector_CopyLlmSlice"];
        SaveLlmSliceButtonText.Text = localizationService["Inspector_SaveLlmSlice"];
        ToolTip.SetTip(CopyLlmSliceButton, localizationService["Tooltip_CopyLlmSlice"]);
        ToolTip.SetTip(SaveLlmSliceButton, localizationService["Tooltip_SaveLlmSlice"]);
        InspectorSecBlast.Text = localizationService["Inspector_Sec_BlastRadius"];
        InspectorLabelDirectCallers.Text = localizationService["Inspector_DirectCallersCount"];
        InspectorLabelIndirectCallers.Text = localizationService["Inspector_IndirectCallersCount"];
        InspectorLabelMaxDepth.Text = localizationService["Inspector_MaxDepth"];
        InspectorSecForms.Text = localizationService["Inspector_Sec_Forms"];
        InspectorSecClasses.Text = localizationService["Inspector_Sec_Classes"];
        InspectorSecPaths.Text = localizationService["Inspector_Sec_Paths"];
        InspectorSecCallers.Text = localizationService["Inspector_Sec_WhoCallsMe"];
        InspectorSecCallees.Text = localizationService["Inspector_Sec_WhatDoICall"];
        InspectorSecInstantiates.Text = localizationService["Inspector_Sec_Instantiates"];

        // Modal Notifica Aggiornamento (se visibile)
        if (UpdateOverlay.IsVisible && _availableUpdate != null)
        {
            UpdateModalTitle.Text = localizationService["Update_AvailableTitle"];
            UpdateModalSubtitle.Text = localizationService.Get("Update_AvailableSubtitle", _availableUpdate.VersionString, UpdateService.CurrentVersionString);
            UpdateModalNotesLabel.Text = localizationService["Update_NotesLabel"];
            UpdateModalNotes.Text = _availableUpdate.GetReleaseNotes(localizationService.CurrentLanguage == AppLanguage.Italian ? "it" : "en");
            UpdateLaterButton.Content = localizationService["Update_ButtonLater"];
            UpdateNowButton.Content = localizationService["Update_ButtonNow"];
        }

        // Overlay Conferma Esportazione (se visibile)
        if (ExportSuccessOverlay.IsVisible)
        {
            ExportSuccessModalTitle.Text = localizationService["Export_ModalTitle"];
            ExportSuccessModalSubtitle.Text = localizationService["Export_ModalSubtitle"];
            ExportSuccessFileLabel.Text = localizationService["Export_ModalFileLabel"];
            ExportSuccessPathLabel.Text = localizationService["Export_ModalPathLabel"];
            ExportSuccessOpenFolderButton.Content = localizationService["Export_ModalOpenFolder"];
            ExportSuccessCloseButton.Content = localizationService["Export_ModalOk"];
        }

        // Overlay Caricamento
        LoadingCancelButton.Content = localizationService["Loading_Cancel"];

        // Overlay Conferma Cambio Layout
        LayoutConfirmModalTitle.Text = localizationService["Layout_ConfirmTitle"];
        LayoutConfirmModalSubtitle.Text = localizationService["Layout_ConfirmMessage"];
        LayoutConfirmCancelButton.Content = localizationService["Layout_ConfirmCancel"];
        LayoutConfirmApplyButton.Content = localizationService["Layout_ConfirmApply"];

        // Refresh dynamic strings in Grid and Status
        UpdateStatsBar();
        ApplyGridFilters();
        if (_selectedNode == null)
        {
            StatusText.Text = localizationService["Status_Ready"];
        }
        else
        {
            StatusText.Text = localizationService.Get("Status_Selected", _selectedNode.Kind, _selectedNode.QualifiedName);
            if (_selectedNode.SemanticSymbol is { } sym && _graph?.Queries is { } q && q.TryGetCachedImpactReport(sym, 10, out var cachedReport) && cachedReport != null)
            {
                UpdateInspectorImpactDetails(cachedReport, q.GetDirectCallees(sym), q.GetInstantiatedClasses(sym));
            }
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
