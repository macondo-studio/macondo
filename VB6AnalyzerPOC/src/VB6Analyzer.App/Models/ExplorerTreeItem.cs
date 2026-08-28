namespace VB6Analyzer.App.Models;

/// <summary>
/// Nodo per il Project Explorer (sezione 25). Wrappa un GraphNode per il TreeView;
/// non è il modello dati principale (quello resta CodeGraph/GraphNode).
/// </summary>
public sealed class ExplorerTreeItem
{
    public required string Name { get; init; }
    public required Guid NodeId { get; init; }
    public List<ExplorerTreeItem> Children { get; } = new();

    public static ExplorerTreeItem BuildTree(CodeGraph graph)
    {
        var project = graph.Nodes.First(n => n.Kind == NodeKind.Project);
        return Build(graph, project);
    }

    private static ExplorerTreeItem Build(CodeGraph graph, GraphNode node)
    {
        var item = new ExplorerTreeItem { Name = node.Name, NodeId = node.Id };
        foreach (var child in graph.GetChildren(node.Id).OrderBy(c => c.Name))
        {
            item.Children.Add(Build(graph, child));
        }
        return item;
    }
}
