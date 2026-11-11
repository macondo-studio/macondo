using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.DataFlow;

/// <summary>
/// Type of uncontrolled / external data source (Taint Source).
/// </summary>
public enum TaintSourceKind
{
    UserInput,
    NetworkOrApi,
    FileOrFileSystem,
    RegistryOrConfig,
    EnvironmentVariable,
    Custom
}

/// <summary>
/// Type of critical execution/data consumption point (Taint Sink).
/// </summary>
public enum TaintSinkKind
{
    SqlExecution,
    ShellOrProcessExecution,
    FileSystemWrite,
    ReflectionOrLateBinding,
    CryptoOrSecuritySensitive,
    Custom
}

/// <summary>
/// Defining a detection pattern for data flow sources or sinks.
/// </summary>
public sealed class TaintPattern
{
    public string Name { get; init; } = string.Empty;
    public string MatchingPattern { get; init; } = string.Empty;
    public SymbolKind? KindFilter { get; init; }

    public bool Matches(Symbol symbol)
    {
        if (KindFilter.HasValue && symbol.Kind != KindFilter.Value)
            return false;

        var parts = MatchingPattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            var clean = p.Replace("*", "");
            if (symbol.Name.Contains(clean, StringComparison.OrdinalIgnoreCase) ||
                symbol.QualifiedName.Contains(clean, StringComparison.OrdinalIgnoreCase))

                return true;
        }
        return false;
    }
}

/// <summary>
/// Uncontrolled data propagation path from the source to the critical point (Source-to-Sink Path).
/// </summary>
public sealed class TaintPath
{
    public Symbol SourceSymbol { get; init; } = null!;
    public TaintSourceKind SourceKind { get; init; }
    public Symbol SinkSymbol { get; init; } = null!;
    public TaintSinkKind SinkKind { get; init; }

    /// <summary>Ordered chain of invocations or reads/writes that connect the source to the sink.</summary>
    public List<Symbol> PropagationChain { get; init; } = new();

    /// <summary>Length of the propagation path.</summary>
    public int HopCount => Math.Max(0, PropagationChain.Count - 1);

    /// <summary>Description of the security or integrity risk.</summary>
    public string Description => $"Unsanitized flow from [{SourceKind}] '{SourceSymbol.Name}' to [{SinkKind}] '{SinkSymbol.Name}' ({HopCount} hops).";
}

/// <summary>
/// Detection of Def-Use anomalies (e.g. variable written but never read -> Dead Store).
/// </summary>
public sealed class DefUseAnomaly
{
    public Symbol VariableSymbol { get; init; } = null!;
    public Symbol? ContainerRoutine { get; init; }
    public string AnomalyType { get; init; } = "DeadStore";
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Overall dataset of Data Flow and Taint Tracking analysis.
/// </summary>
public sealed class DataFlowDataset
{
    public List<TaintPath> TaintPaths { get; init; } = new();
    public List<DefUseAnomaly> Anomalies { get; init; } = new();

    public int TotalTaintPaths => TaintPaths.Count;
    public int SqlInjectionRisks => TaintPaths.Count(p => p.SinkKind == TaintSinkKind.SqlExecution);
    public int CommandExecutionRisks => TaintPaths.Count(p => p.SinkKind == TaintSinkKind.ShellOrProcessExecution);
    public int TotalDeadStores => Anomalies.Count(a => a.AnomalyType == "DeadStore");
}
