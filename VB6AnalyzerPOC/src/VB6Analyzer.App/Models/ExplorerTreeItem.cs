using Macondo.App.Services;
using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Nodo gerarchico per il Project Explorer (sezione 25 del charter).
/// Wrappa un <see cref="GraphNode"/> per la visualizzazione ad albero nel TreeView,
/// raggruppando i file di primo livello per tipologia (Forms, Moduli, Moduli di classe, Controlli utente, ecc.).
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
    public bool IsGroup { get; init; }
    public string? FilePath { get; init; }
    public int LineStart { get; init; }
    public int LineEnd { get; init; }
    public bool HasFile => !string.IsNullOrEmpty(FilePath);
    public string ToolTipText => !string.IsNullOrEmpty(FilePath)
        ? (LineStart > 0 ? $"{FilePath}:{LineStart}" : FilePath)
        : DisplayText;
    public List<ExplorerTreeItem> Children { get; } = new();

    /// <summary>
    /// Costruisce l'albero gerarchico del progetto a partire dal CodeGraph visuale,
    /// con raggruppamento dei file per tipologia.
    /// </summary>
    public static ExplorerTreeItem BuildTree(VisualCodeGraph graph, string? projectFolderPath = null)
    {
        if (graph.Nodes.Count == 0)
        {
            var (emptyBadge, emptyBg, emptyFg) = GetBadgeInfo(SymbolKind.Project);
            return new ExplorerTreeItem
            {
                Name = "Project",
                DisplayText = "Project",
                Kind = SymbolKind.Project,
                NodeId = Guid.Empty,
                KindBadge = emptyBadge,
                BadgeBackground = emptyBg,
                BadgeForeground = emptyFg,
                FilePath = projectFolderPath
            };
        }

        var project = graph.Nodes.FirstOrDefault(n => n.Kind == SymbolKind.Project)
                      ?? graph.Nodes.First();

        var (prjBadge, prjBg, prjFg) = GetBadgeInfo(project.Kind);
        var rootItem = new ExplorerTreeItem
        {
            Name = project.Name,
            DisplayText = project.Name,
            Kind = project.Kind,
            NodeId = project.Id,
            KindBadge = prjBadge,
            BadgeBackground = prjBg,
            BadgeForeground = prjFg,
            FilePath = ResolvePath(project.SemanticSymbol?.FilePath, projectFolderPath) ?? projectFolderPath
        };

        var topLevelChildren = graph.GetChildren(project.Id).ToList();
        if (topLevelChildren.Count == 0)
        {
            topLevelChildren = graph.Nodes
                .Where(n => n.Id != project.Id && (n.ParentId == null || n.ParentId == project.Id))
                .ToList();
        }

        // Raggruppa i file di primo livello per tipologia (Kind)
        var groups = topLevelChildren
            .GroupBy(c => c.Kind)
            .OrderBy(g => GetGroupOrder(g.Key));

        foreach (var group in groups)
        {
            var groupKind = group.Key;
            var groupItems = group.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var groupName = GetGroupName(groupKind);
            var (badge, bg, fg) = GetBadgeInfo(groupKind);

            var groupFolderItem = new ExplorerTreeItem
            {
                Name = groupName,
                DisplayText = $"{groupName} ({groupItems.Count})",
                Kind = groupKind,
                NodeId = Guid.Empty,
                KindBadge = badge,
                BadgeBackground = bg,
                BadgeForeground = fg,
                IsGroup = true,
                FilePath = projectFolderPath
            };

            foreach (var childNode in groupItems)
            {
                groupFolderItem.Children.Add(Build(graph, childNode, projectFolderPath));
            }

            rootItem.Children.Add(groupFolderItem);
        }

        return rootItem;
    }

    private static ExplorerTreeItem Build(VisualCodeGraph graph, GraphNode node, string? projectFolderPath = null)
    {
        var (badge, bg, fg) = GetBadgeInfo(node.Kind);
        var nodeFilePath = ResolvePath(node.SemanticSymbol?.FilePath, projectFolderPath);

        var item = new ExplorerTreeItem
        {
            Name = node.Name,
            DisplayText = node.Name,
            Kind = node.Kind,
            NodeId = node.Id,
            KindBadge = badge,
            BadgeBackground = bg,
            BadgeForeground = fg,
            FilePath = nodeFilePath,
            LineStart = node.SemanticSymbol?.LineStart ?? 0,
            LineEnd = node.SemanticSymbol?.LineEnd ?? 0
        };

        foreach (var child in graph.GetChildren(node.Id).OrderBy(c => c.Kind).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var childItem = Build(graph, child, projectFolderPath);
            if (string.IsNullOrEmpty(childItem.FilePath) && !string.IsNullOrEmpty(item.FilePath))
            {
                childItem = new ExplorerTreeItem
                {
                    Name = childItem.Name,
                    DisplayText = childItem.DisplayText,
                    Kind = childItem.Kind,
                    NodeId = childItem.NodeId,
                    KindBadge = childItem.KindBadge,
                    BadgeBackground = childItem.BadgeBackground,
                    BadgeForeground = childItem.BadgeForeground,
                    FilePath = item.FilePath,
                    LineStart = childItem.LineStart,
                    LineEnd = childItem.LineEnd
                };
            }
            item.Children.Add(childItem);
        }

        return item;
    }

    public static string? ResolvePath(string? filePath, string? baseFolder)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        if (Path.IsPathRooted(filePath) && (File.Exists(filePath) || Directory.Exists(filePath))) return filePath;
        if (!string.IsNullOrEmpty(baseFolder))
        {
            var combined = Path.Combine(baseFolder, filePath);
            if (File.Exists(combined) || Directory.Exists(combined)) return combined;
            return combined;
        }
        return filePath;
    }

    public static (string Badge, string Background, string Foreground) GetBadgeInfo(SymbolKind kind) => kind switch
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
        SymbolKind.Event => ("EVT", "#382D14", "#E5C07B"),
        _ => ("•", "#222230", "#8A8A9E")
    };

    private static int GetGroupOrder(SymbolKind kind) => kind switch
    {
        SymbolKind.Form => 1,
        SymbolKind.Module => 2,
        SymbolKind.Class => 3,
        SymbolKind.UserControl => 4,
        SymbolKind.Type => 5,
        SymbolKind.Enum => 6,
        _ => 100 + (int)kind
    };

    private static string GetGroupName(SymbolKind kind)
    {
        var loc = LocalizationService.Instance;
        return kind switch
        {
            SymbolKind.Form => loc["Explorer_Group_Forms"],
            SymbolKind.Module => loc["Explorer_Group_Modules"],
            SymbolKind.Class => loc["Explorer_Group_Classes"],
            SymbolKind.UserControl => loc["Explorer_Group_UserControls"],
            SymbolKind.Type => loc["Explorer_Group_Types"],
            SymbolKind.Enum => loc["Explorer_Group_Enums"],
            _ => loc["Explorer_Group_Other"]
        };
    }
}
