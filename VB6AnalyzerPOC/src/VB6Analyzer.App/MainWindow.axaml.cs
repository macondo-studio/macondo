using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VB6Analyzer.App.Models;
using VB6Analyzer.App.Services;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.App;

/// <summary>
/// Finestra principale dell'applicazione VB6 Analyzer (Milestone M5).
/// Gestisce l'interazione tra Toolbar, Project Explorer (TreeView),
/// Canvas del Grafo (<see cref="Controls.GraphControl"/>) e Symbol Inspector.
/// </summary>
public partial class MainWindow : Window
{
    private VisualCodeGraph? _graph;

    public MainWindow()
    {
        InitializeComponent();

        LoadFixturesButton.Click += (_, _) => LoadDefaultFixtures();
        GenerateFakeButton.Click += (_, _) => LoadSyntheticGraph();
        OpenFolderButton.Click += async (_, _) => await OpenFolderAsync();
        ResetViewButton.Click += (_, _) => Graph.ResetView();
        ExpandAllButton.Click += (_, _) => SetAllExpanded(true);
        CollapseAllButton.Click += (_, _) => SetAllExpanded(false);

        ShowCallersButton.Click += (_, _) => Graph.HighlightCallersForSelected();
        ShowCalleesButton.Click += (_, _) => Graph.HighlightCalleesForSelected();
        ClearHighlightButton.Click += (_, _) => Graph.ClearHighlightMode();

        // Caricamento iniziale automatico delle fixture reali
        LoadDefaultFixtures();
    }

    /// <summary>
    /// Genera e carica un grafo sintetico di ~500 nodi tramite FakeGraphGenerator per benchmark e test.
    /// </summary>
    private void LoadSyntheticGraph()
    {
        try
        {
            StatusText.Text = "Generazione del grafo sintetico (~500 nodi)...";
            _graph = Generation.FakeGraphGenerator.Generate(seed: Environment.TickCount);
            ApplyGraphToUI();
            StatusText.Text = "Grafo sintetico di benchmark (~500 nodi) generato con successo.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore durante la generazione: {ex.Message}";
        }
    }

    /// <summary>
    /// Carica e analizza la suite di fixture VB6 reali integrate nel progetto.
    /// </summary>
    private void LoadDefaultFixtures()
    {
        try
        {
            StatusText.Text = "Analisi semantica delle fixture in corso...";
            _graph = ProjectAnalysisService.LoadDefaultFixtures();
            ApplyGraphToUI();
            StatusText.Text = "Suite di fixture VB6 analizzata con successo in memoria.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore durante il caricamento: {ex.Message}";
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
        try
        {
            StatusText.Text = $"Analisi cartella '{folderPath}' in corso...";
            _graph = ProjectAnalysisService.AnalyzeFolder(folderPath);
            ApplyGraphToUI();
            StatusText.Text = $"Progetto '{Path.GetFileName(folderPath)}' analizzato con successo.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Errore durante l'analisi: {ex.Message}";
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

    private void OnGraphSelectionChanged(object? sender, GraphNode? node)
    {
        if (node is null)
        {
            ShowEmptyInspector();
            StatusText.Text = "Nessun simbolo selezionato.";
        }
        else
        {
            ShowInspectorDetails(node);
            StatusText.Text = $"Selezionato: [{node.Kind}] {node.QualifiedName}";
        }
    }

    private void OnExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not ExplorerTreeItem item) return;
        if (_graph is null) return;

        var node = _graph.GetNode(item.NodeId);
        if (node is null) return;

        // Se è un membro interno, assicurati che il container padre sia espanso
        if (node.ParentId.HasValue)
        {
            var parent = _graph.GetNode(node.ParentId.Value);
            if (parent is not null)
                parent.IsExpanded = true;
        }

        Graph.FocusOnNode(node.Id);
        Graph.SelectNode(node);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query) || _graph is null) return;

        var match = _graph.Nodes
            .FirstOrDefault(n => n.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                n.QualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            if (match.ParentId.HasValue)
            {
                var parent = _graph.GetNode(match.ParentId.Value);
                if (parent is not null) parent.IsExpanded = true;
            }

            Graph.FocusOnNode(match.Id);
            Graph.SelectNode(match);
        }
    }

    private void ShowEmptyInspector()
    {
        InspectorEmptyPanel.IsVisible = true;
        InspectorDetailsPanel.IsVisible = false;
    }

    private void ShowInspectorDetails(GraphNode node)
    {
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

        // Callers & Callees dal Call Graph
        if (_graph?.Queries != null && sym != null)
        {
            var callers = _graph.Queries.GetDirectCallers(sym);
            InspectorCallers.Text = callers.Count > 0
                ? string.Join("\n", callers.Select(c => $"• {c.QualifiedName}"))
                : "(nessun chiamante diretto)";

            var callees = _graph.Queries.GetDirectCallees(sym);
            InspectorCallees.Text = callees.Count > 0
                ? string.Join("\n", callees.Select(c => $"• {c.QualifiedName}"))
                : "(nessuna chiamata effettuata)";

            var instantiations = _graph.Queries.GetInstantiatedClasses(sym);
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
        else
        {
            InspectorCallers.Text = "(nessuno)";
            InspectorCallees.Text = "(nessuno)";
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
