using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.SharedState;

/// <summary>
/// Tipologia di accesso a una variabile o stato globale condiviso.
/// </summary>
public enum SharedStateAccessKind
{
    Read,
    Write,
    ReadWrite
}

/// <summary>
/// Singola routine o procedura che accede in lettura o scrittura a una variabile globale/statica.
/// </summary>
public sealed class SharedVariableAccessor
{
    public Guid RoutineId { get; init; }
    public string RoutineName { get; init; } = string.Empty;
    public string RoutineQualifiedName { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int Line { get; init; }
    public SharedStateAccessKind AccessKind { get; set; }
    public bool IsCrossModule { get; init; }
}

/// <summary>
/// Singola variabile globale, campo statico o stato mutabile condiviso tra più moduli.
/// </summary>
public sealed class SharedVariableItem
{
    public Guid VariableId { get; init; }
    public string VariableName { get; init; } = string.Empty;
    public string QualifiedName { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int Line { get; init; }
    public SymbolKind Kind { get; init; }
    public Accessibility Accessibility { get; init; }

    public List<SharedVariableAccessor> Accessors { get; init; } = new();

    public int TotalAccessorsCount => Accessors.Count;
    public int ReadersCount => Accessors.Count(a => a.AccessKind is SharedStateAccessKind.Read or SharedStateAccessKind.ReadWrite);
    public int WritersCount => Accessors.Count(a => a.AccessKind is SharedStateAccessKind.Write or SharedStateAccessKind.ReadWrite);
    public int CrossModuleWritersCount => Accessors.Count(a => a.IsCrossModule && (a.AccessKind is SharedStateAccessKind.Write or SharedStateAccessKind.ReadWrite));

    /// <summary>
    /// Punteggio di rischio mutazione: proporzionale a quanti moduli differenti scrivono direttamente su questa variabile.
    /// </summary>
    public ImpactRiskLevel MutationRisk
    {
        get
        {
            if (WritersCount == 0) return ImpactRiskLevel.Low;
            if (CrossModuleWritersCount >= 4) return ImpactRiskLevel.Critical;
            if (CrossModuleWritersCount >= 2) return ImpactRiskLevel.High;
            if (WritersCount >= 2) return ImpactRiskLevel.Medium;
            return ImpactRiskLevel.Low;
        }
    }
}

/// <summary>
/// Dataset completo dell'analisi dello Stato Condiviso per l'intero progetto.
/// </summary>
public sealed class SharedStateDataset
{
    public List<SharedVariableItem> Items { get; init; } = new();

    public int TotalSharedVariablesCount => Items.Count;
    public int TotalWritersCount => Items.Sum(i => i.WritersCount);
    public int TotalCrossModuleWritersCount => Items.Sum(i => i.CrossModuleWritersCount);
    public int CriticalVariablesCount => Items.Count(i => i.MutationRisk == ImpactRiskLevel.Critical);
    public int HighRiskVariablesCount => Items.Count(i => i.MutationRisk == ImpactRiskLevel.High);
}
