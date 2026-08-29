using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.Semantic.Resolution;

/// <summary>
/// Scope a livello di intero progetto VB6.
/// Contiene:
/// 1. Tipi e classi definite nel progetto (.cls, .frm, .ctl, Type, Enum)
/// 2. Membri Public dei moduli standard (.bas), accessibili globalmente ovunque nel progetto senza qualifica.
/// </summary>
public sealed class ProjectScope : IScope
{
    private readonly CodeGraph _graph;
    private readonly TypeResolver _typeResolver;

    // Simboli di tipo modulo o classe indicizzati per nome (es. "Customer", "Module1")
    private readonly Dictionary<string, Symbol> _projectTypes = new(StringComparer.OrdinalIgnoreCase);

    // Membri Public dei moduli standard (.bas) indicizzati per nome (es. "LogMessage")
    private readonly Dictionary<string, List<Symbol>> _globalBasMembers = new(StringComparer.OrdinalIgnoreCase);

    public IScope? ParentScope => null;
    public Symbol? ScopeSymbol => null;
    public string ScopeName => "Project";

    public ProjectScope(CodeGraph graph, TypeResolver typeResolver)
    {
        _graph = graph;
        _typeResolver = typeResolver;
        BuildIndex();
    }

    private void BuildIndex()
    {
        // 1. Tipi definiti a livello di progetto
        foreach (var symbol in _graph.Symbols)
        {
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum)
            {
                _projectTypes[symbol.Name] = symbol;
            }
        }

        // 2. Membri Public dei moduli standard (.bas)
        var basModules = _graph.Symbols
            .Where(s => s.Kind == SymbolKind.Module)
            .ToList();

        foreach (var bas in basModules)
        {
            var children = _graph.GetChildren(bas.Id);
            foreach (var child in children)
            {
                // In VB6 i membri dei moduli .bas sono Public di default se non diversamente specificato
                if (child.Accessibility != Accessibility.Private)
                {
                    if (!_globalBasMembers.TryGetValue(child.Name, out var list))
                    {
                        list = new List<Symbol>();
                        _globalBasMembers[child.Name] = list;
                    }
                    list.Add(child);
                }
            }
        }
    }

    public ResolutionResult Resolve(string name)
    {
        // 1. Cerca nei membri Public dei moduli standard .bas
        if (_globalBasMembers.TryGetValue(name, out var basMemberList))
        {
            if (basMemberList.Count == 1)
            {
                var target = basMemberList[0];
                return ResolutionResult.Exact(target);
            }
            if (basMemberList.Count > 1)
            {
                return ResolutionResult.Ambiguous(name, $"Simbolo '{name}' ambiguo: definito in più moduli standard .bas ({string.Join(", ", basMemberList.Select(m => m.QualifiedName))})");
            }
        }

        // 2. Cerca tra i tipi di progetto (Class, Form, UDT, Enum, ecc.)
        if (_projectTypes.TryGetValue(name, out var typeSymbol))
        {
            return ResolutionResult.Exact(typeSymbol);
        }

        // 3. Tipi primitivi o non trovati
        var typeInfo = _typeResolver.ResolveType(name);
        if (typeInfo.IsPrimitive)
        {
            return ResolutionResult.Unknown(name, typeInfo.Name, $"Tipo primitivo VB6: {name}");
        }

        return ResolutionResult.Unknown(name, details: $"Simbolo '{name}' non trovato nello scope di progetto");
    }

    public Symbol? FindTypeSymbol(string typeName)
    {
        if (_projectTypes.TryGetValue(typeName, out var sym))
            return sym;
        return null;
    }
}

/// <summary>
/// Scope a livello di modulo / classe / form / user control (.cls, .frm, .bas, .ctl).
/// Contiene tutti i membri dichiarati nel file: campi, costanti, metodi, proprietà, UDT, enum.
/// Gestisce la keyword "Me".
/// </summary>
public sealed class ModuleScope : IScope
{
    private readonly Symbol _moduleSymbol;
    private readonly CodeGraph _graph;
    private readonly ProjectScope _projectScope;

    private readonly Dictionary<string, Symbol> _members = new(StringComparer.OrdinalIgnoreCase);

    public IScope? ParentScope => _projectScope;
    public Symbol? ScopeSymbol => _moduleSymbol;
    public string ScopeName => _moduleSymbol.QualifiedName;

    public ModuleScope(Symbol moduleSymbol, CodeGraph graph, ProjectScope projectScope)
    {
        _moduleSymbol = moduleSymbol;
        _graph = graph;
        _projectScope = projectScope;
        BuildIndex();
    }

    private void BuildIndex()
    {
        foreach (var child in _graph.GetChildren(_moduleSymbol.Id))
        {
            _members[child.Name] = child;
        }
    }

    public ResolutionResult Resolve(string name)
    {
        // 1. Keyword Me: si riferisce all'istanza della classe/form corrente
        if (string.Equals(name, "Me", StringComparison.OrdinalIgnoreCase))
        {
            return ResolutionResult.Exact(_moduleSymbol, _moduleSymbol.Name, _moduleSymbol.Id);
        }

        // 2. Membri del modulo corrente
        if (_members.TryGetValue(name, out var member))
        {
            return ResolutionResult.Exact(member);
        }

        // 3. Deferisce allo scope di progetto
        return _projectScope.Resolve(name);
    }

    public Symbol? FindMember(string memberName)
    {
        if (_members.TryGetValue(memberName, out var member))
            return member;
        return null;
    }
}

