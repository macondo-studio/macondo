using Antlr4.Runtime.Tree;
using Macondo.Parser.Grammar;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Resolution;

/// <summary>
/// Rappresenta un call-site (punto di invocazione o accesso a membro) analizzato e risolto nel codice VB6.
/// </summary>
public sealed class ResolvedCallSite
{
    /// <summary>Testo sorgente della chiamata o accesso (es. "Customer.Update", "c.Save", "LogMessage").</summary>
    public required string CallText { get; init; }

    /// <summary>Numero di riga nel file sorgente in cui risiede la chiamata.</summary>
    public required int Line { get; init; }

    /// <summary>Esito della risoluzione semantica con confidenza (Exact, Unknown, Ambiguous).</summary>
    public required ResolutionResult Result { get; init; }

    /// <summary>Nome della routine contenitore in cui risiede la chiamata (es. "ProcessOrder").</summary>
    public string? EnclosingRoutineName { get; init; }

    /// <summary>Indica se la chiamata risiede all'interno di un blocco contestuale 'With'.</summary>
    public bool IsInWithBlock { get; init; }

    public override string ToString() =>
        $"Riga {Line,3}: '{CallText}' -> {Result}";
}

/// <summary>
/// Rappresenta un'istanziazione di classe rilevata nel codice (es. "Set c = New Customer" o "New OrderRepository").
/// </summary>
public sealed class ResolvedInstantiation
{
    /// <summary>Nome della classe istanziata (es. "Customer").</summary>
    public required string ClassName { get; init; }

    /// <summary>Numero di riga nel file sorgente in cui risiede l'istanziazione.</summary>
    public required int Line { get; init; }

    /// <summary>Simbolo della classe istanziata nel CodeGraph (se risolta).</summary>
    public Symbol? TargetClassSymbol { get; init; }

    /// <summary>Nome della routine contenitore che esegue l'istanziazione.</summary>
    public string? EnclosingRoutineName { get; init; }

    /// <summary>Livello di confidenza della risoluzione della classe.</summary>
    public ResolutionConfidence Confidence { get; init; } = ResolutionConfidence.Exact;

    public override string ToString() =>
        $"Riga {Line,3}: New '{ClassName}' -> {(TargetClassSymbol != null ? TargetClassSymbol.QualifiedName : "[Unknown]")}";
}

/// <summary>
/// Analizzatore AST per individuare e risolvere tutti i call-site, gli accessi a membro
/// e le istanziazioni di oggetti all'interno del corpo di un modulo VB6.
/// 
/// Questa classe implementa il pattern Visitor di ANTLR (<see cref="VisualBasic6ParserBaseVisitor{TResult}"/>)
/// per percorrere le istruzioni all'interno dei corpi di Sub, Function e Property, tracciando lo scope attivo
/// (inclusi i blocchi 'With') e registrando invocazioni e istanziazioni.
/// </summary>
public sealed class RoutineInvocationAnalyzer : VisualBasic6ParserBaseVisitor<object?>
{
    private readonly SymbolResolver _symbolResolver;
    private readonly Symbol _moduleSymbol;

    /// <summary>Stack degli scope attivi (ModuleScope -> LocalScope -> eventuale WithScope).</summary>
    private readonly Stack<IScope> _scopeStack = new();

    /// <summary>Lista dei call-site individuati e risolti durante l'attraversamento.</summary>
    private readonly List<ResolvedCallSite> _callSites = new();

    /// <summary>Lista delle istanziazioni (New Type) individuate nel modulo.</summary>
    private readonly List<ResolvedInstantiation> _instantiations = new();

    /// <summary>Nome della routine attualmente in fase di analisi (es. "ProcessOrder").</summary>
    private string? _currentRoutineName;

    /// <summary>Elenco pubblico di tutti i call-site risolti nel modulo.</summary>
    public IReadOnlyList<ResolvedCallSite> CallSites => _callSites;

