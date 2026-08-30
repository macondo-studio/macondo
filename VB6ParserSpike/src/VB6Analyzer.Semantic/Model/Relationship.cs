namespace VB6Analyzer.Semantic.Model;

/// <summary>
/// Tipologie di relazioni / archi tra simboli nel CodeGraph (sezione 8 del charter).
/// </summary>
public enum RelationshipKind
{
    /// <summary>Relazione strutturale gerarchica di contenimento (es. Modulo contiene Metodo, Metodo contiene Variabile locale).</summary>
    Contains,

    /// <summary>Dichiarazione formale di un elemento.</summary>
    Declares,

    /// <summary>Invocazione / chiamata da una routine a un'altra (es. Customer.Save -> Database.Execute).</summary>
    Calls,

    /// <summary>Riferimento generico a un simbolo o libreria esterna.</summary>
    References,

    /// <summary>Utilizzo di un tipo come tipo di dato di una variabile, parametro o ritorno (es. Dim c As Customer).</summary>
    UsesType,

    /// <summary>Ereditarietà (non comune in VB6 classico, supportata nel modello agnostico).</summary>
    Inherits,

    /// <summary>Implementazione di interfaccia tramite keyword 'Implements' (es. Implements ICustomerRepository).</summary>
    Implements,

    /// <summary>Istanziazione di un oggetto tramite 'New' o 'Set ... = New' (es. Set c = New Customer).</summary>
    Instantiates,

    /// <summary>Gestione di un evento (es. cmdSave.Click -> Form1.cmdSave_Click).</summary>
    HandlesEvent,

    /// <summary>Accesso in lettura a un campo o proprietà.</summary>
    Reads,

    /// <summary>Accesso in scrittura / assegnazione a un campo o proprietà.</summary>
    Writes
}

/// <summary>
/// Livello di confidenza / certezza della risoluzione semantica di un simbolo o relazione (sezione 11 del charter).
/// 
/// In VB6, costrutti come Variant, Object o CreateObject("...") non possono essere risolti staticamente con certezza assoluta.
/// Il sistema non inventa relazioni false ma riflette esplicitamente il grado di certezza.
/// </summary>
public enum ResolutionConfidence
{
    /// <summary>Risoluzione statica certa e deterministica al 100% (es. tipo esplicito 'Dim c As Customer: c.Save').</summary>
    Exact,

    /// <summary>Risoluzione dedotta tramite inferenza o euristica contestuale.</summary>
    Inferred,

    /// <summary>Presenza di molteplici candidati compatibili con lo stesso nome senza disambiguazione univoca.</summary>
    Ambiguous,

    /// <summary>Target non risolvibile staticamente (es. late binding su 'Dim obj As Object: obj.Save').</summary>
    Unknown
}

/// <summary>
/// Rappresenta una relazione diretta / arco orientato tra due simboli all'interno del <see cref="CodeGraph"/> (sezione 10 del charter).
/// </summary>
public sealed class Relationship
{
    /// <summary>ID del simbolo sorgente della relazione.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>ID del simbolo di destinazione della relazione.</summary>
    public required Guid TargetId { get; init; }

    /// <summary>Tipologia della relazione (Contains, Calls, Instantiates, UsesType, ecc.).</summary>
    public required RelationshipKind Kind { get; init; }

    /// <summary>Numero di riga nel file sorgente in cui si origina la relazione (opzionale).</summary>
    public int? Line { get; init; }

    /// <summary>Grado di certezza della relazione semantica.</summary>
    public ResolutionConfidence Confidence { get; init; } = ResolutionConfidence.Exact;
}
