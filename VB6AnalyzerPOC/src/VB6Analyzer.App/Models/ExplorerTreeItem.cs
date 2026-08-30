using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.App.Models;

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
        var kindPrefix = node.Kind switch
        {
            SymbolKind.Project => "📁",
            SymbolKind.Class => "📄 [Cls]",
            SymbolKind.Form => "🖼️ [Frm]",
            SymbolKind.Module => "📦 [Bas]",
            SymbolKind.UserControl => "🎛️ [Ctl]",
            SymbolKind.Method => "⚡",
            SymbolKind.Property => "🔧",
            SymbolKind.Field => "🔹",
            SymbolKind.Constant => "🔒",
            SymbolKind.Type => "📐 [Type]",
            SymbolKind.Enum => "🔢 [Enum]",
            _ => "▫️"
        };

        var item = new ExplorerTreeItem
        {
            Name = node.Name,
            DisplayText = $"{kindPrefix} {node.Name}",
            Kind = node.Kind,
            NodeId = node.Id
        };

        foreach (var child in graph.GetChildren(node.Id).OrderBy(c => c.Kind).ThenBy(c => c.Name))
        {
            item.Children.Add(Build(graph, child));
        }

        return item;
    }
}
