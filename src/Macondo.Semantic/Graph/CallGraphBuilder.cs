using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;

namespace Macondo.Semantic.Graph;

/// <summary>
/// Call Graph builder and centralized generator of semantic relations in <see cref="CodeGraph"/>.
/// 
/// RESPONSIBILITIES AND GRAPH CONSTRUCTION PHASES:
/// 1. Analysis of VB6 Invocations (CALLS): traverses the ASTs of all modules via <see cref="RoutineInvocationAnalyzer"/>
/// and generates <see cref="RelationshipKind.Calls"/> relationships between calling routines and calls, with associated confidence level.
/// 2. Analysis of C# Invocations and Types (CALLS, INSTANTIATES, INHERITS, IMPLEMENTS): resolves C# call-sites with receiver or index by name,
/// 'new' instantiations and inheritance/interfaces for polyglot or hybrid projects.
/// 3. Analysis of Explicit and Implicit Instances (INSTANTIATES): identifies both the 'Set ... = New Type' expressions and the declarations
/// self-instantiating 'Dim ... As New Type' and generates <see cref="RelationshipKind.Instantiates"/> relations.
/// 4. Type Dependency Analysis (USES_TYPE): identifies all fields, variables, formal parameters and return values
/// typed with user/project types and generates <see cref="RelationshipKind.UsesType"/> relationships.
/// 5. Analysis of UI and Form Event Handlers (HANDLES_EVENT): identify VB6 conventions (e.g. "cmdOk_Click", "Form_Load")
/// and connect both the UI control and the form container to the event handler procedure.
/// 6. Resource Release: dereference the ANTLR ParseTrees to allow the garbage collector to deallocate the memory occupied by the ASTs.
/// </summary>
public sealed class CallGraphBuilder
{
    private readonly CodeGraph _graph;
    private readonly SymbolResolver _symbolResolver;

    /// <summary>
    /// Initializes a new instance of the Call Graph builder for the specified graph.
    /// </summary>
    /// <param name="graph">The shared <see cref="CodeGraph"/> in which to insert the semantic relations.</param>
    /// <param name="symbolResolver">The symbol resolution and scope engine (<see cref="SymbolResolver"/>).</param>
    public CallGraphBuilder(CodeGraph graph, SymbolResolver symbolResolver)
    {
        _graph = graph;
        _symbolResolver = symbolResolver;
    }

