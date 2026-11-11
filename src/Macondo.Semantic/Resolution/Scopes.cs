using Macondo.Semantic.Model;

namespace Macondo.Semantic.Resolution;

/// <summary>
/// Scope at the entire VB6 project level (root of the lookup hierarchy).
/// 
/// CARATTERISTICHE VB6:
/// 1. Project Types: index all Classes (.cls), Forms (.frm), Modules (.bas),
/// UserControl (.ctl), UDT (Type) and Enum defined in the project.
/// 2. Public Members of Standard Modules (.bas): in VB6, any Sub, Function, Property,
/// Const or Variable declared 'Public' in a .bas file is part of the global namespace
/// and can be invoked anywhere in the project without specifying the module name (e.g. "LogMessage").
/// </summary>
public sealed class ProjectScope : IScope
{
    private readonly CodeGraph _graph;
    private readonly TypeResolver _typeResolver;

    /// <summary>Module or class type symbols indexed by name (e.g. "Customer", "UtilsModule").</summary>
    private readonly Dictionary<string, Symbol> _projectTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Public members of standard modules (.bas) indexed by name.
    /// Use a list to handle any ambiguous collisions between multiple standard modules.
    /// </summary>
    private readonly Dictionary<string, List<Symbol>> _globalBasMembers = new(StringComparer.OrdinalIgnoreCase);

    public IScope? ParentScope => null;
    public Symbol? ScopeSymbol => null;
    public string ScopeName => "Project";

    public ProjectScope(CodeGraph graph, TypeResolver typeResolver)
    {
        _graph = graph;
        _typeResolver = typeResolver;

        // Constructs project indexes for .bas global types and members.
        // 1. Index all types and projects defined at the workspace/project level
        foreach (var symbol in _graph.Symbols)
        {
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum or SymbolKind.Project)
            {
                _projectTypes[symbol.Name] = symbol;
                if (!string.IsNullOrEmpty(symbol.QualifiedName) && !symbol.QualifiedName.Equals(symbol.Name, StringComparison.OrdinalIgnoreCase))

                    _projectTypes[symbol.QualifiedName] = symbol;
            }
        }

        // 2. Collects Public members of all standard modules (.bas)
        var basModules = _graph.Symbols
            .Where(s => s.Kind == SymbolKind.Module)
            .ToList();

