using Antlr4.Runtime;
using Macondo.Parser.Grammar;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Semantic symbol extractor based on ANTLR parse tree.
/// 
/// This class implements the ANTLR Visitor pattern (<see cref="VisualBasic6ParserBaseVisitor{TResult}"/>)
/// to walk the abstract syntax tree (AST / Parse Tree) of a single VB6 source file
/// (.cls, .frm, .bas, .ctl) and extract all the declared symbols (Modules, Routines, Properties,
/// Variables, Constants, UDTs, Enums) by inserting them into the shared <see cref="CodeGraph"/>.
/// 
/// PRINCIPI CHIAVE:
/// 1. Parsing/Semantics Separation:
/// The parser produces the raw syntax tree; this visitor interprets it semantically
/// building structured symbols and hierarchical relationships CONTAINS.
/// 2. Hierarchy and Containment (Container Stack):
/// Track the current context (e.g. Module -> Method -> Local Variable) via <see cref="_containerStack"/>,
/// so that the local variables belong to the method and not to the module.
/// 3. Unification of Properties (Get / Let / Set):
/// In VB6 a property can be defined by multiple separate blocks (e.g. "Property Get Name" and
/// "Property Let Name"). This extractor unifies them into a single logical <see cref="Symbol"/> of type Property,
/// aggregating parameters, return type and row range.
/// 4. Metadata Capture for Symbol Resolution:
///    Extracts visibility (Public/Private/Friend/Global), declared types (As Customer, As Long),
///    formal parameters (ByVal/ByRef, Optional, ParamArray), Static/Array/New flags.
/// </summary>
public sealed class SymbolExtractor : VisualBasic6ParserBaseVisitor<object?>
{
    /// <summary>
    /// Most common standard VB6 event names for event handler heuristics.
    /// In VB6, a Sub with the name "ControlName_EventName" (e.g. Command1_Click) is conventionally an event handler.
    /// </summary>
    private static readonly HashSet<string> CommonEventNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Click", "DblClick", "Change", "Load", "Unload", "QueryUnload", "Activate", "Deactivate",
        "Initialize", "Terminate", "Resize", "Paint", "KeyPress", "KeyDown", "KeyUp",
        "GotFocus", "LostFocus", "Timer", "MouseDown", "MouseUp", "MouseMove",
        "DragDrop", "DragOver", "Scroll", "Validate", "RowColChange", "SelChange",
        "ItemCheck", "NodeClick", "Collapse", "Expand", "BeforeUpdate", "AfterUpdate",
        "Error", "DataArrival", "Connect", "Close", "SendComplete", "SendProgress",
        "Show", "Hide", "ReadProperties", "WriteProperties", "DropDown", "OLEStartDrag",
        "OLEDragDrop", "OLEDragOver", "OLESetData", "OLECompleteDrag", "LinkOpen", "LinkClose", "LinkError", "LinkExecute"
    };

    /// <summary>Shared semantic graph in which symbols and relations are inserted CONTAINS.</summary>
    private readonly CodeGraph _graph;

    /// <summary>Root symbol representing the current module/file (.cls, .bas, .frm, .ctl).</summary>
    private readonly Symbol _moduleSymbol;

    /// <summary>Path of the source file being analyzed.</summary>
    private readonly string _filePath;

    /// <summary>
    /// Stack that keeps the active container symbol while traversing the AST.
    /// At the beginning it contains the root module; entering a Sub/Function/Property/UDT/Enum,
    /// the relevant symbol is pushed onto the stack; upon exiting, it is removed.
    /// It allows you to correctly associate CONTAINS relations (e.g. Local Var -> Method).
    /// </summary>
    private readonly Stack<Symbol> _containerStack = new();

    /// <summary>
    /// Support map to unify Property Get, Let, and Set accessors with the same name.
    /// Key: property name (case-insensitive); Value: Unified instance of Symbol.
    /// </summary>
    private readonly Dictionary<string, Symbol> _propertiesByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Current accessibility level inherited from the variable/constant declaration statement.
    /// In VB6, a line like "Public a As Long, b As String" specifies Public only once at the beginning
    /// (variableStmt rule), but declares multiple separate variables (variableSubStmt rules).
    /// </summary>
    private Accessibility _currentVarAccessibility = Accessibility.Default;

    /// <summary>
    /// Flag indicating whether variables declared in the current line are marked "Static".
    /// </summary>
    private bool _currentVarIsStatic;

    /// <summary>
    /// List of Subs identified by the syntactic heuristics as probable event handlers (e.g. "Button1_Click").
    /// </summary>
    public List<string> LikelyEventHandlerNames { get; } = new();

    /// <summary>
    /// List of interfaces implemented via 'Implements' identified in the module.
    /// </summary>
    public List<(string InterfaceName, int Line)> ImplementedInterfaces { get; } = new();

    /// <summary>
    /// Initialize the semantic extractor for a VB6 module.
    /// </summary>
    /// <param name="graph">The shared in-memory CodeGraph.</param>
    /// <param name="moduleSymbol">The symbol of the module/file just created.</param>
    /// <param name="filePath">Path to source file.</param>
    public SymbolExtractor(CodeGraph graph, Symbol moduleSymbol, string filePath)
    {
        _graph = graph;
        _moduleSymbol = moduleSymbol;
        _filePath = filePath;
        _containerStack.Push(moduleSymbol);
    }

    /// <summary>Returns the container symbol currently at the top of the stack.</summary>
    private Symbol CurrentContainer => _containerStack.Peek();

    // =========================================================================
    // UI CONTROL EXTRACTION (Form .frm and UserControl .ctl)
    // =========================================================================

    /// <summary>
    /// Visit the ANTLR rule 'controlProperties' to extract the visual controls defined in Forms (.frm) and UserControl (.ctl).
    /// Example: 'Begin VB.CommandButton cmdClick' -> generate Field symbol 'cmdClick' with type 'VB.CommandButton'.
    /// </summary>
    public override object? VisitControlProperties(VisualBasic6Parser.ControlPropertiesContext context)
    {
        var controlName = context.cp_ControlIdentifier()?.GetText()?.Trim();
        var controlType = context.cp_ControlType()?.GetText()?.Trim() ?? "Control";

        if (!string.IsNullOrEmpty(controlName) &&
            !string.Equals(controlName, _moduleSymbol.Name, StringComparison.OrdinalIgnoreCase))
        {
            var existingControl = _graph.GetChildren(_moduleSymbol.Id)
                .FirstOrDefault(s => string.Equals(s.Name, controlName, StringComparison.OrdinalIgnoreCase));

            if (existingControl == null)
            {
                var controlSymbol = new Symbol
                {
                    Id = Guid.NewGuid(),
                    Kind = SymbolKind.Field,
                    Name = controlName,
                    QualifiedName = $"{_moduleSymbol.Name}.{controlName}",
                    DeclaredTypeName = controlType,
                    Accessibility = Accessibility.Private,
                    Language = SourceLanguage.VisualBasic6,
                    FilePath = _filePath,
                    LineStart = context.Start.Line,
                    // Designer controls are declared on the single line 'Begin ... Name';
                    // they must not extend to the entire block of property to avoid overestimating the LOC (Issue 6)
                    LineEnd = context.Start.Line
                };

                _graph.Symbols.Add(controlSymbol);
                _graph.AddContains(_moduleSymbol, controlSymbol, context.Start.Line);
            }
        }

        return base.VisitControlProperties(context);
    }

    // =========================================================================
    // ROUTINE EXTRACTION (Sub and Function)
    // =========================================================================

    /// <summary>
    /// Visit the ANTLR rule 'subStmt' (declaring a Sub procedure in VB6).
    /// Sintassi VB6: [Private|Public|Friend] [Static] Sub NomeSub([argList]) ... End Sub
    /// </summary>
    public override object? VisitSubStmt(VisualBasic6Parser.SubStmtContext context)
    {
        // 1. Extracts the procedure name from the ANTLR identifier
        var name = context.ambiguousIdentifier().GetText();

        // 2. Create the Method symbol associated with the current container (the module)
        var symbol = CreateChildSymbol(SymbolKind.Method, name, context);
        symbol.Accessibility = GetAccessibility(context.visibility());
        symbol.IsStatic = context.STATIC() is not null;

        // 3. Extracts all formal parameters (ByVal/ByRef, declared type, default value)
        var parameters = ExtractParameters(context.argList());
        symbol.Parameters.AddRange(parameters);

        // 4. Record the symbol and structural relationship CONTAINS (Module -> Sub) in the CodeGraph
        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        // 5. Check whether the name follows the standard convention of a VB6 event handler (Event_Control, e.g. "cmdOK_Click")
        var underscoreIndex = name.LastIndexOf('_');
        if (underscoreIndex > 0 && underscoreIndex < name.Length - 1)
        {
            var prefix = name[..underscoreIndex];
            var eventPart = name[(underscoreIndex + 1)..];

            bool isEventHandler = CommonEventNames.Contains(eventPart) ||
                                  _moduleSymbol.Kind is SymbolKind.Form or SymbolKind.UserControl ||
                                  string.Equals(prefix, "Form", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(prefix, "UserControl", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(prefix, "Class", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(prefix, _moduleSymbol.Name, StringComparison.OrdinalIgnoreCase);

            if (isEventHandler)

                LikelyEventHandlerNames.Add(name);
        }

        // 6. Set the Sub as the new active container and extract the body (local variables, etc.)
        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    /// <summary>
    /// Visit the ANTLR rule 'functionStmt' (declaring a Function with return type).
    /// Sintassi VB6: [Private|Public|Friend] [Static] Function NomeFun([argList]) [As TipoRitorno] ... End Function
    /// </summary>
    public override object? VisitFunctionStmt(VisualBasic6Parser.FunctionStmtContext context)
    {
        // 1. Extract the function name
        var name = context.ambiguousIdentifier().GetText();

        // 2. Create the symbol of type Method
        var symbol = CreateChildSymbol(SymbolKind.Method, name, context);
        symbol.Accessibility = GetAccessibility(context.visibility());
        symbol.IsStatic = context.STATIC() is not null;

        // 3. Extracts the return type from the 'As Type' clause or type suffix (if omitted in VB6, it is Variant)
        var returnType = ExtractTypeName(context.asTypeClause()) ?? ExtractTypeHint(context.typeHint());
        symbol.ReturnType = returnType;
        symbol.DeclaredTypeName = returnType;

        // 4. Extracts the formal parameters
        var parameters = ExtractParameters(context.argList());
        symbol.Parameters.AddRange(parameters);

        // 5. Record the symbol and structural relationship CONTAINS (Module -> Function)
        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        // 6. Visit the body of the function setting it as a container for local variables
        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    /// <summary>
    /// Visit the ANTLR rule 'declareStmt' (declaring an external Win32/DLL function/sub).
    /// Sintassi VB6: [Private|Public] Declare Function/Sub Nome Lib "..." [Alias "..."] ([argList]) [As Tipo]
    /// </summary>
    public override object? VisitDeclareStmt(VisualBasic6Parser.DeclareStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();

        var symbol = CreateChildSymbol(SymbolKind.Method, name, context);
        symbol.Accessibility = GetAccessibility(context.visibility());

        var returnType = ExtractTypeName(context.asTypeClause());
        symbol.ReturnType = returnType;
        symbol.DeclaredTypeName = returnType;

        if (context.argList() != null)
        {
            var parameters = ExtractParameters(context.argList());
            symbol.Parameters.AddRange(parameters);
        }

        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        return null;
    }

    // =========================================================================
    // PROPERTY EXTRACTION (Property Get/Let/Set with unification)
    // =========================================================================

    /// <summary>
    /// Visit the ANTLR rule 'propertyGetStmt' (reading a property).
    /// Sintassi VB6: [Public|Private|Friend] Property Get NomeProp([argList]) [As Tipo] ... End Property
    /// </summary>
    public override object? VisitPropertyGetStmt(VisualBasic6Parser.PropertyGetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();

        // Retrieves or creates the unified symbol of type Property
        var symbol = GetOrCreateProperty(name, context, out var wasCreated);
        symbol.Accessibility = GetAccessibility(context.visibility());
        symbol.IsStatic = context.STATIC() is not null;

        // The return type of the Get defines the type of the Property
        var returnType = ExtractTypeName(context.asTypeClause());
        if (returnType != null)
        {
            symbol.ReturnType = returnType;
            symbol.DeclaredTypeName = returnType;
        }

        // Extracts and merges any parameters (e.g. indexed properties)
        var parameters = ExtractParameters(context.argList());
        MergeParameters(symbol.Parameters, parameters);

        // Adds the symbol and CONTAINS relationship only if the property symbol has just been created
        if (wasCreated)
        {
            _graph.Symbols.Add(symbol);
            _graph.AddContains(_moduleSymbol, symbol, context.Start.Line);
        }

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    /// <summary>
    /// Visit the ANTLR rule 'propertySetStmt' (assigning an object reference to a property).
    /// Sintassi VB6: [Public|Private|Friend] Property Set NomeProp([argList], ByVal obj As Tipo) ... End Property
    /// </summary>
    public override object? VisitPropertySetStmt(VisualBasic6Parser.PropertySetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = GetOrCreateProperty(name, context, out var wasCreated);

        if (symbol.Accessibility == Accessibility.Default)
            symbol.Accessibility = GetAccessibility(context.visibility());

        var parameters = ExtractParameters(context.argList());
        if (parameters.Count > 0)
        {
            var indexParams = parameters.Take(parameters.Count - 1).ToList();
            MergeParameters(symbol.Parameters, indexParams);

            if (symbol.DeclaredTypeName == null)

                symbol.DeclaredTypeName = parameters[^1].DeclaredTypeName;
        }

        if (wasCreated)
        {
            _graph.Symbols.Add(symbol);
            _graph.AddContains(_moduleSymbol, symbol, context.Start.Line);
        }

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    /// <summary>
    /// Visit the ANTLR rule 'propertyLetStmt' (assigning a primitive/scalar value to a property).
    /// Sintassi VB6: [Public|Private|Friend] Property Let NomeProp([argList], ByVal val As Tipo) ... End Property
    /// </summary>
    public override object? VisitPropertyLetStmt(VisualBasic6Parser.PropertyLetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = GetOrCreateProperty(name, context, out var wasCreated);

        if (symbol.Accessibility == Accessibility.Default)
            symbol.Accessibility = GetAccessibility(context.visibility());

        var parameters = ExtractParameters(context.argList());
        if (parameters.Count > 0)
        {
            var indexParams = parameters.Take(parameters.Count - 1).ToList();
            MergeParameters(symbol.Parameters, indexParams);

            // In VB6, if the Property does not have a Get Property, its type is that of the last parameter of the Let (the assigned value)
            if (symbol.DeclaredTypeName == null)

                symbol.DeclaredTypeName = parameters[^1].DeclaredTypeName;
        }

        if (wasCreated)
        {
            _graph.Symbols.Add(symbol);
            _graph.AddContains(_moduleSymbol, symbol, context.Start.Line);
        }

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    // =========================================================================
    // VARIABLE EXTRACTION (Dim, Private, Public, Static at module or local level)
    // =========================================================================

    /// <summary>
    /// Visit the ANTLR rule 'variableStmt' (declaring a line of variables).
    /// Esempi:
    ///   - "Dim x As Long, y As String"
    ///   - "Public m_Id As Integer"
    ///   - "Static counter As Long"
    /// Save the visibility and Static flag to apply to all 'variableSubStmt' contained in the row.
    /// </summary>
    public override object? VisitVariableStmt(VisualBasic6Parser.VariableStmtContext context)
    {
        var prevAccessibility = _currentVarAccessibility;
        var prevIsStatic = _currentVarIsStatic;

        // Determines the accessibility of the variable row
        if (context.DIM() is not null)

            _currentVarAccessibility = CurrentContainer == _moduleSymbol ? Accessibility.Private : Accessibility.Default;

        else if (context.STATIC() is not null)
        {
            _currentVarAccessibility = Accessibility.Default;
            _currentVarIsStatic = true;
        }
        else if (context.visibility() is not null)

    _currentVarAccessibility = GetAccessibility(context.visibility());

        // Visit the individual elements of the list (variableSubStmt)
        var result = base.VisitVariableStmt(context);

        // Restore the previous state so as not to pollute subsequent statements
        _currentVarAccessibility = prevAccessibility;
        _currentVarIsStatic = prevIsStatic;
        return result;
    }

    /// <summary>
    /// Visita la singola variabile dichiarata (es. 'c As Customer' o 'arr(10) As String' o 'c As New Customer').
    /// It is associated with the current container (Module if class/module field, Routine if local variable).
    /// </summary>
    public override object? VisitVariableSubStmt(VisualBasic6Parser.VariableSubStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Field, name, context);
        symbol.Accessibility = _currentVarAccessibility;
        symbol.IsStatic = _currentVarIsStatic;
        symbol.DeclaredTypeName = ExtractTypeName(context.asTypeClause());

        // Detect whether it is a self-instantiated statement ("Dim c As New Customer")
        symbol.IsNewInstance = context.asTypeClause()?.NEW() is not null;

        // Detect if it is an array ("Dim arr() As String" or "Dim arr(10) As Integer")
        symbol.IsArray = context.LPAREN() is not null;

        // Register the symbol and the CONTAINS relationship to the active container (Module or Routine)
        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);
        return base.VisitVariableSubStmt(context);
    }

    // =========================================================================
    // CONSTANT EXTRACTION (Const)
    // =========================================================================

    /// <summary>
    /// Visit the constant declaration line ('constStmt').
    /// Esempi: "Public Const MaxUsers As Integer = 100", "Const Pi = 3.14"
    /// </summary>
    public override object? VisitConstStmt(VisualBasic6Parser.ConstStmtContext context)
    {
        var prevAccessibility = _currentVarAccessibility;
        // Map the ANTLR rule 'publicPrivateGlobalVisibility' to the Accessibility enum
        _currentVarAccessibility = context.publicPrivateGlobalVisibility()?.GetText().ToUpperInvariant() switch
        {
            "PUBLIC" => Accessibility.Public,
            "PRIVATE" => Accessibility.Private,
            "GLOBAL" => Accessibility.Global,
            _ => Accessibility.Default
        };

        var result = base.VisitConstStmt(context);

        _currentVarAccessibility = prevAccessibility;
        return result;
    }

    /// <summary>
    /// Visit the single declared constant ('constSubStmt').
    /// </summary>
    public override object? VisitConstSubStmt(VisualBasic6Parser.ConstSubStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Constant, name, context);
        symbol.Accessibility = _currentVarAccessibility;
        symbol.DeclaredTypeName = ExtractTypeName(context.asTypeClause());

        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);
        return base.VisitConstSubStmt(context);
    }

    // =========================================================================
    // ENUMERATION EXTRACTION (Enum)
    // =========================================================================

    /// <summary>
    /// Visit the declaration of a VB6 enumeration ('enumerationStmt').
    /// Sintassi: [Public|Private] Enum NomeEnum ... Costante1 ... End Enum
    /// Extracts the Enum symbol and all individual child constant values.
    /// </summary>
    public override object? VisitEnumerationStmt(VisualBasic6Parser.EnumerationStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Enum, name, context);
        // Map the ANTLR rule 'publicPrivateVisibility' to the Accessibility enum
        symbol.Accessibility = context.publicPrivateVisibility()?.GetText().ToUpperInvariant() switch
        {
            "PUBLIC" => Accessibility.Public,
            "PRIVATE" => Accessibility.Private,
            _ => Accessibility.Default
        };
        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        // Pushes the Enum onto the stack so that the individual constants are children of the Enum
        _containerStack.Push(symbol);
        foreach (var constantCtx in context.enumerationStmt_Constant())
        {
            var constName = constantCtx.ambiguousIdentifier().GetText();
            var constSymbol = CreateChildSymbol(SymbolKind.Constant, constName, constantCtx);
            constSymbol.Accessibility = Accessibility.Public;
            constSymbol.DeclaredTypeName = symbol.Name; // Il tipo del valore è l'Enum stesso

            _graph.Symbols.Add(constSymbol);
            _graph.AddContains(symbol, constSymbol, constantCtx.Start.Line);
        }
        _containerStack.Pop();

        return null;
    }

    // =========================================================================
    // EXTRACTION OF USER DEFINED TYPES (UDT - Type ... End Type)
    // =========================================================================

    /// <summary>
    /// Visit the declaration of a VB6 structure/UDT ('typeStmt').
    /// Sintassi: [Public|Private] Type NomeTipo ... Campo1 As String ... End Type
    /// Extracts the UDT type symbol and all member fields defined within.
    /// </summary>
    public override object? VisitTypeStmt(VisualBasic6Parser.TypeStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Type, name, context);
        symbol.Accessibility = GetAccessibility(context.visibility());

        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        // Pushes the UDT onto the container stack to hold the fields
        _containerStack.Push(symbol);
        foreach (var elem in context.typeStmt_Element())
        {
            var elemName = elem.ambiguousIdentifier().GetText();
            var elemSymbol = CreateChildSymbol(SymbolKind.Field, elemName, elem);
            elemSymbol.Accessibility = Accessibility.Public;
            elemSymbol.DeclaredTypeName = ExtractTypeName(elem.asTypeClause());
            elemSymbol.IsArray = elem.LPAREN() is not null;

            _graph.Symbols.Add(elemSymbol);
            _graph.AddContains(symbol, elemSymbol, elem.Start.Line);
        }
        _containerStack.Pop();

        return null;
    }

    // =========================================================================
    // EXTRACTION OF EVENTS AND INTERFACES (Event, Implements)
    // =========================================================================

    /// <summary>
    /// Visit the VB6 event declaration ('eventStmt').
    /// Sintassi: [Public|Private|Friend] Event NomeEvento([argList])
    /// </summary>
    public override object? VisitEventStmt(VisualBasic6Parser.EventStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Event, name, context);
        symbol.Accessibility = GetAccessibility(context.visibility());

        if (context.argList() != null)
        {
            var parameters = ExtractParameters(context.argList());
            symbol.Parameters.AddRange(parameters);
        }

        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);
        return null;
    }

    /// <summary>
    /// Visit the declaration of an interface implemented via 'Implements' ('implementsStmt').
    /// </summary>
    public override object? VisitImplementsStmt(VisualBasic6Parser.ImplementsStmtContext context)
    {
        var ifaceName = context.ambiguousIdentifier()?.GetText();
        if (!string.IsNullOrWhiteSpace(ifaceName))

            ImplementedInterfaces.Add((ifaceName, context.Start.Line));
        return null;
    }

    // =========================================================================
    // METODI HELPER PRIVATI
    // =========================================================================

    /// <summary>
    /// Sets a new symbol as the active container on the stack and executes the instruction block visit.
    /// Once done, restore the previous container via pop.
    /// </summary>
    /// <param name="container">The symbol that acts as a container (e.g. method for its local variables).</param>
    /// <param name="body">The ANTLR statement block (may be null if body is empty).</param>
    private void VisitBodyAsNewContainer(Symbol container, VisualBasic6Parser.BlockContext? body)
    {
        _containerStack.Push(container);
        if (body is not null)

            Visit(body);
        _containerStack.Pop();
    }

    /// <summary>
    /// Creates an instance of child <see cref="Symbol"/> by automatically calculating the qualified name
    /// with respect to the current container (e.g. "Module1.MySub" or "Module1.MySub.myLocalVariable").
    /// </summary>
    private Symbol CreateChildSymbol(SymbolKind kind, string name, ParserRuleContext context)
    {
        return new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Name = name,
            QualifiedName = $"{CurrentContainer.QualifiedName}.{name}",
            Language = SourceLanguage.VisualBasic6,
            FilePath = _filePath,
            LineStart = context.Start.Line,
            LineEnd = context.Stop?.Line ?? context.Start.Line
        };
    }

    /// <summary>
    /// Retrieves an existing property with the same name or creates a new one if it is the first accessor encountered.
    /// In VB6, Property Get, Property Let, and Property Set define different aspects of the same logical property.
    /// </summary>
    private Symbol GetOrCreateProperty(string name, ParserRuleContext context, out bool wasCreated)
    {
        if (_propertiesByName.TryGetValue(name, out var existing))
        {
            // Extends the row range to cover all accessories
            existing.LineEnd = Math.Max(existing.LineEnd, context.Stop?.Line ?? context.Start.Line);
            wasCreated = false;
            return existing;
        }

        var symbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = SymbolKind.Property,
            Name = name,
            QualifiedName = $"{_moduleSymbol.QualifiedName}.{name}",
            Language = SourceLanguage.VisualBasic6,
            FilePath = _filePath,
            LineStart = context.Start.Line,
            LineEnd = context.Stop?.Line ?? context.Start.Line
        };
        _propertiesByName[name] = symbol;
        wasCreated = true;
        return symbol;
    }

    /// <summary>
    /// Merges a list of formal parameters avoiding duplicates by name (useful for merging Get and Let parameters).
    /// </summary>
    private static void MergeParameters(List<ParameterSymbol> target, List<ParameterSymbol> incoming)
    {
        foreach (var p in incoming)
        {
            if (!target.Any(t => string.Equals(t.Name, p.Name, StringComparison.OrdinalIgnoreCase)))

                target.Add(p);
        }
    }

    /// <summary>
    /// Extracts the type name declared by the ANTLR clause 'asTypeClause' (e.g. "As Customer" -> "Customer").
    /// </summary>
    private static string? ExtractTypeName(VisualBasic6Parser.AsTypeClauseContext? ctx)
    {
        if (ctx?.type_() is null) return null;
        return ctx.type_().GetText();
    }

    /// <summary>
    /// Extracts the list of formal parameters from the ANTLR rule 'argList'.
    /// Handles: ByVal, ByRef (default in VB6 if omitted), Optional, ParamArray, declared type and default value.
    /// </summary>
    private static List<ParameterSymbol> ExtractParameters(VisualBasic6Parser.ArgListContext? argListCtx)
    {
        var result = new List<ParameterSymbol>();
        if (argListCtx is null) return result;

        foreach (var argCtx in argListCtx.arg())
        {
            var name = argCtx.ambiguousIdentifier().GetText();
            var isByVal = argCtx.BYVAL() is not null;
            // In VB6 ByRef is the default if ByVal is not specified
            var isByRef = argCtx.BYREF() is not null || !isByVal;
            var isOptional = argCtx.OPTIONAL() is not null;
            var isParamArray = argCtx.PARAMARRAY() is not null;
            var typeName = ExtractTypeName(argCtx.asTypeClause()) ?? ExtractTypeHint(argCtx.typeHint());
            var defVal = argCtx.argDefaultValue()?.valueStmt()?.GetText();

            result.Add(new ParameterSymbol
            {
                Name = name,
                DeclaredTypeName = typeName,
                IsByVal = isByVal,
                IsByRef = isByRef,
                IsOptional = isOptional,
                IsParamArray = isParamArray,
                DefaultValue = defVal,
                Line = argCtx.Start.Line
            });
        }
        return result;
    }

    /// <summary>
    /// Converts legacy VB6 type suffix characters (%, &, !, #, $, @) to the corresponding semantic type.
    /// </summary>
    private static string? ExtractTypeHint(VisualBasic6Parser.TypeHintContext? ctx)
    {
        if (ctx is null) return null;
        return ctx.GetText() switch
        {
            "%" => "Integer",
            "&" => "Long",
            "!" => "Single",
            "#" => "Double",
            "$" => "String",
            "@" => "Currency",
            _ => null
        };
    }

    /// <summary>Maps ANTLR rule 'visibility' to enum <see cref="Accessibility"/>.</summary>
    private static Accessibility GetAccessibility(VisualBasic6Parser.VisibilityContext? ctx) =>
        ctx?.GetText().ToUpperInvariant() switch
        {
            "PUBLIC" => Accessibility.Public,
            "PRIVATE" => Accessibility.Private,
            "FRIEND" => Accessibility.Friend,
            "GLOBAL" => Accessibility.Global,
            _ => Accessibility.Default
        };

}