    /// <summary>
    /// Constructs all semantic relations (CALLS, INSTANTIATES, USES_TYPE, HANDLES_EVENT, INHERITS, IMPLEMENTS) in the CodeGraph
    /// for all VB6 modules and parsed C# source files.
    /// </summary>
    /// <param name="extractionResults">List of extraction results of VB6 modules containing ASTs (<see cref="ModuleExtractionResult"/>).</param>
    /// <param name="csharpResults">Optional list of C# source file extraction results (<see cref="CSharpExtractionResult"/>).</param>
    /// <param name="javaResults">Optional list of Java source file extraction results (<see cref="JavaExtractionResult"/>).</param>
    /// <param name="kotlinResults">Optional list of Kotlin source file extraction results (<see cref="KotlinExtractionResult"/>).</param>
    /// <param name="cancellationToken">Optional delete token to stop Call Graph processing.</param>
    public void BuildGraph(
        IEnumerable<ModuleExtractionResult> extractionResults,
        IEnumerable<CSharpExtractionResult>? csharpResults = null,
        IEnumerable<JavaExtractionResult>? javaResults = null,
        IEnumerable<KotlinExtractionResult>? kotlinResults = null,
        CancellationToken cancellationToken = default)
    {
        // =========================================================================
        // PHASE 1: GENERATION OF REPORTS FROM AST OF VB6 MODULES (CALLS & INSTANTIATES)
        // =========================================================================
        // In this phase we traverse the ANTLR Parse Tree of each VB6 module via RoutineInvocationAnalyzer.
        // The analyzer identifies all explicit calls (Call Foo), implicit calls (Foo arg), and instantiations (New Foo).
        Dictionary<string, List<Symbol>>? publicRoutinesByName = null;
        Dictionary<Guid, SymbolKind>? symbolParentKindById = null;

        foreach (var extraction in extractionResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (extraction.ParseTree == null)
                continue;

            try
            {
                // Initializes the AST parser contextualized to the scope of the current module
                var analyzer = new RoutineInvocationAnalyzer(_symbolResolver, extraction.ModuleSymbol);
                analyzer.Visit(extraction.ParseTree);

                // 1a. Generation of CALLS and READS relationships between calling routine and referenced symbols
                foreach (var callSite in analyzer.CallSites)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var callerRoutine = FindEnclosingRoutine(extraction.ModuleSymbol, callSite.EnclosingRoutineName);
                    if (callerRoutine == null)
                        continue;

                    if (callSite.Result.TargetSymbol != null)
                    {
                        // Record CALLS relationships for Method, Property and Event (generated by 'RaiseEventEventName' statements).
                        // In VB6 'RaiseEvent' formally invokes the declared event: attaching the caller to the event guarantees
                        // that the Event symbol is marked as active and reachable in the CodeGraph.
                        if (callSite.Result.TargetSymbol.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Event)
                            _graph.AddCalls(callerRoutine, callSite.Result.TargetSymbol, callSite.Line, callSite.Result.Confidence);
                        else if (callSite.Result.TargetSymbol.Kind is SymbolKind.Field or SymbolKind.Constant or SymbolKind.Enum)
                            _graph.AddReads(callerRoutine, callSite.Result.TargetSymbol, callSite.Line);
                    }
                    else if (!string.IsNullOrEmpty(callSite.InvokedMemberName))
                    {
                        // =====================================================================
                        // Late Binding Resolution in VB6 ('As Form', 'As Object', 'ActiveForm' receivers, etc.)
                        // =====================================================================
                        // In VB6 calls on loosely typed or abstract UI base (IDispatch::Invoke)
                        // they cannot be resolved to a single static type at compile time.
                        // To prevent false positives in Dead Code analysis (where otherwise public methods
                        // polymorphic would be discarded because they are only called via 'frm.Method' or 'ActiveForm.Method'):
                        // 1. We 'lazy' build a global index of public routines by name O(1).
                        // 2. We map the Kind of the parent container (Form, UserControl, Class) to each procedure.
                        // 3. We filter the candidates based on the declared or inferred semantic type of the receiver:
                        // - 'Form' or 'MDIForm' receiver -> matches routines defined in Form or UserControl
                        // - Receiver 'Control' -> matches routines defined in UserControl or Form
                        // - Receiver 'Object' / Variant -> matches routines defined in Form, UserControl or Class
                        // 4. We connect calls with Inferred confidence.
                        if (publicRoutinesByName == null)
                        {
                            publicRoutinesByName = new Dictionary<string, List<Symbol>>(StringComparer.OrdinalIgnoreCase);
                            symbolParentKindById = new Dictionary<Guid, SymbolKind>();

                            foreach (var rel in _graph.Relationships)
                            {
                                if (rel.Kind == RelationshipKind.Contains)
                                {
                                    var parent = _graph.FindSymbolById(rel.SourceId);
                                    if (parent != null)
                                        symbolParentKindById[rel.TargetId] = parent.Kind;
                                }
                            }

                            foreach (var s in _graph.Symbols)
                            {
                                if (s.Kind is SymbolKind.Method or SymbolKind.Property && s.Accessibility != Accessibility.Private)
                                {
                                    if (!publicRoutinesByName.TryGetValue(s.Name, out var list))
                                    {
                                        list = new List<Symbol>();
                                        publicRoutinesByName[s.Name] = list;
                                    }
                                    list.Add(s);
                                }
                            }
                        }

                        if (publicRoutinesByName.TryGetValue(callSite.InvokedMemberName, out var candidates))
                        {
                            var receiverType = callSite.ReceiverTypeName;
                            foreach (var candidate in candidates)
                            {
                                if (callerRoutine.Id == candidate.Id)
                                    continue;

                                symbolParentKindById!.TryGetValue(candidate.Id, out var parentKind);

                                bool matches = false;
                                if (string.Equals(receiverType, "Form", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(receiverType, "MDIForm", StringComparison.OrdinalIgnoreCase))
                                    matches = parentKind is SymbolKind.Form or SymbolKind.UserControl;
                                else if (string.Equals(receiverType, "Control", StringComparison.OrdinalIgnoreCase))
                                    matches = parentKind is SymbolKind.UserControl or SymbolKind.Form;
                                else if (string.Equals(receiverType, "Object", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(receiverType, "Variant", StringComparison.OrdinalIgnoreCase) ||
                                         string.IsNullOrEmpty(receiverType))
                                    matches = parentKind is SymbolKind.Form or SymbolKind.UserControl or SymbolKind.Class;

                                if (matches)
                                    _graph.AddCalls(callerRoutine, candidate, callSite.Line, ResolutionConfidence.Inferred);
                            }
                        }
                    }
                }

                // 1b. Generation of INSTANTIATES relations from explicit 'New Type' expressions (e.g. "Set x = New Customer")
                foreach (var inst in analyzer.Instantiations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (inst.TargetClassSymbol != null)
                    {
                        // The caller is the containing routine or the module itself if at declaration level
                        var sourceSymbol = FindEnclosingRoutine(extraction.ModuleSymbol, inst.EnclosingRoutineName) ?? extraction.ModuleSymbol;
                        _graph.AddInstantiates(sourceSymbol, inst.TargetClassSymbol, inst.Line, inst.Confidence);
                    }
                }

                // 1c. Generating IMPLEMENTS relationships from 'Implements InterfaceName' statements
                foreach (var (ifaceName, line) in extraction.ImplementedInterfaces)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var targetType = _symbolResolver.TypeResolver.ResolveType(ifaceName).UserTypeSymbol
                                     ?? _graph.Symbols.FirstOrDefault(s => s.Kind is SymbolKind.Class or SymbolKind.Interface && string.Equals(s.Name, ifaceName, StringComparison.OrdinalIgnoreCase));
                    if (targetType != null)
                        _graph.AddImplements(extraction.ModuleSymbol, targetType, line);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Resilience: Isolate any unexpected exceptions on individual modules to allow the rest of the project to complete
                System.Diagnostics.Trace.TraceWarning($"[Macondo] Analisi modulo '{extraction.ModuleSymbol.Name}' saltata per errore: {ex.Message}");
            }
        }

        // =========================================================================
        // PHASE 1b: GENERATION OF SEMANTIC RELATIONSHIPS FOR C# FILES (Roslyn)
        // =========================================================================
        // If there are parsed C# files (.NET or hybrid VB6/C# projects), we solve:
        // - Method invocations (CALLS) with receiver type resolution or fallback by name.
        // - Creations of 'new' objects (INSTANTIATES).
        // - Inheritance of classes and implementation of interfaces (INHERITS / IMPLEMENTS).
        if (csharpResults != null)
        {
            // Index of methods/properties by lazy constructed name (only if necessary)
            // to guarantee O(1) lookups instead of O(N) linear scans on graphs with thousands of symbols.
            Dictionary<string, Symbol>? routineIndexByName = null;

            foreach (var csResult in csharpResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. Resolving C# Method Invocations (CALLS)
                foreach (var callSite in csResult.CallSites)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Symbol? targetMethod = null;

                    // Step A: If there is a receiver expression (e.g. "customerService.Process()"),
                    // we try to resolve the type of the receiver using the TypeResolver and look for the method among the children of the type.
                    if (!string.IsNullOrEmpty(callSite.ReceiverExpressionText))
                    {
                        var receiverType = _symbolResolver.TypeResolver.ResolveType(callSite.ReceiverExpressionText).UserTypeSymbol;
                        if (receiverType != null)
                            targetMethod = _graph.GetChildren(receiverType.Id)
                                .FirstOrDefault(s => s.Kind is SymbolKind.Method or SymbolKind.Property &&
                                                    string.Equals(s.Name, callSite.InvokedMethodName, StringComparison.OrdinalIgnoreCase));
                    }

                    // Step B: If the method was not resolved via receiver (e.g. direct call 'Process()' or untyped receiver),
                    // we use the global index of O(1) lookup methods.
                    if (targetMethod == null)
                    {
                        if (routineIndexByName == null)
                        {
                            routineIndexByName = new Dictionary<string, Symbol>(StringComparer.OrdinalIgnoreCase);
                            foreach (var s in _graph.Symbols)
                            {
                                if (s.Kind is SymbolKind.Method or SymbolKind.Property && !routineIndexByName.ContainsKey(s.Name))
                                    routineIndexByName[s.Name] = s;
                            }
                        }

                        routineIndexByName.TryGetValue(callSite.InvokedMethodName, out targetMethod);
                    }

                    // Step C: If the target method is found, insert the CALLS edge into the CodeGraph
                    if (targetMethod != null)
                        _graph.AddCalls(callSite.CallerSymbol, targetMethod, callSite.Line, ResolutionConfidence.Exact);
                }

                // 2. Resolving C# Instantiations (INSTANTIATES from 'new ClassName()')
                foreach (var inst in csResult.Instantiations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var targetType = _symbolResolver.TypeResolver.ResolveType(inst.TargetTypeName).UserTypeSymbol;
                    if (targetType != null)
                        _graph.AddInstantiates(inst.SourceSymbol, targetType, inst.Line, ResolutionConfidence.Exact);
                }

                // 3. Resolving Inheritance and C# Interfaces (INHERITS / IMPLEMENTS from ': BaseType, IInterface')
                foreach (var inh in csResult.Inheritances)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var baseType = _symbolResolver.TypeResolver.ResolveType(inh.BaseOrInterfaceTypeName).UserTypeSymbol;
                    if (baseType != null)
                    {
                        // If the derived type is an interface or the target is a class -> INHERITS;
                        // If a class/struct implements an interface -> IMPLEMENTS
                        if (inh.DerivedTypeSymbol.Kind == SymbolKind.Interface || baseType.Kind != SymbolKind.Interface)
                            _graph.AddInherits(inh.DerivedTypeSymbol, baseType, inh.Line);
                        else
                        {
                            _graph.AddImplements(inh.DerivedTypeSymbol, baseType, inh.Line);
                        }
                    }
                }
            }
        }

