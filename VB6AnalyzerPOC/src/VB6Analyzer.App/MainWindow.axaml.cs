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
using Macondo.Semantic.Analytics.ControlFlow;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.Hotspots;
using Macondo.Semantic.Analytics.ScreenFlow;
using Macondo.Semantic.Analytics.Treemap;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;

namespace Macondo.App;

/// <summary>
/// Finestra principale dell'applicazione VB6 Analyzer (Milestone M5 + M6).
/// Gestisce l'interazione tra Toolbar, Project Explorer (TreeView),
/// Canvas del Grafo (<see cref="Controls.GraphControl"/>), Symbol Inspector e Blast Radius Engine.
/// </summary>
public partial class MainWindow : Window
{
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

        // Pulsanti Dialog Aggiornamento Software
        UpdateLaterButton.Click += (_, _) => DismissUpdateModal();
        UpdateNowButton.Click += (_, _) => ApplyUpdateAndOpenDownload();

        // Menu Bar Superiore - File (Command + Click per massima affidabilità)
        OpenFolderMenuItem.Command = new ActionCommand(async () => await OpenFolderAsync());
        OpenProjectFilesMenuItem.Command = new ActionCommand(async () => await OpenProjectFilesAsync());
        RefreshProjectMenuItem.Command = new ActionCommand(async () => await RefreshCurrentProjectAsync());
        LoadFixturesMenuItem.Command = new ActionCommand(() => LoadDefaultFixtures());
        GenerateFakeMenuItem.Command = new ActionCommand(() => LoadSyntheticGraph());
        ExportMarkdownMenuItem.Command = new ActionCommand(async () => await ExportMarkdownAsync());
        ExportJsonMenuItem.Command = new ActionCommand(async () => await ExportJsonAsync());
        ImportJsonMenuItem.Command = new ActionCommand(async () => await ImportJsonAsync());
        ExportSliceMenuItem.Command = new ActionCommand(async () => await ExportLlmSliceAsync());
        SettingsMenuItem.Command = new ActionCommand(OpenSettings);
        CheckUpdatesMenuItem.Command = new ActionCommand(async () => await CheckForUpdatesManualAsync());
        ResetSettingsMenuItem.Command = new ActionCommand(ResetSettingsToDefaults);
        ClearCacheMenuItem.Command = new ActionCommand(ClearProjectCache);
        ExitMenuItem.Command = new ActionCommand(() => Close());

        OpenFolderMenuItem.Click += async (_, _) => await OpenFolderAsync();
        OpenProjectFilesMenuItem.Click += async (_, _) => await OpenProjectFilesAsync();
        RefreshProjectMenuItem.Click += async (_, _) => await RefreshCurrentProjectAsync();
        LoadFixturesMenuItem.Click += (_, _) => LoadDefaultFixtures();
        GenerateFakeMenuItem.Click += (_, _) => LoadSyntheticGraph();
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
        ResetViewMenuItem.Command = new ActionCommand(() => Graph.ResetView());
        ExpandAllMenuItem.Command = new ActionCommand(() => SetAllExpanded(true));
        CollapseAllMenuItem.Command = new ActionCommand(() => SetAllExpanded(false));
        LayoutStandardMenuItem.Command = new ActionCommand(() => SetLayoutMode(Layout.GraphLayoutMode.StandardRadial));
        LayoutAffinityMenuItem.Command = new ActionCommand(() => SetLayoutMode(Layout.GraphLayoutMode.AffinityRadial));
        LayoutIslandsMenuItem.Command = new ActionCommand(() => SetLayoutMode(Layout.GraphLayoutMode.ClusteredIslands));
        LayoutForceMenuItem.Command = new ActionCommand(() => SetLayoutMode(Layout.GraphLayoutMode.ForceDirected));
        BlastRadiusMenuItem.Command = new ActionCommand(() => OnBlastRadiusClicked());
        ShowCallersMenuItem.Command = new ActionCommand(() => OnShowCallersClicked());
        ShowCalleesMenuItem.Command = new ActionCommand(() => OnShowCalleesClicked());
        ClearHighlightMenuItem.Command = new ActionCommand(() =>
        {
            Graph.ClearHighlightMode();
            StatusText.Text = LocalizationService.Instance["Menu_ClearHighlight"];
        });

        ResetViewMenuItem.Click += (_, _) => Graph.ResetView();
        ExpandAllMenuItem.Click += (_, _) => SetAllExpanded(true);
        CollapseAllMenuItem.Click += (_, _) => SetAllExpanded(false);
        LayoutStandardMenuItem.Click += (_, _) => SetLayoutMode(Layout.GraphLayoutMode.StandardRadial);
        LayoutAffinityMenuItem.Click += (_, _) => SetLayoutMode(Layout.GraphLayoutMode.AffinityRadial);
        LayoutIslandsMenuItem.Click += (_, _) => SetLayoutMode(Layout.GraphLayoutMode.ClusteredIslands);
        LayoutForceMenuItem.Click += (_, _) => SetLayoutMode(Layout.GraphLayoutMode.ForceDirected);
        BlastRadiusMenuItem.Click += (_, _) => OnBlastRadiusClicked();
        ShowCallersMenuItem.Click += (_, _) => OnShowCallersClicked();
        ShowCalleesMenuItem.Click += (_, _) => OnShowCalleesClicked();
        ClearHighlightMenuItem.Click += (_, _) =>
        {
            Graph.ClearHighlightMode();
            StatusText.Text = LocalizationService.Instance["Menu_ClearHighlight"];
        };
                
        // Toolbar Azioni Rapide
        ResetViewButton.Click += (_, _) => Graph.ResetView();
        ExpandAllButton.Click += (_, _) => SetAllExpanded(true);
        CollapseAllButton.Click += (_, _) => SetAllExpanded(false);
        RefreshProjectButton.Click += async (_, _) => await RefreshCurrentProjectAsync();

