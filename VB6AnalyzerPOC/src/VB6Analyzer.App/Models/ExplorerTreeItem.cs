using System.ComponentModel;
using Macondo.App.Services;
using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Nodo gerarchico per il Project Explorer.
/// Wrappa un <see cref="GraphNode"/> per la visualizzazione ad albero nel TreeView,
/// raggruppando i file di primo livello per tipologia (Forms, Moduli, Moduli di classe, Controlli utente, ecc.).
/// </summary>
public sealed class ExplorerTreeItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Name { get; init; }
    public required string DisplayText { get; init; }
    public required SymbolKind Kind { get; init; }
    public required Guid NodeId { get; init; }
    public required string KindBadge { get; init; }
    public required string BadgeBackground { get; init; }
    public required string BadgeForeground { get; init; }
    public bool IsGroup { get; init; }
    public bool IsProjectRoot { get; init; }
    public Guid? ProjectId { get; init; }

    private bool _isProjectVisible = true;
    public bool IsProjectVisible
    {
        get => _isProjectVisible;
        set
        {
            if (_isProjectVisible != value)
            {
                _isProjectVisible = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsProjectVisible)));
            }
        }
    }

    public string? AccessibilityText { get; init; }
    public string? AccessibilityBackground { get; init; }
    public string? AccessibilityForeground { get; init; }
    public string? AccessibilityTooltip { get; init; }
    public bool HasAccessibility => !string.IsNullOrEmpty(AccessibilityText);

    public string? LanguageBadgeText { get; init; }
    public string? LanguageBadgeBackground { get; init; }
    public string? LanguageBadgeForeground { get; init; }
    public string? LanguageTooltip { get; init; }
    public bool HasLanguageBadge => !string.IsNullOrEmpty(LanguageBadgeText);

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
    public static ExplorerTreeItem BuildTree(VisualCodeGraph graph, string? projectFolderPath = null, IReadOnlySet<Guid>? hiddenProjectIds = null)
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

        var projects = graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        if (projects.Count > 1)
        {
            // Workspace multi-progetto: crea una radice Workspace contenente ciascun progetto
            var (wsBadge, wsBg, wsFg) = GetBadgeInfo(SymbolKind.Project);
            var workspaceItem = new ExplorerTreeItem
            {
                Name = "Workspace",
                DisplayText = LocalizationService.Instance.Get("Explorer_WorkspaceFormat", projects.Count),
                Kind = SymbolKind.Project,
                NodeId = Guid.Empty,
                KindBadge = "WS",
                BadgeBackground = "#6366F1",
                BadgeForeground = "#FFFFFF",
                FilePath = projectFolderPath
            };

            foreach (var proj in projects)
            {
                workspaceItem.Children.Add(BuildProjectSubTree(graph, proj, projectFolderPath, isWorkspace: true, hiddenProjectIds: hiddenProjectIds));
            }

            return workspaceItem;
        }

        var singleProject = projects.FirstOrDefault() ?? graph.Nodes.First();
        return BuildProjectSubTree(graph, singleProject, projectFolderPath, isWorkspace: false, hiddenProjectIds: hiddenProjectIds);
    }

    private static ExplorerTreeItem BuildProjectSubTree(VisualCodeGraph graph, GraphNode project, string? projectFolderPath, bool isWorkspace = false, IReadOnlySet<Guid>? hiddenProjectIds = null)
    {
        var (prjBadge, prjBg, prjFg) = GetBadgeInfo(project.Kind);
        var detectedLang = DetectProjectLanguage(graph, project, projectFolderPath);
        var (langBadge, langBg, langFg, langTip) = GetLanguageBadgeInfo(detectedLang);
        var isVis = hiddenProjectIds == null || !hiddenProjectIds.Contains(project.Id);
        var projectItem = new ExplorerTreeItem
        {
            Name = project.Name,
            DisplayText = project.Name,
            Kind = project.Kind,
            NodeId = project.Id,
            IsProjectRoot = isWorkspace,
            ProjectId = project.Id,
            IsProjectVisible = isVis,
            KindBadge = prjBadge,
            BadgeBackground = prjBg,
            BadgeForeground = prjFg,
            LanguageBadgeText = langBadge,
            LanguageBadgeBackground = langBg,
            LanguageBadgeForeground = langFg,
            LanguageTooltip = langTip,
            FilePath = ResolvePath(project.SemanticSymbol?.FilePath, projectFolderPath) ?? projectFolderPath
        };

        var topLevelChildren = graph.GetChildren(project.Id).ToList();
        if (topLevelChildren.Count == 0)
        {
            topLevelChildren = graph.Nodes
                .Where(n => n.Id != project.Id && (n.ParentId == null || n.ParentId == project.Id) && n.Kind != SymbolKind.Project)
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
                FilePath = null
            };

            foreach (var childNode in groupItems)
            {
                groupFolderItem.Children.Add(Build(graph, childNode, projectFolderPath));
            }

            projectItem.Children.Add(groupFolderItem);
        }

        return projectItem;
    }

    private static ExplorerTreeItem Build(VisualCodeGraph graph, GraphNode node, string? projectFolderPath = null)
    {
        var (badge, bg, fg) = GetBadgeInfo(node.Kind);
        var (accText, accBg, accFg, accTip) = GetAccessibilityInfo(node.SemanticSymbol?.Accessibility, node.Kind);
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
            AccessibilityText = accText,
            AccessibilityBackground = accBg,
            AccessibilityForeground = accFg,
            AccessibilityTooltip = accTip,
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
                    AccessibilityText = childItem.AccessibilityText,
                    AccessibilityBackground = childItem.AccessibilityBackground,
                    AccessibilityForeground = childItem.AccessibilityForeground,
                    AccessibilityTooltip = childItem.AccessibilityTooltip,
                    FilePath = item.FilePath,
                    LineStart = childItem.LineStart,
                    LineEnd = childItem.LineEnd
                };
            }
            item.Children.Add(childItem);
        }

        return item;
    }

    public static (string? Text, string? Background, string? Foreground, string? Tooltip) GetAccessibilityInfo(Accessibility? accessibility, SymbolKind kind)
    {
        // Visualizza il tag di accessibilità per routine, proprietà, campi, costanti, tipi, enum ed eventi
        if (accessibility == null) return (null, null, null, null);

        var acc = accessibility.Value;
        if (acc == Accessibility.Default)
        {
            // In VB6 le routine (Sub, Function, Property) senza specificatore esplicito sono Public di default
            if (kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Event)
                return ("Pub", "#1C3328", "#4ADE80", "Visibilità: Public (Default)");
            return (null, null, null, null);
        }

        return acc switch
        {
            Accessibility.Public => ("Pub", "#1C3328", "#4ADE80", "Visibilità: Public"),
            Accessibility.Private => ("Priv", "#382024", "#F87171", "Visibilità: Private"),
            Accessibility.Friend or Accessibility.Internal => ("Int", "#382D14", "#FBBF24", "Visibilità: Internal / Friend"),
            Accessibility.Protected => ("Prot", "#2A2A48", "#A78BFA", "Visibilità: Protected"),
            Accessibility.ProtectedInternal => ("ProtInt", "#382D14", "#FBBF24", "Visibilità: Protected Internal"),
            Accessibility.PrivateProtected => ("PrivProt", "#382024", "#F87171", "Visibilità: Private Protected"),
            Accessibility.Global => ("Glob", "#1C2D3D", "#60A5FA", "Visibilità: Global"),
            _ => (null, null, null, null)
        };
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
        SymbolKind.Interface => ("INT", "#1C3328", "#4ADE80"),
        SymbolKind.Record => ("REC", "#382D14", "#E5C07B"),
        SymbolKind.Struct => ("STR", "#1C3330", "#56B6C2"),
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
        SymbolKind.Namespace => ("NS", "#2A2A3E", "#A0A0B8"),
        _ => ("•", "#222230", "#8A8A9E")
    };

    private static int GetGroupOrder(SymbolKind kind) => kind switch
    {
        SymbolKind.Form => 1,
        SymbolKind.Module => 2,
        SymbolKind.Class => 3,
        SymbolKind.Interface => 4,
        SymbolKind.Record => 5,
        SymbolKind.Struct => 6,
        SymbolKind.UserControl => 7,
        SymbolKind.Type => 8,
        SymbolKind.Enum => 9,
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
            SymbolKind.Interface => loc["Explorer_Group_Interfaces"],
            SymbolKind.Record => loc["Explorer_Group_Records"],
            SymbolKind.Struct => loc["Explorer_Group_Structs"],
            SymbolKind.UserControl => loc["Explorer_Group_UserControls"],
            SymbolKind.Type => loc["Explorer_Group_Types"],
            SymbolKind.Enum => loc["Explorer_Group_Enums"],
            _ => loc["Explorer_Group_Other"]
        };
    }

    public static (string? Text, string? Background, string? Foreground, string? Tooltip) GetLanguageBadgeInfo(SourceLanguage lang) => lang switch
    {
        SourceLanguage.VisualBasic6 => ("VB6", "#2563EB", "#FFFFFF", "Visual Basic 6.0"),
        SourceLanguage.CSharp => ("C#", "#7C3AED", "#FFFFFF", "C# (.NET)"),
        SourceLanguage.CPlusPlus => ("C++", "#00599C", "#FFFFFF", "C / C++"),
        SourceLanguage.Java => ("JAVA", "#EA580C", "#FFFFFF", "Java"),
        SourceLanguage.Python => ("PY", "#3B82F6", "#FFFFFF", "Python"),
        SourceLanguage.TypeScript => ("TS", "#3178C6", "#FFFFFF", "TypeScript"),
        SourceLanguage.JavaScript => ("JS", "#F59E0B", "#FFFFFF", "JavaScript"),
        _ => (null, null, null, null)
    };

    private static SourceLanguage DetectProjectLanguage(VisualCodeGraph graph, GraphNode project, string? projectFolderPath)
    {
        if (project.Language != SourceLanguage.Unknown)
            return project.Language;

        // Cerca nei nodi figli o associati al progetto
        var projectChildren = graph.GetChildren(project.Id);
        foreach (var child in projectChildren)
        {
            if (child.Language != SourceLanguage.Unknown)
                return child.Language;
        }

        var projectNodes = graph.Nodes.Where(n => n.ProjectId == project.Id || n.ParentId == project.Id);
        foreach (var node in projectNodes)
        {
            if (node.Language != SourceLanguage.Unknown)
                return node.Language;
        }

        // Cerca dall'estensione del file di progetto o della cartella
        var path = project.SemanticSymbol?.FilePath ?? projectFolderPath;
        if (!string.IsNullOrEmpty(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".vbp" or ".vbg" or ".frm" or ".bas" or ".cls" or ".ctl" => SourceLanguage.VisualBasic6,
                ".csproj" or ".sln" or ".cs" => SourceLanguage.CSharp,
                ".cpp" or ".c" or ".h" or ".hpp" or ".vcxproj" => SourceLanguage.CPlusPlus,
                ".java" => SourceLanguage.Java,
                ".py" => SourceLanguage.Python,
                ".ts" => SourceLanguage.TypeScript,
                ".js" => SourceLanguage.JavaScript,
                _ => SourceLanguage.Unknown
            };
        }

        return SourceLanguage.Unknown;
    }
}
