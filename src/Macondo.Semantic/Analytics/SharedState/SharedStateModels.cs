using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.SharedState;

/// <summary>
/// Type of access to a shared global variable or state.
/// </summary>
public enum SharedStateAccessKind
{
    Read,
    Write,
    ReadWrite
}

/// <summary>
/// Single routine or procedure that reads or writes to a global/static variable.
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
/// Single global variable, static field or mutable state shared across multiple modules.
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
    /// Mutation risk score: proportional to how many different modules write directly to this variable.
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
/// Complete Shared State analysis dataset for the entire project.
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