        foreach (var bas in basModules)
        {
            var children = _graph.GetChildren(bas.Id);
            foreach (var child in children)
            {
                // In VB6 the members of the standard .bas modules are Public by default
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

    /// <summary>
    /// Resolves an unqualified identifier in project scope:
    /// first searches the Public members of .bas modules, then the project types, and finally the primitive types.
    /// </summary>
    public ResolutionResult Resolve(string name)
    {
        // 1. Search the Public members of standard .bas modules (e.g. "LogMessage")
        if (_globalBasMembers.TryGetValue(name, out var basMemberList))
        {
            if (basMemberList.Count == 1)
            {
                var target = basMemberList[0];
                return new ResolutionResult
                {
                    TargetSymbol = target,
                    Confidence = ResolutionConfidence.Exact,
                    TargetTypeName = target.ReturnType ?? target.DeclaredTypeName,
                    ResolvedTypeSymbolId = target.ResolvedReturnTypeSymbolId ?? target.ResolvedTypeSymbolId
                };
            }
            if (basMemberList.Count > 1)

                return new ResolutionResult
                {
                    Confidence = ResolutionConfidence.Ambiguous,
                    Details = $"Simbolo '{name}' ambiguo: definito in più moduli standard .bas ({string.Join(", ", basMemberList.Select(m => m.QualifiedName))})"
                };
        }

        // 2. Search among project types (e.g. "Customer", "Address")
        if (_projectTypes.TryGetValue(name, out var typeSymbol))
            return new ResolutionResult
            {
                TargetSymbol = typeSymbol,
                Confidence = ResolutionConfidence.Exact,
                TargetTypeName = typeSymbol.DeclaredTypeName,
                ResolvedTypeSymbolId = typeSymbol.ResolvedTypeSymbolId
            };

        // 2b. Fallback for types or controls with project prefix (e.g. "DUE.cntrDestination").
        // Allows resolution of referenced type symbols with project or library qualification
        // (typical of user control declarations in the Form designer), extracting the unqualified base name.
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < name.Length - 1)
        {
            var unqualified = name[(lastDot + 1)..].Trim();
            if (_projectTypes.TryGetValue(unqualified, out var unqSymbol))
                return new ResolutionResult
                {
                    TargetSymbol = unqSymbol,
                    Confidence = ResolutionConfidence.Exact,
                    TargetTypeName = unqSymbol.DeclaredTypeName,
                    ResolvedTypeSymbolId = unqSymbol.ResolvedTypeSymbolId
                };
        }

        // 3. Check if it is a standard primitive type
        var typeInfo = _typeResolver.ResolveType(name);
        if (typeInfo.IsPrimitive)
            return new ResolutionResult
            {
                Confidence = ResolutionConfidence.Unknown,
                TargetTypeName = typeInfo.Name,
                Details = $"Tipo primitivo VB6: {name}"
            };

        return new ResolutionResult
        {
            Confidence = ResolutionConfidence.Unknown,
            Details = $"Simbolo '{name}' non trovato nello scope di progetto"
        };
    }

    /// <summary>
    /// Searches for the symbol of a specific type defined in the project.
    /// Supports both simple names (e.g. "Customer", "cntrDestination") and prefixed qualified names
    /// of project or library (e.g. "DUE.cntrDestinazione"), applying the fallback on the unqualified base name.
    /// </summary>
    /// <param name="typeName">The name of the type to search for.</param>
    /// <returns>The corresponding <see cref="Symbol"/> if found in the project; otherwise <c>null</c>.</returns>
    public Symbol? FindTypeSymbol(string typeName)
    {
        if (_projectTypes.TryGetValue(typeName, out var sym))
            return sym;

        // Fallback for qualified types with project prefix (e.g. "DUE.cntrDestination" -> "cntrDestination")
        var lastDot = typeName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < typeName.Length - 1)
        {
            var unqualified = typeName[(lastDot + 1)..].Trim();
            if (_projectTypes.TryGetValue(unqualified, out var unqSym))
                return unqSym;
        }

        return null;
    }
}

/// <summary>
/// Scope at the individual module or class level (.cls, .frm, .bas, .ctl).
/// 
/// Contains all form-level members (private/public fields, constants, methods, properties, UDTs, and enums).
/// It also handles the 'Me' keyword, which resolves to the current module instance.
/// </summary>
public sealed class ModuleScope : IScope
{
    private readonly Symbol _moduleSymbol;
    private readonly CodeGraph _graph;
    private readonly ProjectScope _projectScope;

    /// <summary>Form members indexed by name (case-insensitive).</summary>
    private readonly Dictionary<string, Symbol> _members = new(StringComparer.OrdinalIgnoreCase);

    public IScope? ParentScope => _projectScope;
    public Symbol? ScopeSymbol => _moduleSymbol;
    public string ScopeName => _moduleSymbol.QualifiedName;

    public ModuleScope(Symbol moduleSymbol, CodeGraph graph, ProjectScope projectScope)
    {
        _moduleSymbol = moduleSymbol;
        _graph = graph;
        _projectScope = projectScope;

        // Index module members by name
        foreach (var child in _graph.GetChildren(_moduleSymbol.Id))
        {
            _members[child.Name] = child;
        }
    }

