using Antlr4.Runtime.Tree;
using Macondo.Parser.Grammar;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Resolution;

/// <summary>
/// Represents a call-site (member invocation or access point) analyzed and resolved in VB6 code.
/// </summary>
public sealed class ResolvedCallSite
{
    /// <summary>Source text of the call or access (e.g. "Customer.Update", "c.Save", "LogMessage").</summary>
    public required string CallText { get; init; }

    /// <summary>Line number in the source file where the call resides.</summary>
    public required int Line { get; init; }

    /// <summary>Result of semantic resolution with confidence (Exact, Unknown, Ambiguous).</summary>
    public required ResolutionResult Result { get; init; }

    /// <summary>Name of the container routine in which the call resides (e.g. "ProcessOrder").</summary>
    public string? EnclosingRoutineName { get; init; }

    /// <summary>Indicates whether the call resides within a 'With' contextual block.</summary>
    public bool IsInWithBlock { get; init; }

    /// <summary>
    /// Name of the invoked member (if calling a qualified member or late binding, e.g. "DoSomething").
    /// Used by <see cref="Graph.CallGraphBuilder"/> for heuristic fallback on dynamically bound calls.
    /// </summary>
    public string? InvokedMemberName { get; init; }

    /// <summary>
    /// Name of the call receiver type (e.g. "Form", "Control", "Object", "Variant", "Customer").
    /// It allows you to contextualize and filter compatible candidate symbols during late binding resolution.
    /// </summary>
    public string? ReceiverTypeName { get; init; }

    public override string ToString() =>
        $"Riga {Line,3}: '{CallText}' -> {Result}";
}

/// <summary>
/// Represents a class instantiation found in code (e.g. "Set c = New Customer" or "New OrderRepository").
/// </summary>
public sealed class ResolvedInstantiation
{
    /// <summary>Name of the instantiated class (e.g. "Customer").</summary>
    public required string ClassName { get; init; }

    /// <summary>Line number in the source file where the instantiation resides.</summary>
    public required int Line { get; init; }

    /// <summary>Symbol of the class instantiated in the CodeGraph (if resolved).</summary>
    public Symbol? TargetClassSymbol { get; init; }

    /// <summary>Name of the container routine that performs the instantiation.</summary>
    public string? EnclosingRoutineName { get; init; }

    /// <summary>Class resolution confidence level.</summary>
    public ResolutionConfidence Confidence { get; init; } = ResolutionConfidence.Exact;

    public override string ToString() =>
        $"Riga {Line,3}: New '{ClassName}' -> {(TargetClassSymbol != null ? TargetClassSymbol.QualifiedName : "[Unknown]")}";
}

/// <summary>
/// AST analyzer to locate and resolve all call-site, member logins
/// and object instantiations within the body of a VB6 module.
/// 
/// This class implements the ANTLR Visitor pattern (<see cref="VisualBasic6ParserBaseVisitor{TResult}"/>)
/// to walk through the instructions within the Sub, Function and Property bodies, tracing the active scope
/// (including 'With' blocks) and recording invocations and instantiations.
/// </summary>
public sealed class RoutineInvocationAnalyzer : VisualBasic6ParserBaseVisitor<object?>
{
    private readonly SymbolResolver _symbolResolver;
    private readonly Symbol _moduleSymbol;

    /// <summary>Stack of active scopes (ModuleScope -> LocalScope -> possible WithScope).</summary>
    private readonly Stack<IScope> _scopeStack = new();

    /// <summary>List of call-sites identified and resolved during the traversal.</summary>
    private readonly List<ResolvedCallSite> _callSites = new();

    /// <summary>List of instances (New Type) identified in the module.</summary>
    private readonly List<ResolvedInstantiation> _instantiations = new();

    /// <summary>Name of the routine currently being analyzed (e.g. "ProcessOrder").</summary>
    private string? _currentRoutineName;

    /// <summary>Public list of all call-sites resolved in the module.</summary>
    public IReadOnlyList<ResolvedCallSite> CallSites => _callSites;

    /// <summary>Public list of all resolved instances in the module.</summary>
    public IReadOnlyList<ResolvedInstantiation> Instantiations => _instantiations;

    /// <summary>
    /// Initializes the parser for the specified module.
    /// </summary>
    /// <param name="symbolResolver">The symbol resolver with project indexes and types.</param>
    /// <param name="moduleSymbol">The symbol of the VB6 module to be analyzed.</param>
    public RoutineInvocationAnalyzer(SymbolResolver symbolResolver, Symbol moduleSymbol)
    {
        _symbolResolver = symbolResolver;
        _moduleSymbol = moduleSymbol;
        _scopeStack.Push(symbolResolver.GetModuleScope(moduleSymbol));
    }

