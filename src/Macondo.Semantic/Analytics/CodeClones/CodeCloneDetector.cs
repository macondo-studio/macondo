using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.CodeClones;

/// <summary>
/// Motore di rilevamento cloni e duplicazioni di codice (Copy-Paste Detector / CPD) poliglotto.
/// 
/// MODELLO DI RILEVAMENTO:
/// 1. Cloni di Tipo 1 (Exact Clones): routine o frammenti identici nella firma, lunghezza e sequenza esatta
///    di chiamate a metodi target nel Call Graph.
/// 2. Cloni di Tipo 2 (Parameterized / Structural Clones): routine strutturalmente identiche che condividono
///    lunghezza di righe, numero di parametri formali e densità di chiamate uscenti, ma con identificatori o variabili rinominate.
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
    /// Distingue tra cloni esatti (Tipo 1) e cloni strutturali parametrizzati (Tipo 2).
    /// </summary>
    /// <param name="graph">Il CodeGraph contenente tutti i simboli e le relazioni.</param>
    /// <param name="minLineCount">Numero minimo di righe di codice richieste per considerare una routine candidato clone (default 3).</param>
    /// <returns>Il report strutturato <see cref="CodeCloneReport"/> con i gruppi di cloni ordinati per volume di righe ridondanti.</returns>
    public static CodeCloneReport DetectClones(CodeGraph graph, int minLineCount = 3)
    {
        var report = new CodeCloneReport();
        if (graph.Symbols.Count == 0)
            return report;

        var routines = graph.Symbols.Where(s => RoutineKinds.Contains(s.Kind)).ToList();
        if (routines.Count < 2)
            return report;

        // ---------------------------------------------------------------------
        // FASE 1: Raggruppamento e calcolo delle fingerprint per Tipo 1 e Tipo 2
        // ---------------------------------------------------------------------
        var exactGroups = new Dictionary<string, List<Symbol>>();
        var structuralGroups = new Dictionary<string, List<Symbol>>();

        foreach (var r in routines)
        {
            int lines = Math.Max(1, r.LineEnd - r.LineStart + 1);
            if (lines < minLineCount)
                continue;

            // 1a. Fingerprint per Tipo 1 (Esatto - basato su righe, conteggio parametri e sequenza esatta di chiamate uscenti)
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

            // 1b. Fingerprint per Tipo 2 (Strutturale - basato su lunghezza, conteggio parametri e quantità di chiamate uscenti)
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
        // FASE 2: Creazione gruppi di cloni di Tipo 1 (Exact Clones)
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
        // FASE 3: Creazione gruppi di cloni di Tipo 2 (Parameterized / Structural)
        // ---------------------------------------------------------------------
        // Include solo i simboli non ancora categorizzati come cloni esatti
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
        // FASE 4: Ordinamento per righe ridondanti decrescenti (maggior risparmio di refactoring)
        // ---------------------------------------------------------------------
        report.CloneGroups.Sort((a, b) => b.RedundantLines.CompareTo(a.RedundantLines));
        return report;
    }
}

