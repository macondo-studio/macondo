namespace VB6Analyzer.App.Models;

/// <summary>
/// Categoria del nodo. Rispecchia (in forma ridotta) il SymbolKind
/// descritto nella sezione 8-9 del project charter.
/// </summary>
public enum NodeKind
{
    Project,
    Form,
    Class,
    Module,
    Method
}

/// <summary>
/// Nodo del CodeGraph fittizio usato dal POC.
/// Non rappresenta ancora un vero Symbol VB6 (quello arriverà con M2/M3),
/// serve solo a validare rendering, layout e interazione del GraphControl.
/// </summary>
public sealed class GraphNode
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required NodeKind Kind { get; init; }

    /// <summary>Nodo padre nella gerarchia CONTAINS (null per il Project root).</summary>
    public Guid? ParentId { get; init; }

    /// <summary>Posizione nel layout, calcolata dal GraphLayout (vedi sezione 23).</summary>
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Raggio di disegno, dipende dal Kind (progressive disclosure, sezione 21).</summary>
    public double Radius => Kind switch
    {
        NodeKind.Project => 26,
        NodeKind.Form or NodeKind.Class or NodeKind.Module => 18,
        NodeKind.Method => 8,
        _ => 10
    };

    /// <summary>
    /// Stato di espansione: i figli (es. i Method di una Class) sono visibili
    /// solo se il nodo è espanso. Vedi "expand/collapse" sezione 22.
    /// </summary>
    public bool IsExpanded { get; set; }

    public override string ToString() => $"{Kind}: {Name}";
}