    /// <summary>Returns the active scope at the top of the stack.</summary>
    private IScope CurrentScope => _scopeStack.Peek();

    // =========================================================================
    // VISIT TO THE ROUTINES (Sub, Function, Property)
    // =========================================================================

    /// <summary>
    /// Enter a Sub procedure: create the LocalScope (with local parameters and variables) and visit the statement block.
    /// </summary>
    public override object? VisitSubStmt(VisualBasic6Parser.SubStmtContext context)
    {
        // 1. Extracts the Sub identifier name
        var name = context.ambiguousIdentifier()?.GetText();
        if (string.IsNullOrEmpty(name))
            return null;

        var routineSymbol = FindChildSymbol(name, SymbolKind.Method);

        if (routineSymbol != null)
        {
            // 2. Create the LocalScope to isolate local parameters and variables of the routine
            var localScope = _symbolResolver.CreateLocalScope(routineSymbol, CurrentScope);
            _scopeStack.Push(localScope);
            _currentRoutineName = name;

            // 3. Visit the internal instruction block
            if (context.block() != null)
                Visit(context.block());

            // 4. Restore the parent scope (ModuleScope) when the procedure is finished
            _scopeStack.Pop();
            _currentRoutineName = null;
        }
        return null;
    }

    /// <summary>
    /// Enter a Function: create the LocalScope and visit the instruction block.
    /// </summary>
    public override object? VisitFunctionStmt(VisualBasic6Parser.FunctionStmtContext context)
    {
        // 1. Extracts the name of the Function identifier
        var name = context.ambiguousIdentifier()?.GetText();
        if (string.IsNullOrEmpty(name))
            return null;

        var routineSymbol = FindChildSymbol(name, SymbolKind.Method);

        if (routineSymbol != null)
        {
            // 2. Create the LocalScope contextualized to the Function
            var localScope = _symbolResolver.CreateLocalScope(routineSymbol, CurrentScope);
            _scopeStack.Push(localScope);
            _currentRoutineName = name;

            // 3. Visit the instruction body
            if (context.block() != null)
                Visit(context.block());

            // 4. Reset the scope
            _scopeStack.Pop();
            _currentRoutineName = null;
        }
        return null;
    }