        BlastRadiusButton.Click += (_, _) => OnBlastRadiusClicked();
        ShowCallersButton.Click += (_, _) => OnShowCallersClicked();
        ShowCalleesButton.Click += (_, _) => OnShowCalleesClicked();
        ClearHighlightButton.Click += (_, _) =>
        {
            Graph.ClearHighlightMode();
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
            if (!string.IsNullOrEmpty(query) && SearchResultsList.ItemsSource is IList<GraphNode> { Count: > 1 })
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
                StatusText.Text = $"DSM: Dipendenza [{cell.Weight} collegamenti] tra container.";
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
                StatusText.Text = $"CFG Blocco: [{block.BlockKind}] {block.Title}";
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

        // Applica i valori salvati prima del rendering iniziale
        ThemeService.Instance.ApplyTheme(settings.Theme);
        LocalizationService.Instance.SetLanguage(settings.Language);
        UpdateLocalizedUI();

        // Ripristino Filtro di Rischio salvato
        RiskFilterComboBox.SelectedIndex = Math.Clamp(settings.DefaultRiskFilterIndex, 0, 4);

        // Caricamento del Progetto: Ultima cartella aperta o Fixture reali di default
        if (!string.IsNullOrEmpty(settings.LastOpenedFolder) && Directory.Exists(settings.LastOpenedFolder))
        {
            _ = AnalyzeAndLoadFolderAsync(settings.LastOpenedFolder);
        }
        else
        {
            LoadDefaultFixtures();
        }

        // Controllo automatico aggiornamenti in background all'avvio
        _ = CheckForUpdatesOnStartupAsync();
    }

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

    /// <summary>
    /// Mostra l'overlay modale di caricamento centrato a tutto schermo con opzioni per la barra di avanzamento e statistiche.
    /// </summary>
    private void ShowLoading(string title, string subtitle, bool isIndeterminate = true, double progressValue = 0, double progressMax = 100, string? countText = null, string? statsText = null, bool canCancel = true)
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
    private void UpdateLoadingProgress(double current, double total, string? subtitle = null, string? countText = null, string? statsText = null)
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

    /// <summary>
    /// Genera e carica un grafo sintetico di ~500 nodi tramite FakeGraphGenerator per benchmark e test.
    /// </summary>
    private async void LoadSyntheticGraph()
    {
        ShowLoading("Generazione del grafo sintetico...", "Creazione di ~500 nodi e calcolo del layout radiale...", canCancel: true);
        var token = _loadingCts?.Token ?? CancellationToken.None;
        try
        {
            await Task.Delay(16, token);
            _currentFolderPath = null;
            UpdateVcsBranchStatus(null);
            StatusText.Text = "Generazione del grafo sintetico (~500 nodi)...";
            _graph = await Task.Run(() => Generation.FakeGraphGenerator.Generate(seed: Environment.TickCount), token);
            token.ThrowIfCancellationRequested();
            ApplyGraphToUI();
            StatusText.Text = "Grafo sintetico di benchmark (~500 nodi) generato con successo.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = LocalizationService.Instance["Status_AnalysisCancelled"];
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore durante la generazione: {ex.Message}";
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
        var loc = LocalizationService.Instance;
        ShowLoading(loc["Loading_FixturesTitle"], loc["Loading_FixturesSub"], isIndeterminate: false, progressValue: 0, progressMax: 100, canCancel: true);
        var token = _loadingCts?.Token ?? CancellationToken.None;

        var progress = new Progress<ProjectAnalysisProgress>(p =>
        {
            var countStr = loc.Get("Loading_FilesProgressFormat", p.CurrentFileIndex, p.TotalFiles, (int)p.Percentage);
            var statsStr = loc.Get("Loading_NodesExtractedFormat", p.ExtractedNodesCount);
            UpdateLoadingProgress(p.CurrentFileIndex, p.TotalFiles, p.PhaseMessage, countStr, statsStr);
        });

        try
        {
            await Task.Delay(16, token);
            _currentFolderPath = null;
            UpdateVcsBranchStatus(null);
            StatusText.Text = loc["Loading_FixturesSub"];
            _graph = await Task.Run(() => ProjectAnalysisService.LoadDefaultFixtures(progress, token), token);
            token.ThrowIfCancellationRequested();
            ApplyGraphToUI();
            StatusText.Text = "Suite di fixture VB6 analizzata con successo in memoria.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = loc["Status_AnalysisCancelled"];
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore durante il caricamento: {ex.Message}";
        }
        finally
        {
            HideLoading();
        }
    }

    /// <summary>
    /// Apre un selettore di cartelle per analizzare uno o più progetti VB6 contemporaneamente.
    /// </summary>
    private async Task OpenFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleziona una o più cartelle contenenti file/progetti VB6",
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
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleziona file progetto VB6 (.vbp) o file gruppo (.vbg)",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Progetti e Gruppi VB6 (*.vbp, *.vbg)")
                {
                    Patterns = new[] { "*.vbp", "*.vbg" }
                },
                new FilePickerFileType("Tutti i file (*.*)")
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
                ? "No project open to reload. Use 'Open VB6 Folder...' (Ctrl+O)."
                : "Nessun progetto aperto da ricaricare. Usa 'Apri Cartella VB6...' (Ctrl+O).";
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

        var loc = LocalizationService.Instance;
        var firstPath = paths[0];
        var titleName = paths.Count == 1 
            ? Path.GetFileName(firstPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) 
            : $"Workspace ({paths.Count} percorsi)";

        // Mostra subito l'overlay di caricamento per dare feedback immediato all'utente
        ShowLoading(loc["Loading_AnalyzeProjectTitle"], loc.Get("Loading_AnalyzeProjectSub", titleName), canCancel: true);
        var token = _loadingCts?.Token ?? CancellationToken.None;

