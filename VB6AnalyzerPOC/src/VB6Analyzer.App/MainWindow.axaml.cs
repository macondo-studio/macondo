using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.App.Views;
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

    public MainWindow()
    {
        InitializeComponent();

        // Menu Bar Superiore - File (Command + Click per massima affidabilità)
        OpenFolderMenuItem.Command = new ActionCommand(async () => await OpenFolderAsync());
        RefreshProjectMenuItem.Command = new ActionCommand(async () => await RefreshCurrentProjectAsync());
        LoadFixturesMenuItem.Command = new ActionCommand(() => LoadDefaultFixtures());
        GenerateFakeMenuItem.Command = new ActionCommand(() => LoadSyntheticGraph());
        ExportMarkdownMenuItem.Command = new ActionCommand(async () => await ExportMarkdownAsync());
        ExportJsonMenuItem.Command = new ActionCommand(async () => await ExportJsonAsync());
        ImportJsonMenuItem.Command = new ActionCommand(async () => await ImportJsonAsync());
        ExportSliceMenuItem.Command = new ActionCommand(async () => await ExportLlmSliceAsync());
        SettingsMenuItem.Command = new ActionCommand(OpenSettings);
        ExitMenuItem.Command = new ActionCommand(() => Close());

        OpenFolderMenuItem.Click += async (_, _) => await OpenFolderAsync();
        RefreshProjectMenuItem.Click += async (_, _) => await RefreshCurrentProjectAsync();
        LoadFixturesMenuItem.Click += (_, _) => LoadDefaultFixtures();
        GenerateFakeMenuItem.Click += (_, _) => LoadSyntheticGraph();
        ExportMarkdownMenuItem.Click += async (_, _) => await ExportMarkdownAsync();
        ExportJsonMenuItem.Click += async (_, _) => await ExportJsonAsync();
        ImportJsonMenuItem.Click += async (_, _) => await ImportJsonAsync();
        ExportSliceMenuItem.Click += async (_, _) => await ExportLlmSliceAsync();
        SettingsMenuItem.Click += (_, _) => OpenSettings();
        ExitMenuItem.Click += (_, _) => Close();

        // Menu Bar Superiore - Visualizza & Analisi
        ResetViewMenuItem.Command = new ActionCommand(() => Graph.ResetView());
        ExpandAllMenuItem.Command = new ActionCommand(() => SetAllExpanded(true));
        CollapseAllMenuItem.Command = new ActionCommand(() => SetAllExpanded(false));
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

        // Export & Import HUD buttons
        ExportMarkdownButton.Click += async (_, _) => await ExportMarkdownAsync();
        ExportJsonButton.Click += async (_, _) => await ExportJsonAsync();
        ImportJsonButton.Click += async (_, _) => await ImportJsonAsync();

        // Inspector AI Context Slice buttons
        CopyLlmSliceButton.Click += async (_, _) => await CopyLlmSliceToClipboardAsync();
        SaveLlmSliceButton.Click += async (_, _) => await ExportLlmSliceAsync();

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
            else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F5)
            {
                OnBlastRadiusClicked();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        // Sincronizzazione selezione tra Tab Grafo e Tab Griglia Blast Radius
        CentralTabControl.SelectionChanged += OnCentralTabSelectionChanged;

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

        if (settings.ExplorerWidth >= 160 && settings.ExplorerWidth <= 700)
        {
            CentralGrid.ColumnDefinitions[0].Width = new GridLength(settings.ExplorerWidth);
        }

        if (settings.InspectorWidth >= 200 && settings.InspectorWidth <= 800)
        {
            CentralGrid.ColumnDefinitions[4].Width = new GridLength(settings.InspectorWidth);
        }

        // Salvataggio Geometria Finestra e Larghezza Pannelli alla chiusura
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
                if (ExplorerBorder.Bounds.Width >= 160)
                {
                    s.ExplorerWidth = ExplorerBorder.Bounds.Width;
                }
                if (InspectorBorder.Bounds.Width >= 200)
                {
                    s.InspectorWidth = InspectorBorder.Bounds.Width;
                }
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
    /// Mostra l'overlay modale di caricamento centrato a tutto schermo.
    /// </summary>
    private void ShowLoading(string title, string subtitle)
    {
        LoadingTitle.Text = title;
        LoadingSubtitle.Text = subtitle;
        LoadingOverlay.IsVisible = true;
    }

    /// <summary>
    /// Nasconde l'overlay di caricamento.
    /// </summary>
    private void HideLoading()
    {
        LoadingOverlay.IsVisible = false;
    }

    /// <summary>
    /// Genera e carica un grafo sintetico di ~500 nodi tramite FakeGraphGenerator per benchmark e test.
    /// </summary>
    private async void LoadSyntheticGraph()
    {
        ShowLoading("Generazione del grafo sintetico...", "Creazione di ~500 nodi e calcolo del layout radiale...");
        await Task.Delay(16);

        try
        {
            _currentFolderPath = null;
            StatusText.Text = "Generazione del grafo sintetico (~500 nodi)...";
            _graph = await Task.Run(() => Generation.FakeGraphGenerator.Generate(seed: Environment.TickCount));
            ApplyGraphToUI();
            StatusText.Text = "Grafo sintetico di benchmark (~500 nodi) generato con successo.";
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
    /// Carica e analizza la suite di fixture VB6 reali integrate nel progetto.
    /// </summary>
    private async void LoadDefaultFixtures()
    {
        ShowLoading("Caricamento fixture VB6...", "Analisi semantica dei 16 moduli reali in corso...");
        await Task.Delay(16);

        try
        {
            _currentFolderPath = null;
            StatusText.Text = "Analisi semantica delle fixture in corso...";
            _graph = await Task.Run(() => ProjectAnalysisService.LoadDefaultFixtures());
            ApplyGraphToUI();
            StatusText.Text = "Suite di fixture VB6 analizzata con successo in memoria.";
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
    /// Apre un selettore di cartelle per analizzare sorgenti VB6 arbitrari.
    /// </summary>
    private async Task OpenFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleziona cartella con file sorgente VB6 (.cls, .bas, .frm, .ctl)",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var folderPath = folders[0].Path.LocalPath;
        await AnalyzeAndLoadFolderAsync(folderPath);
    }

    /// <summary>
    /// Ricarica forzatamente il progetto corrente da disco, ri-eseguendo parsing e analisi e aggiornando la cache.
    /// </summary>
    private async Task RefreshCurrentProjectAsync()
    {
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

        await AnalyzeAndLoadFolderAsync(targetFolder, forceReanalyze: true);
    }

    /// <summary>
    /// Esegue o ripristina da cache l'analisi dei file VB6 di una cartella.
    /// </summary>
    private async Task AnalyzeAndLoadFolderAsync(string folderPath, bool forceReanalyze = false)
    {
        if (!Directory.Exists(folderPath)) return;

        // 1. Tenta il ripristino ultra-rapido dalla cache locale (%APPDATA%\Macondo\cache\) se non forzato
        if (!forceReanalyze)
        {
            var cachedSnapshot = await ProjectCacheService.Instance.TryLoadSnapshotAsync(folderPath);
            if (cachedSnapshot != null)
            {
                var (cachedGraph, cachedRows) = ProjectCacheService.Instance.RestoreGraphAndRows(cachedSnapshot);
                _graph = cachedGraph;
                _currentFolderPath = folderPath;
                ApplyGraphToUI(cachedRows);

                // Salva l'ultima cartella aperta nelle impostazioni
                SettingsService.Instance.Update(s => s.LastOpenedFolder = folderPath);

                // Rileva eventuali modifiche ai sorgenti intervenute dopo il salvataggio della cache
                var isStale = ProjectCacheService.Instance.CheckStaleness(cachedSnapshot, folderPath);
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

        // 2. Analisi completa da sorgenti su disco con overlay di caricamento
        ShowLoading("Analisi del progetto VB6...", $"Scansione ed estrazione da '{Path.GetFileName(folderPath)}'...");
        await Task.Delay(16);

        try
        {
            StatusText.Text = $"Analisi cartella '{folderPath}' in corso...";
            _graph = await Task.Run(() => ProjectAnalysisService.AnalyzeFolder(folderPath));
            _currentFolderPath = folderPath;
            ApplyGraphToUI(); // Avvierà PopulateBlastRadiusGridAsync che poi salverà la cache aggiornata

            var projectName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            StatusText.Text = LocalizationService.Instance.Get("Status_Refreshed", projectName, _graph.Nodes.Count);

            // Salva l'ultima cartella aperta nelle impostazioni
            SettingsService.Instance.Update(s => s.LastOpenedFolder = folderPath);
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

        Graph.Graph = _graph;
        Graph.ResetView();
        Graph.SelectNode(null);

        ExplorerTree.ItemsSource = new[] { ExplorerTreeItem.BuildTree(_graph) };

        ShowEmptyInspector();
        UpdateStatsBar();

        if (precalculatedRows != null && precalculatedRows.Count > 0)
        {
            _allBlastRadiusRows = precalculatedRows;
            ApplyGridFilters();
        }
        else
        {
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
    }

    /// <summary>
    /// Calcola ed evidenzia il Blast Radius per il nodo selezionato in modo asincrono con loading overlay.
    /// Se il risultato è già in cache, viene applicato all'istante senza mostrare l'overlay.
    /// </summary>
    private async void OnBlastRadiusClicked()
    {
        if (_selectedNode?.SemanticSymbol is null || _graph?.Queries is null)
        {
            StatusText.Text = "Seleziona prima un simbolo per calcolare il Blast Radius.";
            return;
        }

        var node = _selectedNode;
        var sym = node.SemanticSymbol;

        // Se già calcolato e presente in cache, applica all'istante (0 ms)
        if (_graph.Queries.TryGetCachedImpactReport(sym, 10, out var cachedReport) && cachedReport != null)
        {
            Graph.HighlightBlastRadius(cachedReport);
            UpdateInspectorImpactSection(cachedReport);
            StatusText.Text = $"Blast Radius per '{node.QualifiedName}': {cachedReport.DirectCallers.Count} diretti, {cachedReport.IndirectCallers.Count} indiretti, {cachedReport.AffectedForms.Count} form, Rischio: {cachedReport.RiskLevel}";
            return;
        }

        ShowLoading("Calcolo Blast Radius in corso...", $"Analisi d'impatto e percorsi di chiamata per '{node.Name}'");
        await Task.Delay(16);

        try
        {
            var report = await Task.Run(() => _graph.Queries.GetImpactReport(sym));

            if (_selectedNode?.Id == node.Id)
            {
                Graph.HighlightBlastRadius(report);
                UpdateInspectorImpactSection(report);
                StatusText.Text = $"Blast Radius per '{node.QualifiedName}': {report.DirectCallers.Count} diretti, {report.IndirectCallers.Count} indiretti, {report.AffectedForms.Count} form, Rischio: {report.RiskLevel}";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore nel calcolo del Blast Radius: {ex.Message}";
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
        if (_selectedNode is null)
        {
            StatusText.Text = "Seleziona prima una routine o un modulo per visualizzare le chiamate uscenti (callees).";
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
        if (_selectedNode is null)
        {
            StatusText.Text = "Seleziona prima una routine o un modulo per visualizzare i chiamanti (callers).";
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
        RiskLevelText.Text = "CALCOLO...";
        RiskBadge.Background = new SolidColorBrush(Color.Parse("#FF6633"));
        BlastDirectCallersCount.Text = "...";
        BlastIndirectCallersCount.Text = "...";
        BlastMaxDepth.Text = "...";
        InspectorCallers.Text = "Calcolo chiamanti in corso...";
        InspectorCallees.Text = "Calcolo chiamate in corso...";
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
                        ShowLoading("Analisi del nodo in corso...", $"Esplorazione chiamanti e dipendenze per '{node.Name}'");
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
                StatusText.Text = $"Errore nell'analisi del simbolo: {ex.Message}";
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
        InspectorCallers.Text = "(nessuno)";
        InspectorCallees.Text = "(nessuno)";
        InspectorInstantiatesSection.IsVisible = false;
        BlastFormsSection.IsVisible = false;
        BlastClassesSection.IsVisible = false;
        BlastPathsSection.IsVisible = false;
    }

    private void UpdateInspectorImpactSection(ImpactReport impact)
    {
        // Risk Badge
        RiskLevelText.Text = $"RISK: {impact.RiskLevel.ToString().ToUpperInvariant()}";
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

        // Callers diretti standard
        InspectorCallers.Text = impact.DirectCallers.Count > 0
            ? string.Join("\n", impact.DirectCallers.Select(c => $"• {c.QualifiedName}"))
            : "(nessun chiamante diretto)";

        // Callees diretti
        InspectorCallees.Text = callees.Count > 0
            ? string.Join("\n", callees.Select(c => $"• {c.QualifiedName}"))
            : "(nessuna chiamata effettuata)";

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
                StatusText.Text = $"Selezionato dalla griglia: [{node.Kind}] {node.QualifiedName}";
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

        var nodeCount = _graph.Nodes.Count;
        var relCount = _graph.Edges.Count;
        var callCount = _graph.Edges.Count(e => e.Kind == RelationshipKind.Calls);
        var instCount = _graph.Edges.Count(e => e.Kind == RelationshipKind.Instantiates);

        StatusStats.Text = LocalizationService.Instance.Get("Status_StatsFormat", nodeCount, relCount, callCount, instCount);
    }

    private void OpenSettings()
    {
        var settingsWin = new SettingsWindow();
        settingsWin.ShowDialog(this);
    }

    private void ApplyTheme(ThemeDefinition theme)
    {
        if (Color.TryParse(theme.BackgroundPrimary, out var bgPrimary))
            Background = new SolidColorBrush(bgPrimary);

        if (Color.TryParse(theme.BackgroundSecondary, out var bgSecondary))
        {
            var secondaryBrush = new SolidColorBrush(bgSecondary);
            TopBarBorder.Background = secondaryBrush;
            SearchBarBorder.Background = secondaryBrush;
            StatusBarBorder.Background = secondaryBrush;
            FloatingHudBorder.Background = secondaryBrush;
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
            SearchBarBorder.BorderBrush = borderBrush;
            StatusBarBorder.BorderBrush = borderBrush;
            FloatingHudBorder.BorderBrush = borderBrush;
            ExplorerBorder.BorderBrush = borderBrush;
            GridToolbarBorder.BorderBrush = borderBrush;
            InspectorBorder.BorderBrush = borderBrush;
            LeftGridSplitter.Background = borderBrush;
            RightGridSplitter.Background = borderBrush;
        }

        Graph.ApplyTheme(theme);
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
            _graph = visualGraph;
            ApplyGraphToUI();

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

    private void UpdateLocalizedUI()
    {
        var loc = LocalizationService.Instance;

        // Menu File
        FileMenu.Header = loc["Menu_File"];
        OpenFolderMenuItem.Header = loc["Menu_OpenFolder"];
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
        ResetViewMenuItem.Header = loc["Menu_ResetView"];
        ExpandAllMenuItem.Header = loc["Menu_ExpandAll"];
        CollapseAllMenuItem.Header = loc["Menu_CollapseAll"];

        // Menu Analisi
        AnalysisMenu.Header = loc["Menu_Analysis"];
        BlastRadiusMenuItem.Header = loc["Menu_BlastRadius"];
        ShowCallersMenuItem.Header = loc["Menu_ShowCallers"];
        ShowCalleesMenuItem.Header = loc["Menu_ShowCallees"];
        ClearHighlightMenuItem.Header = loc["Menu_ClearHighlight"];

        // Caption Buttons Tooltips
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

        // Explorer
        ExplorerTitleText.Text = loc["Explorer_Title"];

        // Tab Centrali
        GraphTabHeader.Text = loc["Tab_GraphView"];
        GridTabHeader.Text = loc["Tab_BlastRadiusGrid"];

        // Floating HUD
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
        RefreshGridButtonText.Text = loc["Grid_Recalculate"];

        // DataGrid Colonne
        if (BlastRadiusDataGrid?.Columns.Count >= 9)
        {
            BlastRadiusDataGrid.Columns[0].Header = loc["Col_Routine"];
            BlastRadiusDataGrid.Columns[1].Header = loc["Col_Module"];
            BlastRadiusDataGrid.Columns[2].Header = loc["Col_DirectCallers"];
            BlastRadiusDataGrid.Columns[3].Header = loc["Col_IndirectCallers"];
            BlastRadiusDataGrid.Columns[4].Header = loc["Col_Depth"];
            BlastRadiusDataGrid.Columns[5].Header = loc["Col_Risk"];
            BlastRadiusDataGrid.Columns[6].Header = loc["Col_Forms"];
            BlastRadiusDataGrid.Columns[7].Header = loc["Col_TotalImpact"];
            BlastRadiusDataGrid.Columns[8].Header = loc["Col_Action"];
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

        // Refresh dynamic strings in Grid and Status
        UpdateStatsBar();
        ApplyGridFilters();
        if (_selectedNode == null)
        {
            StatusText.Text = loc["Status_Ready"];
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