    /// <summary>Elenco pubblico di tutte le istanziazioni risolte nel modulo.</summary>
    public IReadOnlyList<ResolvedInstantiation> Instantiations => _instantiations;

    /// <summary>
    /// Inizializza l'analizzatore per il modulo specificato.
    /// </summary>
    /// <param name="symbolResolver">Il resolver dei simboli con gli indici di progetto e tipi.</param>
    /// <param name="moduleSymbol">Il simbolo del modulo VB6 da analizzare.</param>
    public RoutineInvocationAnalyzer(SymbolResolver symbolResolver, Symbol moduleSymbol)
    {
        _symbolResolver = symbolResolver;
        _moduleSymbol = moduleSymbol;
        _scopeStack.Push(symbolResolver.GetModuleScope(moduleSymbol));
    }

    /// <summary>Restituisce lo scope attivo in cima allo stack.</summary>
    private IScope CurrentScope => _scopeStack.Peek();

    // =========================================================================
    // VISITA DELLE ROUTINE (Sub, Function, Property)
    // =========================================================================

    /// <summary>
    /// Entra in una procedura Sub: crea il LocalScope (con parametri e variabili locali) e visita il blocco istruzioni.
    /// </summary>
    public override object? VisitSubStmt(VisualBasic6Parser.SubStmtContext context)
    {
        // 1. Estrae il nome dell'identificatore della Sub
        var name = context.ambiguousIdentifier().GetText();
        var routineSymbol = FindChildSymbol(name, SymbolKind.Method);

        if (routineSymbol != null)
        {
            // 2. Crea il LocalScope per isolare parametri e variabili locali della routine
            var localScope = _symbolResolver.CreateLocalScope(routineSymbol, CurrentScope);
            _scopeStack.Push(localScope);
            _currentRoutineName = name;

            // 3. Visita il blocco di istruzioni interne
            if (context.block() != null)
                Visit(context.block());

            // 4. Ripristina lo scope genitore (ModuleScope) al termine della procedura
            _scopeStack.Pop();
            _currentRoutineName = null;
        }
        return null;
    }

    /// <summary>
    /// Entra in una Function: crea il LocalScope e visita il blocco istruzioni.
    /// </summary>
    public override object? VisitFunctionStmt(VisualBasic6Parser.FunctionStmtContext context)
    {
        // 1. Estrae il nome dell'identificatore della Function
        var name = context.ambiguousIdentifier().GetText();
        var routineSymbol = FindChildSymbol(name, SymbolKind.Method);

        if (routineSymbol != null)
        {
            // 2. Crea il LocalScope contestualizzato alla Function
            var localScope = _symbolResolver.CreateLocalScope(routineSymbol, CurrentScope);
            _scopeStack.Push(localScope);
            _currentRoutineName = name;

            // 3. Visita il corpo di istruzioni
            if (context.block() != null)
                Visit(context.block());

            // 4. Ripristina lo scope
            _scopeStack.Pop();
            _currentRoutineName = null;
        }
        return null;
    }

    /// <summary>
    /// Entra in un blocco Property Get: crea il LocalScope e visita le istruzioni.
    /// </summary>
    public override object? VisitPropertyGetStmt(VisualBasic6Parser.PropertyGetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var propSymbol = FindChildSymbol(name, SymbolKind.Property);

        if (propSymbol != null)
        {
            var localScope = _symbolResolver.CreateLocalScope(propSymbol, CurrentScope);
            _scopeStack.Push(localScope);
            _currentRoutineName = name;

            if (context.block() != null)
                Visit(context.block());

            _scopeStack.Pop();
            _currentRoutineName = null;
        }
        return null;
    }

