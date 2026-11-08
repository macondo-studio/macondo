using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;

namespace Macondo.Semantic.Impact;


/// <summary>
/// Descrittore di un chiamante impattato lungo la catena del Blast Radius, con indicazione della profondità.
/// </summary>
public sealed class ImpactedCaller
{
    /// <summary>Il simbolo della routine chiamante (Method o Property).</summary>
    public required Symbol Symbol { get; init; }

    /// <summary>
    /// Distanza / profondità rispetto al simbolo target della modifica (1 = chiamante diretto, 2 = chiamante di secondo livello, ecc.).
    /// </summary>
    public required int Depth { get; init; }

    /// <summary>Il simbolo del container padre (Form, Class, Module) in cui risiede la routine.</summary>
    public Symbol? EnclosingContainer { get; init; }

    public override string ToString() =>
        $"[Livello {Depth}] {Symbol.QualifiedName} (in {EnclosingContainer?.Name ?? "N/A"})";
}

/// <summary>
/// Report completo di Impact Analysis / Blast Radius.
/// Calcola tutte le entità e i percorsi di esecuzione a rischio generati dalla modifica di un simbolo target.
/// </summary>
public sealed class ImpactReport
{
    /// <summary>Il simbolo analizzato (il punto di origine della potenziale modifica o refactoring).</summary>
    public required Symbol TargetSymbol { get; init; }

    /// <summary>Chiamanti diretti (profondità = 1).</summary>
    public required IReadOnlyList<Symbol> DirectCallers { get; init; }

    /// <summary>Tutti i chiamanti indiretti/transitivi (profondità >= 2).</summary>
    public required IReadOnlyList<ImpactedCaller> IndirectCallers { get; init; }

    /// <summary>Tutti i chiamanti (diretti + indiretti) raggruppati per livello di profondità.</summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<Symbol>> CallersByDepth { get; init; }

    /// <summary>Elenco univoco delle Classi (.cls) impattate che contengono chiamanti o dipendenze di tipo.</summary>
    public required IReadOnlyList<Symbol> AffectedClasses { get; init; }

    /// <summary>Elenco univoco dei Moduli standard (.bas) impattati.</summary>
    public required IReadOnlyList<Symbol> AffectedModules { get; init; }

    /// <summary>Elenco univoco delle Form (.frm) impattate (punti di contatto UI per l'utente finale).</summary>
    public required IReadOnlyList<Symbol> AffectedForms { get; init; }

    /// <summary>Elenco univoco degli User Control (.ctl) impattati.</summary>
    public required IReadOnlyList<Symbol> AffectedUserControls { get; init; }

    /// <summary>Tutti i cammini di esecuzione che partono da entry-point di primo livello e arrivano al target.</summary>
    public required IReadOnlyList<CallPath> AffectedExecutionPaths { get; init; }

    /// <summary>Profondità massima della catena di dipendenze (Max dependency depth).</summary>
    public required int MaxDependencyDepth { get; init; }

    /// <summary>Simboli che istanziano il target (se il target è una Classe o UDT).</summary>
    public required IReadOnlyList<Symbol> Instantiators { get; init; }

    /// <summary>Simboli che hanno una dipendenza di tipo diretta (USES_TYPE) sul target.</summary>
    public required IReadOnlyList<Symbol> TypeDependents { get; init; }

    /// <summary>Totale complessivo dei simboli distinti toccati dal blast radius.</summary>
    public int TotalImpactedSymbolsCount =>
        DirectCallers.Count + IndirectCallers.Count + TypeDependents.Count + Instantiators.Count;

    /// <summary>Livello di rischio stimato in base alle metriche calcolate.</summary>
    public ImpactRiskLevel RiskLevel
    {
        get
        {
            if (AffectedForms.Count >= 3 || (AffectedClasses.Count + AffectedModules.Count) >= 6 || MaxDependencyDepth >= 4)
                return ImpactRiskLevel.Critical;

            if (AffectedForms.Count >= 1 || (AffectedClasses.Count + AffectedModules.Count) >= 3 || MaxDependencyDepth >= 2)
                return ImpactRiskLevel.High;

            if (DirectCallers.Count > 0 || TypeDependents.Count > 0)
                return ImpactRiskLevel.Medium;

            return ImpactRiskLevel.Low;
        }
    }
}
