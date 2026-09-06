using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VB6Analyzer.App.Models;
using VB6Analyzer.App.Services;
using VB6Analyzer.Semantic.Impact;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.App;

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

    public MainWindow()
    {
        InitializeComponent();

        LoadFixturesButton.Click += (_, _) => LoadDefaultFixtures();
        GenerateFakeButton.Click += (_, _) => LoadSyntheticGraph();
        OpenFolderButton.Click += async (_, _) => await OpenFolderAsync();
        ResetViewButton.Click += (_, _) => Graph.ResetView();
        ExpandAllButton.Click += (_, _) => SetAllExpanded(true);
        CollapseAllButton.Click += (_, _) => SetAllExpanded(false);

        BlastRadiusButton.Click += (_, _) => OnBlastRadiusClicked();
        ShowCallersButton.Click += (_, _) => OnShowCallersClicked();
        ShowCalleesButton.Click += (_, _) => OnShowCalleesClicked();
        ClearHighlightButton.Click += (_, _) =>
        {
            Graph.ClearHighlightMode();
            StatusText.Text = "Evidenziazione ripristinata alla modalità standard.";
        };

        SearchBox.GotFocus += (_, _) =>
        {
            var query = SearchBox.Text?.Trim();
            if (!string.IsNullOrEmpty(query) && SearchResultsList.ItemsSource is IList<GraphNode> { Count: > 1 })
            {
                SearchResultsPopup.IsOpen = true;
            }
        };

        // Caricamento iniziale automatico delle fixture reali
        LoadDefaultFixtures();
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
        ShowLoading("Analisi del progetto VB6...", $"Scansione ed estrazione da '{Path.GetFileName(folderPath)}'...");
        await Task.Delay(16);

        try
        {
            StatusText.Text = $"Analisi cartella '{folderPath}' in corso...";
            _graph = await Task.Run(() => ProjectAnalysisService.AnalyzeFolder(folderPath));
            ApplyGraphToUI();
            StatusText.Text = $"Progetto '{Path.GetFileName(folderPath)}' analizzato con successo.";
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
    /// </summary>
    private void ApplyGraphToUI()
    {
        if (_graph is null) return;

        Graph.Graph = _graph;
        Graph.ResetView();
        Graph.SelectNode(null);

        ExplorerTree.ItemsSource = new[] { ExplorerTreeItem.BuildTree(_graph) };

        ShowEmptyInspector();
        UpdateStatsBar();
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
        if (node is null)
        {
            _selectionCts?.Cancel();
            ShowEmptyInspector();
            StatusText.Text = "Nessun simbolo selezionato.";
        }
        else
        {
            SelectAndInspectNodeAsync(node);
            StatusText.Text = $"Selezionato: [{node.Kind}] {node.QualifiedName}";
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
            SearchResultsHeader.Text = $"Nessun simbolo trovato per '{query}'";
            SearchResultsList.ItemsSource = null;
            SearchResultsPopup.IsOpen = true;
            StatusText.Text = $"Nessun simbolo corrispondente a '{query}'.";
        }
        else if (matches.Count == 1)
        {
            // Un solo risultato: chiudi la lista e seleziona direttamente il nodo
            SearchResultsPopup.IsOpen = false;
            SearchResultsList.ItemsSource = matches;
            SelectAndFocusGraphNode(matches[0]);
            StatusText.Text = $"Trovato 1 simbolo: [{matches[0].Kind}] {matches[0].QualifiedName}";
        }
        else
        {
            // Più di un risultato: NON mostrare direttamente il primo nodo nel grafico,
            // ma mostra la lista dei risultati da cui l'utente può selezionare l'elemento specifico
            SearchResultsHeader.Text = $"Trovati {matches.Count} simboli (clicca per selezionare):";
            SearchResultsList.ItemsSource = matches;
            SearchResultsPopup.IsOpen = true;
            StatusText.Text = $"Trovati {matches.Count} simboli corrispondenti a '{query}'. Seleziona un elemento dalla lista.";
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

    private void UpdateStatsBar()
    {
        if (_graph is null) return;

        var nodeCount = _graph.Nodes.Count;
        var relCount = _graph.Edges.Count;
        var callCount = _graph.Edges.Count(e => e.Kind == RelationshipKind.Calls);
        var instCount = _graph.Edges.Count(e => e.Kind == RelationshipKind.Instantiates);

        StatusStats.Text = $"Nodi: {nodeCount} | Relazioni: {relCount} (Calls: {callCount}, Instantiates: {instCount})";
    }
}
