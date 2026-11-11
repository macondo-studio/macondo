using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ArchitecturalRules;

/// <summary>
/// Engine for evaluating and enforcing architectural rules and fitness functions on the <see cref="CodeGraph"/>.
/// 
/// FEATURES AND TYPES OF RULES:
/// 1. ForbiddenDependency: vieta qualsiasi dipendenza diretta o indiretta (Calls, Instantiates, UsesType, Reads, Writes, Inherits)
/// from a source layer to a prohibited target layer (e.g. Domain to UI/DataAccess).
/// 2. MustDependOnInterfaceOnly: forces that access to a layer occurs exclusively via contracts/interfaces
/// decoupling the concrete implementation.
/// 3. IsolatedInternalComponent: Ensures that internal components or private modules are not referenced
/// by components outside their designated context.
/// </summary>
public static class ArchitecturalRulesEngine
{
    /// <summary>
    /// CodeGraph relationships considered direct architectural dependencies between components.
    /// </summary>
    private static readonly HashSet<RelationshipKind> DependencyKinds = new()
    {
        RelationshipKind.Calls,
        RelationshipKind.Instantiates,
        RelationshipKind.UsesType,
        RelationshipKind.Reads,
        RelationshipKind.Writes,
        RelationshipKind.Inherits,
        RelationshipKind.Implements
    };

    /// <summary>
    /// Returns a predefined set of standard architectural rules for enterprise and multi-tier projects (Clean Architecture, N-Tier).
    /// </summary>
    public static List<ArchitecturalRule> GetDefaultRules()
    {
        return new List<ArchitecturalRule>
        {
            new ArchitecturalRule
            {
                Name = "Domain Isolation (Clean Architecture)",
                Description = "The Domain / Model layer must not depend on outer layers (UI, Controller, or DataAccess).",
                Type = RuleType.ForbiddenDependency,
                Severity = RuleSeverity.Critical,
                SourceSelector = new ComponentSelector { Name = "Domain", Pattern = "*Domain*;*Model*;*Entity*" },
                TargetSelector = new ComponentSelector { Name = "OuterLayers", Pattern = "*UI*;*Form*;*View*;*Controller*;*Repository*;*DataAccess*" }
            },
            new ArchitecturalRule
            {
                Name = "No Direct DB Access from UI / Forms",
                Description = "Forms and UI controls must not directly invoke database access components or repositories without passing through services.",
                Type = RuleType.ForbiddenDependency,
                Severity = RuleSeverity.Error,
                SourceSelector = new ComponentSelector { Name = "UI / Forms", Pattern = "*.frm;*View*;*Form*", KindFilter = SymbolKind.Form },
                TargetSelector = new ComponentSelector { Name = "Data Access", Pattern = "*Repository*;*DataAccess*;*DbContext*;*AdoHelper*" }
            },
            new ArchitecturalRule
            {
                Name = "Standard Modules Must Not Instantiate Forms",
                Description = "Utility modules (.bas or Helper classes) must not instantiate or directly depend on UI Forms.",
                Type = RuleType.ForbiddenDependency,
                Severity = RuleSeverity.Warning,
                SourceSelector = new ComponentSelector { Name = "Standard Modules", Pattern = "*.bas;*Helper*;*Util*", KindFilter = SymbolKind.Module },
                TargetSelector = new ComponentSelector { Name = "Forms", Pattern = "*.frm", KindFilter = SymbolKind.Form }
            }
        };
    }

    /// <summary>
    /// It evaluates a set of architectural rules on the code graph and produces a detailed report of violations.
    /// Performs checks on forbidden dependencies (ForbiddenDependency), obligation of interfaces (MustDependOnInterfaceOnly)
    /// and isolation of internal components (IsolatedInternalComponent).
    /// </summary>
    /// <param name="graph">The CodeGraph containing project symbols and relationships.</param>
    /// <param name="customRules">Optional list of custom rules (if null, use the default standard rules).</param>
    /// <returns>The <see cref="ArchitecturalRulesReport"/> report containing the list of violations detected.</returns>
    public static ArchitecturalRulesReport Evaluate(CodeGraph graph, IEnumerable<ArchitecturalRule>? customRules = null)
    {
        var rules = (customRules ?? GetDefaultRules()).ToList();
        var report = new ArchitecturalRulesReport { Rules = rules };

        if (graph.Symbols.Count == 0 || graph.Relationships.Count == 0)
            return report;

        var symbolById = graph.Symbols.ToDictionary(s => s.Id);

        // ---------------------------------------------------------------------
        // STEP 1: Global member mapping (method/property/field) -> root container
        // ---------------------------------------------------------------------
        var memberToContainerMap = new Dictionary<Guid, Symbol>();
        foreach (var sym in graph.Symbols)
        {
            memberToContainerMap[sym.Id] = sym;
            foreach (var child in graph.GetChildren(sym.Id))
            {
                memberToContainerMap[child.Id] = sym;
            }
        }

        // ---------------------------------------------------------------------
        // STEP 2: Evaluation for each active rule
        // ---------------------------------------------------------------------
        foreach (var rule in rules.Where(r => r.IsEnabled))
        {
            switch (rule.Type)
            {
                case RuleType.ForbiddenDependency:
                    EvaluateForbiddenDependency(graph, rule, symbolById, memberToContainerMap, report);
                    break;

                case RuleType.MustDependOnInterfaceOnly:
                    EvaluateMustDependOnInterfaceOnly(graph, rule, symbolById, memberToContainerMap, report);
                    break;

                case RuleType.IsolatedInternalComponent:
                    EvaluateIsolatedComponent(graph, rule, symbolById, memberToContainerMap, report);
                    break;
            }
        }

        return report;
    }

