using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;

namespace Macondo.Semantic.Graph;

/// <summary>
/// Costruttore del Call Graph e generatore centralizzato delle relazioni semantiche nel <see cref="CodeGraph"/>.
/// 
/// RESPONSABILITÀ E FASI DI COSTRUZIONE DEL GRAFO:
/// 1. Analisi Invocazioni VB6 (CALLS): attraversa gli AST di tutti i moduli tramite <see cref="RoutineInvocationAnalyzer"/>
///    e genera le relazioni <see cref="RelationshipKind.Calls"/> tra routine chiamanti e chiamate, con livello di confidenza associato.
/// 2. Analisi Invocazioni e Tipi C# (CALLS, INSTANTIATES, INHERITS, IMPLEMENTS): risolve i call-site C# con receiver o indice per nome,
///    istanziazioni 'new' ed ereditarietà/interfacce per progetti polyglot o ibridi.
/// 3. Analisi Istanziazioni Esplicite ed Implicite (INSTANTIATES): individua sia le espressioni 'Set ... = New Type' sia le dichiarazioni
///    auto-istantianti 'Dim ... As New Type' e genera le relazioni <see cref="RelationshipKind.Instantiates"/>.
/// 4. Analisi Dipendenze di Tipo (USES_TYPE): individua tutti i campi, variabili, parametri formali e valori di ritorno
///    tipizzati con tipi utente/progetto e genera le relazioni <see cref="RelationshipKind.UsesType"/>.
/// 5. Analisi Gestori Eventi UI e Moduli (HANDLES_EVENT): individua le convenzioni VB6 (es. "cmdOk_Click", "Form_Load")
///    e collega sia il controllo UI che il container del modulo alla routine gestore evento.
/// 6. Rilascio Risorse: dereferenzia i ParseTree ANTLR per consentire al Garbage Collector di deallocare la memoria occupata dagli AST.
/// </summary>
public sealed class CallGraphBuilder
{
    private readonly CodeGraph _graph;
    private readonly SymbolResolver _symbolResolver;

    /// <summary>
    /// Inizializza una nuova istanza del builder del Call Graph per il grafo specificato.
    /// </summary>
    /// <param name="graph">Il <see cref="CodeGraph"/> condiviso in cui inserire le relazioni semantiche.</param>
    /// <param name="symbolResolver">Il motore di risoluzione dei simboli e scope (<see cref="SymbolResolver"/>).</param>
    public CallGraphBuilder(CodeGraph graph, SymbolResolver symbolResolver)
    {
        _graph = graph;
        _symbolResolver = symbolResolver;
    }