/// <summary>
/// Scope locale all'interno di una routine (Sub, Function, Property).
/// Contiene:
/// 1. Variabili locali (Dim, Static)
/// 2. Parametri formali della routine
/// </summary>
public sealed class LocalScope : IScope
{
    private readonly Symbol _routineSymbol;
    private readonly IScope _parentScope;
    private readonly CodeGraph _graph;

    private readonly Dictionary<string, Symbol> _localVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ParameterSymbol> _parameters = new(StringComparer.OrdinalIgnoreCase);

    public IScope? ParentScope => _parentScope;
    public Symbol? ScopeSymbol => _routineSymbol;
    public string ScopeName => _routineSymbol.QualifiedName;

    public LocalScope(Symbol routineSymbol, IScope parentScope, CodeGraph graph)
    {
        _routineSymbol = routineSymbol;
        _parentScope = parentScope;
        _graph = graph;
        BuildIndex();
    }

    private void BuildIndex()
    {
        // 1. Parametri della routine
        foreach (var param in _routineSymbol.Parameters)
        {
            _parameters[param.Name] = param;
        }

        // 2. Variabili locali contenute nella routine
        foreach (var child in _graph.GetChildren(_routineSymbol.Id))
        {
            if (child.Kind is SymbolKind.Field or SymbolKind.Constant)
            {
                _localVariables[child.Name] = child;
            }
        }
    }

    public ResolutionResult Resolve(string name)
    {
        // 1. Variabili locali
        if (_localVariables.TryGetValue(name, out var localVar))
        {
            return ResolutionResult.Exact(localVar);
        }

        // 2. Parametri formali
        if (_parameters.TryGetValue(name, out var param))
        {
            return ResolutionResult.ExactParam(param);
        }

        // 3. Assegnazione al nome della funzione stessa per il valore di ritorno (es. MyFunction = 42)
        if (string.Equals(name, _routineSymbol.Name, StringComparison.OrdinalIgnoreCase))
        {
            return ResolutionResult.Exact(_routineSymbol);
        }

        // 4. Deferisce al parent scope (WithScope o ModuleScope)
        return _parentScope.Resolve(name);
    }
}

/// <summary>
/// Scope contestuale per i blocchi "With <expr> ... End With" (sezione 14 e M3 del charter).
/// Le chiamate con punto iniziale (".Member") vengono risolte direttamente sui membri del target del With.
/// Le chiamate normali (senza punto iniziale) deferiscono al parent scope.
/// </summary>
public sealed class WithScope : IScope
{
    private readonly IScope _parentScope;
    private readonly ResolutionResult _targetResult;
    private readonly CodeGraph _graph;
    private readonly TypeResolver _typeResolver;

    public IScope? ParentScope => _parentScope;
    public Symbol? ScopeSymbol => _parentScope.ScopeSymbol;
    public string ScopeName => $"With ({_targetResult})";
    public ResolutionResult TargetResult => _targetResult;

    public WithScope(IScope parentScope, ResolutionResult targetResult, CodeGraph graph, TypeResolver typeResolver)
    {
        _parentScope = parentScope;
        _targetResult = targetResult;
        _graph = graph;
        _typeResolver = typeResolver;
    }

    public ResolutionResult Resolve(string name)
    {
        // Se inizia con punto, risolve sul target del With
        if (name.StartsWith('.'))
        {
            var memberName = name.TrimStart('.');
            return ResolveWithMember(memberName);
        }

        // Altrimenti delega normalmente allo scope contenitore
        return _parentScope.Resolve(name);
    }

    public ResolutionResult ResolveWithMember(string memberName)
    {
        // Se il target del With è sconosciuto o Late-Bound (Object/Variant)
        if (_targetResult.Confidence == ResolutionConfidence.Unknown ||
            string.Equals(_targetResult.TargetTypeName, "Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_targetResult.TargetTypeName, "Variant", StringComparison.OrdinalIgnoreCase))
        {
            return ResolutionResult.Unknown(memberName, details: $"Late binding contestuale su blocco With con target dinamico/sconosciuto ({_targetResult})");
        }

        // Determina il tipo target
        var targetTypeSymbol = _targetResult.TargetSymbol;
        if (targetTypeSymbol == null && _targetResult.ResolvedTypeSymbolId.HasValue)
        {
            targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == _targetResult.ResolvedTypeSymbolId.Value);
        }
        else if (targetTypeSymbol != null && targetTypeSymbol.Kind != SymbolKind.Class && targetTypeSymbol.Kind != SymbolKind.Form && targetTypeSymbol.Kind != SymbolKind.Type)
        {
            // Se il targetSymbol è una variabile o campo, recuperiamo il suo tipo risolto
            if (targetTypeSymbol.ResolvedTypeSymbolId.HasValue)
            {
                targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == targetTypeSymbol.ResolvedTypeSymbolId.Value);
            }
            else if (targetTypeSymbol.DeclaredTypeName != null)
            {
                var typeInfo = _typeResolver.ResolveType(targetTypeSymbol.DeclaredTypeName);
                targetTypeSymbol = typeInfo.UserTypeSymbol;
            }
        }

        if (targetTypeSymbol != null)
        {
            var member = _graph.GetChildren(targetTypeSymbol.Id)
                .FirstOrDefault(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase));

            if (member != null)
            {
                return ResolutionResult.Exact(member);
            }

            return ResolutionResult.Unknown(memberName, details: $"Membro '{memberName}' non trovato nel tipo '{targetTypeSymbol.QualifiedName}' del blocco With");
        }

        return ResolutionResult.Unknown(memberName, details: $"Impossibile determinare il tipo target del blocco With: {_targetResult}");
    }
}
