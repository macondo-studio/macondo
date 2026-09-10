using Antlr4.Runtime;
using Macondo.Parser.Grammar;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Estrattore semantico dei simboli basato su parse tree ANTLR (Milestone M2 + M3).
/// 
/// Questa classe implementa il pattern Visitor di ANTLR (<see cref="VisualBasic6ParserBaseVisitor{TResult}"/>)
/// per percorrere l'albero sintattico astratto (AST / Parse Tree) di un singolo file sorgente VB6
/// (.cls, .frm, .bas, .ctl) ed estrarre tutti i simboli dichiarati (Moduli, Routine, Proprietà,
/// Variabili, Costanti, UDT, Enum) inserendoli nel <see cref="CodeGraph"/> condiviso.
/// 
/// PRINCIPI CHIAVE:
/// 1. Separazione Parsing / Semantica (Sezione 6 del charter):
///    Il parser produce l'albero sintattico grezzo; questo visitor lo interpreta semanticamente
///    costruendo simboli strutturati e relazioni gerarchiche CONTAINS.
/// 2. Gerarchia e Contenimento (Stack dei Container):
///    Traccia il contesto corrente (es. Modulo -> Metodo -> Variabile locale) tramite <see cref="_containerStack"/>,
///    in modo che le variabili locali appartengano al metodo e non al modulo.
/// 3. Unificazione delle Property (Get / Let / Set):
///    In VB6 una proprietà può essere definita da più blocchi separati (es. "Property Get Name" e
///    "Property Let Name"). Questo estrattore le unifica in un unico <see cref="Symbol"/> logico di tipo Property,
///    aggregando parametri, tipo di ritorno e intervallo di righe.
/// 4. Cattura di Metadati per M3 (Risoluzione dei Simboli):
///    Estrae visibilità (Public/Private/Friend/Global), tipi dichiarati (As Customer, As Long),
///    parametri formali (ByVal/ByRef, Optional, ParamArray), flag Static/Array/New.
/// </summary>
public sealed class SymbolExtractor : VisualBasic6ParserBaseVisitor<object?>
{
    /// <summary>
    /// Nomi di eventi VB6 standard più comuni per l'euristica degli event handler.
    /// In VB6, una Sub con nome "NomeControllo_NomeEvento" (es. Command1_Click) è convenzionalmente un event handler.
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

    /// <summary>Grafo semantico condiviso in cui vengono inseriti simboli e relazioni CONTAINS.</summary>
    private readonly CodeGraph _graph;

    /// <summary>Simbolo radice che rappresenta il modulo/file corrente (.cls, .bas, .frm, .ctl).</summary>
    private readonly Symbol _moduleSymbol;

    /// <summary>Percorso del file sorgente in analisi.</summary>
    private readonly string _filePath;

    /// <summary>
    /// Stack che mantiene il simbolo contenitore attivo durante l'attraversamento dell'AST.
    /// All'inizio contiene il modulo radice; entrando in un Sub/Function/Property/UDT/Enum,
    /// il relativo simbolo viene spinto sullo stack; uscendo, viene rimosso.
    /// Permette di associare correttamente le relazioni CONTAINS (es. Local Var -> Method).
    /// </summary>
    private readonly Stack<Symbol> _containerStack = new();

    /// <summary>
    /// Mappa di supporto per unificare gli accessor Property Get, Let e Set con lo stesso nome.
    /// Chiave: nome della proprietà (case-insensitive); Valore: istanza unificata di Symbol.
    /// </summary>
    private readonly Dictionary<string, Symbol> _propertiesByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Livello di accessibilità corrente ereditato dall'istruzione di dichiarazione variabili/costanti.
    /// In VB6, una riga come "Public a As Long, b As String" specifica Public una sola volta all'inizio
    /// (regola variableStmt), ma dichiara più variabili separate (regole variableSubStmt).
    /// </summary>
    private Accessibility _currentVarAccessibility = Accessibility.Default;

    /// <summary>
    /// Flag che indica se le variabili dichiarate nella riga corrente sono contrassegnate con "Static".
    /// </summary>
    private bool _currentVarIsStatic;

    /// <summary>
    /// Elenco delle Sub identificate dall'euristica sintattica come probabili event handler (es. "Button1_Click").
    /// </summary>
    public List<string> LikelyEventHandlerNames { get; } = new();

