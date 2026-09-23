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
    public SharedStateAccessKind AccessKind { get; init; }
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

/// <summary>
/// Analizzatore agnostico dello Stato Globale Condiviso (Global / Shared State Matrix).
/// Rileva variabili Public nei moduli .bas (VB6) e campi statici mutabili (C#), mappando chi le legge e chi le modifica.
/// </summary>
public static class SharedStateAnalyzer
{
    public static SharedStateDataset Analyze(CodeGraph graph)
    {
        var dataset = new SharedStateDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        // 1. Identifica tutte le variabili / campi a visibilità globale o pubblica (VB6 .bas Public/Global o C# public static)
        var globalVars = new List<Symbol>();

        foreach (var sym in graph.Symbols)
        {
            if (sym.Kind != SymbolKind.Field)
                continue;

            // Se è Public o Global
            if (sym.Accessibility is Accessibility.Public or Accessibility.Global)
            {
                var parentRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == sym.Id);
                var parent = parentRel != null ? graph.FindSymbolById(parentRel.SourceId) : null;

                // Modulo standard .bas o Classe/Struttura con stato pubblico
                if (parent != null && parent.Kind is SymbolKind.Module or SymbolKind.Class or SymbolKind.Struct)
                {
                    globalVars.Add(sym);
                }
            }
        }

        if (globalVars.Count == 0)
            return dataset;

        var globalVarById = globalVars.ToDictionary(v => v.Id);

        // Mappa ogni variabile agli accessi da parte delle routine
        var accessorsByVar = globalVars.ToDictionary(v => v.Id, _ => new List<SharedVariableAccessor>());

        // 2. Isola le relazioni Reads e Writes o Calls verso la variabile
        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind is not (RelationshipKind.Reads or RelationshipKind.Writes or RelationshipKind.References or RelationshipKind.Calls))
                continue;

            if (globalVarById.TryGetValue(rel.TargetId, out var targetVar))
            {
                var sourceSym = graph.FindSymbolById(rel.SourceId);
                if (sourceSym == null) continue;

                var parentRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == sourceSym.Id);
                var sourceContainer = parentRel != null ? graph.FindSymbolById(parentRel.SourceId) : null;
                var targetContainerRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == targetVar.Id);
                var targetContainer = targetContainerRel != null ? graph.FindSymbolById(targetContainerRel.SourceId) : null;

                bool isCrossModule = sourceContainer != null && targetContainer != null && sourceContainer.Id != targetContainer.Id;

                SharedStateAccessKind accessKind = rel.Kind switch
                {
                    RelationshipKind.Writes => SharedStateAccessKind.Write,
                    RelationshipKind.Reads => SharedStateAccessKind.Read,
                    _ => SharedStateAccessKind.Read
                };

                accessorsByVar[targetVar.Id].Add(new SharedVariableAccessor
                {
                    RoutineId = sourceSym.Id,
                    RoutineName = sourceSym.Name,
                    RoutineQualifiedName = sourceSym.QualifiedName,
                    ContainerName = sourceContainer?.Name ?? Path.GetFileName(sourceSym.FilePath),
                    FilePath = sourceSym.FilePath,
                    Line = rel.Line ?? sourceSym.LineStart,
                    AccessKind = accessKind,
                    IsCrossModule = isCrossModule
                });
            }
        }

        // Se non ci sono relazioni esplicite Reads/Writes, creiamo comunque gli elementi per permetterne l'ispezione
        foreach (var gv in globalVars)
        {
            var parentRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == gv.Id);
            var parent = parentRel != null ? graph.FindSymbolById(parentRel.SourceId) : null;
            var moduleName = parent?.Name ?? Path.GetFileName(gv.FilePath);

            var accessors = accessorsByVar[gv.Id];

            dataset.Items.Add(new SharedVariableItem
            {
                VariableId = gv.Id,
                VariableName = gv.Name,
                QualifiedName = gv.QualifiedName,
                ModuleName = moduleName,
                FilePath = gv.FilePath,
                Line = gv.LineStart,
                Kind = gv.Kind,
                Accessibility = gv.Accessibility,
                Accessors = accessors
            });
        }

        // Ordina per rischio mutazione decrescente, poi per numero mutatori
        dataset.Items.Sort((a, b) =>
        {
            int r = b.MutationRisk.CompareTo(a.MutationRisk);
            if (r != 0) return r;
            return b.WritersCount.CompareTo(a.WritersCount);
        });

        return dataset;
    }
}
