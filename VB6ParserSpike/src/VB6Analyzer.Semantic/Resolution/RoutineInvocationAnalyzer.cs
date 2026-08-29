using Antlr4.Runtime.Tree;
using VB6Analyzer.Parser.Grammar;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Resolution;

/// <summary>
/// Analizzatore AST per estrarre e risolvere tutti i call-site e riferimenti a simboli
/// all'interno del corpo di un modulo VB6 (M3).
/// </summary>
public sealed class RoutineInvocationAnalyzer : VisualBasic6ParserBaseVisitor<object?>
{
    private readonly SymbolResolver _symbolResolver;
    private readonly Symbol _moduleSymbol;
    private readonly Stack<IScope> _scopeStack = new();
    private readonly List<ResolvedCallSite> _callSites = new();
    private string? _currentRoutineName;

    public IReadOnlyList<ResolvedCallSite> CallSites => _callSites;

    public RoutineInvocationAnalyzer(SymbolResolver symbolResolver, Symbol moduleSymbol)
    {
        _symbolResolver = symbolResolver;
        _moduleSymbol = moduleSymbol;
        _scopeStack.Push(symbolResolver.GetModuleScope(moduleSymbol));
    }

    private IScope CurrentScope => _scopeStack.Peek();

    // ---------- Sub / Function / Property ----------

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

    // ---------- With Statement ----------

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

    // ---------- Chiamate esplicite (Call ...) ----------

    public override object? VisitECS_ProcedureCall(VisualBasic6Parser.ECS_ProcedureCallContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        var result = CurrentScope.Resolve(name);

        RecordCallSite(context.GetText(), context.Start.Line, result);
        return base.VisitECS_ProcedureCall(context);
    }

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
            // Call contestuale dentro With (es. Call .Save())
            result = CurrentScope.Resolve($".{memberName}");
        }

        RecordCallSite(context.GetText(), context.Start.Line, result);
        return base.VisitECS_MemberProcedureCall(context);
    }

    // ---------- Chiamate implicite a livello di blocco (Statement nudo) ----------

    public override object? VisitICS_B_ProcedureCall(VisualBasic6Parser.ICS_B_ProcedureCallContext context)
    {
        var name = context.certainIdentifier().GetText();
        var result = CurrentScope.Resolve(name);

        RecordCallSite(name, context.Start.Line, result);
        return base.VisitICS_B_ProcedureCall(context);
    }

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
            // Chiamata contestuale dentro With (es. .Save)
            result = CurrentScope.Resolve($".{memberName}");
        }

        RecordCallSite(context.GetText().Trim(), context.Start.Line, result);
        return base.VisitICS_B_MemberProcedureCall(context);
    }

    // ---------- Helper ----------

    private ResolutionResult ResolveImplicitCallStmt(VisualBasic6Parser.ImplicitCallStmt_InStmtContext context)
    {
        if (context.iCS_S_VariableOrProcedureCall() != null)
        {
            var name = context.iCS_S_VariableOrProcedureCall().ambiguousIdentifier().GetText();
            return CurrentScope.Resolve(name);
        }

        if (context.iCS_S_ProcedureOrArrayCall() != null)
        {
            var call = context.iCS_S_ProcedureOrArrayCall();
            var name = call.ambiguousIdentifier()?.GetText() ?? call.baseType()?.GetText() ?? call.iCS_S_NestedProcedureCall()?.GetText() ?? "";
            return CurrentScope.Resolve(name);
        }

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

    private Symbol? FindChildSymbol(string name, SymbolKind kind)
    {
        return _symbolResolver.GetModuleScope(_moduleSymbol)
            .FindMember(name);
    }

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
