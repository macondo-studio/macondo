using Antlr4.Runtime.Tree;
using VB6Analyzer.Parser.Grammar;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Resolution;

/// <summary>
/// Analizzatore AST per individuare e risolvere tutti i call-site e gli accessi a membro (Milestone M3).
/// 
/// Questa classe implementa il pattern Visitor di ANTLR (<see cref="VisualBasic6ParserBaseVisitor{TResult}"/>)
/// per percorrere le istruzioni all'interno dei corpi di Sub, Function e Property, tracciando lo scope attivo
/// (inclusi i blocchi 'With') e risolvendo ogni chiamata tramite <see cref="SymbolResolver"/>.
/// 
/// CASI D'USO GESTITI:
/// 1. Routine Scopes: all'ingresso di Sub/Function/Property crea e spinge un <see cref="LocalScope"/> sullo stack.
/// 2. With Blocks: all'ingresso di "With expr ... End With" risolve 'expr', crea un <see cref="WithScope"/> e lo spinge sullo stack.
/// 3. Chiamate Esplicite 'Call':
///    - "Call Procedure()" -> regola ANTLR 'eCS_ProcedureCall'.
///    - "Call Target.Method()" o "Call .Method()" -> regola ANTLR 'eCS_MemberProcedureCall'.
/// 4. Chiamate Implicite Nude (Statement):
///    - "Procedure arg1, arg2" -> regola ANTLR 'iCS_B_ProcedureCall'.
///    - "Target.Method arg1" o ".Method arg1" -> regola ANTLR 'iCS_B_MemberProcedureCall'.
/// 5. Registrazione CallSite: ogni chiamata viene registrata in <see cref="CallSites"/> con riga, testo sorgente ed esito semantico.
/// </summary>
public sealed class RoutineInvocationAnalyzer : VisualBasic6ParserBaseVisitor<object?>
{
    private readonly SymbolResolver _symbolResolver;
    private readonly Symbol _moduleSymbol;

    /// <summary>Stack degli scope attivi (ModuleScope -> LocalScope -> eventuale WithScope).</summary>
    private readonly Stack<IScope> _scopeStack = new();

    /// <summary>Lista dei call-site individuati e risolti durante l'attraversamento.</summary>
    private readonly List<ResolvedCallSite> _callSites = new();

    /// <summary>Nome della routine attualmente in fase di analisi (es. "ProcessOrder").</summary>
    private string? _currentRoutineName;

    /// <summary>Elenco pubblico di tutti i call-site risolti nel modulo.</summary>
    public IReadOnlyList<ResolvedCallSite> CallSites => _callSites;

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
        var name = context.ambiguousIdentifier().GetText();
        var routineSymbol = FindChildSymbol(name, SymbolKind.Method);

        if (routineSymbol != null)
        {
            var localScope = _symbolResolver.CreateLocalScope(routineSymbol, CurrentScope);
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
    /// Entra in una Function: crea il LocalScope e visita il blocco istruzioni.
    /// </summary>
    public override object? VisitFunctionStmt(VisualBasic6Parser.FunctionStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var routineSymbol = FindChildSymbol(name, SymbolKind.Method);

        if (routineSymbol != null)
        {
            var localScope = _symbolResolver.CreateLocalScope(routineSymbol, CurrentScope);
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
    /// 1. Risolve l'espressione target del With nello scope corrente.
    /// 2. Spinge un <see cref="WithScope"/> sullo stack per consentire la risoluzione contestuale di '.Member'.
    /// 3. Visita il blocco di istruzioni interno.
    /// 4. Rimuove il WithScope al termine del blocco.
    /// </summary>
    public override object? VisitWithStmt(VisualBasic6Parser.WithStmtContext context)
    {
        var targetText = context.implicitCallStmt_InStmt().GetText();
        var targetResult = ResolveImplicitCallStmt(context.implicitCallStmt_InStmt());

        var withScope = _symbolResolver.CreateWithScope(CurrentScope, targetResult);
        _scopeStack.Push(withScope);

        if (context.block() != null)
        {
            Visit(context.block());
        }

        _scopeStack.Pop();
        return null;
    }

    // =========================================================================
    // CHIAMATE ESPLICITE (Sintassi con keyword 'Call')
    // =========================================================================

    /// <summary>
    /// Chiamata esplicita a procedura semplice (es. "Call Validate()", "Call SaveCustomer(123)").
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
    // METODI HELPER PRIVATI
    // =========================================================================

    /// <summary>
    /// Risolve un'espressione di chiamata implicita ANTLR (<see cref="VisualBasic6Parser.ImplicitCallStmt_InStmtContext"/>),
    /// gestendo identificatori semplici, chiamate con argomenti e accessi a catena (es. "a.b.c").
    /// </summary>
    private ResolutionResult ResolveImplicitCallStmt(VisualBasic6Parser.ImplicitCallStmt_InStmtContext context)
    {
        // 1. Identificatore semplice o variabile (es. "cust", "Customer")
        if (context.iCS_S_VariableOrProcedureCall() != null)
        {
            var name = context.iCS_S_VariableOrProcedureCall().ambiguousIdentifier().GetText();
            return CurrentScope.Resolve(name);
        }

        // 2. Chiamata con parentesi (es. "GetCustomer(id)")
        if (context.iCS_S_ProcedureOrArrayCall() != null)
        {
            var call = context.iCS_S_ProcedureOrArrayCall();
            var name = call.ambiguousIdentifier()?.GetText() ?? call.baseType()?.GetText() ?? call.iCS_S_NestedProcedureCall()?.GetText() ?? "";
            return CurrentScope.Resolve(name);
        }

        // 3. Accesso a membri qualificati a catena (es. "a.b.c")
        if (context.iCS_S_MembersCall() != null)
        {
            var membersCall = context.iCS_S_MembersCall();
            ResolutionResult currentResult;

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
                // .Member all'inizio -> blocco With
                currentResult = CurrentScope.Resolve(".");
            }

            // Risolve ogni segmento successivo della catena
            foreach (var memberCall in membersCall.iCS_S_MemberCall())
            {
                var memberName = memberCall.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier().GetText()
                                 ?? memberCall.iCS_S_ProcedureOrArrayCall()?.ambiguousIdentifier()?.GetText()
                                 ?? "";

                if (!string.IsNullOrEmpty(memberName))
                {
                    currentResult = _symbolResolver.ResolveMemberOnTarget(currentResult, memberName);
                }
            }

            return currentResult;
        }

        return ResolutionResult.Unknown(context.GetText());
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
    /// Registra un call-site nella lista dei risultati.
    /// </summary>
    private void RecordCallSite(string callText, int line, ResolutionResult result)
    {
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
