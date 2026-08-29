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
/// Modello Symbol arricchito per la risoluzione semantica (M3).
/// Include visibilità, tipi dichiarati e risolti, parametri per routine, ecc.
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

    /// <summary>Livello di visibilità del simbolo (Public, Private, Global, Friend).</summary>
    public Accessibility Accessibility { get; set; } = Accessibility.Default;

    /// <summary>Nome testuale del tipo dichiarato (es. "Customer", "Long", "String", "Object").</summary>
    public string? DeclaredTypeName { get; set; }

    /// <summary>ID del simbolo corrispondente nel CodeGraph se si tratta di un tipo definito dall'utente/progetto.</summary>
    public Guid? ResolvedTypeSymbolId { get; set; }

    /// <summary>Tipo di ritorno (per Function e Property Get).</summary>
    public string? ReturnType { get; set; }

    /// <summary>ID del simbolo del tipo di ritorno nel CodeGraph.</summary>
    public Guid? ResolvedReturnTypeSymbolId { get; set; }

    /// <summary>Parametri formali (per Method e Property).</summary>
    public List<ParameterSymbol> Parameters { get; } = new();

    /// <summary>Indica se la routine o variabile è dichiarata con Static.</summary>
    public bool IsStatic { get; set; }

    /// <summary>Indica se è un array (es. Dim arr(10) As String o Dim arr() As Integer).</summary>
    public bool IsArray { get; set; }

    /// <summary>Indica se è un'istanziazione automatica (es. Dim c As New Customer).</summary>
    public bool IsNewInstance { get; set; }

    public override string ToString() => $"{Accessibility} {Kind} {QualifiedName}{(DeclaredTypeName != null ? $" As {DeclaredTypeName}" : "")}";
}