        try
        {
            await Task.Delay(16, token);

            // 1. Tenta il ripristino ultra-rapido dalla cache solo se singola cartella e non forzato
            if (!forceReanalyze && paths.Count == 1 && Directory.Exists(firstPath))
            {
                var cachedSnapshot = await ProjectCacheService.Instance.TryLoadSnapshotAsync(firstPath);
                token.ThrowIfCancellationRequested();
                if (cachedSnapshot != null)
                {
                    var (cachedGraph, cachedRows) = await Task.Run(() => ProjectCacheService.Instance.RestoreGraphAndRows(cachedSnapshot), token);
                    token.ThrowIfCancellationRequested();
                    _graph = cachedGraph;
                    _currentFolderPath = firstPath;
                    _currentPaths = paths.ToList();
                    UpdateVcsBranchStatus(firstPath);
                    ApplyGraphToUI(cachedRows);

                    // Salva l'ultima cartella aperta nelle impostazioni
                    SettingsService.Instance.Update(s => s.LastOpenedFolder = firstPath);

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
                var countStr = loc.Get("Loading_FilesProgressFormat", p.CurrentFileIndex, p.TotalFiles, (int)p.Percentage);
                var statsStr = loc.Get("Loading_NodesExtractedFormat", p.ExtractedNodesCount);
                UpdateLoadingProgress(p.CurrentFileIndex, p.TotalFiles, p.PhaseMessage, countStr, statsStr);
            });

            StatusText.Text = $"Analisi di {paths.Count} sorgenti/progetti in corso...";
            _graph = await Task.Run(() => ProjectAnalysisService.AnalyzePaths(paths, progress, token), token);
            token.ThrowIfCancellationRequested();
            _currentFolderPath = firstPath;
            _currentPaths = paths.ToList();
            if (Directory.Exists(firstPath)) UpdateVcsBranchStatus(firstPath);
            ApplyGraphToUI(); // Avvierà PopulateBlastRadiusGridAsync che poi salverà la cache aggiornata

            var projectCount = _graph.Nodes.Count(n => n.Kind == SymbolKind.Project);
            var crossCount = _graph.Edges.Count(e => e.IsCrossProject);
            if (projectCount > 1)
            {
                StatusText.Text = $"Workspace analizzato: {projectCount} Progetti, {_graph.Nodes.Count} Simboli, {crossCount} Relazioni Cross-Project.";
            }
            else
            {
                StatusText.Text = loc.Get("Status_Refreshed", titleName, _graph.Nodes.Count);
            }

            // Salva l'ultima cartella aperta nelle impostazioni se singola cartella
            if (paths.Count == 1 && Directory.Exists(firstPath))
            {
                SettingsService.Instance.Update(s => s.LastOpenedFolder = firstPath);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = loc["Status_AnalysisCancelled"];
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore durante l'analisi: {ex.Message}";
        }
        finally
        {
            HideLoading();
        }
    }