    /// <summary>
    /// Inizializza l'estrattore semantico per un modulo VB6.
    /// </summary>
    /// <param name="graph">Il CodeGraph in-memory condiviso.</param>
    /// <param name="moduleSymbol">Il simbolo del modulo/file appena creato.</param>
    /// <param name="filePath">Percorso del file sorgente.</param>
    public SymbolExtractor(CodeGraph graph, Symbol moduleSymbol, string filePath)
    {
        _graph = graph;
        _moduleSymbol = moduleSymbol;
        _filePath = filePath;
        _containerStack.Push(moduleSymbol);
    }

    /// <summary>Restituisce il simbolo contenitore attualmente in cima allo stack.</summary>
    private Symbol CurrentContainer => _containerStack.Peek();

    // =========================================================================
    // ESTRAZIONE CONTROLLI UI (Form .frm e UserControl .ctl)
    // =========================================================================

    /// <summary>
    /// Visita la regola ANTLR 'controlProperties' per estrarre i controlli visivi definiti nei form (.frm) e UserControl (.ctl).
    /// Esempio: 'Begin VB.CommandButton cmdClick' -> genera simbolo Field 'cmdClick' con tipo 'VB.CommandButton'.
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
                    FilePath = _filePath,
                    LineStart = context.Start.Line,
                    LineEnd = context.Stop?.Line ?? context.Start.Line
                };

                _graph.Symbols.Add(controlSymbol);
                _graph.AddContains(_moduleSymbol, controlSymbol, context.Start.Line);
            }
        }

        return base.VisitControlProperties(context);
    }

    // =========================================================================
    // ESTRAZIONE ROUTINE (Sub e Function)
    // =========================================================================

    /// <summary>
    /// Visita la regola ANTLR 'subStmt' (dichiarazione di una procedura Sub in VB6).
    /// Sintassi VB6: [Private|Public|Friend] [Static] Sub NomeSub([argList]) ... End Sub
    /// </summary>
    public override object? VisitSubStmt(VisualBasic6Parser.SubStmtContext context)
    {
        // 1. Estrae il nome della procedura dall'identificatore ANTLR
        var name = context.ambiguousIdentifier().GetText();

        // 2. Crea il simbolo di tipo Method associato al container corrente (il modulo)
        var symbol = CreateChildSymbol(SymbolKind.Method, name, context);
        symbol.Accessibility = GetAccessibility(context.visibility());
        symbol.IsStatic = context.STATIC() is not null;

        // 3. Estrae tutti i parametri formali (ByVal/ByRef, tipo dichiarato, valore di default)
        var parameters = ExtractParameters(context.argList());
        symbol.Parameters.AddRange(parameters);

        // 4. Registra il simbolo e la relazione strutturale CONTAINS (Modulo -> Sub) nel CodeGraph
        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        // 5. Verifica se il nome rispetta la convenzione standard di un event handler VB6 (Controllo_Evento, es. "cmdOK_Click")
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
            {
                LikelyEventHandlerNames.Add(name);
            }
        }

        // 6. Imposta la Sub come nuovo container attivo ed estrae il corpo (variabili locali, ecc.)
        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    /// <summary>
    /// Visita la regola ANTLR 'functionStmt' (dichiarazione di una Function con tipo di ritorno).
    /// Sintassi VB6: [Private|Public|Friend] [Static] Function NomeFun([argList]) [As TipoRitorno] ... End Function
    /// </summary>
    public override object? VisitFunctionStmt(VisualBasic6Parser.FunctionStmtContext context)
    {
        // 1. Estrae il nome della funzione
        var name = context.ambiguousIdentifier().GetText();

        // 2. Crea il simbolo di tipo Method
        var symbol = CreateChildSymbol(SymbolKind.Method, name, context);
        symbol.Accessibility = GetAccessibility(context.visibility());
        symbol.IsStatic = context.STATIC() is not null;

        // 3. Estrae il tipo di ritorno dalla clausola 'As Type' (se omesso in VB6, è Variant)
        var returnType = ExtractTypeName(context.asTypeClause());
        symbol.ReturnType = returnType;
        symbol.DeclaredTypeName = returnType;

        // 4. Estrae i parametri formali
        var parameters = ExtractParameters(context.argList());
        symbol.Parameters.AddRange(parameters);

        // 5. Registra il simbolo e la relazione strutturale CONTAINS (Modulo -> Function)
        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        // 6. Visita il corpo della funzione impostandola come container per le variabili locali
        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    // =========================================================================
    // ESTRAZIONE PROPRIETÀ (Property Get / Let / Set con unificazione)
    // =========================================================================

    /// <summary>
    /// Visita la regola ANTLR 'propertyGetStmt' (lettura di una proprietà).
    /// Sintassi VB6: [Public|Private|Friend] Property Get NomeProp([argList]) [As Tipo] ... End Property
    /// </summary>
    public override object? VisitPropertyGetStmt(VisualBasic6Parser.PropertyGetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();

        // Recupera o crea il simbolo unificato di tipo Property
        var symbol = GetOrCreateProperty(name, context, out var wasCreated);
        symbol.Accessibility = GetAccessibility(context.visibility());
        symbol.IsStatic = context.STATIC() is not null;

        // Il tipo di ritorno del Get definisce il tipo della Proprietà
        var returnType = ExtractTypeName(context.asTypeClause());
        if (returnType != null)
        {
            symbol.ReturnType = returnType;
            symbol.DeclaredTypeName = returnType;
        }

        // Estrae e fonde eventuali parametri (es. proprietà indicizzate)
        var parameters = ExtractParameters(context.argList());
        MergeParameters(symbol.Parameters, parameters);

        // Aggiunge il simbolo e la relazione CONTAINS solo se il simbolo della proprietà è appena stato creato
        if (wasCreated)
        {
            _graph.Symbols.Add(symbol);
            _graph.AddContains(_moduleSymbol, symbol, context.Start.Line);
        }

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    /// <summary>
    /// Visita la regola ANTLR 'propertySetStmt' (assegnazione di un riferimento a oggetto a una proprietà).
    /// Sintassi VB6: [Public|Private|Friend] Property Set NomeProp([argList], ByVal obj As Tipo) ... End Property
    /// </summary>
    public override object? VisitPropertySetStmt(VisualBasic6Parser.PropertySetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = GetOrCreateProperty(name, context, out var wasCreated);

        if (symbol.Accessibility == Accessibility.Default)
            symbol.Accessibility = GetAccessibility(context.visibility());

        var parameters = ExtractParameters(context.argList());
        MergeParameters(symbol.Parameters, parameters);

        if (wasCreated)
        {
            _graph.Symbols.Add(symbol);
            _graph.AddContains(_moduleSymbol, symbol, context.Start.Line);
        }

        VisitBodyAsNewContainer(symbol, context.block());
        return null;
    }

    /// <summary>
    /// Visita la regola ANTLR 'propertyLetStmt' (assegnazione di un valore primitivo/scalare a una proprietà).
    /// Sintassi VB6: [Public|Private|Friend] Property Let NomeProp([argList], ByVal val As Tipo) ... End Property
    /// </summary>
    public override object? VisitPropertyLetStmt(VisualBasic6Parser.PropertyLetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = GetOrCreateProperty(name, context, out var wasCreated);

        if (symbol.Accessibility == Accessibility.Default)
            symbol.Accessibility = GetAccessibility(context.visibility());

        var parameters = ExtractParameters(context.argList());
        MergeParameters(symbol.Parameters, parameters);

        // In VB6, se la Property non ha un Property Get, il suo tipo è quello dell'ultimo parametro del Let (il valore assegnato)
        if (symbol.DeclaredTypeName == null && parameters.Count > 0)
        {
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
    // ESTRAZIONE VARIABILI (Dim, Private, Public, Static a livello modulo o locale)
    // =========================================================================

    /// <summary>
    /// Visita la regola ANTLR 'variableStmt' (dichiarazione di una riga di variabili).
    /// Esempi:
    ///   - "Dim x As Long, y As String"
    ///   - "Public m_Id As Integer"
    ///   - "Static counter As Long"
    /// Salva la visibilità e il flag Static per applicarli a tutte le 'variableSubStmt' contenute nella riga.
    /// </summary>
    public override object? VisitVariableStmt(VisualBasic6Parser.VariableStmtContext context)
    {
        var prevAccessibility = _currentVarAccessibility;
        var prevIsStatic = _currentVarIsStatic;

        // Determina l'accessibilità della riga di variabili
        if (context.DIM() is not null)
        {
            // Dim a livello di modulo è Private in VB6; all'interno di una routine è locale (Default)
            _currentVarAccessibility = CurrentContainer == _moduleSymbol ? Accessibility.Private : Accessibility.Default;
        }
        else if (context.STATIC() is not null)
        {
            _currentVarAccessibility = Accessibility.Default;
            _currentVarIsStatic = true;
        }
        else if (context.visibility() is not null)
        {
            _currentVarAccessibility = GetAccessibility(context.visibility());
        }

        // Visita i singoli elementi della lista (variableSubStmt)
        var result = base.VisitVariableStmt(context);

        // Ripristina lo stato precedente per non inquinare dichiarazioni successive
        _currentVarAccessibility = prevAccessibility;
        _currentVarIsStatic = prevIsStatic;
        return result;
    }

    /// <summary>
    /// Visita la singola variabile dichiarata (es. 'c As Customer' o 'arr(10) As String' o 'c As New Customer').
    /// Viene associata al container corrente (Modulo se campo di classe/modulo, Routine se variabile locale).
    /// </summary>
    public override object? VisitVariableSubStmt(VisualBasic6Parser.VariableSubStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Field, name, context);
        symbol.Accessibility = _currentVarAccessibility;
        symbol.IsStatic = _currentVarIsStatic;
        symbol.DeclaredTypeName = ExtractTypeName(context.asTypeClause());

        // Rileva se è una dichiarazione con auto-istanziazione ("Dim c As New Customer")
        symbol.IsNewInstance = context.asTypeClause()?.NEW() is not null;

        // Rileva se è un array ("Dim arr() As String" o "Dim arr(10) As Integer")
        symbol.IsArray = context.LPAREN() is not null;

        // Registra il simbolo e la relazione CONTAINS verso il container attivo (Modulo o Routine)
        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);
        return base.VisitVariableSubStmt(context);
    }

    // =========================================================================
    // ESTRAZIONE COSTANTI (Const)
    // =========================================================================

    /// <summary>
    /// Visita la riga di dichiarazione costanti ('constStmt').
    /// Esempi: "Public Const MaxUsers As Integer = 100", "Const Pi = 3.14"
    /// </summary>
    public override object? VisitConstStmt(VisualBasic6Parser.ConstStmtContext context)
    {
        var prevAccessibility = _currentVarAccessibility;
        // Mappa la regola ANTLR 'publicPrivateGlobalVisibility' all'enum Accessibility
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
    /// Visita la singola costante dichiarata ('constSubStmt').
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
    // ESTRAZIONE ENUMERAZIONI (Enum)
    // =========================================================================

    /// <summary>
    /// Visita la dichiarazione di un'enumerazione VB6 ('enumerationStmt').
    /// Sintassi: [Public|Private] Enum NomeEnum ... Costante1 ... End Enum
    /// Estrae il simbolo dell'Enum e tutti i singoli valori costanti figli.
    /// </summary>
    public override object? VisitEnumerationStmt(VisualBasic6Parser.EnumerationStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Enum, name, context);
        // Mappa la regola ANTLR 'publicPrivateVisibility' all'enum Accessibility
        symbol.Accessibility = context.publicPrivateVisibility()?.GetText().ToUpperInvariant() switch
        {
            "PUBLIC" => Accessibility.Public,
            "PRIVATE" => Accessibility.Private,
            _ => Accessibility.Default
        };
        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        // Spinge l'Enum sullo stack in modo che le singole costanti siano figlie dell'Enum
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
    // ESTRAZIONE TIPI DEFINITI DALL'UTENTE (UDT - Type ... End Type)
    // =========================================================================

    /// <summary>
    /// Visita la dichiarazione di una struttura/UDT VB6 ('typeStmt').
    /// Sintassi: [Public|Private] Type NomeTipo ... Campo1 As String ... End Type
    /// Estrae il simbolo del tipo UDT e tutti i campi membri definiti all'interno.
    /// </summary>
    public override object? VisitTypeStmt(VisualBasic6Parser.TypeStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var symbol = CreateChildSymbol(SymbolKind.Type, name, context);
        symbol.Accessibility = GetAccessibility(context.visibility());

        _graph.Symbols.Add(symbol);
        _graph.AddContains(CurrentContainer, symbol, context.Start.Line);

        // Spinge l'UDT sullo stack dei container per contenere i campi
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
    // METODI HELPER PRIVATI
    // =========================================================================

    /// <summary>
    /// Imposta un nuovo simbolo come container attivo sullo stack ed esegue la visita del blocco di istruzioni.
    /// Al termine, ripristina il container precedente tramite pop.
    /// </summary>
    /// <param name="container">Il simbolo che fa da contenitore (es. Metodo per le sue variabili locali).</param>
    /// <param name="body">Il blocco di istruzioni ANTLR (può essere null se il corpo è vuoto).</param>
    private void VisitBodyAsNewContainer(Symbol container, VisualBasic6Parser.BlockContext? body)
    {
        _containerStack.Push(container);
        if (body is not null)
        {
            Visit(body);
        }
        _containerStack.Pop();
    }

    /// <summary>
    /// Crea un'istanza di <see cref="Symbol"/> figlio calcolando automaticamente il nome qualificato
    /// rispetto al container corrente (es. "Modulo1.MiaSub" o "Modulo1.MiaSub.miaVariabileLocale").
    /// </summary>
    private Symbol CreateChildSymbol(SymbolKind kind, string name, ParserRuleContext context)
    {
        return new Symbol
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Name = name,
            QualifiedName = $"{CurrentContainer.QualifiedName}.{name}",
            FilePath = _filePath,
            LineStart = context.Start.Line,
            LineEnd = context.Stop?.Line ?? context.Start.Line
        };
    }

    /// <summary>
    /// Recupera una proprietà già esistente con lo stesso nome o ne crea una nuova se è il primo accessor incontrato.
    /// In VB6, Property Get, Property Let e Property Set definiscono aspetti diversi della stessa proprietà logica.
    /// </summary>
    private Symbol GetOrCreateProperty(string name, ParserRuleContext context, out bool wasCreated)
    {
        if (_propertiesByName.TryGetValue(name, out var existing))
        {
            // Estende l'intervallo di righe per coprire tutti gli accessor
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
            FilePath = _filePath,
            LineStart = context.Start.Line,
            LineEnd = context.Stop?.Line ?? context.Start.Line
        };
        _propertiesByName[name] = symbol;
        wasCreated = true;
        return symbol;
    }

    /// <summary>
    /// Fonde una lista di parametri formali evitando duplicati per nome (utile per unire parametri di Get e Let).
    /// </summary>
    private static void MergeParameters(List<ParameterSymbol> target, List<ParameterSymbol> incoming)
    {
        foreach (var p in incoming)
        {
            if (!target.Any(t => string.Equals(t.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
            {
                target.Add(p);
            }
        }
    }

    /// <summary>
    /// Estrae il nome del tipo dichiarato dalla clausola ANTLR 'asTypeClause' (es. "As Customer" -> "Customer").
    /// </summary>
    private static string? ExtractTypeName(VisualBasic6Parser.AsTypeClauseContext? ctx)
    {
        if (ctx?.type_() is null) return null;
        return ctx.type_().GetText();
    }

    /// <summary>
    /// Estrae l'elenco dei parametri formali dalla regola ANTLR 'argList'.
    /// Gestisce: ByVal, ByRef (default in VB6 se omesso), Optional, ParamArray, tipo dichiarato e valore di default.
    /// </summary>
    private static List<ParameterSymbol> ExtractParameters(VisualBasic6Parser.ArgListContext? argListCtx)
    {
        var result = new List<ParameterSymbol>();
        if (argListCtx is null) return result;

        foreach (var argCtx in argListCtx.arg())
        {
            var name = argCtx.ambiguousIdentifier().GetText();
            var isByVal = argCtx.BYVAL() is not null;
            // In VB6 ByRef è il default se non è specificato ByVal
            var isByRef = argCtx.BYREF() is not null || !isByVal;
            var isOptional = argCtx.OPTIONAL() is not null;
            var isParamArray = argCtx.PARAMARRAY() is not null;
            var typeName = ExtractTypeName(argCtx.asTypeClause());
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

    /// <summary>Mappa la regola ANTLR 'visibility' all'enum <see cref="Accessibility"/>.</summary>
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
