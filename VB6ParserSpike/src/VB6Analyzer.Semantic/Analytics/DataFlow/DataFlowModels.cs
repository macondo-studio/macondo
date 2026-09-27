using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.DataFlow;

/// <summary>
/// Tipologia di sorgente di dati non controllati / esterni (Taint Source).
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
/// Tipologia di punto critico di esecuzione / consumo dati (Taint Sink).
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
/// Definizione di un pattern di rilevamento per sorgenti o sink di flusso dati.
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
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Percorso di propagazione del dato non controllato dalla sorgente al punto critico (Source-to-Sink Path).
/// </summary>
public sealed class TaintPath
{
    public Symbol SourceSymbol { get; init; } = null!;
    public TaintSourceKind SourceKind { get; init; }
    public Symbol SinkSymbol { get; init; } = null!;
    public TaintSinkKind SinkKind { get; init; }

    /// <summary>Catena ordinata di invocazioni o letture/scritture che connettono la sorgente al sink.</summary>
    public List<Symbol> PropagationChain { get; init; } = new();

    /// <summary>Lunghezza del percorso di propagazione.</summary>
    public int HopCount => Math.Max(0, PropagationChain.Count - 1);

    /// <summary>Descrizione del rischio di sicurezza o integrità.</summary>
    public string Description => $"Unsanitized flow from [{SourceKind}] '{SourceSymbol.Name}' to [{SinkKind}] '{SinkSymbol.Name}' ({HopCount} hops).";
}

/// <summary>
/// Rilevamento di anomalie Def-Use (es. variabile scritta ma mai letta -> Dead Store).
/// </summary>
public sealed class DefUseAnomaly
{
    public Symbol VariableSymbol { get; init; } = null!;
    public Symbol? ContainerRoutine { get; init; }
    public string AnomalyType { get; init; } = "DeadStore";
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Dataset complessivo dell'analisi di Data Flow e Taint Tracking.
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