        // =========================================================================
        // PHASE 1c: GENERATION OF SEMANTIC RELATIONSHIPS FOR JAVA FILES
        // =========================================================================
        // If there are parsed Java source files (.java in standard, Maven, or Gradle projects),
        // we solve the oriented semantic relations:
        // 1. Method invocations (CALLS):
        // - Step A: If there is an explicit receiver (e.g. 'customerService.saveOrder()'), we try
        // resolve the type of the receiver using the TypeResolver and look for the method in the children of the type.
        // - Step B: If the method is invoked without receiver (internal/inherited call) or the receiver is not resolved,
        // we use the O(1) global index of the methods.
        // 2. Object creations (INSTANTIATES from 'new ClassName()').
        // 3. Inheritance of base classes ('extends') and implementation of interfaces ('implements') (INHERITS / IMPLEMENTS).
        if (javaResults != null)
        {
            // Index of methods and properties by name (lazy built) to ensure fast O(1) lookup
            Dictionary<string, Symbol>? routineIndexByName = null;

            foreach (var javaResult in javaResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. Resolving Java Method Invocations (CALLS)
                foreach (var callSite in javaResult.CallSites)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Symbol? targetMethod = null;

                    // Step A: Contextual resolution via receiver type (e.g. "orderService.process()")
                    if (!string.IsNullOrEmpty(callSite.ReceiverExpressionText))
                    {
                        var receiverType = _symbolResolver.TypeResolver.ResolveType(callSite.ReceiverExpressionText).UserTypeSymbol;
                        if (receiverType != null)
                            targetMethod = _graph.GetChildren(receiverType.Id)
                                .FirstOrDefault(s => s.Kind is SymbolKind.Method or SymbolKind.Property &&
                                                    string.Equals(s.Name, callSite.InvokedMethodName, StringComparison.OrdinalIgnoreCase));
                    }

                    // Step B: Fallback to O(1) global index for unqualified calls or implicit receiver
                    if (targetMethod == null)
                    {
                        if (routineIndexByName == null)
                        {
                            routineIndexByName = new Dictionary<string, Symbol>(StringComparer.OrdinalIgnoreCase);
                            foreach (var s in _graph.Symbols)
                            {
                                if (s.Kind is SymbolKind.Method or SymbolKind.Property && !routineIndexByName.ContainsKey(s.Name))
                                    routineIndexByName[s.Name] = s;
                            }
                        }

                        routineIndexByName.TryGetValue(callSite.InvokedMethodName, out targetMethod);
                    }

                    // Step C: Inserting the CALLS arc into the CodeGraph
                    if (targetMethod != null)
                        _graph.AddCalls(callSite.CallerSymbol, targetMethod, callSite.Line, ResolutionConfidence.Exact);
                }

                // 2. Resolving Java Instantiations (INSTANTIATES from 'new ClassName()')
                foreach (var inst in javaResult.Instantiations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var targetType = _symbolResolver.TypeResolver.ResolveType(inst.TargetTypeName).UserTypeSymbol;
                    if (targetType != null)
                        _graph.AddInstantiates(inst.SourceSymbol, targetType, inst.Line, ResolutionConfidence.Exact);
                }

                // 3. Resolving Inheritance and Java Interfaces (INHERITS / IMPLEMENTS from 'extends' or 'implements')
                foreach (var inh in javaResult.Inheritances)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var baseType = _symbolResolver.TypeResolver.ResolveType(inh.BaseOrInterfaceTypeName).UserTypeSymbol;
                    if (baseType != null)
                    {
                        if (inh.DerivedTypeSymbol.Kind == SymbolKind.Interface || (!inh.IsInterface && baseType.Kind != SymbolKind.Interface))
                            _graph.AddInherits(inh.DerivedTypeSymbol, baseType, inh.Line);
                        else
                        {
                            _graph.AddImplements(inh.DerivedTypeSymbol, baseType, inh.Line);
                        }
                    }
                }
            }
        }

        // =========================================================================
        // PHASE 1d: GENERATION OF SEMANTIC RELATIONSHIPS FOR KOTLIN FILES
        // =========================================================================
        // If there are parsed Kotlin source files (.kt or .kts), we solve:
        // 1. Function or method invocations (CALLS): via receiver or global index O(1).
        // 2. Object creations and instantiations (INSTANTIATES): 'TargetTypeName(...)' constructors.
        // 3. Inheritance of classes and interfaces (INHERITS / IMPLEMENTS from ': BaseType, IInterface').
        if (kotlinResults != null)
        {
            Dictionary<string, Symbol>? routineIndexByName = null;

            foreach (var kotlinResult in kotlinResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. Resolving Kotlin Method Invocations (CALLS)
                foreach (var callSite in kotlinResult.CallSites)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Symbol? targetMethod = null;

                    if (!string.IsNullOrEmpty(callSite.ReceiverExpressionText))
                    {
                        var receiverType = _symbolResolver.TypeResolver.ResolveType(callSite.ReceiverExpressionText).UserTypeSymbol;
                        if (receiverType != null)
                            targetMethod = _graph.GetChildren(receiverType.Id)
                                .FirstOrDefault(s => s.Kind is SymbolKind.Method or SymbolKind.Property &&
                                                    string.Equals(s.Name, callSite.InvokedMethodName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (targetMethod == null)
                    {
                        if (routineIndexByName == null)
                        {
                            routineIndexByName = new Dictionary<string, Symbol>(StringComparer.OrdinalIgnoreCase);
                            foreach (var s in _graph.Symbols)
                            {
                                if (s.Kind is SymbolKind.Method or SymbolKind.Property && !routineIndexByName.ContainsKey(s.Name))
                                    routineIndexByName[s.Name] = s;
                            }
                        }

                        routineIndexByName.TryGetValue(callSite.InvokedMethodName, out targetMethod);
                    }

                    if (targetMethod != null)
                        _graph.AddCalls(callSite.CallerSymbol, targetMethod, callSite.Line, ResolutionConfidence.Exact);
                }

                // 2. Resolution Kotlin Instantiations (INSTANTIATES)
                foreach (var inst in kotlinResult.Instantiations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var targetType = _symbolResolver.TypeResolver.ResolveType(inst.TargetTypeName).UserTypeSymbol;
                    if (targetType != null)
                        _graph.AddInstantiates(inst.SourceSymbol, targetType, inst.Line, ResolutionConfidence.Exact);
                }

                // 3. Resolving Inheritance and Kotlin Interfaces (INHERITS / IMPLEMENTS)
                foreach (var inh in kotlinResult.Inheritances)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var baseType = _symbolResolver.TypeResolver.ResolveType(inh.BaseOrInterfaceTypeName).UserTypeSymbol;
                    if (baseType != null)
                    {
                        if (inh.DerivedTypeSymbol.Kind == SymbolKind.Interface || (!inh.IsInterface && baseType.Kind != SymbolKind.Interface))
                            _graph.AddInherits(inh.DerivedTypeSymbol, baseType, inh.Line);
                        else
                        {
                            _graph.AddImplements(inh.DerivedTypeSymbol, baseType, inh.Line);
                        }
                    }
                }
            }
        }

        // =========================================================================
        // PHASE 2: INSTANTIATIONS FROM SELF-INSTANTING STATEMENTS (Dim As New)
        // =========================================================================
        // In VB6 the 'Dim c As New Customer' or 'Private m_Service As New Service' clause
        // declares a variable and simultaneously generates an implicit instantiation (IsNewInstance == true).
        foreach (var symbol in _graph.Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol.IsNewInstance && symbol.ResolvedTypeSymbolId.HasValue)
            {
                var targetClass = _graph.FindSymbolById(symbol.ResolvedTypeSymbolId.Value);
                if (targetClass != null)
                    _graph.AddInstantiates(symbol, targetClass, symbol.LineStart);
            }
        }

        // =========================================================================
        // PHASE 3: STATIC TYPE DEPENDENCES (USES_TYPE)
        // =========================================================================
        // Inspect all entities in the graph to create USES_TYPE relationships for:
        // - Local variables, fields and properties typed with user types (ResolvedTypeSymbolId).
        // - Return values ​​of functions and properties (ResolvedReturnTypeSymbolId).
        // - Formal parameters of routines and methods (param.ResolvedTypeSymbolId).
        foreach (var symbol in _graph.Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 3a. Type declared on local variables, form fields, or properties
            if (symbol.ResolvedTypeSymbolId.HasValue)
            {
                var targetType = _graph.FindSymbolById(symbol.ResolvedTypeSymbolId.Value);
                if (targetType != null)
                    _graph.AddUsesType(symbol, targetType, symbol.LineStart);
            }

            // 3b. Return type declared on getter functions and properties
            if (symbol.ResolvedReturnTypeSymbolId.HasValue)
            {
                var returnType = _graph.FindSymbolById(symbol.ResolvedReturnTypeSymbolId.Value);
                if (returnType != null)
                    _graph.AddUsesType(symbol, returnType, symbol.LineStart);
            }

            // 3c. Types declared on the individual formal parameters of the routine
            foreach (var param in symbol.Parameters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (param.ResolvedTypeSymbolId.HasValue)
                {
                    var paramType = _graph.FindSymbolById(param.ResolvedTypeSymbolId.Value);
                    if (paramType != null)
                        _graph.AddUsesType(symbol, paramType, param.Line);
                }
            }
        }

        // =========================================================================
        // STEP 4: UI AND FORM EVENT HANDERS (HANDLES_EVENT)
        // =========================================================================
        // In VB6, event handlers follow the syntactic convention 'ControlPrefix_EventName'
        // (es. 'cmdSalva_Click', 'txtNome_Change', 'Form_Load', 'UserControl_Initialize').
        // For each procedure identified as a probable handler:
        // - Connect the UI control (if present as a form field) to the handler routine.
        // - Always attach the root container (Form, UserControl, or Class) to the procedure.
        foreach (var extraction in extractionResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var moduleChildren = _graph.GetChildren(extraction.ModuleSymbol.Id).ToList();
            var moduleFields = moduleChildren.Where(s => s.Kind == SymbolKind.Field).ToList();
            var moduleRoutines = moduleChildren.Where(s => s.Kind is SymbolKind.Method or SymbolKind.Property).ToList();

            // 1. Collects all event handler candidate routines
            var candidateRoutines = new HashSet<Symbol>();
            foreach (var eventHandlerName in extraction.LikelyEventHandlerNames)
            {
                var r = moduleRoutines.FirstOrDefault(s => string.Equals(s.Name, eventHandlerName, StringComparison.OrdinalIgnoreCase));
                if (r != null)
                    candidateRoutines.Add(r);
            }

            // Also includes any procedure that is prefixed with the name of a form's WithEvents field or variable
            foreach (var routine in moduleRoutines)
            {
                if (routine.Name.Contains('_'))
                {
                    bool matchesField = moduleFields.Any(f => routine.Name.StartsWith(f.Name + "_", StringComparison.OrdinalIgnoreCase) ||
                                                              routine.Name.StartsWith(f.Name + "__", StringComparison.OrdinalIgnoreCase));
                    if (matchesField)
                        candidateRoutines.Add(routine);
                }
            }

            // 2. Connect the HANDLES_EVENT relationships
            foreach (var routine in candidateRoutines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Searches for the longest prefix match between form fields (visual controls, WithEvents variables)
                var controlSymbol = moduleFields
                    .Where(f => routine.Name.StartsWith(f.Name + "_", StringComparison.OrdinalIgnoreCase) ||
                                routine.Name.StartsWith(f.Name + "__", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.Name.Length)
                    .FirstOrDefault();

                if (controlSymbol != null)
                {
                    _graph.AddHandlesEvent(controlSymbol, routine, routine.LineStart);

                    // If the WithEvents control or variable has a resolved user type (e.g. UserControl or custom class),
                    // we also connect the explicit symbol of the declared event to the type (SymbolKind.Event)
                    // directly to the handler routine (e.g. Event 'Cancellazione' on UserControl -> Sub 'cntr_Cancellazione').
                    // This guarantees bidirectional event propagation and correct reachability
                    // both when the event is raised (RaiseEvent) and when the Form is instantiated.
                    if (controlSymbol.ResolvedTypeSymbolId.HasValue)
                    {
                        var eventSuffix = routine.Name.Substring(controlSymbol.Name.Length).TrimStart('_');
                        var userTypeChildren = _graph.GetChildren(controlSymbol.ResolvedTypeSymbolId.Value);
                        var eventSym = userTypeChildren.FirstOrDefault(s => s.Kind == SymbolKind.Event &&
                                                                            string.Equals(s.Name, eventSuffix, StringComparison.OrdinalIgnoreCase));
                        if (eventSym != null)
                            _graph.AddHandlesEvent(eventSym, routine, routine.LineStart);
                    }
                }

                // Always connect the container module (Form, UserControl or Class) as the event generator
                _graph.AddHandlesEvent(extraction.ModuleSymbol, routine, routine.LineStart);
            }
        }

        // =========================================================================
        // PHASE 5: MEMORY OPTIMIZATION (ANTLR Parse Tree Release)
        // =========================================================================
        // ANTLR parsing trees (ParseTree) take up a significant amount of RAM memory.
        // Once the semantic resolution and Call Graph visit has been completed,
        // we explicitly dereference the trees to allow the garbage collector to free memory.
        foreach (var extraction in extractionResults)
        {
            extraction.ParseTree = null;
        }
    }

    /// <summary>
    /// Searches for a procedure (Method or Property) declared as a direct child within the specified module,
    /// comparing the name in case-insensitive mode (VB6 convention).
    /// </summary>
    /// <param name="moduleSymbol">The symbol of the container module or class.</param>
    /// <param name="routineName">The name of the searched routine.</param>
    /// <returns>The routine symbol if present; otherwise <c>null</c>.</returns>
    private Symbol? FindEnclosingRoutine(Symbol moduleSymbol, string? routineName)
    {
        if (string.IsNullOrEmpty(routineName))
            return null;

        return _graph.GetChildren(moduleSymbol.Id)
            .FirstOrDefault(s => s.Kind is SymbolKind.Method or SymbolKind.Property &&
                                string.Equals(s.Name, routineName, StringComparison.OrdinalIgnoreCase));
    }
}

