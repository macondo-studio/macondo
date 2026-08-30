using VB6Analyzer.Semantic.Extraction;
using VB6Analyzer.Semantic.Model;
using VB6Analyzer.Semantic.Resolution;

namespace VB6Analyzer.Semantic.Graph;

/// <summary>
/// Costruttore del Call Graph e generatore delle relazioni semantiche nel CodeGraph (Milestone M4).
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
    /// Costruisce tutte le relazioni semantiche (CALLS, INSTANTIATES, USES_TYPE) nel CodeGraph
    /// per tutti i moduli parsati passati come input.
    /// </summary>
    /// <param name="extractionResults">Elenco dei risultati di estrazione dei moduli contenenti gli AST.</param>
    public void BuildGraph(IEnumerable<ModuleExtractionResult> extractionResults)
    {
        // 1. Genera le relazioni CALLS e INSTANTIATES attraversando gli AST delle routine
        foreach (var extraction in extractionResults)
        {
            if (!extraction.Success || extraction.ParseTree == null)
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
