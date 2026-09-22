using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;

namespace Macondo.Semantic.Graph;

/// <summary>
/// Costruttore del Call Graph e generatore delle relazioni semantiche nel CodeGraph.
/// 
/// RESPONSABILITÀ:
/// 1. Analisi Invocazioni: attraversa gli AST di tutti i moduli tramite <see cref="RoutineInvocationAnalyzer"/>
///    e genera le relazioni <see cref="RelationshipKind.Calls"/> tra routine chiamanti e chiamate.
/// 2. Analisi Istanziazioni: individua sia le espressioni 'Set ... = New Type' sia le dichiarazioni
///    'Dim ... As New Type' e genera le relazioni <see cref="RelationshipKind.Instantiates"/>.
/// 3. Analisi Dipendenze di Tipo: individua tutti i campi, variabili, parametri e valori di ritorno
///    tipizzati con tipi utente/progetto e genera le relazioni <see cref="RelationshipKind.UsesType"/>.
/// </summary>
public sealed class CallGraphBuilder
{
    private readonly CodeGraph _graph;
    private readonly SymbolResolver _symbolResolver;

    /// <summary>
    /// Inizializza il builder del Call Graph per il grafo specificato.
    /// </summary>
    /// <param name="graph">Il CodeGraph condiviso in cui inserire le relazioni.</param>
    /// <param name="symbolResolver">Il motore di risoluzione dei simboli e scope.</param>
    public CallGraphBuilder(CodeGraph graph, SymbolResolver symbolResolver)
    {
        _graph = graph;
        _symbolResolver = symbolResolver;
    }