    /// <summary>
    /// Resolves an identifier: first checks 'Me', then searches the module members,
    /// and if not found, delegate to <see cref="ProjectScope"/>.
    /// </summary>
    public ResolutionResult Resolve(string name)
    {
        // 1. Keyword Me: refers to the instance of the current class/form
        if (string.Equals(name, "Me", StringComparison.OrdinalIgnoreCase))

            return new ResolutionResult
            {
                TargetSymbol = _moduleSymbol,
                Confidence = ResolutionConfidence.Exact,
                TargetTypeName = _moduleSymbol.Name,
                ResolvedTypeSymbolId = _moduleSymbol.Id
            };

        // 2. Members of the current form (fields, constants, methods, properties)
        if (_members.TryGetValue(name, out var member))

            return new ResolutionResult
            {
                TargetSymbol = member,
                Confidence = ResolutionConfidence.Exact,
                // If the target symbol is a function/property with return type, we propagate the ReturnType
                TargetTypeName = member.ReturnType ?? member.DeclaredTypeName,
                ResolvedTypeSymbolId = member.ResolvedReturnTypeSymbolId ?? member.ResolvedTypeSymbolId
            };

        // 3. Refers to project scope for global .bas members or types
        return _projectScope.Resolve(name);
    }

    /// <summary>
    /// Search for a declared member directly within this form.
    /// </summary>
    public Symbol? FindMember(string memberName)
    {
        if (_members.TryGetValue(memberName, out var member))
            return member;
        return null;
    }
}

/// <summary>
/// Local scope within the body of a procedure (Sub, Function, Property Get/Let/Set).
/// 
/// LOOKUP ORDER:
/// 1. Local variables (declared with 'Dim' or 'Static' within the routine).
/// 2. Formal parameters of the routine (ByVal / ByRef).
/// 3. Name of the function itself (in VB6, assigning the function name sets the return value).
/// 4. Scope container (ModuleScope or possible WithScope).
/// </summary>
public sealed class LocalScope : IScope
{
    private readonly Symbol _routineSymbol;
    private readonly IScope _parentScope;
    private readonly CodeGraph _graph;

    /// <summary>Local variables indexed by name.</summary>
    private readonly Dictionary<string, Symbol> _localVariables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Formal parameters indexed by name.</summary>
    private readonly Dictionary<string, ParameterSymbol> _parameters = new(StringComparer.OrdinalIgnoreCase);

    public IScope? ParentScope => _parentScope;
    public Symbol? ScopeSymbol => _routineSymbol;
    public string ScopeName => _routineSymbol.QualifiedName;

    public LocalScope(Symbol routineSymbol, IScope parentScope, CodeGraph graph)
    {
        _routineSymbol = routineSymbol;
        _parentScope = parentScope;
        _graph = graph;

        // 1. Index the formal parameters of the routine
        foreach (var param in _routineSymbol.Parameters)
        {
            _parameters[param.Name] = param;
        }

        // 2. Index the local variables and constants contained in the routine
        foreach (var child in _graph.GetChildren(_routineSymbol.Id))
        {
            if (child.Kind is SymbolKind.Field or SymbolKind.Constant)

                _localVariables[child.Name] = child;
        }
    }

    /// <summary>
    /// Resolves an identifier within the local routine.
    /// </summary>
    public ResolutionResult Resolve(string name)
    {
        // 1. Local Variables (Dim / Static)
        if (_localVariables.TryGetValue(name, out var localVar))

            return new ResolutionResult
            {
                TargetSymbol = localVar,
                Confidence = ResolutionConfidence.Exact,
                TargetTypeName = localVar.DeclaredTypeName,
                ResolvedTypeSymbolId = localVar.ResolvedTypeSymbolId
            };

        // 2. Formal parameters
        if (_parameters.TryGetValue(name, out var param))

            return new ResolutionResult
            {
                TargetParameter = param,
                Confidence = ResolutionConfidence.Exact,
                TargetTypeName = param.DeclaredTypeName,
                ResolvedTypeSymbolId = param.ResolvedTypeSymbolId
            };

        // 3. Assignment to the function name itself for the return value (e.g. "CalculateTotal = 42")
        if (string.Equals(name, _routineSymbol.Name, StringComparison.OrdinalIgnoreCase))

            return new ResolutionResult
            {
                TargetSymbol = _routineSymbol,
                Confidence = ResolutionConfidence.Exact,
                TargetTypeName = _routineSymbol.DeclaredTypeName,
                ResolvedTypeSymbolId = _routineSymbol.ResolvedTypeSymbolId
            };

        // 4. Refers to the parent scope (ModuleScope or overlying WithScope)
        return _parentScope.Resolve(name);
    }
}

