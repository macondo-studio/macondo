using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Resolution;

/// <summary>
/// Risoluzione di un call-site o accesso a membro all'interno del codice VB6.
/// </summary>
public sealed class ResolvedCallSite
{
    public required string CallText { get; init; }
    public required int Line { get; init; }
    public required ResolutionResult Result { get; init; }
    public string? EnclosingRoutineName { get; init; }
    public bool IsInWithBlock { get; init; }

    public override string ToString() =>
        $"Riga {Line,3}: '{CallText}' -> {Result}";
}

/// <summary>
/// Motore di risoluzione dei simboli e accessi per VB6 (M3 del charter).
/// Gestisce identificatori semplici, accessi qualificati (a.b), keyword Me,
/// blocchi With (.member), e late binding su Object/Variant.
/// </summary>
public sealed class SymbolResolver
{
    private readonly CodeGraph _graph;
    private readonly TypeResolver _typeResolver;
    private readonly ProjectScope _projectScope;
    private readonly Dictionary<Guid, ModuleScope> _moduleScopes = new();

    public ProjectScope ProjectScope => _projectScope;

    public SymbolResolver(CodeGraph graph, TypeResolver typeResolver)
    {
        _graph = graph;
        _typeResolver = typeResolver;
        _projectScope = new ProjectScope(graph, typeResolver);

        BuildModuleScopes();
    }

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

    public ModuleScope GetModuleScope(Symbol moduleSymbol)
    {
        if (_moduleScopes.TryGetValue(moduleSymbol.Id, out var scope))
            return scope;

        var newScope = new ModuleScope(moduleSymbol, _graph, _projectScope);
        _moduleScopes[moduleSymbol.Id] = newScope;
        return newScope;
    }

    public LocalScope CreateLocalScope(Symbol routineSymbol, IScope parentScope)
    {
        return new LocalScope(routineSymbol, parentScope, _graph);
    }

    public WithScope CreateWithScope(IScope parentScope, ResolutionResult targetResult)
    {
        return new WithScope(parentScope, targetResult, _graph, _typeResolver);
    }

    /// <summary>
    /// Risolve un identificatore non qualificato (es. "SaveCustomer", "mCount", "i", "Customer").
    /// </summary>
    public ResolutionResult ResolveIdentifier(IScope scope, string name)
    {
        return scope.Resolve(name);
    }

    /// <summary>
    /// Risolve un accesso a membro qualificato (es. "Customer.Update", "c.Save", "Me.Reset", "obj.DoSomething").
    /// </summary>
    public ResolutionResult ResolveMemberAccess(IScope scope, string targetName, string memberName)
    {
        // 1. Risolve il target nello scope corrente
        var targetResult = scope.Resolve(targetName);

        if (!targetResult.IsSuccess && targetResult.Confidence != ResolutionConfidence.Unknown)
        {
            return targetResult;
        }

        // 2. Risolve il membro sul target
        return ResolveMemberOnTarget(targetResult, memberName);
    }

    /// <summary>
    /// Risolve un membro su una catena qualificata (es. ["a", "b", "c"]).
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
    /// Risolve un membro su un target già risolto.
    /// </summary>
    public ResolutionResult ResolveMemberOnTarget(ResolutionResult targetResult, string memberName)
    {
        // Se il target è late-bound (Object / Variant) o sconosciuto
        if (string.Equals(targetResult.TargetTypeName, "Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetResult.TargetTypeName, "Variant", StringComparison.OrdinalIgnoreCase))
        {
            return ResolutionResult.Unknown(memberName, typeName: "Variant", details: $"Late binding su oggetto non tipizzato ({targetResult.TargetTypeName})");
        }

        Symbol? targetTypeSymbol = null;

        if (targetResult.TargetSymbol is not null)
        {
            var sym = targetResult.TargetSymbol;
            if (sym.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type)
            {
                targetTypeSymbol = sym;
            }
            else if (sym.ResolvedTypeSymbolId.HasValue)
            {
                targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == sym.ResolvedTypeSymbolId.Value);
            }
            else if (sym.DeclaredTypeName is not null)
            {
                var typeInfo = _typeResolver.ResolveType(sym.DeclaredTypeName);
                targetTypeSymbol = typeInfo.UserTypeSymbol;
            }
        }
        else if (targetResult.TargetParameter is not null)
        {
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