    /// <summary>
    /// Evaluate violations where a source component establishes a prohibited dependency on a target component.
    /// Example: The Domain Layer that invokes or instantiates DataAccess or UI components.
    /// </summary>
    private static void EvaluateForbiddenDependency(
        CodeGraph graph,
        ArchitecturalRule rule,
        Dictionary<Guid, Symbol> symbolById,
        Dictionary<Guid, Symbol> memberToContainerMap,
        ArchitecturalRulesReport report)
    {
        foreach (var rel in graph.Relationships)
        {
            // Consider only relationships that constitute architectural coupling
            if (!DependencyKinds.Contains(rel.Kind))
                continue;

            if (memberToContainerMap.TryGetValue(rel.SourceId, out var srcCont) &&
                memberToContainerMap.TryGetValue(rel.TargetId, out var tgtCont))
            {
                // Ignore dependencies within the same container/class
                if (srcCont.Id == tgtCont.Id)
                    continue;

                // Check if the pair (Source, Destination) matches the forbidden rule selectors
                if (rule.SourceSelector.Matches(srcCont) && rule.TargetSelector.Matches(tgtCont))
                {
                    symbolById.TryGetValue(rel.SourceId, out var actualSrc);
                    symbolById.TryGetValue(rel.TargetId, out var actualTgt);

                    report.Violations.Add(new RuleViolation
                    {
                        Rule = rule,
                        SourceSymbol = actualSrc ?? srcCont,
                        TargetSymbol = actualTgt ?? tgtCont,
                        Relationship = rel,
                        Message = $"Violation '{rule.Name}': '{srcCont.Name}' cannot depend on '{tgtCont.Name}' (relationship: {rel.Kind})."
                    });
                }
            }
        }
    }

    /// <summary>
    /// Evaluate violations where a component directly accesses concrete classes rather than abstract interfaces.
    /// </summary>
    private static void EvaluateMustDependOnInterfaceOnly(
        CodeGraph graph,
        ArchitecturalRule rule,
        Dictionary<Guid, Symbol> symbolById,
        Dictionary<Guid, Symbol> memberToContainerMap,
        ArchitecturalRulesReport report)
    {
        foreach (var rel in graph.Relationships)
        {
            // Excludes direct implementation relationships (Implements) as they are legitimate
            if (!DependencyKinds.Contains(rel.Kind) || rel.Kind == RelationshipKind.Implements)
                continue;

            if (memberToContainerMap.TryGetValue(rel.SourceId, out var srcCont) &&
                memberToContainerMap.TryGetValue(rel.TargetId, out var tgtCont))
            {
                if (srcCont.Id == tgtCont.Id)
                    continue;

                if (rule.SourceSelector.Matches(srcCont) && rule.TargetSelector.Matches(tgtCont))
                {
                    // If the destination is not a pure interface, report decoupling violation
                    if (tgtCont.Kind != SymbolKind.Interface)
                    {
                        symbolById.TryGetValue(rel.SourceId, out var actualSrc);
                        symbolById.TryGetValue(rel.TargetId, out var actualTgt);

                        report.Violations.Add(new RuleViolation
                        {
                            Rule = rule,
                            SourceSymbol = actualSrc ?? srcCont,
                            TargetSymbol = actualTgt ?? tgtCont,
                            Relationship = rel,
                            Message = $"Abstraction violation '{rule.Name}': '{srcCont.Name}' directly accesses concrete class '{tgtCont.Name}' instead of an interface."
                        });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Evaluate violations where an external component accesses a reserved/isolated internal component.
    /// </summary>
    private static void EvaluateIsolatedComponent(
        CodeGraph graph,
        ArchitecturalRule rule,
        Dictionary<Guid, Symbol> symbolById,
        Dictionary<Guid, Symbol> memberToContainerMap,
        ArchitecturalRulesReport report)
    {
        foreach (var rel in graph.Relationships)
        {
            if (!DependencyKinds.Contains(rel.Kind))
                continue;

            if (memberToContainerMap.TryGetValue(rel.SourceId, out var srcCont) &&
                memberToContainerMap.TryGetValue(rel.TargetId, out var tgtCont))
            {
                if (srcCont.Id == tgtCont.Id)
                    continue;

                // If the target matches the isolated component but the caller is not authorized
                if (rule.TargetSelector.Matches(tgtCont) && !rule.SourceSelector.Matches(srcCont))
                {
                    symbolById.TryGetValue(rel.SourceId, out var actualSrc);
                    symbolById.TryGetValue(rel.TargetId, out var actualTgt);

                    report.Violations.Add(new RuleViolation
                    {
                        Rule = rule,
                        SourceSymbol = actualSrc ?? srcCont,
                        TargetSymbol = actualTgt ?? tgtCont,
                        Relationship = rel,
                        Message = $"Isolated component violation '{rule.Name}': '{srcCont.Name}' attempts to access restricted internal component '{tgtCont.Name}'."
                    });
                }
            }
        }
    }
}

