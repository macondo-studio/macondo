using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.SharedState;

/// <summary>
/// Analizzatore agnostico dello Stato Globale Condiviso (Global / Shared State Matrix).
/// Rileva variabili Public nei moduli .bas (VB6) e campi statici mutabili (C#), mappando chi le legge e chi le modifica.
/// </summary>
public static class SharedStateAnalyzer
{
    /// <summary>
    /// Esegue l'analisi di tutti i campi, variabili globali e stati statici condivisi tra moduli del CodeGraph,
    /// individuando lettori, scrittori e calcolando il livello di rischio di mutazione concorrente/cross-module.
    /// </summary>
    /// <param name="graph">Il <see cref="CodeGraph"/> semantico del progetto.</param>
    /// <returns>Il dataset <see cref="SharedStateDataset"/> con l'elenco delle variabili condivise e dei relativi accessor.</returns>
    public static SharedStateDataset Analyze(CodeGraph graph)
    {
        var dataset = new SharedStateDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        // =========================================================================
        // PASSO 1: Identificazione di tutte le variabili / campi a visibilità globale o pubblica
        // (In VB6: variabili Public/Global dichiarate in moduli .bas o classi; in C#: campi statici/pubblici).
        // =========================================================================
        var globalVars = new List<Symbol>();

        foreach (var sym in graph.Symbols)
        {
            if (sym.Kind != SymbolKind.Field)
                continue;

            // Filtra solo campi con accessibilità Public o Global
            if (sym.Accessibility is Accessibility.Public or Accessibility.Global)
            {
                var parentRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == sym.Id);
                var parent = parentRel != null ? graph.FindSymbolById(parentRel.SourceId) : null;

                // Accetta se appartenente a un modulo standard .bas, classe o struttura
                if (parent != null && parent.Kind is SymbolKind.Module or SymbolKind.Class or SymbolKind.Struct)
                {
                    globalVars.Add(sym);
                }
            }
        }

        if (globalVars.Count == 0)
            return dataset;

        var globalVarById = globalVars.ToDictionary(v => v.Id);

        // Mappa da ID Variabile -> Elenco degli Accessor (routine che la leggono o modificano)
        var accessorsByVar = globalVars.ToDictionary(v => v.Id, _ => new List<SharedVariableAccessor>());

        // =========================================================================
        // PASSO 2: Ispezione delle relazioni Reads e Writes verso le variabili globali
        // =========================================================================
        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind is not (RelationshipKind.Reads or RelationshipKind.Writes or RelationshipKind.References or RelationshipKind.Calls))
                continue;

            // Se la destinazione dell'arco è una variabile globale monitorata
            if (globalVarById.TryGetValue(rel.TargetId, out var targetVar))
            {
                var sourceSym = graph.FindSymbolById(rel.SourceId);
                if (sourceSym == null) continue;

                // Risale ai container di origine e destinazione per determinare se l'accesso attraversa i confini di modulo (Cross-Module)
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

        // =========================================================================
        // PASSO 3: Composizione degli elementi del dataset con le informazioni aggregate
        // =========================================================================
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

        // =========================================================================
        // PASSO 4: Ordinamento per livello di rischio decrescente (Cross-Module Writers multipli in cima)
        // =========================================================================
        dataset.Items.Sort((a, b) =>
        {
            int r = b.MutationRisk.CompareTo(a.MutationRisk);
            if (r != 0) return r;
            return b.WritersCount.CompareTo(a.WritersCount);
        });

        return dataset;
    }
}
