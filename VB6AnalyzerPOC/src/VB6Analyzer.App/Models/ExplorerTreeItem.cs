using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Nodo gerarchico per il Project Explorer (sezione 25 del charter).
/// Wrappa un <see cref="GraphNode"/> per la visualizzazione ad albero nel TreeView.
/// </summary>
public sealed class ExplorerTreeItem
{
    public required string Name { get; init; }
    public required string DisplayText { get; init; }
    public required SymbolKind Kind { get; init; }
    public required Guid NodeId { get; init; }
    public required string KindBadge { get; init; }
    public required string BadgeBackground { get; init; }
    public required string BadgeForeground { get; init; }
    public List<ExplorerTreeItem> Children { get; } = new();

    /// <summary>
    /// Costruisce l'albero gerarchico del progetto a partire dal CodeGraph visuale.
    /// </summary>
    public static ExplorerTreeItem BuildTree(VisualCodeGraph graph)
    {
        var project = graph.Nodes.FirstOrDefault(n => n.Kind == SymbolKind.Project)
                      ?? graph.Nodes.First();

        return Build(graph, project);
    }

    private static ExplorerTreeItem Build(VisualCodeGraph graph, GraphNode node)
    {
        var (badge, bg, fg) = node.Kind switch
        {
            SymbolKind.Project => ("PRJ", "#2A2A3E", "#A0A0B8"),
            SymbolKind.Class => ("CLS", "#163348", "#4AE8D0"),
            SymbolKind.Form => ("FRM", "#163830", "#50E3C2"),
            SymbolKind.Module => ("BAS", "#3D3014", "#F5A623"),
            SymbolKind.UserControl => ("CTL", "#352044", "#C678DD"),
            SymbolKind.Method => ("M", "#2D2248", "#B794F6"),
            SymbolKind.Property => ("P", "#382D14", "#E5C07B"),
            SymbolKind.Field => ("F", "#1C2D3D", "#61AFEF"),
            SymbolKind.Constant => ("C", "#382020", "#E06C75"),
            SymbolKind.Type => ("TYP", "#1C3330", "#56B6C2"),
            SymbolKind.Enum => ("ENM", "#1E303D", "#61AFEF"),
            _ => ("•", "#222230", "#8A8A9E")
        };

        var item = new ExplorerTreeItem
        {
            Name = node.Name,
            DisplayText = node.Name,
            Kind = node.Kind,
            NodeId = node.Id,
            KindBadge = badge,
            BadgeBackground = bg,
            BadgeForeground = fg
        };

        foreach (var child in graph.GetChildren(node.Id).OrderBy(c => c.Kind).ThenBy(c => c.Name))
        {
            item.Children.Add(Build(graph, child));
        }

        return item;
    }
}