    /// <summary>
    /// Entra in un blocco Property Let: crea il LocalScope e visita le istruzioni.
    /// </summary>
    public override object? VisitPropertyLetStmt(VisualBasic6Parser.PropertyLetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var propSymbol = FindChildSymbol(name, SymbolKind.Property);

        if (propSymbol != null)
        {
            var localScope = _symbolResolver.CreateLocalScope(propSymbol, CurrentScope);
            _scopeStack.Push(localScope);
            _currentRoutineName = name;

            if (context.block() != null)
                Visit(context.block());

            _scopeStack.Pop();
            _currentRoutineName = null;
        }
        return null;
    }

    /// <summary>
    /// Entra in un blocco Property Set: crea il LocalScope e visita le istruzioni.
    /// </summary>
    public override object? VisitPropertySetStmt(VisualBasic6Parser.PropertySetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var propSymbol = FindChildSymbol(name, SymbolKind.Property);

        if (propSymbol != null)
        {
            var localScope = _symbolResolver.CreateLocalScope(propSymbol, CurrentScope);
            _scopeStack.Push(localScope);
            _currentRoutineName = name;

            if (context.block() != null)
                Visit(context.block());

            _scopeStack.Pop();
            _currentRoutineName = null;
        }
        return null;
    }

    // =========================================================================
    // VISITA DEI BLOCCHI WITH (Contextual Scope)
    // =========================================================================

    /// <summary>
    /// Visita un blocco "With &lt;expr&gt; ... End With".
    /// 1. Risolve l'espressione target del With nello scope corrente per individuarne il tipo semantico.
    /// 2. Spinge un <see cref="WithScope"/> sullo stack per consentire la risoluzione contestuale di accessi relativi '.Member'.
    /// 3. Visita il blocco di istruzioni interno.
    /// 4. Rimuove il WithScope al termine del blocco.
    /// </summary>
    public override object? VisitWithStmt(VisualBasic6Parser.WithStmtContext context)
    {
        // 1. Risolve l'oggetto o record bersaglio su cui opera il With
        var targetText = context.implicitCallStmt_InStmt().GetText();
        var targetResult = ResolveImplicitCallStmt(context.implicitCallStmt_InStmt());

        // 2. Crea lo scope contestuale e lo spinge sullo stack
        var withScope = _symbolResolver.CreateWithScope(CurrentScope, targetResult);
        _scopeStack.Push(withScope);

        // 3. Visita le istruzioni interne al blocco With
        if (context.block() != null)

            Visit(context.block());

        // 4. Ripristina lo scope precedente
        _scopeStack.Pop();
        return null;
    }

    // =========================================================================
    // ISTANZIAZIONI (New TypeName)
    // =========================================================================

    /// <summary>
    /// Visita un'espressione di istanziazione "New &lt;Tipo&gt;" (regola ANTLR 'vsNew').
    /// Esempi: "Set c = New Customer", "New OrderRepository".
    /// Risolve il tipo istanziato tramite il ProjectScope e memorizza l'istanziazione.
    /// </summary>
    public override object? VisitVsNew(VisualBasic6Parser.VsNewContext context)
    {
        // 1. Estrae il nome del tipo dalla clausola valueStmt
        var typeName = context.valueStmt().GetText();
        var typeSymbol = _symbolResolver.ProjectScope.FindTypeSymbol(typeName);

        // 2. Registra l'istanziazione con livello di confidenza Exact o Unknown
        _instantiations.Add(new ResolvedInstantiation
        {
            ClassName = typeName,
            Line = context.Start.Line,
            TargetClassSymbol = typeSymbol,
            EnclosingRoutineName = _currentRoutineName,
            Confidence = typeSymbol != null ? ResolutionConfidence.Exact : ResolutionConfidence.Unknown
        });

        return base.VisitVsNew(context);
    }

    // =========================================================================
    // CHIAMATE ESPLICITE (Sintassi con keyword 'Call')
    // =========================================================================