    /// <summary>
    /// Costruisce tutte le relazioni semantiche (CALLS, INSTANTIATES, USES_TYPE, INHERITS, IMPLEMENTS) nel CodeGraph
    /// per tutti i moduli VB6 e file C# parsati.
    /// </summary>
    /// <param name="extractionResults">Elenco dei risultati di estrazione dei moduli VB6 contenenti gli AST.</param>
    /// <param name="csharpResults">Elenco opzionale dei risultati di estrazione dei file sorgente C#.</param>
    public void BuildGraph(IEnumerable<ModuleExtractionResult> extractionResults, IEnumerable<CSharpExtractionResult>? csharpResults = null)
    {
        // 1. Genera le relazioni CALLS e INSTANTIATES attraversando gli AST delle routine VB6
        foreach (var extraction in extractionResults)
        {
            if (extraction.ParseTree == null)
                continue;

            var analyzer = new RoutineInvocationAnalyzer(_symbolResolver, extraction.ModuleSymbol);
            analyzer.Visit(extraction.ParseTree);

            // Generazione relazioni CALLS
            foreach (var callSite in analyzer.CallSites)
            {
                if (callSite.Result.TargetSymbol != null &&
                    callSite.Result.TargetSymbol.Kind is SymbolKind.Method or SymbolKind.Property)
                {
                    var callerRoutine = FindEnclosingRoutine(extraction.ModuleSymbol, callSite.EnclosingRoutineName);
                    if (callerRoutine != null)
                    {
                        _graph.AddCalls(callerRoutine, callSite.Result.TargetSymbol, callSite.Line, callSite.Result.Confidence);
                    }
                }
            }

            // Generazione relazioni INSTANTIATES da espressioni 'New Type'
            foreach (var inst in analyzer.Instantiations)
            {
                if (inst.TargetClassSymbol != null)
                {
                    var sourceSymbol = FindEnclosingRoutine(extraction.ModuleSymbol, inst.EnclosingRoutineName) ?? extraction.ModuleSymbol;
                    _graph.AddInstantiates(sourceSymbol, inst.TargetClassSymbol, inst.Line, inst.Confidence);
                }
            }
        }

        // 1b. Genera relazioni semantiche per i risultati C# (CALLS, INSTANTIATES, INHERITS, IMPLEMENTS)
        if (csharpResults != null)
        {
            // Indice dei metodi/proprietà per nome per evitare scansioni lineari su grafi estesi
            Dictionary<string, Symbol>? routineIndexByName = null;

            foreach (var csResult in csharpResults)
            {
                // Invocazioni C# (CALLS)
                foreach (var callSite in csResult.CallSites)
                {
                    Symbol? targetMethod = null;

                    if (!string.IsNullOrEmpty(callSite.ReceiverExpressionText))
                    {
                        var receiverType = _symbolResolver.TypeResolver.ResolveType(callSite.ReceiverExpressionText).UserTypeSymbol;
                        if (receiverType != null)
                        {
                            targetMethod = _graph.GetChildren(receiverType.Id)
                                .FirstOrDefault(s => s.Kind is SymbolKind.Method or SymbolKind.Property &&
                                                    string.Equals(s.Name, callSite.InvokedMethodName, StringComparison.OrdinalIgnoreCase));
                        }
                    }

                    if (targetMethod == null)
                    {
                        // Costruzione lazy dell'indice dei metodi per lookup O(1)
                        if (routineIndexByName == null)
                        {
                            routineIndexByName = new Dictionary<string, Symbol>(StringComparer.OrdinalIgnoreCase);
                            foreach (var s in _graph.Symbols)
                            {
                                if (s.Kind is SymbolKind.Method or SymbolKind.Property && !routineIndexByName.ContainsKey(s.Name))
                                {
                                    routineIndexByName[s.Name] = s;
                                }
                            }
                        }

                        routineIndexByName.TryGetValue(callSite.InvokedMethodName, out targetMethod);
                    }

                    if (targetMethod != null)
                    {
                        _graph.AddCalls(callSite.CallerSymbol, targetMethod, callSite.Line, ResolutionConfidence.Exact);
                    }
                }

                // Istanziazioni C# (INSTANTIATES)
                foreach (var inst in csResult.Instantiations)
                {
                    var targetType = _symbolResolver.TypeResolver.ResolveType(inst.TargetTypeName).UserTypeSymbol;
                    if (targetType != null)
                    {
                        _graph.AddInstantiates(inst.SourceSymbol, targetType, inst.Line, ResolutionConfidence.Exact);
                    }
                }

                // Ereditarietà e Interfacce C# (INHERITS, IMPLEMENTS)
                foreach (var inh in csResult.Inheritances)
                {
                    var baseType = _symbolResolver.TypeResolver.ResolveType(inh.BaseOrInterfaceTypeName).UserTypeSymbol;
                    if (baseType != null)
                    {
                        if (baseType.Kind == SymbolKind.Interface)
                        {
                            _graph.AddImplements(inh.DerivedTypeSymbol, baseType, inh.Line);
                        }
                        else
                        {
                            _graph.AddInherits(inh.DerivedTypeSymbol, baseType, inh.Line);
                        }
                    }
                }
            }
        }

        // 2. Genera relazioni INSTANTIATES da dichiarazioni auto-istantianti (Dim c As New Customer)
        foreach (var symbol in _graph.Symbols)
        {
            if (symbol.IsNewInstance && symbol.ResolvedTypeSymbolId.HasValue)
            {
                var targetClass = _graph.FindSymbolById(symbol.ResolvedTypeSymbolId.Value);
                if (targetClass != null)
                {
                    _graph.AddInstantiates(symbol, targetClass, symbol.LineStart);
                }
            }
        }

        // 3. Genera relazioni USES_TYPE da dichiarazioni tipizzate (variabili, parametri, ritorni)
        foreach (var symbol in _graph.Symbols)
        {
            // Tipo dichiarato su variabili/campi/proprietà
            if (symbol.ResolvedTypeSymbolId.HasValue)
            {
                var targetType = _graph.FindSymbolById(symbol.ResolvedTypeSymbolId.Value);
                if (targetType != null)
                {
                    _graph.AddUsesType(symbol, targetType, symbol.LineStart);
                }
            }

            // Tipo di ritorno su funzioni e proprietà
            if (symbol.ResolvedReturnTypeSymbolId.HasValue)
            {
                var returnType = _graph.FindSymbolById(symbol.ResolvedReturnTypeSymbolId.Value);
                if (returnType != null)
                {
                    _graph.AddUsesType(symbol, returnType, symbol.LineStart);
                }
            }

            // Tipi dichiarati sui parametri formali
            foreach (var param in symbol.Parameters)
            {
                if (param.ResolvedTypeSymbolId.HasValue)
                {
                    var paramType = _graph.FindSymbolById(param.ResolvedTypeSymbolId.Value);
                    if (paramType != null)
                    {
                        _graph.AddUsesType(symbol, paramType, param.Line);
                    }
                }
            }
        }

        // 4. Genera relazioni HANDLES_EVENT per tutti i gestori di eventi UI e di classe
        foreach (var extraction in extractionResults)
        {
            var moduleChildren = _graph.GetChildren(extraction.ModuleSymbol.Id).ToList();

            foreach (var eventHandlerName in extraction.LikelyEventHandlerNames)
            {
                var routine = moduleChildren.FirstOrDefault(s => s.Kind is SymbolKind.Method or SymbolKind.Property &&
                                                                string.Equals(s.Name, eventHandlerName, StringComparison.OrdinalIgnoreCase));
                if (routine != null)
                {
                    var underscoreIndex = eventHandlerName.LastIndexOf('_');
                    if (underscoreIndex > 0)
                    {
                        var prefix = eventHandlerName[..underscoreIndex];
                        var controlSymbol = moduleChildren.FirstOrDefault(s => s.Kind == SymbolKind.Field &&
                                                                               string.Equals(s.Name, prefix, StringComparison.OrdinalIgnoreCase));
                        if (controlSymbol != null)
                        {
                            _graph.AddHandlesEvent(controlSymbol, routine, routine.LineStart);
                        }
                    }

                    // Collega sempre il container (Form, UserControl o Class) come generatore dell'evento
                    _graph.AddHandlesEvent(extraction.ModuleSymbol, routine, routine.LineStart);
                }
            }
        }

        // 5. Rilascio esplicito dei parse tree ANTLR per consentire al Garbage Collector di recuperare RAM
        foreach (var extraction in extractionResults)
        {
            extraction.ParseTree = null;
        }
    }

    /// <summary>
    /// Recupera il simbolo della routine racchiudente all'interno del modulo per nome.
    /// </summary>
    private Symbol? FindEnclosingRoutine(Symbol moduleSymbol, string? routineName)
    {
        if (string.IsNullOrEmpty(routineName))
            return null;

        return _graph.GetChildren(moduleSymbol.Id)
            .FirstOrDefault(s => s.Kind is SymbolKind.Method or SymbolKind.Property &&
                                string.Equals(s.Name, routineName, StringComparison.OrdinalIgnoreCase));
    }
}
