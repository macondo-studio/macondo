using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Resolution;



/// <summary>
/// Motore principale di risoluzione dei simboli e degli accessi per VB6 (Milestone M3, sezioni 11, 12, 13, 14 del charter).
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

        BuildModuleScopes();
    }

    /// <summary>
    /// Inizializza i <see cref="ModuleScope"/> per tutte le classi, form, moduli e user control presenti nel grafo.
    /// </summary>
    private void BuildModuleScopes()
    {
        foreach (var symbol in _graph.Symbols)
        {
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl)
            {
                _moduleScopes[symbol.Id] = new ModuleScope(symbol, _graph, _projectScope);
            }
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
    /// Risolve un identificatore non qualificato (es. "SaveCustomer", "mCount", "i", "Customer") nello scope specificato.
    /// </summary>
    public ResolutionResult ResolveIdentifier(IScope scope, string name)
    {
        return scope.Resolve(name);
    }

    /// <summary>
    /// Risolve un accesso qualificato a 2 elementi (es. "c.Save", "Customer.Update", "Me.Reset", "obj.DoSomething").
    /// </summary>
    /// <param name="scope">Lo scope da cui parte la risoluzione del target.</param>
    /// <param name="targetName">Il nome del target (variabile, classe, modulo o 'Me').</param>
    /// <param name="memberName">Il nome del membro da accedere sul target.</param>
    public ResolutionResult ResolveMemberAccess(IScope scope, string targetName, string memberName)
    {
        // 1. Risolve prima il target nello scope corrente (es. trova la variabile locale 'c')
        var targetResult = scope.Resolve(targetName);

        if (!targetResult.IsSuccess && targetResult.Confidence != ResolutionConfidence.Unknown)
        {
            return targetResult;
        }

        // 2. Risolve il membro sul target identificato
        return ResolveMemberOnTarget(targetResult, memberName);
    }

    /// <summary>
    /// Risolve una catena di accessi qualificati arbitrariamente lunga (es. ["a", "b", "c"]).
    /// </summary>
    public ResolutionResult ResolveChainedAccess(IScope scope, IReadOnlyList<string> chain)
    {
        if (chain.Count == 0)
            return ResolutionResult.Unknown("", details: "Catena di accesso vuota");

        if (chain.Count == 1)
            return scope.Resolve(chain[0]);

        var currentResult = scope.Resolve(chain[0]);

        for (var i = 1; i < chain.Count; i++)
        {
            currentResult = ResolveMemberOnTarget(currentResult, chain[i]);
            if (currentResult.Confidence == ResolutionConfidence.Unknown)
                break;
        }

        return currentResult;
    }

    /// <summary>
    /// Risolve un membro su un target già precedentemente risolto.
    /// Gestisce:
    /// - Late binding su Object / Variant -> <see cref="ResolutionConfidence.Unknown"/>
    /// - Accesso su tipo Class / Form / Module / Type -> cerca tra i figli del tipo
    /// - Accesso su variabile/parametro tipizzato -> risale al tipo della variabile e cerca tra i membri di quel tipo.
    /// </summary>
    public ResolutionResult ResolveMemberOnTarget(ResolutionResult targetResult, string memberName)
    {
        // 1. Se il target è un tipo a binding dinamico (Object / Variant) -> Late Binding
        if (string.Equals(targetResult.TargetTypeName, "Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetResult.TargetTypeName, "Variant", StringComparison.OrdinalIgnoreCase))
        {
            return ResolutionResult.Unknown(memberName, typeName: "Variant", details: $"Late binding su oggetto non tipizzato ({targetResult.TargetTypeName})");
        }

        Symbol? targetTypeSymbol = null;

        // 2. Determina il simbolo che descrive il tipo del target
        if (targetResult.TargetSymbol is not null)
        {
            var sym = targetResult.TargetSymbol;
            if (sym.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type)
            {
                // Il target è direttamente una classe, modulo o tipo (es. "Customer.Update" o "UtilsModule.LogMessage")
                targetTypeSymbol = sym;
            }
            else if (sym.ResolvedTypeSymbolId.HasValue)
            {
                // Il target è una variabile/campo tipizzata con tipo utente (es. "localCust" di tipo "Customer")
                targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == sym.ResolvedTypeSymbolId.Value);
            }
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
            {
                targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == param.ResolvedTypeSymbolId.Value);
            }
            else if (param.DeclaredTypeName is not null)
            {
                var typeInfo = _typeResolver.ResolveType(param.DeclaredTypeName);
                targetTypeSymbol = typeInfo.UserTypeSymbol;
            }
        }
        else if (targetResult.ResolvedTypeSymbolId.HasValue)
        {
            targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == targetResult.ResolvedTypeSymbolId.Value);
        }
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
            {
                return ResolutionResult.Exact(member);
            }

            return ResolutionResult.Unknown(memberName, details: $"Membro '{memberName}' non trovato nel tipo '{targetTypeSymbol.QualifiedName}'");
        }

        return ResolutionResult.Unknown(memberName, details: $"Tipo del target '{targetResult}' non risolto o non definito nel progetto");
    }
}
