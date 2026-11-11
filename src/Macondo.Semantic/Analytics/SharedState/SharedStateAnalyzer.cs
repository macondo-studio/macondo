using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.SharedState;

/// <summary>
/// Agnostic Global / Shared State Matrix analyzer.
/// Detects Public variables in .bas modules (VB6) and mutable static fields (C#), mapping who reads them and who modifies them.
/// </summary>
public static class SharedStateAnalyzer
{
    /// <summary>
    /// Performs analysis of all fields, global variables and static states shared between CodeGraph modules,
    /// identifying readers, writers and calculating the risk level of concurrent/cross-module mutation.
    /// </summary>
    /// <param name="graph">The semantic <see cref="CodeGraph"/> of the project.</param>
    /// <returns>The <see cref="SharedStateDataset"/> dataset with the list of shared variables and related accessories.</returns>
    public static SharedStateDataset Analyze(CodeGraph graph)
    {
        var dataset = new SharedStateDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        // =========================================================================
        // STEP 1: Identify all variables / fields with global or public visibility
        // (In VB6: Public/Global variables declared in .bas modules or classes; in C#: static/public fields).
        // =========================================================================
        var globalVars = new List<Symbol>();

        foreach (var sym in graph.Symbols)
        {
            if (sym.Kind != SymbolKind.Field)
                continue;

            // Filter only fields with Public or Global accessibility
            if (sym.Accessibility is Accessibility.Public or Accessibility.Global)
            {
                var parentRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == sym.Id);
                var parent = parentRel != null ? graph.FindSymbolById(parentRel.SourceId) : null;

                // Accept if belonging to a standard .bas module, class or structure
                if (parent != null && parent.Kind is SymbolKind.Module or SymbolKind.Class or SymbolKind.Struct)

                    globalVars.Add(sym);
            }
        }

        if (globalVars.Count == 0)
            return dataset;

        var globalVarById = globalVars.ToDictionary(v => v.Id);

        // Map from Variable ID -> List of Accessors (routines that read or modify it)
        var accessorsByVar = globalVars.ToDictionary(v => v.Id, _ => new List<SharedVariableAccessor>());

        // =========================================================================
        // STEP 2: Inspection of Reads and Writes relationships towards global variables
        // =========================================================================
        foreach (var rel in graph.Relationships)
        {
            if (rel.Kind is not (RelationshipKind.Reads or RelationshipKind.Writes or RelationshipKind.References or RelationshipKind.Calls))
                continue;

            // If the arc target is a monitored global variable
            if (globalVarById.TryGetValue(rel.TargetId, out var targetVar))
            {
                var sourceSym = graph.FindSymbolById(rel.SourceId);
                if (sourceSym == null) continue;

                // Traces source and destination containers to determine if access crosses module boundaries (Cross-Module)
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

                var existingAccessor = accessorsByVar[targetVar.Id].FirstOrDefault(a => a.RoutineId == sourceSym.Id);
                if (existingAccessor != null)
                {
                    if (existingAccessor.AccessKind != accessKind)

                        existingAccessor.AccessKind = SharedStateAccessKind.ReadWrite;
                }
                else
                {
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
        }

        // =========================================================================
        // STEP 3: Composition of the elements of the dataset with the aggregated information
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
        // STEP 4: Sort by decreasing risk level (multiple Cross-Module Writers on top)
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
