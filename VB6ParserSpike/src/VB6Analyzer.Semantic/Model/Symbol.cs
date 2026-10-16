namespace Macondo.Semantic.Model;


/// <summary>
/// Rappresenta un'entità / nodo all'interno del <see cref="CodeGraph"/>.
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
    /// Popolato dal <see cref="Macondo.Semantic.Resolution.TypeResolver"/>.
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

    /// <summary>Linguaggio di programmazione sorgente del simbolo (es. VB6, C#, C++, ecc.).</summary>
    public SourceLanguage Language { get; set; } = SourceLanguage.Unknown;

    /// <summary>
    /// Restituisce il linguaggio effettivo del simbolo, rilevandolo dall'estensione del FilePath se non impostato esplicitamente.
    /// </summary>
    public SourceLanguage GetEffectiveLanguage()
    {
        if (Language != SourceLanguage.Unknown)
            return Language;

        if (!string.IsNullOrEmpty(FilePath))
        {
            var ext = System.IO.Path.GetExtension(FilePath).ToLowerInvariant();
            return ext switch
            {
                ".vbp" or ".vbg" or ".frm" or ".bas" or ".cls" or ".ctl" or ".pag" or ".dsx" or ".dob" => SourceLanguage.VisualBasic6,
                ".csproj" or ".sln" or ".cs" => SourceLanguage.CSharp,
                ".cpp" or ".c" or ".h" or ".hpp" or ".vcxproj" => SourceLanguage.CPlusPlus,
                ".java" => SourceLanguage.Java,
                ".kt" or ".kts" => SourceLanguage.Kotlin,
                ".py" => SourceLanguage.Python,
                ".ts" => SourceLanguage.TypeScript,
                ".js" => SourceLanguage.JavaScript,
                _ => SourceLanguage.Unknown
            };
        }

        return SourceLanguage.Unknown;
    }

    public override string ToString() => $"{Accessibility} {Kind} {QualifiedName}{(DeclaredTypeName != null ? $" As {DeclaredTypeName}" : "")}";
}
