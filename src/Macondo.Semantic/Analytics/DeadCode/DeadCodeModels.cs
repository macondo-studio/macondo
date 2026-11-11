using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.DeadCode;

/// <summary>
/// Category of dead code or orphaned component.
/// </summary>
public enum DeadCodeCategory
{
    /// <summary>Routine (Sub / Function / Property) never called and not identified as Entry Point or Event Handler.</summary>
    UnreachableRoutine,

    /// <summary>Field, Variable or Constant never referenced or read by any routine.</summary>
    UnusedFieldOrConstant,

    /// <summary>Module, Class, Form or Control completely orphaned and isolated from the execution tree.</summary>
    OrphanContainer,

    /// <summary>UDT or Enumeration type not used by any field, parameter, or variable.</summary>
    UnusedType
}

/// <summary>
/// Single code element that is unreachable or orphaned.
/// </summary>
public sealed class DeadCodeItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int LineStart { get; init; }
    public int LineEnd { get; init; }
    public SymbolKind Kind { get; init; }
    public DeadCodeCategory Category { get; init; }

    /// <summary>Estimated code lines of the unused block.</summary>
    public int EstimatedLoc { get; set; }

    /// <summary>Detailed reason why the item is considered dead code.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Indicates whether the symbol has Public/Global visibility (possible external API).</summary>
    public bool IsPublic { get; set; }
}

/// <summary>
/// Dataset completo dell'analisi Dead Code e Raggiungibilità.
/// </summary>
public sealed class DeadCodeDataset
{
    public List<DeadCodeItem> Items { get; init; } = new();
    public int TotalSymbolsAnalyzed { get; set; }
    public int ReachableSymbolsCount { get; set; }
    public int DeadSymbolsCount => Items.Count;
    public double DeadCodeRatio => TotalSymbolsAnalyzed > 0 ? (double)DeadSymbolsCount / TotalSymbolsAnalyzed * 100.0 : 0.0;
    public int TotalDeadLoc => Items.Sum(i => i.EstimatedLoc);

    public int UnreachableRoutinesCount => Items.Count(i => i.Category == DeadCodeCategory.UnreachableRoutine);
    public int UnusedFieldsCount => Items.Count(i => i.Category == DeadCodeCategory.UnusedFieldOrConstant);
    public int OrphanContainersCount => Items.Count(i => i.Category == DeadCodeCategory.OrphanContainer);
}
