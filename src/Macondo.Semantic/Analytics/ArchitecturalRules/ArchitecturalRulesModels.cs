using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ArchitecturalRules;

/// <summary>
/// Type of architectural rule or constraint.
/// </summary>
public enum RuleType
{
    /// <summary>Forbidden dependency: Source components may NOT depend on target components.</summary>
    ForbiddenDependency,

    /// <summary>Exclusive dependency: Source components can ONLY depend on allowed target components.</summary>
    AllowedDependenciesOnly,

    /// <summary>Mandatory abstraction: coupling must occur exclusively via interfaces.</summary>
    MustDependOnInterfaceOnly,

    /// <summary>Isolated component: No external component can call or access this component.</summary>
    IsolatedInternalComponent
}

/// <summary>
/// Severity level of violation of the architectural rule.
/// </summary>
public enum RuleSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Defining a component/layer pattern or selector based on names, namespaces, types, or folders.
/// </summary>
public sealed class ComponentSelector
{
    /// <summary>Visual name of the layer or selector (e.g. "Domain", "Presentation", "Data Access", "Standard Modules").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Match pattern on the file name or path (e.g. "*Domain*", "Services/*", "*.frm", "Repository*").</summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Optional filter on symbol type (Form, Class, Module, Interface, etc.).</summary>
    public SymbolKind? KindFilter { get; set; }

    /// <summary>
    /// Tests whether a symbol matches this selector.
    /// </summary>
    public bool Matches(Symbol symbol)
    {
        if (KindFilter.HasValue && symbol.Kind != KindFilter.Value)
            return false;

        if (string.IsNullOrWhiteSpace(Pattern) || Pattern == "*")
            return true;

        var parts = Pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            var cleanP = p.Replace("*", "");
            if (symbol.Name.Contains(cleanP, StringComparison.OrdinalIgnoreCase) ||
                symbol.QualifiedName.Contains(cleanP, StringComparison.OrdinalIgnoreCase) ||
                symbol.FilePath.Contains(cleanP, StringComparison.OrdinalIgnoreCase))

                return true;
        }

        return false;
    }
}

/// <summary>
/// Formal architectural rule to be verified on the CodeGraph.
/// </summary>
public sealed class ArchitecturalRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RuleType Type { get; set; }
    public RuleSeverity Severity { get; set; } = RuleSeverity.Error;

    /// <summary>Selector of source components subject to the rule.</summary>
    public ComponentSelector SourceSelector { get; set; } = new();

    /// <summary>Target component selector for the rule.</summary>
    public ComponentSelector TargetSelector { get; set; } = new();

    /// <summary>Indicates whether the rule is active in validation.</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Single architectural rule violation detected in CodeGraph.
/// </summary>
public sealed class RuleViolation
{
    public ArchitecturalRule Rule { get; init; } = null!;
    public Symbol SourceSymbol { get; init; } = null!;
    public Symbol TargetSymbol { get; init; } = null!;
    public Relationship Relationship { get; init; } = null!;
    public string Message { get; init; } = string.Empty;
    public RuleSeverity Severity => Rule.Severity;
}

/// <summary>
/// Overall result of the evaluation of the architectural rules on the project.
/// </summary>
public sealed class ArchitecturalRulesReport
{
    public List<ArchitecturalRule> Rules { get; init; } = new();
    public List<RuleViolation> Violations { get; init; } = new();

    public int TotalRulesEvaluated => Rules.Count(r => r.IsEnabled);
    public int TotalViolations => Violations.Count;
    public int CriticalViolations => Violations.Count(v => v.Severity == RuleSeverity.Critical);
    public int ErrorViolations => Violations.Count(v => v.Severity == RuleSeverity.Error);
    public int WarningViolations => Violations.Count(v => v.Severity == RuleSeverity.Warning);
}
