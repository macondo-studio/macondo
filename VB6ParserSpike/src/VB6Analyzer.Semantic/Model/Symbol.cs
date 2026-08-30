namespace VB6Analyzer.Semantic.Model;

/// <summary>
/// Categoria del simbolo semantico, allineata con l'elenco della sezione 8 del charter.
/// Rappresenta la tipologia di elemento nel modello concettuale del codice VB6.
/// </summary>
public enum SymbolKind
{
    /// <summary>Progetto VB6 (.vbp).</summary>
    Project,

    /// <summary>Form / Finestra visuale (.frm).</summary>
    Form,

    /// <summary>Modulo di classe (.cls).</summary>
    Class,

    /// <summary>Modulo standard di codice (.bas).</summary>
    Module,

    /// <summary>Controllo utente ActiveX (.ctl).</summary>
    UserControl,

    /// <summary>Metodo o procedura (Sub o Function).</summary>
    Method,

    /// <summary>Proprietà (Property Get / Let / Set unificate).</summary>
    Property,

    /// <summary>Campo o variabile (sia a livello di modulo che variabile locale a una routine).</summary>
    Field,

    /// <summary>Costante (Const a livello di modulo o locale, o singolo valore di un Enum).</summary>
    Constant,

    /// <summary>Enumerazione (Enum ... End Enum).</summary>
    Enum,

    /// <summary>Tipo definito dall'utente / Struttura UDT (Type ... End Type).</summary>
    Type,

    /// <summary>Evento dichiarato in una classe o UserControl (Event ...).</summary>
    Event
}

/// <summary>
/// Rappresenta un'entità / nodo all'interno del <see cref="CodeGraph"/> (sezioni 8 e 9 del charter).
/// 
/// Ogni dichiarazione identificata nel sorgente VB6 (moduli, metodi, variabili, tipi) produce
/// un'istanza di Symbol con identificatore univoco <see cref="Id"/>, posizione nel file,
/// metadati di tipo dichiarato, visibilità e relazioni con altri simboli.
/// </summary>
public sealed class Symbol
{
    /// <summary>Identificatore univoco globale del simbolo nel grafo.</summary>
    public required Guid Id { get; init; }

    /// <summary>Tipologia del simbolo (Class, Method, Field, ecc.).</summary>
    public required SymbolKind Kind { get; init; }

    /// <summary>Nome semplice non qualificato del simbolo (es. "Save", "mCount", "Customer").</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Nome qualificato completo all'interno della gerarchia (es. "Customer.Save", "Customer.Save.localCust").
    /// </summary>
    public required string QualifiedName { get; set; }

    /// <summary>Percorso assoluto o relativo del file sorgente in cui è dichiarato il simbolo.</summary>
    public required string FilePath { get; init; }

    /// <summary>Numero di riga di inizio della dichiarazione (1-indexed).</summary>
    public int LineStart { get; init; }

    /// <summary>Numero di riga di fine della dichiarazione / blocco (1-indexed).</summary>
    public int LineEnd { get; set; }

    /// <summary>Livello di visibilità del simbolo (Public, Private, Global, Friend, Default).</summary>
    public Accessibility Accessibility { get; set; } = Accessibility.Default;

    /// <summary>
    /// Nome testuale del tipo dichiarato nel sorgente (es. "Customer", "Long", "String", "Object").
    /// Se il tipo non è specificato in VB6, è implicitamente Variant.
    /// </summary>
    public string? DeclaredTypeName { get; set; }

    /// <summary>
    /// ID del simbolo del tipo nel CodeGraph se si tratta di un tipo definito dall'utente/progetto (es. Class, Type, Enum).
    /// Popolato dal <see cref="VB6Analyzer.Semantic.Resolution.TypeResolver"/>.
    /// </summary>
    public Guid? ResolvedTypeSymbolId { get; set; }

    /// <summary>Tipo di ritorno dichiarato per Function e Property Get (es. "Boolean", "Double").</summary>
    public string? ReturnType { get; set; }

    /// <summary>ID del simbolo corrispondente al tipo di ritorno nel CodeGraph.</summary>
    public Guid? ResolvedReturnTypeSymbolId { get; set; }

    /// <summary>Elenco dei parametri formali accettati dalla routine (se Method o Property).</summary>
    public List<ParameterSymbol> Parameters { get; } = new();

    /// <summary>Indica se la routine o variabile è stata dichiarata con la keyword 'Static'.</summary>
    public bool IsStatic { get; set; }

    /// <summary>Indica se la variabile/campo è un array (es. Dim arr(10) As String).</summary>
    public bool IsArray { get; set; }

    /// <summary>
    /// Indica se la variabile è stata dichiarata con auto-istanziazione (es. "Dim c As New Customer").
    /// </summary>
    public bool IsNewInstance { get; set; }

    public override string ToString() => $"{Accessibility} {Kind} {QualifiedName}{(DeclaredTypeName != null ? $" As {DeclaredTypeName}" : "")}";
}
