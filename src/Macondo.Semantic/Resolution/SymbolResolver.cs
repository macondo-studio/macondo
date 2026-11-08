using Macondo.Semantic.Model;

namespace Macondo.Semantic.Resolution;



/// <summary>
/// Motore principale di risoluzione dei simboli e degli accessi per VB6.
/// 
/// RESPONSABILITÀ:
/// 1. Risoluzione Identificatori Semplici: delega alla gerarchia di scope (<see cref="IScope"/>).
/// 2. Risoluzione Accessi Qualificati:
///    - "Target.Member" (es. "c.Save", "Customer.Update", "UtilsModule.LogMessage").
///    - Risolve prima 'Target' per individuarne il tipo semantico (<see cref="SymbolKind.Class"/>, ecc.),
///      poi cerca 'Member' tra i membri figli di quel tipo.
/// 3. Risoluzione di 'Me':
///    - "Me.Member" risolve sui membri dichiarati nella classe/form corrente.
/// 4. Risoluzione Chiamate Contestuali 'With':
///    - ".Member" dentro un blocco "With expr" risolve direttamente sui membri del tipo di 'expr'.
/// 5. Gestione Late Binding e Tipi Dinamici:
///    - Su oggetti dichiarati come 'Object' o 'Variant' (o CreateObject), produce <see cref="ResolutionConfidence.Unknown"/>
///      senza errori e senza inventare relazioni false.
/// </summary>
public sealed class SymbolResolver
{
    private readonly CodeGraph _graph;
    private readonly TypeResolver _typeResolver;
    private readonly ProjectScope _projectScope;

    /// <summary>Cache dei ModuleScope per ciascun modulo/classe nel progetto.</summary>
    private readonly Dictionary<Guid, ModuleScope> _moduleScopes = new();

    /// <summary>Scope di progetto globale condiviso.</summary>
    public ProjectScope ProjectScope => _projectScope;

    /// <summary>Motore di risoluzione dei tipi associato.</summary>
    public TypeResolver TypeResolver => _typeResolver;

