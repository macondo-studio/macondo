using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ArchitecturalRules;

/// <summary>
/// Tipologia di regola o vincolo architetturale.
/// </summary>
public enum RuleType
{
    /// <summary>Dipendenza proibita: i componenti sorgente NON possono dipendere dai componenti target.</summary>
    ForbiddenDependency,

    /// <summary>Dipendenza esclusiva: i componenti sorgente possono dipendere SOLO dai componenti target consentiti.</summary>
    AllowedDependenciesOnly,

    /// <summary>Astrazione obbligatoria: l'accoppiamento deve avvenire esclusivamente tramite interfacce.</summary>
    MustDependOnInterfaceOnly,

    /// <summary>Componente isolato: nessun componente esterno può chiamare o accedere a questo componente.</summary>
    IsolatedInternalComponent
}

/// <summary>
/// Livello di severità della violazione della regola architetturale.
/// </summary>
public enum RuleSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Definizione di un pattern o selettore di componenti/layer basato su nomi, namespace, tipi o cartelle.
/// </summary>
public sealed class ComponentSelector
{
    /// <summary>Nome visuale del layer o selettore (es. "Domain", "Presentation", "Data Access", "Moduli Standard").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Pattern di match sul nome o percorso del file (es. "*Domain*", "Services/*", "*.frm", "Repository*").</summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Filtro opzionale sul tipo di simbolo (Form, Class, Module, Interface, ecc.).</summary>
    public SymbolKind? KindFilter { get; set; }

    /// <summary>
    /// Verifica se un simbolo soddisfa questo selettore.
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
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Regola architetturale formale da verificare sul CodeGraph.
/// </summary>
public sealed class ArchitecturalRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RuleType Type { get; set; }
    public RuleSeverity Severity { get; set; } = RuleSeverity.Error;

    /// <summary>Selettore dei componenti sorgente soggetti alla regola.</summary>
    public ComponentSelector SourceSelector { get; set; } = new();

    /// <summary>Selettore dei componenti target per la regola.</summary>
    public ComponentSelector TargetSelector { get; set; } = new();

    /// <summary>Indica se la regola è attiva nella validazione.</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Singola violazione di una regola architetturale rilevata nel CodeGraph.
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
/// Risultato complessivo della valutazione delle regole architetturali sul progetto.
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