/// <summary>
/// Contextual scope generated by a "With <expr> ... End With" block.
/// 
/// REGOLE VB6:
/// - Calls starting with dot (".Member") resolve directly to members of the target type of the With.
/// - Normal calls (without leading dot) are delegated to the overlying parent scope.
/// - If the target of the With is untyped (Object or Variant), the resolution returns <see cref="ResolutionConfidence.Unknown"/> (late binding).
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

    public WithScope(
        IScope parentScope,
        ResolutionResult targetResult,
        CodeGraph graph,
        TypeResolver typeResolver)
    {
        _parentScope = parentScope;
        _targetResult = targetResult;
        _graph = graph;
        _typeResolver = typeResolver;
    }

    /// <summary>
    /// Resolves an identifier: if it starts with '.' resolves it on the With target, otherwise delegates to the parent scope.
    /// </summary>
    public ResolutionResult Resolve(string name)
    {
        if (name.StartsWith('.'))
        {
            var memberName = name.TrimStart('.');
            return ResolveWithMember(memberName);
        }

        return _parentScope.Resolve(name);
    }

    /// <summary>
    /// Resolves an implicit member (e.g. ".Save") to the active target of the With block.
    /// </summary>
    /// <param name="memberName">Member name (without leading period).</param>
    public ResolutionResult ResolveWithMember(string memberName)
    {
        // 1. If the target of the With is dynamic (Object / Variant) or unresolved -> late binding
        if (_targetResult.Confidence == ResolutionConfidence.Unknown ||
            string.Equals(_targetResult.TargetTypeName, "Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_targetResult.TargetTypeName, "Variant", StringComparison.OrdinalIgnoreCase))

            return new ResolutionResult
            {
                Confidence = ResolutionConfidence.Unknown,
                Details = $"Late binding contestuale su blocco With con target dinamico/sconosciuto ({_targetResult})"
            };

        // 2. Resolves the target type associated with the With expression
        Symbol? targetTypeSymbol = _targetResult.TargetSymbol;
        if (targetTypeSymbol == null && _targetResult.ResolvedTypeSymbolId.HasValue)

            targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == _targetResult.ResolvedTypeSymbolId.Value);

        else if (targetTypeSymbol != null && targetTypeSymbol.Kind != SymbolKind.Class && targetTypeSymbol.Kind != SymbolKind.Form && targetTypeSymbol.Kind != SymbolKind.Type)
        {
            // If targetSymbol is a variable or parameter, retrieves the resolved type of the variable
            if (targetTypeSymbol.ResolvedTypeSymbolId.HasValue)

                targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == targetTypeSymbol.ResolvedTypeSymbolId.Value);

            else if (targetTypeSymbol.DeclaredTypeName != null)
            {
                var typeInfo = _typeResolver.ResolveType(targetTypeSymbol.DeclaredTypeName);
                targetTypeSymbol = typeInfo.UserTypeSymbol;
            }
        }

        // 3. Search for the member within the target type
        if (targetTypeSymbol != null)
        {
            var member = _graph.GetChildren(targetTypeSymbol.Id)
                .FirstOrDefault(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase));

            if (member != null)

                return new ResolutionResult
                {
                    TargetSymbol = member,
                    Confidence = ResolutionConfidence.Exact,
                    TargetTypeName = member.DeclaredTypeName,
                    ResolvedTypeSymbolId = member.ResolvedTypeSymbolId
                };

            return new ResolutionResult
            {
                Confidence = ResolutionConfidence.Unknown,
                Details = $"Membro '{memberName}' non trovato nel tipo '{targetTypeSymbol.QualifiedName}' del blocco With"
            };
        }

        return new ResolutionResult
        {
            Confidence = ResolutionConfidence.Unknown,
            Details = $"Impossibile determinare il tipo target del blocco With: {_targetResult}"
        };
    }
}