    /// <summary>
    /// Inizializza il SymbolResolver per il grafo specificato.
    /// </summary>
    /// <param name="graph">Il CodeGraph con tutti i simboli del progetto.</param>
    /// <param name="typeResolver">Il resolver dei tipi per interrogare classificazioni e tipi utente.</param>
    public SymbolResolver(CodeGraph graph, TypeResolver typeResolver)
    {
        _graph = graph;
        _typeResolver = typeResolver;
        _projectScope = new ProjectScope(graph, typeResolver);

        // Inizializza i ModuleScope per tutte le classi, form, moduli e user control presenti nel grafo.
        foreach (var symbol in _graph.Symbols)
        {
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl)

                _moduleScopes[symbol.Id] = new ModuleScope(symbol, _graph, _projectScope);
        }
    }

    /// <summary>
    /// Recupera o crea il <see cref="ModuleScope"/> per il modulo specificato.
    /// </summary>
    public ModuleScope GetModuleScope(Symbol moduleSymbol)
    {
        if (_moduleScopes.TryGetValue(moduleSymbol.Id, out var scope))
            return scope;

        var newScope = new ModuleScope(moduleSymbol, _graph, _projectScope);
        _moduleScopes[moduleSymbol.Id] = newScope;
        return newScope;
    }

    /// <summary>
    /// Crea un nuovo <see cref="LocalScope"/> per una routine (Sub, Function, Property) agganciato al parent scope specificato.
    /// </summary>
    public LocalScope CreateLocalScope(Symbol routineSymbol, IScope parentScope)
    {
        return new LocalScope(routineSymbol, parentScope, _graph);
    }

    /// <summary>
    /// Crea un nuovo <see cref="WithScope"/> contestuale agganciato all'espressione target del blocco 'With'.
    /// </summary>
    public WithScope CreateWithScope(IScope parentScope, ResolutionResult targetResult)
    {
        return new WithScope(parentScope, targetResult, _graph, _typeResolver);
    }


    /// <summary>
    /// Risolve un membro su un target già precedentemente risolto.
    /// 
    /// GESTISCE:
    /// 1. Proprietà intrinseche del runtime VB6 (es. 'ActiveForm' su MDIForm/Screen, 'Parent' su controlli UI):
    ///    restituisce un <see cref="ResolutionResult"/> tipizzato come "Form" con confidenza <see cref="ResolutionConfidence.Inferred"/>.
    /// 2. Late Binding su tipi dinamici o base astratti UI ('Object', 'Variant', 'Form', 'Control', 'MDIForm'):
    ///    restituisce <see cref="ResolutionConfidence.Unknown"/> preservando il <see cref="ResolutionResult.TargetTypeName"/>
    ///    effettivo per consentire al <see cref="Graph.CallGraphBuilder"/> di applicare l'euristica di candidate matching.
    /// 3. Accesso su tipo o istanza (Class / Form / Module .bas / UserControl / Type UDT):
    ///    risale al tipo semantico e cerca il membro tra i figli diretti del contenitore nel CodeGraph.
    /// 4. Chiamate a catena su metodi/proprietà (es. 'getFirstForm().Method', 'prop.Method'):
    ///    utilizza il ReturnType / ResolvedReturnTypeSymbolId della routine ricevente per risolvere i membri successivi.
    /// </summary>
    /// <param name="targetResult">L'esito della risoluzione semantica dell'espressione ricevitore (target).</param>
    /// <param name="memberName">Il nome del membro da risolvere sul target.</param>
    /// <returns>L'esito <see cref="ResolutionResult"/> con il simbolo risolto e la relativa confidenza.</returns>
    public ResolutionResult ResolveMemberOnTarget(ResolutionResult targetResult, string memberName)
    {
        // 0. Proprietà intrinseche di contenitori VB6 che restituiscono un Form (es. MdiFrm.ActiveForm, ctrl.Parent).
        // Nel runtime VB6 'ActiveForm' (su MDIForm o Screen) e 'Parent' (su controlli visivi) restituiscono sempre
        // un'istanza polimorfica di Form. Inferiamo il tipo "Form" per guidare la successiva risoluzione dei membri.
        if (string.Equals(memberName, "ActiveForm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(memberName, "Parent", StringComparison.OrdinalIgnoreCase))
            return new ResolutionResult
            {
                Confidence = ResolutionConfidence.Inferred,
                TargetTypeName = "Form",
                Details = $"Proprietà intrinseca VB6 '{memberName}' di tipo Form"
            };

        // Calcola il nome del tipo effettivo del ricevitore risalendo a:
        // - TargetTypeName esplicito già determinato
        // - ReturnType se il target è una Function o Property
        // - DeclaredTypeName se il target è un campo, variabile o parametro
        var effectiveTypeName = targetResult.TargetTypeName
            ?? (targetResult.TargetSymbol?.Kind is SymbolKind.Method or SymbolKind.Property ? targetResult.TargetSymbol.ReturnType : null)
            ?? targetResult.TargetSymbol?.DeclaredTypeName
            ?? targetResult.TargetParameter?.DeclaredTypeName;

        // 1. Se il target è un tipo a binding dinamico o base astratta UI (Object / Variant / Form / Control / MDIForm) -> Late Binding.
        // In VB6 le chiamate su 'As Form', 'As Control' o 'As Object' vengono inoltrate a runtime via IDispatch::Invoke.
        // Preserviamo 'effectiveTypeName' nel TargetTypeName per consentire al CallGraphBuilder di filtrare
        // i candidati compatibili (es. solo metodi di Form quando il ricevitore è 'Form').
        if (string.Equals(effectiveTypeName, "Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(effectiveTypeName, "Variant", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(effectiveTypeName, "Form", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(effectiveTypeName, "Control", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(effectiveTypeName, "MDIForm", StringComparison.OrdinalIgnoreCase))
            return new ResolutionResult
            {
                Confidence = ResolutionConfidence.Unknown,
                TargetTypeName = effectiveTypeName,
                Details = $"Late binding su oggetto dinamico o tipo base UI ({effectiveTypeName})"
            };

        Symbol? targetTypeSymbol = null;

        // 2. Determina il simbolo che descrive il tipo del target
        if (targetResult.TargetSymbol is not null)
        {
            var sym = targetResult.TargetSymbol;
            if (sym.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type)
                targetTypeSymbol = sym;
            else if (sym.Kind is SymbolKind.Method or SymbolKind.Property)
            {
                // Se il target è un metodo o una proprietà (es. bcPCassaProcessor), il tipo per la risoluzione dei membri
                // successivi è il tipo restituito dalla routine (ReturnType / ResolvedReturnTypeSymbolId)
                if (sym.ResolvedReturnTypeSymbolId.HasValue)
                    targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == sym.ResolvedReturnTypeSymbolId.Value);
                else if (sym.ReturnType is not null)
                    targetTypeSymbol = _typeResolver.ResolveType(sym.ReturnType).UserTypeSymbol;
                else if (sym.ResolvedTypeSymbolId.HasValue)
                    targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == sym.ResolvedTypeSymbolId.Value);
                else if (sym.DeclaredTypeName is not null)
                    targetTypeSymbol = _typeResolver.ResolveType(sym.DeclaredTypeName).UserTypeSymbol;
            }
            else if (sym.ResolvedTypeSymbolId.HasValue)
                targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == sym.ResolvedTypeSymbolId.Value);
            else if (sym.DeclaredTypeName is not null)
            {
                // Prova la risoluzione del nome del tipo dichiarato
                var typeInfo = _typeResolver.ResolveType(sym.DeclaredTypeName);
                targetTypeSymbol = typeInfo.UserTypeSymbol;
            }
        }
        else if (targetResult.TargetParameter is not null)
        {
            // Il target è un parametro formale (es. "cust" di tipo "Customer")
            var param = targetResult.TargetParameter;
            if (param.ResolvedTypeSymbolId.HasValue)
                targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == param.ResolvedTypeSymbolId.Value);
            else if (param.DeclaredTypeName is not null)
            {
                var typeInfo = _typeResolver.ResolveType(param.DeclaredTypeName);
                targetTypeSymbol = typeInfo.UserTypeSymbol;
            }
        }
        else if (targetResult.ResolvedTypeSymbolId.HasValue)
            targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == targetResult.ResolvedTypeSymbolId.Value);
        else if (targetResult.TargetTypeName is not null)
        {
            var typeInfo = _typeResolver.ResolveType(targetResult.TargetTypeName);
            targetTypeSymbol = typeInfo.UserTypeSymbol;
        }

        // 3. Cerca il membro specificato all'interno del tipo target identificato
        if (targetTypeSymbol is not null)
        {
            var member = _graph.GetChildren(targetTypeSymbol.Id)
                .FirstOrDefault(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase));

            if (member != null)
                return new ResolutionResult
                {
                    TargetSymbol = member,
                    Confidence = ResolutionConfidence.Exact,
                    TargetTypeName = member.ReturnType ?? member.DeclaredTypeName,
                    ResolvedTypeSymbolId = member.ResolvedReturnTypeSymbolId ?? member.ResolvedTypeSymbolId
                };

            return new ResolutionResult
            {
                Confidence = ResolutionConfidence.Unknown,
                Details = $"Membro '{memberName}' non trovato nel tipo '{targetTypeSymbol.QualifiedName}'"
            };
        }

        return new ResolutionResult
        {
            Confidence = ResolutionConfidence.Unknown,
            TargetTypeName = effectiveTypeName,
            Details = $"Tipo del target '{targetResult}' non risolto o non definito nel progetto"
        };
    }
}
