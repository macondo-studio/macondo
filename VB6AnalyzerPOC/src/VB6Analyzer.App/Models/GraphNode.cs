using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Nodo visuale del CodeGraph renderizzato dal GraphControl (sezione 21 e 24 del charter).
/// Incapsula il <see cref="Symbol"/> semantico reale estratto dalla pipeline di analisi VB6
/// e mantiene le coordinate per il layout e lo stato di interazione (zoom, selezione, espansione).
/// </summary>
public sealed class GraphNode
{
    /// <summary>ID univoco del nodo (corrisponde al Symbol.Id per i simboli reali).</summary>
    public required Guid Id { get; init; }

    /// <summary>Nome visualizzato del simbolo.</summary>
    public required string Name { get; init; }

    /// <summary>Nome qualificato completo (es. "Customer.Save").</summary>
    public required string QualifiedName { get; init; }

    /// <summary>Categoria del simbolo semantico.</summary>
    public required SymbolKind Kind { get; init; }

    /// <summary>Il simbolo semantico reale sottostante (null per il nodo radice di progetto sintetico).</summary>
    public Symbol? SemanticSymbol { get; init; }

    /// <summary>Nodo contenitore padre nella gerarchia CONTAINS (null per il Project root).</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Posizione X nel grafo (calcolata dal layout engine).</summary>
    public double X { get; set; }

    /// <summary>Posizione Y nel grafo (calcolata dal layout engine).</summary>
    public double Y { get; set; }

    /// <summary>Raggio di disegno del nodo in pixel.</summary>
    public double Radius => Kind switch
    {
        SymbolKind.Project => 28,
        SymbolKind.Form or SymbolKind.Class or SymbolKind.Module or SymbolKind.UserControl => 20,
        SymbolKind.Method or SymbolKind.Property => 11,
        SymbolKind.Field or SymbolKind.Constant or SymbolKind.Enum or SymbolKind.Type => 9,
        _ => 10
    };

    /// <summary>
    /// Indica se il nodo è un container espandibile (Class, Form, Module, Type, Enum).
    /// </summary>
    public bool IsContainer => Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module
                                    or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum;

    /// <summary>
    /// Stato di espansione: i membri figli sono visibili solo se il container è espanso (progressive disclosure).
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    public override string ToString() => $"{Kind}: {QualifiedName}";
}