    /// <summary>
    /// Aggiorna tutti i componenti della UI (Grafo, Explorer, Status) con il nuovo CodeGraph.
    /// Se precalculatedRows è fornito (es. da cache), la griglia Blast Radius viene popolata istantaneamente.
    /// </summary>
    private void ApplyGraphToUI(List<BlastRadiusGridRow>? precalculatedRows = null)
    {
        if (_graph is null) return;

        Layout.GraphLayoutManager.ApplyLayout(_graph, _currentLayoutMode);
        Graph.Graph = _graph;
        Graph.ResetView();
        Graph.SelectNode(null);

        ExplorerTree.ItemsSource = new[] { ExplorerTreeItem.BuildTree(_graph, _currentFolderPath) };

        ShowEmptyInspector();
        UpdateStatsBar();
        SyncFilterUiState();

        if (_graph.SemanticGraph != null)
        {
            var semGraph = _graph.SemanticGraph;
            Dsm.Matrix = DsmMatrixBuilder.Build(semGraph);
            Treemap.RootItem = TreemapModelBuilder.Build(semGraph);
            Hotspot.Dataset = HotspotAnalyzer.Analyze(semGraph);
            ScreenFlow.Graph = ScreenFlowBuilder.Build(semGraph);
        }

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

    #region Gestione Filtri Livelli e Categorie Simboli

    private void InitFilterControls()
    {
        var settings = SettingsService.Instance.Settings;
        Graph.ShowMethods = settings.ShowMethods;
        Graph.ShowProperties = settings.ShowProperties;
        Graph.ShowFields = settings.ShowFields;
        Graph.ShowConstants = settings.ShowConstants;
        Graph.ShowTypesAndEnums = settings.ShowTypesAndEnums;

        Graph.FilterChanged += (_, _) => SyncFilterUiState();

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
                    StatusText.Text = $"Nodi visualizzati: {vis} di {total}";
                }
            }
        }
        finally
        {
            _isSyncingFilterUi = false;
        }
    }

    #endregion

    /// <summary>
    /// Calcola ed evidenzia il Blast Radius per il nodo selezionato in modo asincrono con loading overlay.
    /// Se il risultato è già in cache, viene applicato all'istante senza mostrare l'overlay.
    /// </summary>
    private async void OnBlastRadiusClicked()
    {
        var loc = LocalizationService.Instance;
        if (_selectedNode?.SemanticSymbol is null || _graph?.Queries is null)
        {
            StatusText.Text = loc["Status_SelectSymbolForBlast"];
            return;
        }

        var node = _selectedNode;
        var sym = node.SemanticSymbol;

        // Se già calcolato e presente in cache, applica all'istante (0 ms)
        if (_graph.Queries.TryGetCachedImpactReport(sym, 10, out var cachedReport) && cachedReport != null)
        {
            Graph.HighlightBlastRadius(cachedReport);
            UpdateInspectorImpactSection(cachedReport);
            StatusText.Text = loc.Get("Status_BlastReportFormat", node.QualifiedName, cachedReport.DirectCallers.Count, cachedReport.IndirectCallers.Count, cachedReport.AffectedForms.Count, loc.GetRiskLevel(cachedReport.RiskLevel));
            return;
        }

        ShowLoading(loc["Loading_BlastRadiusTitle"], loc.Get("Loading_BlastRadiusSub", node.Name));
        await Task.Delay(16);

        try
        {
            var report = await Task.Run(() => _graph.Queries.GetImpactReport(sym));

            if (_selectedNode?.Id == node.Id)
            {
                Graph.HighlightBlastRadius(report);
                UpdateInspectorImpactSection(report);
                StatusText.Text = loc.Get("Status_BlastReportFormat", node.QualifiedName, report.DirectCallers.Count, report.IndirectCallers.Count, report.AffectedForms.Count, loc.GetRiskLevel(report.RiskLevel));
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = loc.Get("Status_BlastError", ex.Message);
        }
        finally
        {
            HideLoading();
        }
    }

    /// <summary>
    /// Evidenzia sul grafo tutte le routine e classi chiamate dalla routine/container selezionato (callees).
    /// </summary>
    private void OnShowCalleesClicked()
    {
        var loc = LocalizationService.Instance;
        if (_selectedNode is null)
        {
            StatusText.Text = loc["Status_SelectSymbolForCallees"];
            return;
        }

        var callees = Graph.HighlightCalleesForSelected();
        if (callees.Count > 0)
        {
            var names = string.Join(", ", callees.Take(4).Select(c => c.Name));
            if (callees.Count > 4) names += $" e altri {callees.Count - 4}...";
            StatusText.Text = $"Callees per '{_selectedNode.Name}': trovate {callees.Count} chiamate uscenti verso ({names}).";
        }
        else
        {
            StatusText.Text = $"Nessuna chiamata uscente effettuata da '{_selectedNode.Name}'.";
        }
    }

    /// <summary>
    /// Evidenzia sul grafo tutti i chiamanti della routine/container selezionato (callers).
    /// </summary>
    private void OnShowCallersClicked()
    {
        var loc = LocalizationService.Instance;
        if (_selectedNode is null)
        {
            StatusText.Text = loc["Status_SelectSymbolForCallers"];
            return;
        }

        var callers = Graph.HighlightCallersForSelected();
        if (callers.Count > 0)
        {
            var names = string.Join(", ", callers.Take(4).Select(c => c.Name));
            if (callers.Count > 4) names += $" e altri {callers.Count - 4}...";
            StatusText.Text = $"Callers per '{_selectedNode.Name}': trovati {callers.Count} chiamanti diretti da ({names}).";
        }
        else
        {
            StatusText.Text = $"Nessun chiamante diretto per '{_selectedNode.Name}'.";
        }
    }

    private void OnGraphSelectionChanged(object? sender, GraphNode? node)
    {
        _selectedNode = node;

        if (!_isSyncingSelection && CentralTabControl?.SelectedIndex == 1 && node != null)
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
        StatusText.Text = $"Selezionato: [{node.Kind}] {node.QualifiedName}";
    }

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

    private void OpenOriginalFile(string? filePath, int line = 0)
    {
        var loc = LocalizationService.Instance;
        var resolved = ResolveEffectiveFilePath(filePath);

        if (string.IsNullOrEmpty(resolved) || (!File.Exists(resolved) && !Directory.Exists(resolved)))
        {
            StatusText.Text = loc.Get("Explorer_FileNotFound", filePath ?? loc["Explorer_Title"]);
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
            StatusText.Text = loc.Get("Explorer_FileOpened", Path.GetFileName(resolved));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore nell'apertura del file: {ex.Message}";
        }
    }

    private void OpenContainingFolder(string? filePath)
    {
        var loc = LocalizationService.Instance;
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
                StatusText.Text = loc.Get("Explorer_FolderOpened", Path.GetFileName(resolved));
            }
            else if (Directory.Exists(resolved))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = resolved,
                    UseShellExecute = true
                });
                StatusText.Text = loc.Get("Explorer_FolderOpened", resolved);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore nell'apertura della cartella: {ex.Message}";
        }
    }

    private async void CopyFilePathToClipboard(string? filePath)
    {
        var loc = LocalizationService.Instance;
        var resolved = ResolveEffectiveFilePath(filePath);
        if (string.IsNullOrEmpty(resolved)) return;

        try
        {
            if (Clipboard != null)
            {
                await Clipboard.SetTextAsync(resolved);
                StatusText.Text = loc.Get("Explorer_PathCopied", resolved);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore copia percorso: {ex.Message}";
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

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query) || _graph is null)
        {
            SearchResultsPopup.IsOpen = false;
            SearchResultsList.ItemsSource = null;
            return;
        }

        // Cerca tutti i nodi corrispondenti per nome o nome qualificato (escludendo il root di progetto sintetico)
        var matches = _graph.Nodes
            .Where(n => n.Kind != SymbolKind.Project &&
                       (n.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        n.QualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(n => n.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(n => n.Name)
            .Take(30)
            .ToList();

        if (matches.Count == 0)
        {
            SearchResultsHeader.Text = LocalizationService.Instance.Get("Search_NoMatches", query);
            SearchResultsList.ItemsSource = null;
            SearchResultsPopup.IsOpen = true;
            StatusText.Text = LocalizationService.Instance.Get("Search_NoMatches", query);
        }
        else if (matches.Count == 1)
        {
            // Un solo risultato: chiudi la lista e seleziona direttamente il nodo
            SearchResultsPopup.IsOpen = false;
            SearchResultsList.ItemsSource = matches;
            SelectAndFocusGraphNode(matches[0]);
            StatusText.Text = LocalizationService.Instance.Get("Search_SingleMatch", matches[0].Kind, matches[0].QualifiedName);
        }
        else
        {
            // Più di un risultato: mostra la lista dei risultati da cui selezionare
            SearchResultsHeader.Text = LocalizationService.Instance.Get("Search_MultipleMatches", matches.Count);
            SearchResultsList.ItemsSource = matches;
            SearchResultsPopup.IsOpen = true;
            StatusText.Text = LocalizationService.Instance.Get("Search_MultipleMatches", matches.Count);
        }
    }

    private void OnSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is GraphNode selectedNode)
        {
            SelectAndFocusGraphNode(selectedNode);
            SearchResultsPopup.IsOpen = false;
            Dispatcher.UIThread.Post(() => SearchResultsList.SelectedItem = null);
        }
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (SearchResultsList.SelectedItem is GraphNode selected)
            {
                SelectAndFocusGraphNode(selected);
                SearchResultsPopup.IsOpen = false;
                e.Handled = true;
            }
            else if (SearchResultsList.ItemsSource is IList<GraphNode> list && list.Count > 0)
            {
                SelectAndFocusGraphNode(list[0]);
                SearchResultsPopup.IsOpen = false;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Down)
        {
            if (SearchResultsPopup.IsOpen && SearchResultsList.ItemsSource is IList<GraphNode> list && list.Count > 0)
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
            if (SearchResultsList.SelectedItem is GraphNode selected)
            {
                SelectAndFocusGraphNode(selected);
                SearchResultsPopup.IsOpen = false;
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

    private void ShowEmptyInspector()
    {
        InspectorEmptyPanel.IsVisible = true;
        InspectorDetailsPanel.IsVisible = false;
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
        var loc = LocalizationService.Instance;
        RiskLevelText.Text = loc["Inspector_Calculating"];
        RiskBadge.Background = new SolidColorBrush(Color.Parse("#FF6633"));
        BlastDirectCallersCount.Text = "...";
        BlastIndirectCallersCount.Text = "...";
        BlastMaxDepth.Text = "...";
        InspectorCallers.Text = loc["Inspector_CalculatingCallers"];
        InspectorCallees.Text = loc["Inspector_CalculatingCallees"];
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
                        ShowLoading(loc["Loading_NodeAnalysisTitle"], loc.Get("Loading_NodeAnalysisSub", node.Name));
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
                StatusText.Text = loc.Get("Status_NodeAnalysisError", ex.Message);
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
        var loc = LocalizationService.Instance;
        InspectorCallers.Text = loc["Inspector_NoItems"];
        InspectorCallees.Text = loc["Inspector_NoItems"];
        InspectorInstantiatesSection.IsVisible = false;
        BlastFormsSection.IsVisible = false;
        BlastClassesSection.IsVisible = false;
        BlastPathsSection.IsVisible = false;
    }

    private void UpdateInspectorImpactSection(ImpactReport impact)
    {
        var loc = LocalizationService.Instance;

        // Risk Badge
        RiskLevelText.Text = loc.Get("Risk_Format", loc.GetRiskLevel(impact.RiskLevel));
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

    private void UpdateInspectorImpactDetails(ImpactReport impact, IReadOnlyList<Symbol> callees, IReadOnlyList<Symbol> instantiations)
    {
        UpdateInspectorImpactSection(impact);

        var loc = LocalizationService.Instance;

        // Callers diretti standard
        InspectorCallers.Text = impact.DirectCallers.Count > 0
            ? string.Join("\n", impact.DirectCallers.Select(c => $"• {c.QualifiedName}"))
            : loc["Inspector_NoCallers"];

        // Callees diretti
        InspectorCallees.Text = callees.Count > 0
            ? string.Join("\n", callees.Select(c => $"• {c.QualifiedName}"))
            : loc["Inspector_NoCallees"];

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

    // =========================================================================
    // VISTA GRIGLIA BLAST RADIUS (Milestone M6 - Tab Risultati Tabulari)
    // =========================================================================

    private void OnCentralTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source == CentralTabControl && CentralTabControl.SelectedIndex == 1 && _selectedNode != null)
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

        if (GridStatsTextBlock != null)
            GridStatsTextBlock.Text = LocalizationService.Instance.Get("Grid_Calculating", routineNodes.Count);

        try
        {
            var rows = await Task.Run(() =>
            {
                var result = new List<BlastRadiusGridRow>(routineNodes.Count);
                foreach (var node in routineNodes)
                {
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
                _ = ProjectCacheService.Instance.SaveSnapshotAsync(_currentFolderPath, _graph, _allBlastRadiusRows);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore durante il calcolo della griglia Blast Radius: {ex.Message}";
        }
    }

    private void ApplyGridFilters()
    {
        if (BlastRadiusDataGrid == null || GridStatsTextBlock == null) return;

        var filterText = GridFilterTextBox?.Text?.Trim() ?? string.Empty;
        var selectedRiskIndex = RiskFilterComboBox?.SelectedIndex ?? 0;

        IEnumerable<BlastRadiusGridRow> query = _allBlastRadiusRows;

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

        var projectCount = _graph.Nodes.Count(n => n.Kind == SymbolKind.Project);
        var nodeCount = _graph.Nodes.Count;
        var relCount = _graph.Edges.Count;
        var callCount = _graph.Edges.Count(e => e.Kind == RelationshipKind.Calls);
        var instCount = _graph.Edges.Count(e => e.Kind == RelationshipKind.Instantiates);
        var crossCount = _graph.Edges.Count(e => e.IsCrossProject);

        if (projectCount > 1)
        {
            StatusStats.Text = $"{projectCount} Progetti | {nodeCount} Nodi | {relCount} Relazioni ({crossCount} Cross-Project) | {callCount} Chiamate | {instCount} Istanze";
        }
        else
        {
            StatusStats.Text = LocalizationService.Instance.Get("Status_StatsFormat", nodeCount, relCount, callCount, instCount);
        }
    }

    private void OpenSettings()
    {
        var settingsWin = new SettingsWindow();
        settingsWin.ShowDialog(this);
    }

    private void OpenInfo()
    {
        var infoWin = new Views.InfoWindow();
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
    }

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

        try
        {
            var markdown = semGraph.ToMarkdown();
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            await writer.WriteAsync(markdown);

            StatusText.Text = LocalizationService.Instance.Get("Status_ExportSuccess", file.Name);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore durante l'esportazione Markdown: {ex.Message}";
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

        try
        {
            var json = semGraph.ToJson(indented: true);
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            await writer.WriteAsync(json);

            StatusText.Text = LocalizationService.Instance.Get("Status_ExportSuccess", file.Name);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore durante l'esportazione JSON: {ex.Message}";
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

            var importedGraph = await Task.Run(() => Macondo.Semantic.Model.CodeGraph.FromJson(json));
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
            StatusText.Text = $"Errore durante l'importazione del JSON: {ex.Message}";
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
            var slice = semGraph.ToMarkdownSlice(symbol);
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(slice);
                StatusText.Text = LocalizationService.Instance.Get("Status_CopiedLlmSlice", symbol.Name);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore durante la copia negli appunti: {ex.Message}";
        }
    }

    /// <summary>
    /// Salva su file Markdown (.md) il Context Slice per il simbolo selezionato.
    /// </summary>
    private async Task ExportLlmSliceAsync()
    {
        var semGraph = _graph?.SemanticGraph;
        var symbol = _selectedNode?.SemanticSymbol;

        if (semGraph == null || symbol == null)
        {
            StatusText.Text = LocalizationService.Instance["Status_NoSymbolSelected"];
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Instance["Menu_ExportSlice"],
            DefaultExtension = "md",
            SuggestedFileName = $"{symbol.QualifiedName}_slice.md",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Markdown Context Slice (*.md)") { Patterns = new[] { "*.md" } }
            }
        });

        if (file == null) return;

        try
        {
            var slice = semGraph.ToMarkdownSlice(symbol);
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            await writer.WriteAsync(slice);

            StatusText.Text = LocalizationService.Instance.Get("Status_ExportSuccess", file.Name);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore durante il salvataggio dello slice: {ex.Message}";
        }
    }

    /// <summary>
    /// Imposta la modalità di layout attiva, ricalcola le coordinate di tutti i nodi e aggiorna il canvas del grafo.
    /// </summary>
    private void SetLayoutMode(Layout.GraphLayoutMode mode, bool resetView = true)
    {
        _currentLayoutMode = mode;
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
                Graph.ResetView();
            }
        }

        SettingsService.Instance.Update(s => s.PreferredLayout = mode);
        StatusText.Text = LocalizationService.Instance.Get("Status_LayoutApplied", GetLocalizedLayoutName(mode));
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
            SetLayoutMode(mode, resetView: true);
        }
    }

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
        var loc = LocalizationService.Instance;

        GitBranchPopupTitle.Text = vcsType == VcsType.Svn
            ? loc["Svn_SwitchBranchTitle"]
            : loc["Git_SwitchBranchTitle"];

        GitBranchSearchBox.Watermark = vcsType == VcsType.Svn
            ? loc["Svn_SearchBranchWatermark"]
            : loc["Git_SearchBranchWatermark"];

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

        var loc = LocalizationService.Instance;
        var switchingMessageKey = selectedBranch.VcsType == VcsType.Svn ? "Svn_SwitchingBranch" : "Git_SwitchingBranch";
        var successMessageKey = selectedBranch.VcsType == VcsType.Svn ? "Svn_SwitchSuccess" : "Git_SwitchSuccess";
        var errorMessageKey = selectedBranch.VcsType == VcsType.Svn ? "Svn_SwitchError" : "Git_SwitchError";

        ShowLoading(loc.Get(switchingMessageKey, selectedBranch.Name), loc["Status_Ready"]);
        await Task.Delay(16);

        try
        {
            var (success, error) = await VcsService.Instance.SwitchBranchAsync(_currentFolderPath, selectedBranch);
            if (success)
            {
                UpdateVcsBranchStatus(_currentFolderPath);
                await AnalyzeAndLoadFolderAsync(_currentFolderPath, forceReanalyze: true);
                StatusText.Text = loc.Get(successMessageKey, selectedBranch.Name);
            }
            else
            {
                StatusText.Text = loc.Get(errorMessageKey, error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = loc.Get(errorMessageKey, ex.Message);
        }
        finally
        {
            HideLoading();
        }
    }

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
        var loc = LocalizationService.Instance;
        StatusText.Text = loc["Update_Checking"];

        try
        {
            var update = await UpdateService.Instance.CheckForUpdatesAsync(forceCheck: true);
            if (update != null)
            {
                ShowUpdateModal(update);
            }
            else
            {
                StatusText.Text = loc.Get("Update_NoUpdates", UpdateService.CurrentVersionString);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore controllo aggiornamenti: {ex.Message}";
        }
    }

    /// <summary>
    /// Mostra l'overlay modale di notifica aggiornamento con le opzioni "Scarica dopo" e "Aggiorna ora".
    /// </summary>
    private void ShowUpdateModal(AppUpdateInfo update)
    {
        _availableUpdate = update;
        var loc = LocalizationService.Instance;

        UpdateModalTitle.Text = loc["Update_AvailableTitle"];
        UpdateModalSubtitle.Text = loc.Get("Update_AvailableSubtitle", update.VersionString, UpdateService.CurrentVersionString);
        UpdateModalNotesLabel.Text = loc["Update_NotesLabel"];
        UpdateModalNotes.Text = string.IsNullOrWhiteSpace(update.ReleaseNotes)
            ? "Miglioramenti generali delle prestazioni e correzioni di bug."
            : update.ReleaseNotes;
        UpdateLaterButton.Content = loc["Update_ButtonLater"];
        UpdateNowButton.Content = loc["Update_ButtonNow"];

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
        var loc = LocalizationService.Instance;
        StatusText.Text = loc["Update_OpeningDownload"];
        UpdateOverlay.IsVisible = false;
        UpdateService.Instance.OpenDownloadPage(_availableUpdate?.DownloadUrl);
    }

    // =========================================================================
    // Gestione Visibilità Pannelli Sinistro (Explorer) e Destro (Inspector)
    // =========================================================================

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
        StatusText.Text = LocalizationService.Instance["Status_SettingsReset"];
    }

    private void ClearProjectCache()
    {
        var count = ProjectCacheService.Instance.ClearAllCache();
        _graph?.Queries?.ClearCache();
        StatusText.Text = LocalizationService.Instance.Get("Status_CacheCleared", count);
    }

    private void UpdateLocalizedUI()
    {
        var loc = LocalizationService.Instance;

        // Menu File
        FileMenu.Header = loc["Menu_File"];
        OpenFolderMenuItem.Header = loc["Menu_OpenFolder"];
        OpenProjectFilesMenuItem.Header = loc["Menu_OpenProjectFiles"];
        RefreshProjectMenuItem.Header = loc["Menu_RefreshProject"];
        LoadFixturesMenuItem.Header = loc["Menu_LoadFixtures"];
        GenerateFakeMenuItem.Header = loc["Menu_GenerateFake"];
        ExportMarkdownMenuItem.Header = loc["Menu_ExportMarkdown"];
        ExportJsonMenuItem.Header = loc["Menu_ExportJson"];
        ImportJsonMenuItem.Header = loc["Menu_ImportJson"];
        ExportSliceMenuItem.Header = loc["Menu_ExportSlice"];
        SettingsMenuItem.Header = loc["Menu_Settings"];
        ExitMenuItem.Header = loc["Menu_Exit"];

        // Menu Visualizza
        ViewMenu.Header = loc["Menu_View"];
        ToggleExplorerMenuItem.Header = loc["Menu_ToggleExplorer"];
        ToggleInspectorMenuItem.Header = loc["Menu_ToggleInspector"];
        ResetViewMenuItem.Header = loc["Menu_ResetView"];
        ExpandAllMenuItem.Header = loc["Menu_ExpandAll"];
        CollapseAllMenuItem.Header = loc["Menu_CollapseAll"];
        FilterDetailLevelMenuItem.Header = loc["Menu_FilterDetailLevel"];
        FilterAllMenuItem.Header = loc["Filter_AllMembers"];
        FilterMethodsPropsMenuItem.Header = loc["Filter_MethodsAndProperties"];
        FilterContainersOnlyMenuItem.Header = loc["Filter_ContainersOnly"];
        FilterMethodsMenuItem.Header = loc["Filter_ShowMethods"];
        FilterPropertiesMenuItem.Header = loc["Filter_ShowProperties"];
        FilterFieldsMenuItem.Header = loc["Filter_ShowFields"];
        FilterConstantsMenuItem.Header = loc["Filter_ShowConstants"];
        FilterTypesMenuItem.Header = loc["Filter_ShowTypesEnums"];
        GraphLayoutMenuItem.Header = loc["Menu_GraphLayout"];
        LayoutStandardMenuItem.Header = loc["Layout_StandardRadial"];
        LayoutAffinityMenuItem.Header = loc["Layout_AffinityRadial"];
        LayoutIslandsMenuItem.Header = loc["Layout_ClusteredIslands"];
        LayoutForceMenuItem.Header = loc["Layout_ForceDirected"];

        // Menu Analisi
        AnalysisMenu.Header = loc["Menu_Analysis"];
        BlastRadiusMenuItem.Header = loc["Menu_BlastRadius"];
        ShowCallersMenuItem.Header = loc["Menu_ShowCallers"];
        ShowCalleesMenuItem.Header = loc["Menu_ShowCallees"];
        ClearHighlightMenuItem.Header = loc["Menu_ClearHighlight"];

        // Menu Aiuto
        HelpMenu.Header = loc["Menu_Help"];
        InfoMenuItem.Header = loc["Menu_HelpInfo"];
        CheckUpdatesMenuItem.Header = loc["Menu_CheckUpdates"];
        ResetSettingsMenuItem.Header = loc["Menu_ResetSettings"];
        ToolTip.SetTip(ResetSettingsMenuItem, loc["Tooltip_ResetSettings"]);
        ClearCacheMenuItem.Header = loc["Menu_ClearCache"];
        ToolTip.SetTip(ClearCacheMenuItem, loc["Tooltip_ClearCache"]);

        // Caption Buttons Tooltips
        ToolTip.SetTip(ToggleExplorerTopButton, loc["Tooltip_ToggleExplorer"]);
        ToolTip.SetTip(ToggleInspectorTopButton, loc["Tooltip_ToggleInspector"]);
        ToolTip.SetTip(SettingsButton, loc["Tooltip_Settings"]);
        ToolTip.SetTip(MinimizeButton, loc["Tooltip_Minimize"]);
        ToolTip.SetTip(MaximizeButton, loc["Tooltip_Maximize"]);
        ToolTip.SetTip(CloseButton, loc["Tooltip_Close"]);

        // Floating HUD Tooltips
        ToolTip.SetTip(ResetViewButton, loc["Tooltip_ResetView"]);
        ToolTip.SetTip(ExpandAllButton, loc["Tooltip_ExpandAll"]);
        ToolTip.SetTip(CollapseAllButton, loc["Tooltip_CollapseAll"]);
        ToolTip.SetTip(RefreshProjectButton, loc["Tooltip_RefreshProject"]);
        ToolTip.SetTip(BlastRadiusButton, loc["Tooltip_BlastRadius"]);
        ToolTip.SetTip(ShowCallersButton, loc["Tooltip_ShowCallers"]);
        ToolTip.SetTip(ShowCalleesButton, loc["Tooltip_ShowCallees"]);
        ToolTip.SetTip(ClearHighlightButton, loc["Tooltip_ClearHighlight"]);
        ToolTip.SetTip(ExportMarkdownButton, loc["Tooltip_ExportMarkdown"]);
        ToolTip.SetTip(ExportJsonButton, loc["Tooltip_ExportJson"]);
        ToolTip.SetTip(ImportJsonButton, loc["Tooltip_ImportJson"]);

        // Grid Toolbar Tooltips
        ToolTip.SetTip(RefreshGridButton, loc["Tooltip_RecalculateAudit"]);

        // Ricerca
        SearchBox.Watermark = loc["Search_Watermark"];

        // Explorer & Inspector
        ExplorerTitleText.Text = loc["Explorer_Title"];
        InspectorTitleText.Text = loc["Inspector_Title"];
        CtxExplorerOpenFile.Header = loc["Explorer_OpenOriginalFile"];
        CtxExplorerOpenFolder.Header = loc["Explorer_OpenContainingFolder"];
        CtxExplorerCopyPath.Header = loc["Explorer_CopyFilePath"];
        ToolTip.SetTip(InspectorOpenFileButton, loc["Inspector_Tooltip_OpenFile"]);
        ToolTip.SetTip(InspectorOpenFolderButton, loc["Inspector_Tooltip_OpenFolder"]);
        if (_graph is not null)
        {
            ExplorerTree.ItemsSource = new[] { ExplorerTreeItem.BuildTree(_graph, _currentFolderPath) };
        }

        // Tab Centrali
        GraphTabHeader.Text = loc["Tab_GraphView"];
        GridTabHeader.Text = loc["Tab_BlastRadiusGrid"];

        // Floating HUD
        HudLayoutLabel.Text = loc["HUD_LayoutLabel"];
        LayoutItemStandard.Content = loc["Layout_StandardRadial"];
        LayoutItemAffinity.Content = loc["Layout_AffinityRadial"];
        LayoutItemIslands.Content = loc["Layout_ClusteredIslands"];
        LayoutItemForce.Content = loc["Layout_ForceDirected"];
        HudFilterLabel.Text = loc["HUD_FilterLabel"];
        HudFilterButtonText.Text = loc["HUD_FilterButton"];
        DetailItemAll.Content = loc["Filter_AllMembers"];
        DetailItemMethodsProps.Content = loc["Filter_MethodsAndProperties"];
        DetailItemContainers.Content = loc["Filter_ContainersOnly"];
        DetailItemCustom.Content = loc["Filter_Custom"];
        FilterMethodsCheckBox.Content = loc["Filter_ShowMethods"];
        FilterPropertiesCheckBox.Content = loc["Filter_ShowProperties"];
        FilterFieldsCheckBox.Content = loc["Filter_ShowFields"];
        FilterConstantsCheckBox.Content = loc["Filter_ShowConstants"];
        FilterTypesCheckBox.Content = loc["Filter_ShowTypesEnums"];
        ToolTip.SetTip(FilterOptionsButton, loc["Tooltip_FilterMethods"]);
        ToolTip.SetTip(LegendMethodButton, loc["Tooltip_LegendToggle"]);
        ToolTip.SetTip(LegendPropertyButton, loc["Tooltip_LegendToggle"]);
        ToolTip.SetTip(LegendFieldButton, loc["Tooltip_LegendToggle"]);
        ToolTip.SetTip(LegendTypeButton, loc["Tooltip_LegendToggle"]);

        // Legenda Grafo
        LegendTitleText.Text = loc["Legend_Title"];
        LegendFormText.Text = loc["Legend_Forms"];
        LegendModuleText.Text = loc["Legend_Modules"];
        LegendClassText.Text = loc["Legend_Classes"];
        LegendControlText.Text = loc["Legend_Controls"];
        LegendMethodText.Text = loc["Legend_Methods"];
        LegendPropertyText.Text = loc["Legend_Properties"];
        LegendFieldText.Text = loc["Legend_Fields"];
        LegendTypeText.Text = loc["Legend_Types"];
        LegendCallsText.Text = loc["Legend_Calls"];
        LegendInstantiatesText.Text = loc["Legend_Instantiates"];
        HudResetViewText.Text = loc["HUD_ResetView"];
        HudExpandAllText.Text = loc["HUD_ExpandAll"];
        HudCollapseAllText.Text = loc["HUD_CollapseAll"];
        HudRefreshText.Text = loc["HUD_Refresh"];
        HudBlastRadiusText.Text = loc["HUD_BlastRadius"];
        HudShowCallersText.Text = loc["HUD_ShowCallers"];
        HudShowCalleesText.Text = loc["HUD_ShowCallees"];
        HudClearHighlightText.Text = loc["HUD_ClearFilter"];
        HudExportMarkdownText.Text = loc["HUD_ExportMarkdown"];
        HudExportJsonText.Text = loc["HUD_ExportJson"];
        HudImportJsonText.Text = loc["HUD_ImportJson"];

        // Grid Toolbar
        GridFilterLabel.Text = loc["Grid_FilterLabel"];
        GridFilterTextBox.Watermark = loc["Grid_FilterWatermark"];
        GridRiskLabel.Text = loc["Grid_RiskLabel"];
        RiskItemAll.Content = loc["Grid_AllRisks"];
        RiskItemCritical.Content = loc["Risk_Critical"];
        RiskItemHigh.Content = loc["Risk_High"];
        RiskItemMedium.Content = loc["Risk_Medium"];
        RiskItemLow.Content = loc["Risk_Low"];
        RefreshGridButtonText.Text = loc["Grid_Recalculate"];

        // VCS (Git & SVN) Popup & Tooltip
        if (!string.IsNullOrEmpty(_currentFolderPath))
        {
            var currentBranch = VcsService.Instance.GetCurrentBranch(_currentFolderPath, out var vcsType);
            if (vcsType != VcsType.None && !string.IsNullOrEmpty(currentBranch))
            {
                var tooltipKey = vcsType == VcsType.Svn ? "Svn_BranchTooltip" : "Git_BranchTooltip";
                ToolTip.SetTip(GitBranchButton, loc.Get(tooltipKey, currentBranch));
                GitBranchPopupTitle.Text = vcsType == VcsType.Svn ? loc["Svn_SwitchBranchTitle"] : loc["Git_SwitchBranchTitle"];
                GitBranchSearchBox.Watermark = vcsType == VcsType.Svn ? loc["Svn_SearchBranchWatermark"] : loc["Git_SearchBranchWatermark"];
            }
        }

        // DataGrid Colonne
        if (BlastRadiusDataGrid?.Columns.Count >= 10)
        {
            BlastRadiusDataGrid.Columns[0].Header = loc["Col_Project"];
            BlastRadiusDataGrid.Columns[1].Header = loc["Col_Routine"];
            BlastRadiusDataGrid.Columns[2].Header = loc["Col_Module"];
            BlastRadiusDataGrid.Columns[3].Header = loc["Col_DirectCallers"];
            BlastRadiusDataGrid.Columns[4].Header = loc["Col_IndirectCallers"];
            BlastRadiusDataGrid.Columns[5].Header = loc["Col_Depth"];
            BlastRadiusDataGrid.Columns[6].Header = loc["Col_Risk"];
            BlastRadiusDataGrid.Columns[7].Header = loc["Col_Forms"];
            BlastRadiusDataGrid.Columns[8].Header = loc["Col_TotalImpact"];
            BlastRadiusDataGrid.Columns[9].Header = loc["Col_Action"];
        }

        // Inspector
        InspectorTitleText.Text = loc["Inspector_Title"];
        InspectorEmptyTitle.Text = loc["Inspector_NoSelection"];
        InspectorEmptyHint1.Text = loc["Inspector_Hint1"];
        InspectorEmptyHint2.Text = loc["Inspector_Hint2"];
        InspectorEmptyHint3.Text = loc["Inspector_Hint3"];
        InspectorSecSymbol.Text = loc["Inspector_Sec_Symbol"];
        InspectorSecKind.Text = loc["Inspector_Sec_Kind"];
        InspectorSecVisibility.Text = loc["Inspector_Sec_Visibility"];
        InspectorSecType.Text = loc["Inspector_Sec_Type"];
        InspectorSecParams.Text = loc["Inspector_Sec_Params"];
        InspectorSecFile.Text = loc["Inspector_Sec_File"];
        InspectorSecAiContext.Text = loc["Inspector_Sec_AIContext"];
        InspectorAiContextDesc.Text = loc["Inspector_AiContextDesc"];
        CopyLlmSliceButtonText.Text = loc["Inspector_CopyLlmSlice"];
        SaveLlmSliceButtonText.Text = loc["Inspector_SaveLlmSlice"];
        ToolTip.SetTip(CopyLlmSliceButton, loc["Tooltip_CopyLlmSlice"]);
        ToolTip.SetTip(SaveLlmSliceButton, loc["Tooltip_SaveLlmSlice"]);
        InspectorSecBlast.Text = loc["Inspector_Sec_BlastRadius"];
        InspectorLabelDirectCallers.Text = loc["Inspector_DirectCallersCount"];
        InspectorLabelIndirectCallers.Text = loc["Inspector_IndirectCallersCount"];
        InspectorLabelMaxDepth.Text = loc["Inspector_MaxDepth"];
        InspectorSecForms.Text = loc["Inspector_Sec_Forms"];
        InspectorSecClasses.Text = loc["Inspector_Sec_Classes"];
        InspectorSecPaths.Text = loc["Inspector_Sec_Paths"];
        InspectorSecCallers.Text = loc["Inspector_Sec_WhoCallsMe"];
        InspectorSecCallees.Text = loc["Inspector_Sec_WhatDoICall"];
        InspectorSecInstantiates.Text = loc["Inspector_Sec_Instantiates"];

        // Modal Notifica Aggiornamento (se visibile)
        if (UpdateOverlay.IsVisible && _availableUpdate != null)
        {
            UpdateModalTitle.Text = loc["Update_AvailableTitle"];
            UpdateModalSubtitle.Text = loc.Get("Update_AvailableSubtitle", _availableUpdate.VersionString, UpdateService.CurrentVersionString);
            UpdateModalNotesLabel.Text = loc["Update_NotesLabel"];
            UpdateLaterButton.Content = loc["Update_ButtonLater"];
            UpdateNowButton.Content = loc["Update_ButtonNow"];
        }

        // Overlay Caricamento
        LoadingCancelButton.Content = loc["Loading_Cancel"];

        // Refresh dynamic strings in Grid and Status
        UpdateStatsBar();
        ApplyGridFilters();
        if (_selectedNode == null)
        {
            StatusText.Text = loc["Status_Ready"];
        }
        else
        {
            StatusText.Text = loc.Get("Status_Selected", _selectedNode.Kind, _selectedNode.QualifiedName);
            if (_selectedNode.SemanticSymbol is { } sym && _graph?.Queries is { } q && q.TryGetCachedImpactReport(sym, 10, out var cachedReport) && cachedReport != null)
            {
                UpdateInspectorImpactDetails(cachedReport, q.GetDirectCallees(sym), q.GetInstantiatedClasses(sym));
            }
        }
    }
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