    /// <summary>
    /// Chiamata esplicita a procedura semplice (es. "Call Validate()", "Call SaveCustomer(123)").
    /// Risolve il nome nello scope attivo e registra il call-site.
    /// </summary>
    public override object? VisitECS_ProcedureCall(VisualBasic6Parser.ECS_ProcedureCallContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var result = CurrentScope.Resolve(name);

        RecordCallSite(context.GetText(), context.Start.Line, result);
        return base.VisitECS_ProcedureCall(context);
    }

    /// <summary>
    /// Chiamata esplicita a membro qualificato o contestuale (es. "Call Customer.Update()", "Call c.Save()", "Call .Save()").
    /// Se è presente un target esplicito, risolve prima il target e poi il membro sul target; altrimenti risolve ".membro" nel WithScope.
    /// </summary>
    public override object? VisitECS_MemberProcedureCall(VisualBasic6Parser.ECS_MemberProcedureCallContext context)
    {
        var memberName = context.ambiguousIdentifier().GetText();
        ResolutionResult result;

        if (context.implicitCallStmt_InStmt() != null)
        {
            var targetResult = ResolveImplicitCallStmt(context.implicitCallStmt_InStmt());
            result = _symbolResolver.ResolveMemberOnTarget(targetResult, memberName);
        }
        else
        {
            // Chiamata con punto iniziale dentro un blocco With (es. "Call .Save()")
            result = CurrentScope.Resolve($".{memberName}");
        }

        RecordCallSite(context.GetText(), context.Start.Line, result);
        return base.VisitECS_MemberProcedureCall(context);
    }

    // =========================================================================
    // CHIAMATE IMPLICITE A LIVELLO DI BLOCCO (Istruzioni senza keyword 'Call')
    // =========================================================================

    /// <summary>
    /// Invocazione nuda di procedura (es. "LogMessage \"test\"", "Validate", "DoSomething a, b").
    /// In VB6 è la modalità di chiamata più diffusa per procedure Sub senza valore di ritorno.
    /// </summary>
    public override object? VisitICS_B_ProcedureCall(VisualBasic6Parser.ICS_B_ProcedureCallContext context)
    {
        var name = context.certainIdentifier().GetText();
        var result = CurrentScope.Resolve(name);

        RecordCallSite(name, context.Start.Line, result);
        return base.VisitICS_B_ProcedureCall(context);
    }

    /// <summary>
    /// Invocazione nuda di membro qualificato o contestuale (es. "Customer.Update", "c.Save", "Me.Reset", ".Save").
    /// </summary>
    public override object? VisitICS_B_MemberProcedureCall(VisualBasic6Parser.ICS_B_MemberProcedureCallContext context)
    {
        var memberName = context.ambiguousIdentifier().GetText();
        ResolutionResult result;

        if (context.implicitCallStmt_InStmt() != null)
        {
            var targetResult = ResolveImplicitCallStmt(context.implicitCallStmt_InStmt());
            result = _symbolResolver.ResolveMemberOnTarget(targetResult, memberName);
        }
        else
        {
            // Chiamata con punto iniziale dentro un blocco With (es. ".Save")
            result = CurrentScope.Resolve($".{memberName}");
        }

        RecordCallSite(context.GetText().Trim(), context.Start.Line, result);
        return base.VisitICS_B_MemberProcedureCall(context);
    }

    // =========================================================================
    // CHIAMATE IN ESPRESSIONI, ASSEGNAZIONI ED EVENTI
    // =========================================================================

    /// <summary>
    /// Visita chiamate e accessi a membri all'interno di espressioni generiche ('vsICS' in 'valueStmt').
    /// Esempi: 'If IsValid() Then', 'x = CalculateTotal(a, b)', 'For i = 1 To GetMax()', argomenti di funzioni.
    /// </summary>
    public override object? VisitVsICS(VisualBasic6Parser.VsICSContext context)
    {
        if (context.implicitCallStmt_InStmt() != null && _currentRoutineName != null)
        {
            var result = ResolveImplicitCallStmt(context.implicitCallStmt_InStmt());
            if (result.TargetSymbol != null && result.TargetSymbol.Kind is SymbolKind.Method or SymbolKind.Property)

                RecordCallSite(context.GetText().Trim(), context.Start.Line, result);
        }
        return base.VisitVsICS(context);
    }