    /// <summary>
    /// Costruisce tutte le relazioni semantiche (CALLS, INSTANTIATES, USES_TYPE, HANDLES_EVENT, INHERITS, IMPLEMENTS) nel CodeGraph
    /// per tutti i moduli VB6 e i file sorgente C# parsati.
    /// </summary>
    /// <param name="extractionResults">Elenco dei risultati di estrazione dei moduli VB6 contenenti gli AST (<see cref="ModuleExtractionResult"/>).</param>
    /// <param name="csharpResults">Elenco opzionale dei risultati di estrazione dei file sorgente C# (<see cref="CSharpExtractionResult"/>).</param>
    /// <param name="javaResults">Elenco opzionale dei risultati di estrazione dei file sorgente Java (<see cref="JavaExtractionResult"/>).</param>
    /// <param name="cancellationToken">Token opzionale di cancellazione per interrompere l'elaborazione del Call Graph.</param>
    public void BuildGraph(
        IEnumerable<ModuleExtractionResult> extractionResults,
        IEnumerable<CSharpExtractionResult>? csharpResults = null,
        IEnumerable<JavaExtractionResult>? javaResults = null,
        CancellationToken cancellationToken = default)
    {
        // =========================================================================
        // FASE 1: GENERAZIONE RELAZIONI DA AST DI MODULI VB6 (CALLS & INSTANTIATES)
        // =========================================================================
        // In questa fase attraversiamo il Parse Tree ANTLR di ogni modulo VB6 tramite RoutineInvocationAnalyzer.
        // L'analizzatore identifica tutte le chiamate esplicite (Call Foo), implicite (Foo arg) e istanziazioni (New Foo).
        Dictionary<string, List<Symbol>>? publicRoutinesByName = null;
        Dictionary<Guid, SymbolKind>? symbolParentKindById = null;

        foreach (var extraction in extractionResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (extraction.ParseTree == null)
                continue;

            try
            {
                // Inizializza l'analizzatore AST contestualizzato allo scope del modulo corrente
                var analyzer = new RoutineInvocationAnalyzer(_symbolResolver, extraction.ModuleSymbol);
                analyzer.Visit(extraction.ParseTree);

                // 1a. Generazione delle relazioni CALLS e READS tra routine chiamante e simboli referenziati
                foreach (var callSite in analyzer.CallSites)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var callerRoutine = FindEnclosingRoutine(extraction.ModuleSymbol, callSite.EnclosingRoutineName);
                    if (callerRoutine == null)
                        continue;

                    if (callSite.Result.TargetSymbol != null)
                    {
                        // Registra relazioni CALLS per Method, Property ed Event (generati da istruzioni 'RaiseEvent NomeEvento').
                        // In VB6 'RaiseEvent' invoca formalmente l'evento dichiarato: collegare il chiamante all'evento garantisce
                        // che il simbolo Event sia marcato come attivo e raggiungibile nel CodeGraph.
                        if (callSite.Result.TargetSymbol.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Event)
                            _graph.AddCalls(callerRoutine, callSite.Result.TargetSymbol, callSite.Line, callSite.Result.Confidence);
                        else if (callSite.Result.TargetSymbol.Kind is SymbolKind.Field or SymbolKind.Constant or SymbolKind.Enum)
                            _graph.AddReads(callerRoutine, callSite.Result.TargetSymbol, callSite.Line);
                    }
                    else if (!string.IsNullOrEmpty(callSite.InvokedMemberName))
                    {
                        // =====================================================================
                        // Risoluzione Late Binding in VB6 (ricevitori 'As Form', 'As Object', 'ActiveForm', ecc.)
                        // =====================================================================
                        // In VB6 le chiamate su tipi debolmente tipizzati o base astratta UI (IDispatch::Invoke)
                        // non possono essere risolte su un unico tipo statico a tempo di compilazione.
                        // Per prevenire falsi positivi nell'analisi del Dead Code (dove altrimenti metodi pubblici
                        // polimorfici verrebbero scartati perché chiamati solo tramite 'frm.Metodo' o 'ActiveForm.Metodo'):
                        // 1. Costruiamo in modo 'lazy' un indice globale delle routine pubbliche per nome O(1).
                        // 2. Mappiamo il Kind del contenitore padre (Form, UserControl, Class) per ogni routine.
                        // 3. Filtriamo i candidati in base al tipo semantico dichiarato o inferito del ricevitore:
                        //    - Ricevitore 'Form' o 'MDIForm' -> abbina routine definite in Form o UserControl
                        //    - Ricevitore 'Control'          -> abbina routine definite in UserControl o Form
                        //    - Ricevitore 'Object' / Variant -> abbina routine definite in Form, UserControl o Class
                        // 4. Colleghiamo le chiamate con confidenza Inferred.
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

                // 1b. Generazione delle relazioni INSTANTIATES da espressioni esplicite 'New Type' (es. "Set x = New Customer")
                foreach (var inst in analyzer.Instantiations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (inst.TargetClassSymbol != null)
                    {
                        // Il chiamante è la routine contenitrice oppure il modulo stesso se a livello di dichiarazione
                        var sourceSymbol = FindEnclosingRoutine(extraction.ModuleSymbol, inst.EnclosingRoutineName) ?? extraction.ModuleSymbol;
                        _graph.AddInstantiates(sourceSymbol, inst.TargetClassSymbol, inst.Line, inst.Confidence);
                    }
                }

                // 1c. Generazione delle relazioni IMPLEMENTS da istruzioni 'Implements InterfaceName'
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
                // Resilienza: isola eventuali eccezioni inattese su singoli moduli per consentire il completamento del resto del progetto
                System.Diagnostics.Trace.TraceWarning($"[Macondo] Analisi modulo '{extraction.ModuleSymbol.Name}' saltata per errore: {ex.Message}");
            }
        }

        // =========================================================================
        // FASE 1b: GENERAZIONE RELAZIONI SEMANTICHE PER FILE C# (Roslyn)
        // =========================================================================
        // Se sono presenti file C# analizzati (progetti .NET o ibridi VB6/C#), risolviamo:
        // - Invocazioni di metodi (CALLS) con risoluzione di tipo del ricevitore o fallback per nome.
        // - Creazioni di oggetti 'new' (INSTANTIATES).
        // - Ereditarietà di classi e implementazione di interfacce (INHERITS / IMPLEMENTS).
        if (csharpResults != null)
        {
            // Indice dei metodi/proprietà per nome costruito in modo 'lazy' (solo se necessario)
            // per garantire lookup O(1) invece di scansioni lineari O(N) su grafi con migliaia di simboli.
            Dictionary<string, Symbol>? routineIndexByName = null;

            foreach (var csResult in csharpResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. Risoluzione Invocazioni di Metodi C# (CALLS)
                foreach (var callSite in csResult.CallSites)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Symbol? targetMethod = null;

                    // Passo A: Se è presente un'espressione ricevitore (es. "customerService.Process()"),
                    // tentiamo di risolvere il tipo del ricevitore tramite il TypeResolver e cerchiamo il metodo tra i figli del tipo.
                    if (!string.IsNullOrEmpty(callSite.ReceiverExpressionText))
                    {
                        var receiverType = _symbolResolver.TypeResolver.ResolveType(callSite.ReceiverExpressionText).UserTypeSymbol;
                        if (receiverType != null)
                            targetMethod = _graph.GetChildren(receiverType.Id)
                                .FirstOrDefault(s => s.Kind is SymbolKind.Method or SymbolKind.Property &&
                                                    string.Equals(s.Name, callSite.InvokedMethodName, StringComparison.OrdinalIgnoreCase));
                    }

                    // Passo B: Se il metodo non è stato risolto tramite receiver (es. chiamata diretta 'Process()' o receiver non tipizzato),
                    // utilizziamo l'indice globale dei metodi per lookup O(1).
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

                    // Passo C: Se il metodo bersaglio è stato individuato, inserisce l'arco CALLS nel CodeGraph
                    if (targetMethod != null)
                        _graph.AddCalls(callSite.CallerSymbol, targetMethod, callSite.Line, ResolutionConfidence.Exact);
                }

                // 2. Risoluzione Istanziazioni C# (INSTANTIATES da 'new ClassName()')
                foreach (var inst in csResult.Instantiations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var targetType = _symbolResolver.TypeResolver.ResolveType(inst.TargetTypeName).UserTypeSymbol;
                    if (targetType != null)
                        _graph.AddInstantiates(inst.SourceSymbol, targetType, inst.Line, ResolutionConfidence.Exact);
                }

                // 3. Risoluzione Ereditarietà e Interfacce C# (INHERITS / IMPLEMENTS da ': BaseType, IInterface')
                foreach (var inh in csResult.Inheritances)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var baseType = _symbolResolver.TypeResolver.ResolveType(inh.BaseOrInterfaceTypeName).UserTypeSymbol;
                    if (baseType != null)
                    {
                        // Se il tipo derivato è un'interfaccia o il bersaglio è una classe -> INHERITS;
                        // Se una classe/struct implementa un'interfaccia -> IMPLEMENTS
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
        // FASE 1c: GENERAZIONE RELAZIONI SEMANTICHE PER FILE JAVA
        // =========================================================================
        // Se sono presenti file sorgente Java analizzati (.java in progetti standard, Maven o Gradle),
        // risolviamo le relazioni semantiche orientate:
        // 1. Invocazioni di metodi (CALLS):
        //    - Passo A: Se è presente un ricevitore esplicito (es. 'customerService.saveOrder()'), tentiamo
        //      la risoluzione del tipo del ricevitore tramite il TypeResolver e cerchiamo il metodo nei figli del tipo.
        //    - Passo B: Se il metodo è invocato senza ricevitore (chiamata interna/ereditata) o il ricevitore non è risolto,
        //      usiamo l'indice globale O(1) dei metodi.
        // 2. Creazioni di oggetti (INSTANTIATES da 'new ClassName()').
        // 3. Ereditarietà di classi base ('extends') e implementazione di interfacce ('implements') (INHERITS / IMPLEMENTS).
        if (javaResults != null)
        {
            // Indice dei metodi e proprietà per nome (costruito lazy) per garantire lookup O(1) rapido
            Dictionary<string, Symbol>? routineIndexByName = null;

            foreach (var javaResult in javaResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. Risoluzione Invocazioni di Metodi Java (CALLS)
                foreach (var callSite in javaResult.CallSites)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Symbol? targetMethod = null;

                    // Passo A: Risoluzione contestuale tramite tipo del ricevitore (es. "orderService.process()")
                    if (!string.IsNullOrEmpty(callSite.ReceiverExpressionText))
                    {
                        var receiverType = _symbolResolver.TypeResolver.ResolveType(callSite.ReceiverExpressionText).UserTypeSymbol;
                        if (receiverType != null)
                            targetMethod = _graph.GetChildren(receiverType.Id)
                                .FirstOrDefault(s => s.Kind is SymbolKind.Method or SymbolKind.Property &&
                                                    string.Equals(s.Name, callSite.InvokedMethodName, StringComparison.OrdinalIgnoreCase));
                    }

                    // Passo B: Fallback su indice globale O(1) per chiamate non qualificate o receiver implicito
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

                    // Passo C: Inserimento dell'arco CALLS nel CodeGraph
                    if (targetMethod != null)
                        _graph.AddCalls(callSite.CallerSymbol, targetMethod, callSite.Line, ResolutionConfidence.Exact);
                }

                // 2. Risoluzione Istanziazioni Java (INSTANTIATES da 'new ClassName()')
                foreach (var inst in javaResult.Instantiations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var targetType = _symbolResolver.TypeResolver.ResolveType(inst.TargetTypeName).UserTypeSymbol;
                    if (targetType != null)
                        _graph.AddInstantiates(inst.SourceSymbol, targetType, inst.Line, ResolutionConfidence.Exact);
                }

                // 3. Risoluzione Ereditarietà e Interfacce Java (INHERITS / IMPLEMENTS da 'extends' o 'implements')
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
        // FASE 2: ISTANZIAZIONI DA DICHIARAZIONI AUTO-ISTANTIANTI (Dim As New)
        // =========================================================================
        // In VB6 la clausola 'Dim c As New Customer' o 'Private m_Service As New Service'
        // dichiara una variabile e contestualmente genera un'istanziazione implicita (IsNewInstance == true).
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
        // FASE 3: DIPENDENZE STATICHE DI TIPO (USES_TYPE)
        // =========================================================================
        // Ispeziona tutte le entità del grafo per creare le relazioni USES_TYPE per:
        // - Variabili locali, campi e proprietà tipizzati con tipi utente (ResolvedTypeSymbolId).
        // - Valori di ritorno di funzioni e proprietà (ResolvedReturnTypeSymbolId).
        // - Parametri formali di routine e metodi (param.ResolvedTypeSymbolId).
        foreach (var symbol in _graph.Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 3a. Tipo dichiarato su variabili locali, campi di modulo o proprietà
            if (symbol.ResolvedTypeSymbolId.HasValue)
            {
                var targetType = _graph.FindSymbolById(symbol.ResolvedTypeSymbolId.Value);
                if (targetType != null)
                    _graph.AddUsesType(symbol, targetType, symbol.LineStart);
            }

            // 3b. Tipo di ritorno dichiarato su funzioni e proprietà getter
            if (symbol.ResolvedReturnTypeSymbolId.HasValue)
            {
                var returnType = _graph.FindSymbolById(symbol.ResolvedReturnTypeSymbolId.Value);
                if (returnType != null)
                    _graph.AddUsesType(symbol, returnType, symbol.LineStart);
            }

            // 3c. Tipi dichiarati sui singoli parametri formali della routine
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
        // FASE 4: GESTORI DI EVENTI UI E MODULO (HANDLES_EVENT)
        // =========================================================================
        // In VB6, gli handler degli eventi seguono la convenzione sintattica 'PrefissoControllo_NomeEvento'
        // (es. 'cmdSalva_Click', 'txtNome_Change', 'Form_Load', 'UserControl_Initialize').
        // Per ogni procedura identificata come probabile handler:
        // - Collega il controllo UI (se presente come campo del form/modulo) alla routine gestore.
        // - Collega sempre il contenitore radice (Form, UserControl o Class) alla routine.
        foreach (var extraction in extractionResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var moduleChildren = _graph.GetChildren(extraction.ModuleSymbol.Id).ToList();
            var moduleFields = moduleChildren.Where(s => s.Kind == SymbolKind.Field).ToList();
            var moduleRoutines = moduleChildren.Where(s => s.Kind is SymbolKind.Method or SymbolKind.Property).ToList();

            // 1. Raccoglie tutte le routine candidate gestori evento
            var candidateRoutines = new HashSet<Symbol>();
            foreach (var eventHandlerName in extraction.LikelyEventHandlerNames)
            {
                var r = moduleRoutines.FirstOrDefault(s => string.Equals(s.Name, eventHandlerName, StringComparison.OrdinalIgnoreCase));
                if (r != null)
                    candidateRoutines.Add(r);
            }

            // Include anche qualsiasi routine che abbia come prefisso il nome di un campo o variabile WithEvents del modulo
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

            // 2. Collega le relazioni HANDLES_EVENT
            foreach (var routine in candidateRoutines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Cerca il match con prefisso più lungo tra i campi del modulo (controlli visivi, variabili WithEvents)
                var controlSymbol = moduleFields
                    .Where(f => routine.Name.StartsWith(f.Name + "_", StringComparison.OrdinalIgnoreCase) ||
                                routine.Name.StartsWith(f.Name + "__", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.Name.Length)
                    .FirstOrDefault();

                if (controlSymbol != null)
                {
                    _graph.AddHandlesEvent(controlSymbol, routine, routine.LineStart);

                    // Se il controllo o variabile WithEvents ha un tipo utente risolto (es. UserControl o classe custom),
                    // colleghiamo anche il simbolo esplicito dell'evento dichiarato sul tipo (SymbolKind.Event)
                    // direttamente alla routine gestore (es. Event 'Cancellazione' su UserControl -> Sub 'cntr_Cancellazione').
                    // Questo garantisce la bidirezionalità della propagazione degli eventi e la corretta raggiungibilità
                    // sia quando l'evento viene sollevato (RaiseEvent) sia quando la Form viene istanziata.
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

                // Collega sempre il modulo container (Form, UserControl o Class) come generatore dell'evento
                _graph.AddHandlesEvent(extraction.ModuleSymbol, routine, routine.LineStart);
            }
        }

        // =========================================================================
        // FASE 5: OTTIMIZZAZIONE MEMORIA (Rilascio Parse Tree ANTLR)
        // =========================================================================
        // Gli alberi di parsing ANTLR (ParseTree) occupano un quantitativo significativo di memoria RAM.
        // Una volta completata la visita per la risoluzione semantica e il Call Graph,
        // dereferenziamo esplicitamente gli alberi per consentire al Garbage Collector di liberare la memoria.
        foreach (var extraction in extractionResults)
        {
            extraction.ParseTree = null;
        }
    }

    /// <summary>
    /// Ricerca una routine (Metodo o Proprietà) dichiarata come figlia diretta all'interno del modulo specificato,
    /// confrontando il nome in modalità case-insensitive (convenzione VB6).
    /// </summary>
    /// <param name="moduleSymbol">Il simbolo del modulo o classe contenitore.</param>
    /// <param name="routineName">Il nome della routine ricercata.</param>
    /// <returns>Il simbolo della routine se presente; in caso contrario <c>null</c>.</returns>
    private Symbol? FindEnclosingRoutine(Symbol moduleSymbol, string? routineName)
    {
        if (string.IsNullOrEmpty(routineName))
            return null;

        return _graph.GetChildren(moduleSymbol.Id)
            .FirstOrDefault(s => s.Kind is SymbolKind.Method or SymbolKind.Property &&
                                string.Equals(s.Name, routineName, StringComparison.OrdinalIgnoreCase));
    }
}

