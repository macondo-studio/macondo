using Macondo.Semantic.Model;

namespace Macondo.Semantic.Resolution;

/// <summary>
/// Scope a livello di intero progetto VB6 (radice della gerarchia di lookup).
/// 
/// CARATTERISTICHE VB6:
/// 1. Tipi di Progetto: indicizza tutte le Classi (.cls), Form (.frm), Moduli (.bas),
///    UserControl (.ctl), UDT (Type) ed Enum definiti nel progetto.
/// 2. Membri Public dei Moduli Standard (.bas): in VB6, qualsiasi Sub, Function, Property,
///    Const o Variable dichiarata 'Public' in un file .bas fa parte del namespace globale
///    ed è invocabile ovunque nel progetto senza specificare il nome del modulo (es. "LogMessage").
/// </summary>
public sealed class ProjectScope : IScope
{
    private readonly CodeGraph _graph;
    private readonly TypeResolver _typeResolver;

    /// <summary>Simboli di tipo modulo o classe indicizzati per nome (es. "Customer", "UtilsModule").</summary>
    private readonly Dictionary<string, Symbol> _projectTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Membri Public dei moduli standard (.bas) indicizzati per nome.
    /// Usa una lista per gestire eventuali collisioni ambigue tra più moduli standard.
    /// </summary>
    private readonly Dictionary<string, List<Symbol>> _globalBasMembers = new(StringComparer.OrdinalIgnoreCase);

    public IScope? ParentScope => null;
    public Symbol? ScopeSymbol => null;
    public string ScopeName => "Project";