    /// <summary>
    /// Visita istruzioni di assegnazione 'Let' (o implicite 'a = b').
    /// Rileva eventuali chiamate a Property Let sul lato sinistro (LHS).
    /// </summary>
    public override object? VisitLetStmt(VisualBasic6Parser.LetStmtContext context)
    {
        if (context.implicitCallStmt_InStmt() != null && _currentRoutineName != null)
        {
            var targetResult = ResolveImplicitCallStmt(context.implicitCallStmt_InStmt());
            if (targetResult.TargetSymbol != null && targetResult.TargetSymbol.Kind is SymbolKind.Property or SymbolKind.Method)

                RecordCallSite(context.implicitCallStmt_InStmt().GetText().Trim(), context.Start.Line, targetResult);
        }
        return base.VisitLetStmt(context);
    }

    /// <summary>
    /// Visita istruzioni di assegnazione 'Set obj = expr'.
    /// Rileva eventuali chiamate a Property Set sul lato sinistro (LHS).
    /// </summary>
    public override object? VisitSetStmt(VisualBasic6Parser.SetStmtContext context)
    {
        if (context.implicitCallStmt_InStmt() != null && _currentRoutineName != null)
        {
            var targetResult = ResolveImplicitCallStmt(context.implicitCallStmt_InStmt());
            if (targetResult.TargetSymbol != null && targetResult.TargetSymbol.Kind is SymbolKind.Property or SymbolKind.Method)

                RecordCallSite(context.implicitCallStmt_InStmt().GetText().Trim(), context.Start.Line, targetResult);
        }
        return base.VisitSetStmt(context);
    }

    /// <summary>
    /// Visita istruzioni 'RaiseEvent NomeEvento(args)' per registrare le chiamate a eventi.
    /// </summary>
    public override object? VisitRaiseEventStmt(VisualBasic6Parser.RaiseEventStmtContext context)
    {
        var eventName = context.ambiguousIdentifier().GetText();
        var result = CurrentScope.Resolve(eventName);
        RecordCallSite(context.GetText().Trim(), context.Start.Line, result);
        return base.VisitRaiseEventStmt(context);
    }

    // =========================================================================
    // METODI HELPER PRIVATI
    // =========================================================================

