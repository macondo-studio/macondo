using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ArchitecturalRules;

/// <summary>
/// Motore di valutazione ed enforcement delle regole e fitness function architetturali sul <see cref="CodeGraph"/>.
/// 
/// CARATTERISTICHE E TIPOLOGIE DI REGOLE:
/// 1. ForbiddenDependency: vieta qualsiasi dipendenza diretta o indiretta (Calls, Instantiates, UsesType, Reads, Writes, Inherits)
///    da un layer sorgente verso un layer bersaglio non consentito (es. Domain verso UI/DataAccess).
/// 2. MustDependOnInterfaceOnly: impone che l'accesso a un layer avvenga esclusivamente tramite contratti/interfacce
///    disaccoppiando l'implementazione concreta.
/// 3. IsolatedInternalComponent: garantisce che i componenti interni o moduli privati non siano referenziati
///    da componenti al di fuori del loro contesto designato.
/// </summary>
public static class ArchitecturalRulesEngine
{
    /// <summary>
    /// Relazioni del CodeGraph considerate dipendenze architetturali dirette tra componenti.
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
    /// Restituisce un set predefinito di regole architetturali standard per progetti enterprise e multi-tier (Clean Architecture, N-Tier).
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
    /// Valuta un insieme di regole architetturali sul grafo del codice e produce il report dettagliato delle violazioni.
    /// Esegue il controllo su dipendenze vietate (ForbiddenDependency), obbligo di interfacce (MustDependOnInterfaceOnly)
    /// e isolamento di componenti interni (IsolatedInternalComponent).
    /// </summary>
    /// <param name="graph">Il CodeGraph contenente simboli e relazioni del progetto.</param>
    /// <param name="customRules">Lista opzionale di regole personalizzate (se null, usa le regole standard di default).</param>
    /// <returns>Il report <see cref="ArchitecturalRulesReport"/> contenente l'elenco delle violazioni rilevate.</returns>
    public static ArchitecturalRulesReport Evaluate(CodeGraph graph, IEnumerable<ArchitecturalRule>? customRules = null)
    {
        var rules = (customRules ?? GetDefaultRules()).ToList();
        var report = new ArchitecturalRulesReport { Rules = rules };

        if (graph.Symbols.Count == 0 || graph.Relationships.Count == 0)
            return report;

        var symbolById = graph.Symbols.ToDictionary(s => s.Id);

        // ---------------------------------------------------------------------
        // FASE 1: Mappatura globale membro (metodo/proprietà/campo) -> container radice
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
        // FASE 2: Valutazione per ciascuna regola attiva
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
    /// Valuta violazioni in cui un componente sorgente stabilisce una dipendenza vietata verso un componente target.
    /// Esempio: Il Domain Layer che invoca o istanzia componenti del DataAccess o della UI.
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
            // Considera solo le relazioni che costituiscono accoppiamento architetturale
            if (!DependencyKinds.Contains(rel.Kind))
                continue;

            if (memberToContainerMap.TryGetValue(rel.SourceId, out var srcCont) &&
                memberToContainerMap.TryGetValue(rel.TargetId, out var tgtCont))
            {
                // Ignora dipendenze interne allo stesso container/classe
                if (srcCont.Id == tgtCont.Id)
                    continue;

                // Verifica se la coppia (Sorgente, Destinazione) corrisponde ai selettori della regola vietata
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
    /// Valuta violazioni in cui un componente accede direttamente a classi concrete anziché a interfacce astratte.
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
            // Esclude le relazioni di implementazione diretta (Implements) in quanto lecite
            if (!DependencyKinds.Contains(rel.Kind) || rel.Kind == RelationshipKind.Implements)
                continue;

            if (memberToContainerMap.TryGetValue(rel.SourceId, out var srcCont) &&
                memberToContainerMap.TryGetValue(rel.TargetId, out var tgtCont))
            {
                if (srcCont.Id == tgtCont.Id)
                    continue;

                if (rule.SourceSelector.Matches(srcCont) && rule.TargetSelector.Matches(tgtCont))
                {
                    // Se la destinazione non è un'interfaccia pura, segnala violazione di disaccoppiamento
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
    /// Valuta violazioni in cui un componente esterno accede a un componente interno riservato / isolato.
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

                // Se il target corrisponde al componente isolato ma il chiamante non è autorizzato
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

