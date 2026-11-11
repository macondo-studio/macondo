using Macondo.Semantic.Model;

namespace Macondo.Semantic.Resolution;



/// <summary>
/// Core symbol and access resolution engine for VB6.
/// 
/// RESPONSABILITÀ:
/// 1. Simple Identifiers Resolution: delegation to the scope hierarchy (<see cref="IScope"/>).
/// 2. Qualified Access Resolution:
///    - "Target.Member" (es. "c.Save", "Customer.Update", "UtilsModule.LogMessage").
/// - Resolve 'Target' first to find its semantic type (<see cref="SymbolKind.Class"/>, etc.),
/// then search for 'Member' among the child members of that type.
/// 3. Resolution of 'Me':
/// - "Me.Member" resolves to the members declared in the current class/form.
/// 4. Risoluzione Chiamate Contestuali 'With':
/// - ".Member" inside a "With expr" block resolves directly to members of the type of 'expr'.
/// 5. Late Binding and Dynamic Type Management:
/// - On objects declared as 'Object' or 'Variant' (or CreateObject), produces <see cref="ResolutionConfidence.Unknown"/>
/// without errors and without inventing false relationships.
/// </summary>
public sealed class SymbolResolver
{
    private readonly CodeGraph _graph;
    private readonly TypeResolver _typeResolver;
    private readonly ProjectScope _projectScope;

    /// <summary>Cache of ModuleScopes for each module/class in the project.</summary>
    private readonly Dictionary<Guid, ModuleScope> _moduleScopes = new();

    /// <summary>Shared global project scope.</summary>
    public ProjectScope ProjectScope => _projectScope;

    /// <summary>Associated type resolution engine.</summary>
    public TypeResolver TypeResolver => _typeResolver;

    /// <summary>
    /// Initializes the SymbolResolver for the specified graph.
    /// </summary>
    /// <param name="graph">The CodeGraph with all the project symbols.</param>
    /// <param name="typeResolver">The type resolver for querying user classifications and types.</param>
    public SymbolResolver(CodeGraph graph, TypeResolver typeResolver)
    {
        _graph = graph;
        _typeResolver = typeResolver;
        _projectScope = new ProjectScope(graph, typeResolver);

        // Initializes ModuleScopes for all classes, forms, modules and user controls in the graph.
        foreach (var symbol in _graph.Symbols)
        {
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl)

                _moduleScopes[symbol.Id] = new ModuleScope(symbol, _graph, _projectScope);
        }
    }

    /// <summary>
    /// Gets or creates the <see cref="ModuleScope"/> for the specified module.
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
    /// Creates a new <see cref="LocalScope"/> for a procedure (Sub, Function, Property) hooked to the specified parent scope.
    /// </summary>
    public LocalScope CreateLocalScope(Symbol routineSymbol, IScope parentScope)
    {
        return new LocalScope(routineSymbol, parentScope, _graph);
    }

    /// <summary>
    /// Creates a new contextual <see cref="WithScope"/> hooked to the target expression of the 'With' block.
    /// </summary>
    public WithScope CreateWithScope(IScope parentScope, ResolutionResult targetResult)
    {
        return new WithScope(parentScope, targetResult, _graph, _typeResolver);
    }


    /// <summary>
    /// Resolves a member on a previously resolved target.
    /// 
    /// HANDLES:
    /// 1. Intrinsic properties of the VB6 runtime (e.g. 'ActiveForm' on MDIForm/Screen, 'Parent' on UI controls):
    /// returns a <see cref="ResolutionResult"/> typed as "Form" with <see cref="ResolutionConfidence.Inferred"/> confidence.
    /// 2. Late Binding on Dynamic or Basic UI Abstract Types ('Object', 'Variant', 'Form', 'Control', 'MDIForm'):
    /// returns <see cref="ResolutionConfidence.Unknown"/> preserving the <see cref="ResolutionResult.TargetTypeName"/>
    /// effective to allow the <see cref="Graph.CallGraphBuilder"/> to apply the candidate matching heuristic.
    /// 3. Access on type or instance (Class / Form / Module .bas / UserControl / Type UDT):
    /// goes back to the semantic type and looks for the member among the direct children of the container in the CodeGraph.
    /// 4. Chain calls on methods/properties (e.g. 'getFirstForm().Method', 'prop.Method'):
    /// uses the ReturnType / ResolvedReturnTypeSymbolId of the receiving routine to resolve subsequent members.
    /// </summary>
    /// <param name="targetResult">The outcome of the semantic resolution of the receiver (target) expression.</param>
    /// <param name="memberName">The name of the member to resolve to the target.</param>
    /// <returns>The outcome <see cref="ResolutionResult"/> with the solved symbol and the related confidence.</returns>
    public ResolutionResult ResolveMemberOnTarget(ResolutionResult targetResult, string memberName)
    {
        // 0. Intrinsic properties of VB6 containers that return a Form (e.g. MdiFrm.ActiveForm, ctrl.Parent).
        // In the VB6 runtime 'ActiveForm' (on MDIForm or Screen) and 'Parent' (on visual controls) always return
        // a polymorphic instance of Form. We infer the type "Form" to guide subsequent member resolution.
        if (string.Equals(memberName, "ActiveForm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(memberName, "Parent", StringComparison.OrdinalIgnoreCase))
            return new ResolutionResult
            {
                Confidence = ResolutionConfidence.Inferred,
                TargetTypeName = "Form",
                Details = $"Proprietà intrinseca VB6 '{memberName}' di tipo Form"
            };

        // Calculate the actual type name of the receiver by going back to:
        // - TargetTypeName esplicito già determinato
        // - ReturnType if the target is a Function or Property
        // - DeclaredTypeName if the target is a field, variable or parameter
        var effectiveTypeName = targetResult.TargetTypeName
            ?? (targetResult.TargetSymbol?.Kind is SymbolKind.Method or SymbolKind.Property ? targetResult.TargetSymbol.ReturnType : null)
            ?? targetResult.TargetSymbol?.DeclaredTypeName
            ?? targetResult.TargetParameter?.DeclaredTypeName;

        // 1. If the target is a dynamically bound type or abstract base UI (Object / Variant / Form / Control / MDIForm) -> Late Binding.
        // In VB6 calls to 'As Form', 'As Control' or 'As Object' are forwarded to runtime via IDispatch::Invoke.
        // We preserve 'effectiveTypeName' in the TargetTypeName to allow the CallGraphBuilder to filter
        // compatible candidates (e.g. only Form methods when the receiver is 'Form').
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

        // 2. Determine the symbol that describes the type of the target
        if (targetResult.TargetSymbol is not null)
        {
            var sym = targetResult.TargetSymbol;
            if (sym.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type)
                targetTypeSymbol = sym;
            else if (sym.Kind is SymbolKind.Method or SymbolKind.Property)
            {
                // If the target is a method or property (e.g. bcPCassaProcessor), the type for member resolution
                // subsequent is the type returned by the procedure (ReturnType / ResolvedReturnTypeSymbolId)
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
                // Test name resolution of declared type
                var typeInfo = _typeResolver.ResolveType(sym.DeclaredTypeName);
                targetTypeSymbol = typeInfo.UserTypeSymbol;
            }
        }
        else if (targetResult.TargetParameter is not null)
        {
            // The target is a formal parameter (e.g. "cust" of type "Customer")
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

        // 3. Searches for the specified member within the identified target type
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
