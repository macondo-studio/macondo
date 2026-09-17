namespace Macondo.Semantic.Model;

/// <summary>
/// Categoria del simbolo semantico.
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
    Event,

    /// <summary>Interfaccia (interface IFoo).</summary>
    Interface,

    /// <summary>Struttura (struct Point).</summary>
    Struct,

    /// <summary>Record (record Person).</summary>
    Record,

    /// <summary>Namespace di raggruppamento logico.</summary>
    Namespace
}

/// <summary>
/// Linguaggio di programmazione sorgente del simbolo o progetto.
/// </summary>
public enum SourceLanguage
{
    /// <summary>Linguaggio sconosciuto o non specificato.</summary>
    Unknown,

    /// <summary>Microsoft Visual Basic 6.0 / VBA legacy.</summary>
    VisualBasic6,

    /// <summary>C# (.NET / Roslyn).</summary>
    CSharp,

    /// <summary>C / C++.</summary>
    CPlusPlus,

    /// <summary>Java.</summary>
    Java,

    /// <summary>Python.</summary>
    Python,

    /// <summary>TypeScript.</summary>
    TypeScript,

    /// <summary>JavaScript.</summary>
    JavaScript
}

/// <summary>
/// Tipologie di relazioni / archi tra simboli nel CodeGraph.
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
/// Livello di confidenza / certezza della risoluzione semantica di un simbolo o relazione.
/// In VB6, costrutti come Variant, Object o CreateObject("...") non possono essere risolti staticamente con certezza assoluta.
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
/// Livelli di visibilità e modificatori di accesso dei simboli in VB6 e C#.
/// </summary>
public enum Accessibility
{
    /// <summary>Accessibilità predefinita implicita (Private per membri di classe/form, Public per membri di moduli .bas).</summary>
    Default,

    /// <summary>Accessibile solo all'interno del modulo/classe corrente ('Private' / 'private').</summary>
    Private,

    /// <summary>Accessibile pubblicamente all'intero progetto o dall'esterno ('Public' / 'public').</summary>
    Public,

    /// <summary>Accessibile a tutti i moduli all'interno dello stesso progetto/componente ('Friend').</summary>
    Friend,

    /// <summary>Dichiarazione a livello globale per moduli standard legacy ('Global').</summary>
    Global,

    /// <summary>Accessibile dalla classe corrente e dalle classi derivate ('protected').</summary>
    Protected,

    /// <summary>Accessibile all'interno dello stesso assembly ('internal').</summary>
    Internal,

    /// <summary>Accessibile all'interno dello stesso assembly o dalle classi derivate ('protected internal').</summary>
    ProtectedInternal,

    /// <summary>Accessibile solo dalle classi derivate all'interno dello stesso assembly ('private protected').</summary>
    PrivateProtected
}

/// <summary>
/// Livello di rischio stimato per la modifica di un simbolo in base al suo Blast Radius.
/// </summary>
public enum ImpactRiskLevel
{
    /// <summary>Rischio Basso: nessun chiamante o solo chiamanti privati interni alla stessa classe.</summary>
    Low,

    /// <summary>Rischio Medio: chiamanti in altre classi, ma nessuna Form UI direttamente impattata.</summary>
    Medium,

    /// <summary>Rischio Alto: più classi e almeno una Form UI impattata dalla modifica.</summary>
    High,

    /// <summary>Rischio Critico: elevato numero di form, classi o moduli impattati con catena di chiamate profonda.</summary>
    Critical
}

/// <summary>
/// Colori di stato dei nodi per l'algoritmo di visita DFS a 3 stati (White/Gray/Black)
/// utilizzato per il rilevamento di cicli di invocazione ricorsiva nel Call Graph.
/// </summary>
public enum NodeColor
{
    /// <summary>Non ancora visitato.</summary>
    White,

    /// <summary>In corso di visita nello stack di chiamata corrente (se incontrato indica un ciclo ricorsivo).</summary>
    Gray,

    /// <summary>Visita completata con successo per tutti i discendenti.</summary>
    Black
}

/// <summary>
/// Modalità di evidenziazione visiva attiva nel canvas di visualizzazione del grafo (<see cref="Controls.GraphControl"/>).
/// </summary>
public enum HighlightMode
{
    /// <summary>Visualizzazione standard con evidenziazione del nodo selezionato e dei vicini immediati.</summary>
    Normal,

    /// <summary>Evidenziazione gerarchica di tutti i chiamanti della routine selezionata (Who calls me?).</summary>
    Callers,

    /// <summary>Evidenziazione gerarchica di tutte le routine chiamate (What do I call?).</summary>
    Callees,

    /// <summary>Evidenziazione completa del Blast Radius / Impatto di modifica con gradiente di profondità e Form UI toccate.</summary>
    BlastRadius
}
