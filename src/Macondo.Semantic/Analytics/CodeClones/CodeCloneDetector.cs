using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.CodeClones;

/// <summary>
/// Polyglot clone and code duplication detection engine (Copy-Paste Detector / CPD).
/// 
/// DETECTION MODEL:
/// 1. Type 1 Clones (Exact Clones): routines or fragments identical in signature, length and exact sequence
/// of calls to target methods in the Call Graph.
/// 2. Type 2 Clones (Parameterized / Structural Clones): structurally identical routines that share
/// length of lines, number of formal parameters and density of outgoing calls, but with renamed identifiers or variables.
/// </summary>
public static class CodeCloneDetector
{
    private static readonly HashSet<SymbolKind> RoutineKinds = new()
    {
        SymbolKind.Method,
        SymbolKind.Property
    };

    /// <summary>
    /// Performs duplication detection on all CodeGraph routines and components.
    /// Distinguishes between exact clones (Type 1) and parameterized structural clones (Type 2).
    /// </summary>
    /// <param name="graph">The CodeGraph containing all the symbols and relationships.</param>
    /// <param name="minLineCount">Minimum number of lines of code required to consider a clone candidate routine (default 3).</param>
    /// <returns>The structured report <see cref="CodeCloneReport"/> with clone groups sorted by volume of redundant rows.</returns>
    public static CodeCloneReport DetectClones(CodeGraph graph, int minLineCount = 3)
    {
        var report = new CodeCloneReport();
        if (graph.Symbols.Count == 0)
            return report;

        var routines = graph.Symbols.Where(s => RoutineKinds.Contains(s.Kind)).ToList();
        if (routines.Count < 2)
            return report;

        // ---------------------------------------------------------------------
        // PHASE 1: Grouping and calculation of fingerprints for Type 1 and Type 2
        // ---------------------------------------------------------------------
        var exactGroups = new Dictionary<string, List<Symbol>>();
        var structuralGroups = new Dictionary<string, List<Symbol>>();

        foreach (var r in routines)
        {
            int lines = Math.Max(1, r.LineEnd - r.LineStart + 1);
            if (lines < minLineCount)
                continue;

            // 1a. Fingerprint for Type 1 (Exact - based on rows, parameter count and exact sequence of outgoing calls)
            var outgoingCalls = graph.Relationships
                .Where(rel => rel.SourceId == r.Id && rel.Kind == RelationshipKind.Calls)
                .Select(rel => rel.TargetId.ToString())
                .OrderBy(id => id)
                .ToList();

            var exactKey = $"{lines}_{r.Parameters.Count}_{string.Join(",", outgoingCalls)}";

            if (!exactGroups.TryGetValue(exactKey, out var listExact))
            {
                listExact = new List<Symbol>();
                exactGroups[exactKey] = listExact;
            }
            listExact.Add(r);

            // 1b. Fingerprint for Type 2 (Structural - based on length, parameter count and quantity of outgoing calls)
            var structKey = $"{lines}_{r.Parameters.Count}_{outgoingCalls.Count}";
            if (!structuralGroups.TryGetValue(structKey, out var listStruct))
            {
                listStruct = new List<Symbol>();
                structuralGroups[structKey] = listStruct;
            }
            listStruct.Add(r);
        }

        var processedSymbols = new HashSet<Guid>();

        // ---------------------------------------------------------------------
        // PHASE 2: Creation of groups of Type 1 clones (Exact Clones)
        // ---------------------------------------------------------------------
        foreach (var (key, group) in exactGroups)
        {
            if (group.Count > 1)
            {
                var first = group[0];
                int lineCount = Math.Max(1, first.LineEnd - first.LineStart + 1);

                var cloneGroup = new CodeCloneGroup
                {
                    Type = CloneType.Type1Exact,
                    LineCount = lineCount,
                    TokenCount = lineCount * 5
                };

                foreach (var sym in group)
                {
                    processedSymbols.Add(sym.Id);
                    cloneGroup.Occurrences.Add(new CodeCloneFragment
                    {
                        Symbol = sym,
                        LineStart = sym.LineStart,
                        LineEnd = sym.LineEnd,
                        CodeSnippet = $"{sym.Kind} {sym.QualifiedName}({string.Join(", ", sym.Parameters.Select(p => p.Name))})"
                    });
                }

                report.CloneGroups.Add(cloneGroup);
            }
        }

        // ---------------------------------------------------------------------
        // PHASE 3: Creation of Type 2 clone groups (Parameterized / Structural)
        // ---------------------------------------------------------------------
        // Includes only symbols not yet categorized as exact clones
        foreach (var (key, group) in structuralGroups)
        {
            var unassigned = group.Where(s => !processedSymbols.Contains(s.Id)).ToList();
            if (unassigned.Count > 1)
            {
                var first = unassigned[0];
                int lineCount = Math.Max(1, first.LineEnd - first.LineStart + 1);

                var cloneGroup = new CodeCloneGroup
                {
                    Type = CloneType.Type2Parameterized,
                    LineCount = lineCount,
                    TokenCount = lineCount * 5
                };

                foreach (var sym in unassigned)
                {
                    cloneGroup.Occurrences.Add(new CodeCloneFragment
                    {
                        Symbol = sym,
                        LineStart = sym.LineStart,
                        LineEnd = sym.LineEnd,
                        CodeSnippet = $"{sym.Kind} {sym.QualifiedName}(...)"
                    });
                }

                report.CloneGroups.Add(cloneGroup);
            }
        }

        // ---------------------------------------------------------------------
        // PHASE 4: Sorting by decreasing redundant rows (greater refactoring savings)
        // ---------------------------------------------------------------------
        report.CloneGroups.Sort((a, b) => b.RedundantLines.CompareTo(a.RedundantLines));
        return report;
    }
}

