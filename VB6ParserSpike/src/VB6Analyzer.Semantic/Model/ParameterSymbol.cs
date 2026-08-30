namespace VB6Analyzer.Semantic.Model;

/// <summary>
/// Rappresenta un parametro formale nella firma di una routine (Sub, Function, Property) in VB6.
/// 
/// CARATTERISTICHE VB6:
/// - Modalità di passaggio: 'ByRef' è il default in VB6 se omesso; 'ByVal' crea una copia.
/// - Parametri opzionali: 'Optional' con eventuale valore di default.
/// - Parametri variabili: 'ParamArray' (array di Variant).
/// </summary>
public sealed class ParameterSymbol
{
    /// <summary>Nome dell'argomento formale (es. "cust", "taxRate").</summary>
    public required string Name { get; init; }

    /// <summary>Nome del tipo dichiarato (es. "Customer", "Double", "Single").</summary>
    public string? DeclaredTypeName { get; set; }

    /// <summary>
    /// ID del simbolo corrispondente nel CodeGraph se si tratta di un tipo definito dall'utente/progetto.
    /// </summary>
    public Guid? ResolvedTypeSymbolId { get; set; }

    /// <summary>Indica se il parametro è passato per riferimento (default in VB6).</summary>
    public bool IsByRef { get; init; } = true;

    /// <summary>Indica se il parametro è passato esplicitamente per valore ('ByVal').</summary>
    public bool IsByVal { get; init; }

    /// <summary>Indica se il parametro è opzionale ('Optional').</summary>
    public bool IsOptional { get; init; }

    /// <summary>Indica se il parametro accetta una lista variabile di argomenti ('ParamArray').</summary>
    public bool IsParamArray { get; init; }

    /// <summary>Espressione del valore di default testuale per parametri opzionali (es. "0#", "\"default\"").</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Riga del file sorgente in cui è dichiarato il parametro.</summary>
    public int Line { get; init; }

    public override string ToString() =>
        $"{(IsOptional ? "Optional " : "")}{(IsByVal ? "ByVal " : "ByRef ")}{(IsParamArray ? "ParamArray " : "")}{Name}{(DeclaredTypeName != null ? $" As {DeclaredTypeName}" : "")}";
}
