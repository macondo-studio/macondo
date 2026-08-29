namespace VB6Analyzer.Semantic.Model;

/// <summary>
/// Categoria del simbolo, esattamente come elencata nella sezione 8 del charter.
/// </summary>
public enum SymbolKind
{
    Project,
    Form,
    Class,
    Module,
    UserControl,
    Method,
    Property,
    Field,
    Constant,
    Enum,
    Type,
    Event
}

/// <summary>
/// Modello Symbol come descritto nella sezione 9 del project charter. A differenza
/// del GraphNode "finto" usato nel POC Avalonia (M0), questo è il modello reale,
/// popolato dal vero parser (M1) tramite il SymbolExtractor (M2).
///
/// Campi come Visibility, Signature, ReturnType, Parameters (menzionati in sezione 9
/// come possibili aggiunte future) non sono ancora presenti: aggiunti quando servirà
/// davvero, per non anticipare decisioni di modellazione non ancora necessarie.
/// </summary>
public sealed class Symbol
{
    public required Guid Id { get; init; }
    public required SymbolKind Kind { get; init; }
    public required string Name { get; init; }
    public required string QualifiedName { get; set; }
    public required string FilePath { get; init; }
    public int LineStart { get; init; }
    public int LineEnd { get; set; }

    public override string ToString() => $"{Kind} {QualifiedName}";
}
