using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VB6Analyzer.App.Generation;
using VB6Analyzer.App.Layout;
using VB6Analyzer.App.Models;

namespace VB6Analyzer.App;

public partial class MainWindow : Window
{
    private CodeGraph? _graph;

    public MainWindow()
    {
        InitializeComponent();

        RegenerateButton.Click += (_, _) => LoadNewGraph();
        ResetViewButton.Click += (_, _) => Graph.ResetView();
        ExpandAllButton.Click += (_, _) => SetAllExpanded(true);
        CollapseAllButton.Click += (_, _) => SetAllExpanded(false);

        LoadNewGraph();
    }

    /// <summary>
    /// Genera un nuovo CodeGraph fittizio (~500 nodi), calcola il layout e
    /// popola Graph, Explorer e status bar. Corrisponde ai punti 2-4 del
    /// checklist del POC (sezione 41).
    /// </summary>
    private void LoadNewGraph()
    {
        _graph = FakeGraphGenerator.Generate();
        RadialLayoutEngine.Apply(_graph);

        Graph.Graph = _graph;
        Graph.ResetView();
        Graph.SelectNode(null);

        ExplorerTree.ItemsSource = new[] { ExplorerTreeItem.BuildTree(_graph) };

        ShowEmptyInspector();
        UpdateStatus(null);
    }

    private void SetAllExpanded(bool expanded)
    {
        if (_graph is null) return;
        foreach (var node in _graph.Nodes.Where(n => n.Kind is NodeKind.Form or NodeKind.Class or NodeKind.Module))
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
        }
        else
        {
            ShowInspectorDetails(node);
        }
        UpdateStatus(node);
    }

    private void OnExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not ExplorerTreeItem item) return;
        if (_graph is null) return;

        var node = _graph.GetNode(item.NodeId);
        if (node is null) return;

        // se è un metodo, assicurati che il container sia espanso così il nodo è visibile nel grafo
        if (node.Kind == NodeKind.Method && node.ParentId.HasValue)
        {
            var parent = _graph.GetNode(node.ParentId.Value);
            if (parent is not null) parent.IsExpanded = true;
        }

        Graph.FocusOnNode(node.Id);
        Graph.SelectNode(node);
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
        InspectorKind.Text = node.Kind.ToString();

        if (_graph is null) return;

        var callers = _graph.GetCallers(node.Id).Select(n => n.Name).ToList();
        var callees = _graph.GetCallees(node.Id).Select(n => n.Name).ToList();

        InspectorCallers.Text = callers.Count > 0 ? string.Join(", ", callers) : "(nessuno)";
        InspectorCallees.Text = callees.Count > 0 ? string.Join(", ", callees) : "(nessuno)";
    }

    private void UpdateStatus(GraphNode? selected)
    {
        if (_graph is null) return;

        var baseText = $"Nodi: {_graph.Nodes.Count}   Archi: {_graph.Edges.Count}";
        StatusText.Text = selected is null
            ? $"{baseText}   Pronto."
            : $"{baseText}   Selezionato: {selected.Kind} \"{selected.Name}\"";
    }
}
