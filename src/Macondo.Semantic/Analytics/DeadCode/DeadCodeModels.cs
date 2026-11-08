using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.DeadCode;

/// <summary>
/// Categoria di codice morto o componente orfano.
/// </summary>
public enum DeadCodeCategory
{
    /// <summary>Routine (Sub / Function / Property) mai chiamata e non identificata come Entry Point o Event Handler.</summary>
    UnreachableRoutine,

    /// <summary>Campo, Variabile o Costante mai referenziata o letta da nessuna routine.</summary>
    UnusedFieldOrConstant,

    /// <summary>Modulo, Classe, Form o Controllo completamente orfano e isolato dall'albero di esecuzione.</summary>
    OrphanContainer,

    /// <summary>Tipo UDT o Enumerazione non utilizzato da nessun campo, parametro o variabile.</summary>
    UnusedType
}

/// <summary>
/// Singolo elemento di codice non raggiungibile o orfano.
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

    /// <summary>Linee di codice stimate del blocco non utilizzato.</summary>
    public int EstimatedLoc { get; set; }

    /// <summary>Motivo dettagliato per cui l'elemento è considerato codice morto.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Indica se il simbolo ha visibilità Public/Global (possibile API esterna).</summary>
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