    /// <summary>
    /// Risolve un'espressione di chiamata implicita ANTLR (<see cref="VisualBasic6Parser.ImplicitCallStmt_InStmtContext"/>),
    /// gestendo identificatori semplici, chiamate con argomenti e accessi a catena (es. "a.b.c").
    /// </summary>
    private ResolutionResult ResolveImplicitCallStmt(VisualBasic6Parser.ImplicitCallStmt_InStmtContext context)
    {
        // ---------------------------------------------------------------------
        // CASO 1: Identificatore semplice o variabile/funzione diretta (es. "ProcessData", "m_Count")
        // ---------------------------------------------------------------------
        if (context.iCS_S_VariableOrProcedureCall() != null)
        {
            var name = context.iCS_S_VariableOrProcedureCall().ambiguousIdentifier().GetText();
            return CurrentScope.Resolve(name);
        }

        // ---------------------------------------------------------------------
        // CASO 2: Chiamata con parentesi o argomenti (es. "GetCustomer(id, True)")
        // ---------------------------------------------------------------------
        if (context.iCS_S_ProcedureOrArrayCall() != null)
        {
            var call = context.iCS_S_ProcedureOrArrayCall();
            var name = call.ambiguousIdentifier()?.GetText() ?? call.baseType()?.GetText() ?? call.iCS_S_NestedProcedureCall()?.GetText() ?? "";
            return CurrentScope.Resolve(name);
        }

        // ---------------------------------------------------------------------
        // CASO 3: Accesso a membri qualificati a catena (es. "rs.Fields", "obj.Service.Execute()")
        // ---------------------------------------------------------------------
        if (context.iCS_S_MembersCall() != null)
        {
            var membersCall = context.iCS_S_MembersCall();
            ResolutionResult currentResult;

            // 3a. Risoluzione della radice della catena
            if (membersCall.iCS_S_VariableOrProcedureCall() != null)
            {
                var baseName = membersCall.iCS_S_VariableOrProcedureCall().ambiguousIdentifier().GetText();
                currentResult = CurrentScope.Resolve(baseName);
            }
            else if (membersCall.iCS_S_ProcedureOrArrayCall() != null)
            {
                var call = membersCall.iCS_S_ProcedureOrArrayCall();
                var baseName = call.ambiguousIdentifier()?.GetText() ?? call.baseType()?.GetText() ?? "";
                currentResult = CurrentScope.Resolve(baseName);
            }
            else
            {
                // Accesso relativo con punto iniziale (es. ".Name" all'interno di un blocco "With oggetto")
                currentResult = CurrentScope.Resolve(".");
            }

            // Se la radice stessa è una chiamata a routine, registra il relativo call-site
            if (currentResult.TargetSymbol != null && currentResult.TargetSymbol.Kind is SymbolKind.Method or SymbolKind.Property)

                RecordCallSite(currentResult.TargetSymbol.Name, context.Start.Line, currentResult);

            // 3b. Risoluzione progressiva di ciascun membro successivo della catena (.Member1.Member2...)
            foreach (var memberCall in membersCall.iCS_S_MemberCall())
            {
                var memberName = memberCall.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier().GetText()
                                 ?? memberCall.iCS_S_ProcedureOrArrayCall()?.ambiguousIdentifier()?.GetText()
                                 ?? "";

                if (!string.IsNullOrEmpty(memberName))
                {
                    // Risolve il membro 'memberName' sul tipo semantico ricavato dal risultato precedente
                    currentResult = _symbolResolver.ResolveMemberOnTarget(currentResult, memberName);
                    if (currentResult.TargetSymbol != null && currentResult.TargetSymbol.Kind is SymbolKind.Method or SymbolKind.Property)

                        RecordCallSite(memberName, context.Start.Line, currentResult);
                }
            }

            return currentResult;
        }

        // Fallback: se la regola sintattica non rientra nei 3 casi noti, ritorna Unknown
        return new ResolutionResult
        {
            Confidence = ResolutionConfidence.Unknown,
            Details = $"Simbolo non risolvibile staticamente: '{context.GetText()}'"
        };
    }

    /// <summary>
    /// Cerca una routine dichiarata all'interno del modulo corrente.
    /// </summary>
    private Symbol? FindChildSymbol(string name, SymbolKind kind)
    {
        return _symbolResolver.GetModuleScope(_moduleSymbol)
            .FindMember(name);
    }

    /// <summary>
    /// Registra un call-site nella lista dei risultati evitando duplicazioni identiche sulla stessa riga.
    /// </summary>
    private void RecordCallSite(string callText, int line, ResolutionResult result)
    {
        // Evita registrazioni duplicate dello stesso simbolo bersaglio sulla medesima riga di codice
        if (result.TargetSymbol != null)
        {
            if (_callSites.Any(c => c.Line == line &&
                                   c.EnclosingRoutineName == _currentRoutineName &&
                                   c.Result.TargetSymbol?.Id == result.TargetSymbol.Id))

                return;
        }

        _callSites.Add(new ResolvedCallSite
        {
            CallText = callText,
            Line = line,
            Result = result,
            EnclosingRoutineName = _currentRoutineName,
            IsInWithBlock = CurrentScope is WithScope
        });
    }
}
