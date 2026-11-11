using Macondo.Semantic.Model;

namespace Macondo.Semantic.Resolution;

/// <summary>
/// Interface for symbol visibility and lookup scope in VB6.
/// 
/// The scopes are organized in a hierarchical parent-child chain:
/// - LocalScope (local variables, parameters)
/// -> ModuleScope (class or module members, Me)
/// -> ProjectScope (project types, Public members of .bas modules)
/// 
/// Contextual blocks like 'With' insert a <see cref="WithScope"/> into the chain.
/// </summary>
public interface IScope
{
    /// <summary>Parent Scope to delegate the search to if the symbol is not found locally.</summary>
    IScope? ParentScope { get; }

    /// <summary>Symbol associated with this scope (e.g. the Method for LocalScope or the Class for ModuleScope).</summary>
    Symbol? ScopeSymbol { get; }

    /// <summary>Identifying name of the scope for diagnostic and tracking purposes.</summary>
    string ScopeName { get; }

    /// <summary>
    /// Resolves an unqualified identifier within the current scope,
    /// moving up the hierarchy if necessary.
    /// </summary>
    /// <param name="name">Name of the symbol to resolve (e.g. "localCust", "Save", "LogMessage").</param>
    /// <returns>The resolution result with target symbol and confidence level.</returns>
    ResolutionResult Resolve(string name);
}