    public ProjectScope(CodeGraph graph, TypeResolver typeResolver)
    {
        _graph = graph;
        _typeResolver = typeResolver;

        // Costruisce gli indici del progetto per tipi e membri globali .bas.
        // 1. Indicizza tutti i tipi e progetti definiti a livello di workspace/progetto
        foreach (var symbol in _graph.Symbols)
        {
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum or SymbolKind.Project)
            {
                _projectTypes[symbol.Name] = symbol;
                if (!string.IsNullOrEmpty(symbol.QualifiedName) && !symbol.QualifiedName.Equals(symbol.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _projectTypes[symbol.QualifiedName] = symbol;
                }
            }
        }

        // 2. Raccoglie i membri Public di tutti i moduli standard (.bas)
        var basModules = _graph.Symbols
            .Where(s => s.Kind == SymbolKind.Module)
            .ToList();

        foreach (var bas in basModules)
        {
            var children = _graph.GetChildren(bas.Id);
            foreach (var child in children)
            {
                // In VB6 i membri dei moduli standard .bas sono Public per default
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
    /// Risolve un identificatore non qualificato nello scope di progetto:
    /// prima cerca nei membri Public dei moduli .bas, poi tra i tipi di progetto, e infine tra i tipi primitivi.
    /// </summary>
    public ResolutionResult Resolve(string name)
    {
        // 1. Cerca tra i membri Public dei moduli standard .bas (es. "LogMessage")
        if (_globalBasMembers.TryGetValue(name, out var basMemberList))
        {
            if (basMemberList.Count == 1)
            {
                var target = basMemberList[0];
                return new ResolutionResult
                {
                    TargetSymbol = target,
                    Confidence = ResolutionConfidence.Exact,
                    TargetTypeName = target.DeclaredTypeName,
                    ResolvedTypeSymbolId = target.ResolvedTypeSymbolId
                };
            }
            if (basMemberList.Count > 1)
            {
                return new ResolutionResult
                {
                    Confidence = ResolutionConfidence.Ambiguous,
                    Details = $"Simbolo '{name}' ambiguo: definito in più moduli standard .bas ({string.Join(", ", basMemberList.Select(m => m.QualifiedName))})"
                };
            }
        }

        // 2. Cerca tra i tipi di progetto (es. "Customer", "Address")
        if (_projectTypes.TryGetValue(name, out var typeSymbol))
        {
            return new ResolutionResult
            {
                TargetSymbol = typeSymbol,
                Confidence = ResolutionConfidence.Exact,
                TargetTypeName = typeSymbol.DeclaredTypeName,
                ResolvedTypeSymbolId = typeSymbol.ResolvedTypeSymbolId
            };
        }

        // 3. Verifica se è un tipo primitivo standard
        var typeInfo = _typeResolver.ResolveType(name);
        if (typeInfo.IsPrimitive)
        {
            return new ResolutionResult
            {
                Confidence = ResolutionConfidence.Unknown,
                TargetTypeName = typeInfo.Name,
                Details = $"Tipo primitivo VB6: {name}"
            };
        }

        return new ResolutionResult
        {
            Confidence = ResolutionConfidence.Unknown,
            Details = $"Simbolo '{name}' non trovato nello scope di progetto"
        };
    }

    /// <summary>
    /// Cerca il simbolo di un tipo specifico definito nel progetto.
    /// </summary>
    public Symbol? FindTypeSymbol(string typeName)
    {
        if (_projectTypes.TryGetValue(typeName, out var sym))
            return sym;
        return null;
    }
}

/// <summary>
/// Scope a livello di singolo modulo o classe (.cls, .frm, .bas, .ctl).
/// 
/// Contiene tutti i membri a livello di modulo (campi privati/pubblici, costanti, metodi, proprietà, UDT ed enum).
/// Gestisce inoltre la keyword 'Me', che risolve sull'istanza del modulo corrente.
/// </summary>
public sealed class ModuleScope : IScope
{
    private readonly Symbol _moduleSymbol;
    private readonly CodeGraph _graph;
    private readonly ProjectScope _projectScope;

    /// <summary>Membri del modulo indicizzati per nome (case-insensitive).</summary>
    private readonly Dictionary<string, Symbol> _members = new(StringComparer.OrdinalIgnoreCase);

    public IScope? ParentScope => _projectScope;
    public Symbol? ScopeSymbol => _moduleSymbol;
    public string ScopeName => _moduleSymbol.QualifiedName;

    public ModuleScope(Symbol moduleSymbol, CodeGraph graph, ProjectScope projectScope)
    {
        _moduleSymbol = moduleSymbol;
        _graph = graph;
        _projectScope = projectScope;

        // Indicizza i membri del modulo per nome
        foreach (var child in _graph.GetChildren(_moduleSymbol.Id))
        {
            _members[child.Name] = child;
        }
    }

    /// <summary>
    /// Risolve un identificatore: prima verifica 'Me', poi cerca tra i membri del modulo,
    /// e se non trovato delega al <see cref="ProjectScope"/>.
    /// </summary>
    public ResolutionResult Resolve(string name)
    {
        // 1. Keyword Me: si riferisce all'istanza della classe/form corrente
        if (string.Equals(name, "Me", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolutionResult
            {
                TargetSymbol = _moduleSymbol,
                Confidence = ResolutionConfidence.Exact,
                TargetTypeName = _moduleSymbol.Name,
                ResolvedTypeSymbolId = _moduleSymbol.Id
            };
        }

        // 2. Membri del modulo corrente (campi, costanti, metodi, proprietà)
        if (_members.TryGetValue(name, out var member))
        {
            return new ResolutionResult
            {
                TargetSymbol = member,
                Confidence = ResolutionConfidence.Exact,
                TargetTypeName = member.DeclaredTypeName,
                ResolvedTypeSymbolId = member.ResolvedTypeSymbolId
            };
        }

        // 3. Deferisce allo scope di progetto per membri globali .bas o tipi
        return _projectScope.Resolve(name);
    }

    /// <summary>
    /// Cerca un membro dichiarato direttamente all'interno di questo modulo.
    /// </summary>
    public Symbol? FindMember(string memberName)
    {
        if (_members.TryGetValue(memberName, out var member))
            return member;
        return null;
    }
}

/// <summary>
/// Scope locale all'interno del corpo di una routine (Sub, Function, Property Get/Let/Set).
/// 
/// ORDINE DI LOOKUP:
/// 1. Variabili locali (dichiarate con 'Dim' o 'Static' all'interno della routine).
/// 2. Parametri formali della routine (ByVal / ByRef).
/// 3. Nome della funzione stessa (in VB6, l'assegnazione al nome della funzione imposta il valore di ritorno).
/// 4. Scope contenitore (ModuleScope o eventuale WithScope).
/// </summary>
public sealed class LocalScope : IScope
{
    private readonly Symbol _routineSymbol;
    private readonly IScope _parentScope;
    private readonly CodeGraph _graph;

    /// <summary>Variabili locali indicizzate per nome.</summary>
    private readonly Dictionary<string, Symbol> _localVariables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parametri formali indicizzati per nome.</summary>
    private readonly Dictionary<string, ParameterSymbol> _parameters = new(StringComparer.OrdinalIgnoreCase);

    public IScope? ParentScope => _parentScope;
    public Symbol? ScopeSymbol => _routineSymbol;
    public string ScopeName => _routineSymbol.QualifiedName;

    public LocalScope(Symbol routineSymbol, IScope parentScope, CodeGraph graph)
    {
        _routineSymbol = routineSymbol;
        _parentScope = parentScope;
        _graph = graph;

        // 1. Indicizza i parametri formali della routine
        foreach (var param in _routineSymbol.Parameters)
        {
            _parameters[param.Name] = param;
        }

        // 2. Indicizza le variabili e costanti locali contenute nella routine
        foreach (var child in _graph.GetChildren(_routineSymbol.Id))
        {
            if (child.Kind is SymbolKind.Field or SymbolKind.Constant)
            {
                _localVariables[child.Name] = child;
            }
        }
    }

    /// <summary>
    /// Risolve un identificatore all'interno della routine locale.
    /// </summary>
    public ResolutionResult Resolve(string name)
    {
        // 1. Variabili locali (Dim / Static)
        if (_localVariables.TryGetValue(name, out var localVar))
        {
            return new ResolutionResult
            {
                TargetSymbol = localVar,
                Confidence = ResolutionConfidence.Exact,
                TargetTypeName = localVar.DeclaredTypeName,
                ResolvedTypeSymbolId = localVar.ResolvedTypeSymbolId
            };
        }

        // 2. Parametri formali
        if (_parameters.TryGetValue(name, out var param))
        {
            return new ResolutionResult
            {
                TargetParameter = param,
                Confidence = ResolutionConfidence.Exact,
                TargetTypeName = param.DeclaredTypeName,
                ResolvedTypeSymbolId = param.ResolvedTypeSymbolId
            };
        }

        // 3. Assegnazione al nome della funzione stessa per il valore di ritorno (es. "CalculateTotal = 42")
        if (string.Equals(name, _routineSymbol.Name, StringComparison.OrdinalIgnoreCase))
        {
            return new ResolutionResult
            {
                TargetSymbol = _routineSymbol,
                Confidence = ResolutionConfidence.Exact,
                TargetTypeName = _routineSymbol.DeclaredTypeName,
                ResolvedTypeSymbolId = _routineSymbol.ResolvedTypeSymbolId
            };
        }

        // 4. Deferisce allo scope genitore (ModuleScope o WithScope sovrastante)
        return _parentScope.Resolve(name);
    }
}

/// <summary>
/// Scope contestuale generato da un blocco "With &lt;expr&gt; ... End With".
/// 
/// REGOLE VB6:
/// - Le chiamate che iniziano con punto (".Member") vengono risolte direttamente sui membri del tipo target del With.
/// - Le chiamate normali (senza punto iniziale) vengono delegate allo scope genitore sovrastante.
/// - Se il target del With è non tipizzato (Object o Variant), la risoluzione restituisce <see cref="ResolutionConfidence.Unknown"/> (late binding).
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
    /// Risolve un identificatore: se inizia con '.' lo risolve sul target del With, altrimenti delega allo scope genitore.
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
    /// Risolve un membro implicito (es. ".Save") sul target attivo del blocco With.
    /// </summary>
    /// <param name="memberName">Nome del membro (senza punto iniziale).</param>
    public ResolutionResult ResolveWithMember(string memberName)
    {
        // 1. Se il target del With è dinamico (Object / Variant) o non risolto -> late binding
        if (_targetResult.Confidence == ResolutionConfidence.Unknown ||
            string.Equals(_targetResult.TargetTypeName, "Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_targetResult.TargetTypeName, "Variant", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolutionResult
            {
                Confidence = ResolutionConfidence.Unknown,
                Details = $"Late binding contestuale su blocco With con target dinamico/sconosciuto ({_targetResult})"
            };
        }

        // 2. Risolve il tipo target associato all'espressione del With
        Symbol? targetTypeSymbol = _targetResult.TargetSymbol;
        if (targetTypeSymbol == null && _targetResult.ResolvedTypeSymbolId.HasValue)
        {
            targetTypeSymbol = _graph.Symbols.FirstOrDefault(s => s.Id == _targetResult.ResolvedTypeSymbolId.Value);
        }
        else if (targetTypeSymbol != null && targetTypeSymbol.Kind != SymbolKind.Class && targetTypeSymbol.Kind != SymbolKind.Form && targetTypeSymbol.Kind != SymbolKind.Type)
        {
            // Se targetSymbol è una variabile o parametro, recupera il tipo risolto della variabile
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

        // 3. Cerca il membro all'interno del tipo target
        if (targetTypeSymbol != null)
        {
            var member = _graph.GetChildren(targetTypeSymbol.Id)
                .FirstOrDefault(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase));

            if (member != null)
            {
                return new ResolutionResult
                {
                    TargetSymbol = member,
                    Confidence = ResolutionConfidence.Exact,
                    TargetTypeName = member.DeclaredTypeName,
                    ResolvedTypeSymbolId = member.ResolvedTypeSymbolId
                };
            }

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
