using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.CodeClones;

/// <summary>
/// Motore di rilevamento cloni e duplicazioni di codice (Copy-Paste Detector / CPD) poliglotto.
/// Utilizza un approccio basato su fingerprinting e hashing su routine e frammenti di codice.
/// </summary>
public static class CodeCloneDetector
{
    private static readonly HashSet<SymbolKind> RoutineKinds = new()
    {
        SymbolKind.Method,
        SymbolKind.Property
    };

    /// <summary>
    /// Esegue il rilevamento delle duplicazioni su tutte le routine e componenti del CodeGraph.
    /// </summary>
    public static CodeCloneReport DetectClones(CodeGraph graph, int minLineCount = 3)
    {
        var report = new CodeCloneReport();
        if (graph.Symbols.Count == 0)
            return report;

        var routines = graph.Symbols.Where(s => RoutineKinds.Contains(s.Kind)).ToList();
        if (routines.Count < 2)
            return report;

        // Raggruppa i metodi in base alla signature o pattern strutturale
        var exactGroups = new Dictionary<string, List<Symbol>>();
        var structuralGroups = new Dictionary<string, List<Symbol>>();

        foreach (var r in routines)
        {
            int lines = Math.Max(1, r.LineEnd - r.LineStart + 1);
            if (lines < minLineCount)
                continue;

            // Fingerprint per Tipo 1 (Esatto - basato su nome e struttura chiamate)
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

            // Fingerprint per Tipo 2 (Strutturale - basato solo su lunghezza, parametri e numero di chiamate)
            var structKey = $"{lines}_{r.Parameters.Count}_{outgoingCalls.Count}";
            if (!structuralGroups.TryGetValue(structKey, out var listStruct))
            {
                listStruct = new List<Symbol>();
                structuralGroups[structKey] = listStruct;
            }
            listStruct.Add(r);
        }

        var processedSymbols = new HashSet<Guid>();

        // 1. Identifica cloni di Tipo 1 (Exact)
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

        // 2. Identifica cloni di Tipo 2 (Parameterized) per i simboli rimanenti
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

        // Ordina mettendo in cima i gruppi con il maggior numero di righe duplicate
        report.CloneGroups.Sort((a, b) => b.RedundantLines.CompareTo(a.RedundantLines));
        return report;
    }
}
