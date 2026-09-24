using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ArchitecturalRules;

/// <summary>
/// Motore di valutazione ed enforcement delle regole e fitness function architetturali sul CodeGraph.
/// </summary>
public static class ArchitecturalRulesEngine
{
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
    /// Restituisce un set predefinito di regole architetturali standard per progetti enterprise e multi-tier.
    /// </summary>
    public static List<ArchitecturalRule> GetDefaultRules()
    {
        return new List<ArchitecturalRule>
        {
            new ArchitecturalRule
            {
                Name = "Domain Isolation (Clean Architecture)",
                Description = "Il layer di Dominio / Modelli non deve avere dipendenze verso i layer esterni (UI, Controller o DataAccess).",
                Type = RuleType.ForbiddenDependency,
                Severity = RuleSeverity.Critical,
                SourceSelector = new ComponentSelector { Name = "Domain", Pattern = "*Domain*;*Model*;*Entity*" },
                TargetSelector = new ComponentSelector { Name = "OuterLayers", Pattern = "*UI*;*Form*;*View*;*Controller*;*Repository*;*DataAccess*" }
            },
            new ArchitecturalRule
            {
                Name = "No Direct DB Access from UI / Forms",
                Description = "I Form e i controlli UI non devono invocare direttamente componenti o repository di accesso ai dati senza passare da servizi.",
                Type = RuleType.ForbiddenDependency,
                Severity = RuleSeverity.Error,
                SourceSelector = new ComponentSelector { Name = "UI / Forms", Pattern = "*.frm;*View*;*Form*", KindFilter = SymbolKind.Form },
                TargetSelector = new ComponentSelector { Name = "Data Access", Pattern = "*Repository*;*DataAccess*;*DbContext*;*AdoHelper*" }
            },
            new ArchitecturalRule
            {
                Name = "Standard Modules Must Not Instantiate Forms",
                Description = "I moduli di utilità pura (.bas o classi Helper) non devono istanziare né dipendere direttamente da interfacce grafiche/Form.",
                Type = RuleType.ForbiddenDependency,
                Severity = RuleSeverity.Warning,
                SourceSelector = new ComponentSelector { Name = "Standard Modules", Pattern = "*.bas;*Helper*;*Util*", KindFilter = SymbolKind.Module },
                TargetSelector = new ComponentSelector { Name = "Forms", Pattern = "*.frm", KindFilter = SymbolKind.Form }
            }
        };
    }

    /// <summary>
    /// Valuta un insieme di regole architetturali sul grafo del codice e produce il report dettagliato delle violazioni.
    /// </summary>
    public static ArchitecturalRulesReport Evaluate(CodeGraph graph, IEnumerable<ArchitecturalRule>? customRules = null)
    {
        var rules = (customRules ?? GetDefaultRules()).ToList();
        var report = new ArchitecturalRulesReport { Rules = rules };

        if (graph.Symbols.Count == 0 || graph.Relationships.Count == 0)
            return report;

        var symbolById = graph.Symbols.ToDictionary(s => s.Id);

        // Mappa ogni simbolo membro al rispettivo contenitore (se applicabile)
        var memberToContainerMap = new Dictionary<Guid, Symbol>();
        foreach (var sym in graph.Symbols)
        {
            memberToContainerMap[sym.Id] = sym;
            foreach (var child in graph.GetChildren(sym.Id))
            {
                memberToContainerMap[child.Id] = sym;
            }
        }

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

    private static void EvaluateForbiddenDependency(
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
                        Message = $"Violazione '{rule.Name}': '{srcCont.Name}' non può dipendere da '{tgtCont.Name}' (relazione: {rel.Kind})."
                    });
                }
            }
        }
    }

    private static void EvaluateMustDependOnInterfaceOnly(
        CodeGraph graph,
        ArchitecturalRule rule,
        Dictionary<Guid, Symbol> symbolById,
        Dictionary<Guid, Symbol> memberToContainerMap,
        ArchitecturalRulesReport report)
    {
        foreach (var rel in graph.Relationships)
        {
            if (!DependencyKinds.Contains(rel.Kind) || rel.Kind == RelationshipKind.Implements)
                continue;

            if (memberToContainerMap.TryGetValue(rel.SourceId, out var srcCont) &&
                memberToContainerMap.TryGetValue(rel.TargetId, out var tgtCont))
            {
                if (srcCont.Id == tgtCont.Id)
                    continue;

                if (rule.SourceSelector.Matches(srcCont) && rule.TargetSelector.Matches(tgtCont))
                {
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
                            Message = $"Violazione astrazione '{rule.Name}': '{srcCont.Name}' accede direttamente alla classe concreta '{tgtCont.Name}' invece di un'interfaccia."
                        });
                    }
                }
            }
        }
    }

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
                        Message = $"Violazione componente isolato '{rule.Name}': '{srcCont.Name}' tenta di accedere al componente interno riservato '{tgtCont.Name}'."
                    });
                }
            }
        }
    }
}
