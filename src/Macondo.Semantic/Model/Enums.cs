namespace Macondo.Semantic.Model;

/// <summary>
/// Semantic symbol category.
/// Represents the type of element in the conceptual model of the VB6 code.
/// </summary>
public enum SymbolKind
{
    /// <summary>Progetto VB6 (.vbp).</summary>
    Project,

    /// <summary>Form / Visual window (.frm).</summary>
    Form,

    /// <summary>Class module (.cls).</summary>
    Class,

    /// <summary>Standard code module (.bas).</summary>
    Module,

    /// <summary>ActiveX User Control (.ctl).</summary>
    UserControl,

    /// <summary>Method or procedure (Sub or Function).</summary>
    Method,

    /// <summary>Properties (Property Get / Let / Set unified).</summary>
    Property,

    /// <summary>Field or variable (either module-level or variable local to a routine).</summary>
    Field,

    /// <summary>Constant (module-level or local Const, or single value of an Enum).</summary>
    Constant,

    /// <summary>Enumeration (Enum ... End Enum).</summary>
    Enum,

    /// <summary>Tipo definito dall'utente / Struttura UDT (Type ... End Type).</summary>
    Type,

    /// <summary>Event declared in a class or UserControl (Event ...).</summary>
    Event,

    /// <summary>Interface (IFoo interface).</summary>
    Interface,

    /// <summary>Struttura (struct Point).</summary>
    Struct,

    /// <summary>Record (record Person).</summary>
    Record,

    /// <summary>Logical grouping namespace.</summary>
    Namespace
}

/// <summary>
/// Source programming language of the symbol or project.
/// </summary>
public enum SourceLanguage
{
    /// <summary>Unknown or unspecified language.</summary>
    Unknown,

    /// <summary>Microsoft Visual Basic 6.0 / VBA legacy.</summary>
    VisualBasic6,

    /// <summary>C# (.NET / Roslyn).</summary>
    CSharp,

    /// <summary>C / C++.</summary>
    CPlusPlus,

    /// <summary>Java.</summary>
    Java,

    /// <summary>Kotlin (.kt / .kts).</summary>
    Kotlin,

    /// <summary>Python.</summary>
    Python,

    /// <summary>TypeScript.</summary>
    TypeScript,

    /// <summary>JavaScript.</summary>
    JavaScript
}

/// <summary>
/// Types of relationships / arcs between symbols in CodeGraph.
/// </summary>
public enum RelationshipKind
{
    /// <summary>Hierarchical structural containment relationship (e.g. Module contains Method, Method contains Local Variable).</summary>
    Contains,

    /// <summary>Formal declaration of an element.</summary>
    Declares,

    /// <summary>Invocation / call from one routine to another (e.g. Customer.Save -> Database.Execute).</summary>
    Calls,

    /// <summary>Generic reference to an external symbol or library.</summary>
    References,

    /// <summary>Using a type as the data type of a variable, parameter, or return (e.g. Dim c As Customer).</summary>
    UsesType,

    /// <summary>Inheritance (uncommon in classic VB6, supported in model agnostic).</summary>
    Inherits,

    /// <summary>Interface implementation via 'Implements' keyword (e.g. Implements ICustomerRepository).</summary>
    Implements,

    /// <summary>Instantiation of an object via 'New' or 'Set ... = New' (e.g. Set c = New Customer).</summary>
    Instantiates,

    /// <summary>Management of an event (e.g. cmdSave.Click -> Form1.cmdSave_Click).</summary>
    HandlesEvent,

    /// <summary>Read access to a field or property.</summary>
    Reads,

    /// <summary>Write access / assignment to a field or property.</summary>
    Writes
}

/// <summary>
/// Confidence level / certainty of the semantic resolution of a symbol or relationship.
/// In VB6, constructs like Variant, Object, or CreateObject("...") cannot be resolved statically with absolute certainty.
/// </summary>
public enum ResolutionConfidence
{
    /// <summary>100% certain and deterministic static resolution (e.g. explicit type 'Dim c As Customer: c.Save').</summary>
    Exact,

    /// <summary>Resolution inferred via inference or contextual heuristics.</summary>
    Inferred,

    /// <summary>Presence of multiple compatible candidates with the same name without unambiguous disambiguation.</summary>
    Ambiguous,

    /// <summary>Target not statically resolvable (e.g. late binding on 'Dim obj As Object: obj.Save').</summary>
    Unknown
}

/// <summary>
/// Visibility levels and symbol access modifiers in VB6 and C#.
/// </summary>
public enum Accessibility
{
    /// <summary>Implicit default accessibility (Private for class/form members, Public for .bas module members).</summary>
    Default,

    /// <summary>Accessible only within the current module/class ('Private' / 'private').</summary>
    Private,

    /// <summary>Publicly accessible to the entire project or from outside ('Public' / 'public').</summary>
    Public,

    /// <summary>Accessible to all modules within the same project/component ('Friend').</summary>
    Friend,

    /// <summary>Global level declaration for legacy standard modules ('Global').</summary>
    Global,

    /// <summary>Accessible from the current class and derived classes ('protected').</summary>
    Protected,

    /// <summary>Accessible within the same assembly ('internal').</summary>
    Internal,

    /// <summary>Accessible within the same assembly or from derived classes ('protected internal').</summary>
    ProtectedInternal,

    /// <summary>Accessible only from derived classes within the same assembly ('private protected').</summary>
    PrivateProtected
}

/// <summary>
/// Estimated risk level for editing a symbol based on its Blast Radius.
/// </summary>
public enum ImpactRiskLevel
{
    /// <summary>Low Risk: no callers or only private callers within the same class.</summary>
    Low,

    /// <summary>Medium Risk: callers in other classes, but no Form UI directly impacted.</summary>
    Medium,

    /// <summary>High Risk: multiple classes and at least one Form UI impacted by the change.</summary>
    High,

    /// <summary>Critical Risk: high number of forms, classes or modules impacted with a deep call chain.</summary>
    Critical
}

/// <summary>
/// Node state colors for the 3-state DFS visit algorithm (White/Gray/Black)
/// used for detecting recursive invocation loops in the Call Graph.
/// </summary>
public enum NodeColor
{
    /// <summary>Not visited yet.</summary>
    White,

    /// <summary>Visiting the current call stack (if encountered indicates a recursive loop).</summary>
    Gray,

    /// <summary>Visit completed successfully for all descendants.</summary>
    Black
}

/// <summary>
/// Visual highlighting mode active in the graph visualization canvas (<see cref="Controls.GraphControl"/>).
/// </summary>
public enum HighlightMode
{
    /// <summary>Standard display with highlighting of the selected node and immediate neighbors.</summary>
    Normal,

    /// <summary>Hierarchical highlighting of all the callers of the selected routine (Who calls me?).</summary>
    Callers,

    /// <summary>Hierarchical highlighting of all called routines (What do I call?).</summary>
    Callees,

    /// <summary>Full Blast Radius Highlight/Edit Impact with Depth Gradient and Form UI Touched.</summary>
    BlastRadius
}