    /// <summary>
    /// Enter a Property Get block: Create the LocalScope and visit the instructions.
    /// </summary>
    public override object? VisitPropertyGetStmt(VisualBasic6Parser.PropertyGetStmtContext context)
    {
        var name = context.ambiguousIdentifier()?.GetText();
        if (string.IsNullOrEmpty(name))
            return null;

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
    /// Enter a Property Let block: Create the LocalScope and visit the instructions.
    /// </summary>
    public override object? VisitPropertyLetStmt(VisualBasic6Parser.PropertyLetStmtContext context)
    {
        var name = context.ambiguousIdentifier()?.GetText();
        if (string.IsNullOrEmpty(name))
            return null;

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
    /// Enter a Property Set block: create the LocalScope and visit the instructions.
    /// </summary>
    public override object? VisitPropertySetStmt(VisualBasic6Parser.PropertySetStmtContext context)
    {
        var name = context.ambiguousIdentifier()?.GetText();
        if (string.IsNullOrEmpty(name))
            return null;

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
    // VISIT THE BLOCKS WITH (Contextual Scope)
    // =========================================================================

    /// <summary>
    /// Visita un blocco "With &lt;expr&gt; ... End With".
    /// 1. Resolve the target expression of the With in the current scope to find its semantic type.
    /// 2. Pushes a <see cref="WithScope"/> onto the stack to allow contextual resolution of '.Member' relative accesses.
    /// 3. Visit the internal instruction block.
    /// 4. Remove the WithScope at the end of the block.
    /// </summary>
    public override object? VisitWithStmt(VisualBasic6Parser.WithStmtContext context)
    {
        if (context.implicitCallStmt_InStmt() == null)
            return null;

        // 1. Resolves the target object or record on which the With operates
        var targetText = context.implicitCallStmt_InStmt().GetText();
        var targetResult = ResolveImplicitCallStmt(context.implicitCallStmt_InStmt());

        // 2. Creates the contextual scope and pushes it onto the stack
        var withScope = _symbolResolver.CreateWithScope(CurrentScope, targetResult);
        _scopeStack.Push(withScope);

        // 3. Visit the instructions inside the With block
        if (context.block() != null)

            Visit(context.block());

        // 4. Restore the previous scope
        _scopeStack.Pop();
        return null;
    }

    // =========================================================================
    // INSTANCIATIONS (New TypeName)
    // =========================================================================

    /// <summary>
    /// Visits a "New <Type>" instantiation expression (ANTLR rule 'vsNew').
    /// Esempi: "Set c = New Customer", "New OrderRepository".
    /// Resolves the type instantiated via the ProjectScope and stores the instantiation.
    /// </summary>
    public override object? VisitVsNew(VisualBasic6Parser.VsNewContext context)
    {
        // 1. Extracts the type name from the valueStmt clause
        var typeName = context.valueStmt().GetText();
        var typeSymbol = _symbolResolver.ProjectScope.FindTypeSymbol(typeName);

        // 2. Register the instance with Exact or Unknown confidence level
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
    // EXPLICIT CALLS (Syntax with keyword 'Call')
    // =========================================================================

    /// <summary>
    /// Explicit call to a simple procedure (e.g. "Call Validate()", "Call SaveCustomer(123)").
    /// Resolves the name in the active scope and registers the call-site.
    /// </summary>
    public override object? VisitECS_ProcedureCall(VisualBasic6Parser.ECS_ProcedureCallContext context)
    {
        var name = context.ambiguousIdentifier()?.GetText();
        if (string.IsNullOrEmpty(name))
            return base.VisitECS_ProcedureCall(context);

        var result = CurrentScope.Resolve(name);
        RecordCallSite(context.GetText(), context.Start.Line, result, name);
        return base.VisitECS_ProcedureCall(context);
    }

    /// <summary>
    /// Explicit call to qualified or contextual member (e.g. "Call Customer.Update()", "Call c.Save()", "Call .Save()").
    /// If there is an explicit target, resolve the target first and then the member on the target; otherwise resolves ".member" in the WithScope.
    /// Records both the eventual cascading receiving routine (e.g. 'Call getFirstForm().Save()') and the final member invoked.
    /// </summary>
    public override object? VisitECS_MemberProcedureCall(VisualBasic6Parser.ECS_MemberProcedureCallContext context)
    {
        var memberName = context.ambiguousIdentifier()?.GetText();
        if (string.IsNullOrEmpty(memberName))
            return base.VisitECS_MemberProcedureCall(context);

        ResolutionResult result;

        if (context.implicitCallStmt_InStmt() != null)
        {
            // 1. Resolves the target expression to the left of the dot (e.g. "Customer", "c", "getFirstForm()", "accountingmod")
            var targetResult = ResolveImplicitCallStmt(context.implicitCallStmt_InStmt());

            // 1b. If the target expression itself is a Function or Property invocation (chain call),
            // we record the relevant call-site to ensure that the receiving routine is tracked as an active call
            if (targetResult.TargetSymbol != null && targetResult.TargetSymbol.Kind is SymbolKind.Method or SymbolKind.Property)
                RecordCallSite(targetResult.TargetSymbol.Name, context.Start.Line, targetResult, targetResult.TargetSymbol.Name);

            // 2. Resolve the final member on the target obtained in the previous step
            result = _symbolResolver.ResolveMemberOnTarget(targetResult, memberName);

            // 3. Determine the semantic type of the receiver to allow for possible late binding candidate-matching
            var recType = targetResult.TargetTypeName 
                ?? (targetResult.TargetSymbol?.Kind is SymbolKind.Method or SymbolKind.Property ? targetResult.TargetSymbol.ReturnType : null)
                ?? targetResult.TargetSymbol?.DeclaredTypeName;
            RecordCallSite(context.GetText(), context.Start.Line, result, memberName, recType);
        }
        else
        {
            // Call with starting dot inside a With block (e.g. "Call .Save()")
            result = CurrentScope.Resolve($".{memberName}");
            RecordCallSite(context.GetText(), context.Start.Line, result, memberName);
        }

        return base.VisitECS_MemberProcedureCall(context);
    }

    // =========================================================================
    // IMPLICIT BLOCK LEVEL CALLS (Instructions without 'Call' keyword)
    // =========================================================================

    /// <summary>
    /// Bare procedure invocation (e.g. "LogMessage \"test\"", "Validate", "DoSomething a, b").
    /// In VB6 it is the most popular way of calling Sub procedures with no return value.
    /// </summary>
    public override object? VisitICS_B_ProcedureCall(VisualBasic6Parser.ICS_B_ProcedureCallContext context)
    {
        var name = context.certainIdentifier()?.GetText();
        if (string.IsNullOrEmpty(name))
            return base.VisitICS_B_ProcedureCall(context);

        var result = CurrentScope.Resolve(name);
        RecordCallSite(name, context.Start.Line, result, name);
        return base.VisitICS_B_ProcedureCall(context);
    }

    /// <summary>
    /// Bare invocation of qualified or contextual member (e.g. "Customer.Update", "c.Save", "Me.Reset", ".Save").
    /// It handles both cascading explicitly targeted calls and contextual '.Member' calls in With blocks.
    /// </summary>
    public override object? VisitICS_B_MemberProcedureCall(VisualBasic6Parser.ICS_B_MemberProcedureCallContext context)
    {
        var memberName = context.ambiguousIdentifier()?.GetText();
        if (string.IsNullOrEmpty(memberName))
            return base.VisitICS_B_MemberProcedureCall(context);

        ResolutionResult result;

        if (context.implicitCallStmt_InStmt() != null)
        {
            // 1. Resolves the target expression to the left of the dot (e.g. "obj", "cntrDestination", "frmAccountingInvoices")
            var targetResult = ResolveImplicitCallStmt(context.implicitCallStmt_InStmt());

            // 1b. If the target expression is a Function or Property (chain call or module accessor),
            // explicitly registers the call-site to the routine that acts as the receiver
            if (targetResult.TargetSymbol != null && targetResult.TargetSymbol.Kind is SymbolKind.Method or SymbolKind.Property)
                RecordCallSite(targetResult.TargetSymbol.Name, context.Start.Line, targetResult, targetResult.TargetSymbol.Name);

            // 2. Fixes the member on target
            result = _symbolResolver.ResolveMemberOnTarget(targetResult, memberName);

            // 3. Determine the semantic type of the receiver to support late binding or static typing
            var recType = targetResult.TargetTypeName 
                ?? (targetResult.TargetSymbol?.Kind is SymbolKind.Method or SymbolKind.Property ? targetResult.TargetSymbol.ReturnType : null)
                ?? targetResult.TargetSymbol?.DeclaredTypeName;
            RecordCallSite(context.GetText().Trim(), context.Start.Line, result, memberName, recType);
        }
        else
        {
            // Call with starting point inside a With block (e.g. ".Save")
            result = CurrentScope.Resolve($".{memberName}");
            RecordCallSite(context.GetText().Trim(), context.Start.Line, result, memberName);
        }

        return base.VisitICS_B_MemberProcedureCall(context);
    }

    // =========================================================================
    // CALLS IN EXPRESSIONS, ASSIGNMENTS AND EVENTS
    // =========================================================================

    /// <summary>
    /// Visit member calls and accesses within generic expressions ('vsICS' in 'valueStmt').
    /// Examples: 'If IsValid() Then', 'x = CalculateTotal(a, b)', 'For i = 1 To GetMax()', function arguments.
    /// </summary>
    public override object? VisitVsICS(VisualBasic6Parser.VsICSContext context)
    {
        if (context.implicitCallStmt_InStmt() != null && _currentRoutineName != null)
        {
            var result = ResolveImplicitCallStmt(context.implicitCallStmt_InStmt());
            if (result.TargetSymbol != null)
            {
                // Records both calls to routines (Method, Property) and readings of constants, fields and enums
                if (result.TargetSymbol.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Constant or SymbolKind.Field or SymbolKind.Enum)

                    RecordCallSite(context.GetText().Trim(), context.Start.Line, result);
            }
        }
        return base.VisitVsICS(context);
    }

    /// <summary>
    /// Visit 'Let' assignment statements (or implicit 'a = b').
    /// Detects any calls to Property Let on the left side (LHS).
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
    /// Visit 'Set obj = expr' assignment statements.
    /// Detects any calls to Property Set on the left side (LHS).
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
    /// Visit 'RaiseEventEventName(args)' instructions to log event calls.
    /// </summary>
    public override object? VisitRaiseEventStmt(VisualBasic6Parser.RaiseEventStmtContext context)
    {
        var eventName = context.ambiguousIdentifier()?.GetText();
        if (string.IsNullOrEmpty(eventName))
            return base.VisitRaiseEventStmt(context);

        var result = CurrentScope.Resolve(eventName);
        RecordCallSite(context.GetText().Trim(), context.Start.Line, result, eventName);
        return base.VisitRaiseEventStmt(context);
    }

    // =========================================================================
    // METODI HELPER PRIVATI
    // =========================================================================

    /// <summary>
    /// Resolves an implicit ANTLR call expression (<see cref="VisualBasic6Parser.ImplicitCallStmt_InStmtContext"/>),
    /// handling simple identifiers, argument calls and chain accesses (e.g. "a.b.c").
    /// </summary>
    private ResolutionResult ResolveImplicitCallStmt(VisualBasic6Parser.ImplicitCallStmt_InStmtContext context)
    {
        // ---------------------------------------------------------------------
        // CASE 1: Simple identifier or direct variable/function (e.g. "ProcessData", "m_Count")
        // ---------------------------------------------------------------------
        if (context.iCS_S_VariableOrProcedureCall() != null)
        {
            var name = context.iCS_S_VariableOrProcedureCall().ambiguousIdentifier()?.GetText();
            if (string.IsNullOrEmpty(name))
                return new ResolutionResult { Confidence = ResolutionConfidence.Unknown, Details = "Identificatore vuoto o non valido" };

            return CurrentScope.Resolve(name);
        }

        // ---------------------------------------------------------------------
        // CASE 2: Call with parentheses or arguments (e.g. "GetCustomer(id, True)")
        // ---------------------------------------------------------------------
        if (context.iCS_S_ProcedureOrArrayCall() != null)
        {
            var call = context.iCS_S_ProcedureOrArrayCall();
            var name = call.ambiguousIdentifier()?.GetText() ?? call.baseType()?.GetText() ?? call.iCS_S_NestedProcedureCall()?.GetText() ?? "";
            if (string.IsNullOrEmpty(name))
                return new ResolutionResult { Confidence = ResolutionConfidence.Unknown, Details = "Nome procedura vuoto o non valido" };

            return CurrentScope.Resolve(name);
        }

        // ---------------------------------------------------------------------
        // CASE 3: Chain access to qualified members (e.g. "rs.Fields", "obj.Service.Execute()")
        // ---------------------------------------------------------------------
        if (context.iCS_S_MembersCall() != null)
        {
            var membersCall = context.iCS_S_MembersCall();
            ResolutionResult currentResult;

            // 3a. Chain root resolution
            if (membersCall.iCS_S_VariableOrProcedureCall() != null)
            {
                var baseName = membersCall.iCS_S_VariableOrProcedureCall().ambiguousIdentifier()?.GetText() ?? "";
                currentResult = !string.IsNullOrEmpty(baseName)
                    ? CurrentScope.Resolve(baseName)
                    : new ResolutionResult { Confidence = ResolutionConfidence.Unknown, Details = "Radice della chiamata non valida" };
            }
            else if (membersCall.iCS_S_ProcedureOrArrayCall() != null)
            {
                var call = membersCall.iCS_S_ProcedureOrArrayCall();
                var baseName = call.ambiguousIdentifier()?.GetText() ?? call.baseType()?.GetText() ?? "";
                currentResult = !string.IsNullOrEmpty(baseName)
                    ? CurrentScope.Resolve(baseName)
                    : new ResolutionResult { Confidence = ResolutionConfidence.Unknown, Details = "Radice della chiamata non valida" };
            }
            else
            {
                // Relative access with starting point (e.g. ".Name" inside a "With object" block)
                currentResult = CurrentScope.Resolve(".");
            }

            // If the root itself is a routine call (e.g. 'getFirstForm().Member'), register its call-site
            if (currentResult.TargetSymbol != null && currentResult.TargetSymbol.Kind is SymbolKind.Method or SymbolKind.Property)
                RecordCallSite(currentResult.TargetSymbol.Name, context.Start.Line, currentResult, currentResult.TargetSymbol.Name);

            // 3b. Progressive resolution of each subsequent member of the chain (.Member1.Member2...)
            // For each member we keep the type of the previous receiver (prevTargetType):
            // if the member is statically resolved, it is recorded with Exact confidence;
            // if instead the previous receiver is a dynamic or basic UI type (Form, Object, Variant),
            // we still register the call-site with null TargetSymbol to allow CallGraphBuilder
            // to apply the late binding heuristic by binding compatible public methods by name.
            foreach (var memberCall in membersCall.iCS_S_MemberCall())
            {
                var memberName = memberCall.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier()?.GetText()
                                 ?? memberCall.iCS_S_ProcedureOrArrayCall()?.ambiguousIdentifier()?.GetText()
                                 ?? "";

                if (!string.IsNullOrEmpty(memberName))
                {
                    var prevTargetType = currentResult.TargetTypeName 
                        ?? (currentResult.TargetSymbol?.Kind is SymbolKind.Method or SymbolKind.Property ? currentResult.TargetSymbol.ReturnType : null)
                        ?? currentResult.TargetSymbol?.DeclaredTypeName;

                    // Resolves member 'memberName' to the semantic type taken from the previous result
                    currentResult = _symbolResolver.ResolveMemberOnTarget(currentResult, memberName);
                    if (currentResult.TargetSymbol != null && currentResult.TargetSymbol.Kind is SymbolKind.Method or SymbolKind.Property)
                        RecordCallSite(memberName, context.Start.Line, currentResult, memberName, prevTargetType);
                    else if (currentResult.TargetSymbol == null && IsDynamicOrUiType(prevTargetType))
                        RecordCallSite(memberName, context.Start.Line, currentResult, memberName, prevTargetType);
                }
            }

            return currentResult;
        }

        // Fallback: if the syntactic rule does not fit into the 3 known cases, returns Unknown
        return new ResolutionResult
        {
            Confidence = ResolutionConfidence.Unknown,
            Details = $"Simbolo non risolvibile staticamente: '{context.GetText()}'"
        };
    }

    /// <summary>
    /// Searches for a procedure declared within the current module.
    /// </summary>
    private Symbol? FindChildSymbol(string name, SymbolKind kind)
    {
        return _symbolResolver.GetModuleScope(_moduleSymbol)
            .FindMember(name);
    }

    /// <summary>
    /// Registers a call-site in the form results list, avoiding identical duplications on the same line.
    /// It supports both statically resolved calls (with TargetSymbol valued) and late-binding calls
    /// (with TargetSymbol null but InvokedMemberName and ReceiverTypeName valued).
    /// </summary>
    /// <param name="callText">Source text of the invocation (e.g. "Customer.Save", "ActiveForm.Refresh").</param>
    /// <param name="line">Line number in source code.</param>
    /// <param name="result">Semantic outcome of the resolution.</param>
    /// <param name="invokedMemberName">Name of the invoked method or property.</param>
    /// <param name="receiverTypeName">Name of the declared or inferred type of the receiver.</param>
    private void RecordCallSite(string callText, int line, ResolutionResult result, string? invokedMemberName = null, string? receiverTypeName = null)
    {
        // Avoid duplicate registrations of the same target symbol on the same line of code
        if (result.TargetSymbol != null)
        {
            if (_callSites.Any(c => c.Line == line &&
                                   c.EnclosingRoutineName == _currentRoutineName &&
                                   c.Result.TargetSymbol?.Id == result.TargetSymbol.Id))
                return;
        }
        else if (!string.IsNullOrEmpty(invokedMemberName))
        {
            // For late-binding calls without TargetSymbol, de-duplicate by name of the member invoked on the same line
            if (_callSites.Any(c => c.Line == line &&
                                   c.EnclosingRoutineName == _currentRoutineName &&
                                   string.Equals(c.InvokedMemberName, invokedMemberName, StringComparison.OrdinalIgnoreCase)))
                return;
        }

        _callSites.Add(new ResolvedCallSite
        {
            CallText = callText,
            Line = line,
            Result = result,
            EnclosingRoutineName = _currentRoutineName,
            IsInWithBlock = CurrentScope is WithScope,
            InvokedMemberName = invokedMemberName,
            ReceiverTypeName = receiverTypeName ?? result.TargetTypeName
        });
    }

    /// <summary>
    /// Determines whether the type name is a dynamically bound type or a VB6 UI base class
    /// ('Form', 'MDIForm', 'Control', 'Object', 'Variant', 'dynamic').
    /// In such contexts, member invocations are not statically solvable in parsing time
    /// and must be drawn to allow <see cref="Graph.CallGraphBuilder"/> to attach candidate routines.
    /// </summary>
    /// <param name="typeName">The name of the type to test.</param>
    /// <returns><c>true</c> if it is a dynamic or UI base type; otherwise <c>false</c>.</returns>
    private static bool IsDynamicOrUiType(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;
        return string.Equals(typeName, "Form", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(typeName, "MDIForm", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(typeName, "Control", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(typeName, "Object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(typeName, "Variant", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(typeName, "dynamic", StringComparison.OrdinalIgnoreCase);
    }
}
