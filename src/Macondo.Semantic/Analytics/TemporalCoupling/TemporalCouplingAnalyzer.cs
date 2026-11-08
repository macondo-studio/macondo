using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.TemporalCoupling;

/// <summary>
/// Analizzatore agnostico di Temporal Coupling basato sulla storia dei commit (Repository Mining).
/// Calcola la co-occorrenza storica delle modifiche e l'indice di Jaccard tra file.
/// </summary>
public static class TemporalCouplingAnalyzer
{
    private static readonly HashSet<string> RelevantExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cls", ".frm", ".bas", ".ctl", ".cs", ".vb", ".cpp", ".c", ".h", ".py", ".ts", ".js"
    };

    /// <summary>
    /// Esegue l'analisi di accoppiamento temporale a partire dall'elenco dei changelist di ciascun commit VCS.
    /// Calcola la frequenza di co-modifica congiunta e l'indice di similarità di Jaccard per identificare accoppiamenti nascosti (Hidden Coupling).
    /// </summary>
    /// <param name="commitChangelists">Elenco dei file modificati per ciascun commit estratto dal repository.</param>
    /// <param name="graph">Il <see cref="CodeGraph"/> opzionale per verificare se tra i file esiste già una dipendenza statica esplicita.</param>
    /// <param name="gitRoot">Percorso radice del repository Git.</param>
    /// <returns>Il dataset <see cref="TemporalCouplingDataset"/> con tutte le coppie di file accoppiate temporalmente.</returns>
    public static TemporalCouplingDataset Analyze(List<List<string>> commitChangelists, CodeGraph? graph = null, string? gitRoot = null)
    {
        var dataset = new TemporalCouplingDataset
        {
            HasGitHistory = commitChangelists.Count > 0,
            GitRoot = gitRoot,
            TotalCommitsAnalyzed = commitChangelists.Count
        };

        if (commitChangelists.Count == 0)
            return dataset;

        var fileCommitCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var coChangeCounts = new Dictionary<(string, string), int>();

        // =========================================================================
        // PASSO 1: Elaborazione e filtraggio delle changelist dei singoli commit VCS
        // =========================================================================
        foreach (var changelist in commitChangelists)
        {
            // Filtra solo i file sorgente rilevanti (escludendo build artifacts, file temporanei, configurazioni)
            var relevantFiles = changelist
                .Select(f => f.Replace('\\', '/').Trim())
                .Where(f => RelevantExtensions.Contains(Path.GetExtension(f)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (relevantFiles.Count > 35)
                continue;

            // Incrementa il conteggio complessivo di commit per ciascun file
            foreach (var file in relevantFiles)
            {
                fileCommitCounts[file] = fileCommitCounts.GetValueOrDefault(file, 0) + 1;
            }

            // Ignora commit con 1 solo file per le coppie di co-modifica congiunta
            if (relevantFiles.Count < 2)
                continue;

            // Incrementa i conteggi di co-occorrenza congiunta per ciascuna coppia distinta (A, B)
            for (int i = 0; i < relevantFiles.Count; i++)
            {
                for (int j = i + 1; j < relevantFiles.Count; j++)
                {
                    var pairKey = (relevantFiles[i], relevantFiles[j]);
                    coChangeCounts[pairKey] = coChangeCounts.GetValueOrDefault(pairKey, 0) + 1;
                }
            }
        }

        // =========================================================================
        // PASSO 2: Mappatura delle dipendenze statiche esplicite dal CodeGraph
        // Identifica se i due file sono già legati da chiamate o istanziazioni note:
        // se NO ma cambiano sempre insieme, si tratta di un accoppiamento implicito/nascosto critico (Hidden Coupling).
        // =========================================================================
        var staticDependencyPairs = new HashSet<(string, string)>();
        if (graph != null)
        {
            foreach (var rel in graph.Relationships)
            {
                if (rel.Kind is RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.UsesType or RelationshipKind.Inherits or RelationshipKind.Implements)
                {
                    var src = graph.FindSymbolById(rel.SourceId);
                    var tgt = graph.FindSymbolById(rel.TargetId);
                    if (src?.FilePath != null && tgt?.FilePath != null)
                    {
                        var normSrc = src.FilePath.Replace('\\', '/').Trim();
                        var normTgt = tgt.FilePath.Replace('\\', '/').Trim();

                        if (!string.Equals(normSrc, normTgt, StringComparison.OrdinalIgnoreCase))
                        {
                            var f1 = string.Compare(normSrc, normTgt, StringComparison.OrdinalIgnoreCase) < 0 ? normSrc : normTgt;
                            var f2 = string.Compare(normSrc, normTgt, StringComparison.OrdinalIgnoreCase) < 0 ? normTgt : normSrc;
                            staticDependencyPairs.Add((f1, f2));
                        }
                    }
                }
            }
        }

        // =========================================================================
        // PASSO 3: Calcolo dell'indice di Jaccard e costruzione delle coppie
        // J(A, B) = CoChange(A, B) / (Commits(A) + Commits(B) - CoChange(A, B))
        // =========================================================================
        foreach (var (pairKey, coChanges) in coChangeCounts)
        {
            var (fileA, fileB) = pairKey;
            var commitsA = fileCommitCounts.GetValueOrDefault(fileA, coChanges);
            var commitsB = fileCommitCounts.GetValueOrDefault(fileB, coChanges);

            // Calcolo dell'unione degli insiemi dei commit
            int unionCommits = commitsA + commitsB - coChanges;
            double jaccard = unionCommits > 0 ? (double)coChanges / unionCommits : 0.0;

            // Filtro con soglia minima di rilevanza statistica (Jaccard >= 0.20 oppure almeno 3 co-modifiche)
            if (jaccard < 0.20 && (coChanges < 3))
                continue;

            bool hasStaticDep = staticDependencyPairs.Contains((fileA, fileB)) ||
                                staticDependencyPairs.Contains((fileB, fileA));

            dataset.Pairs.Add(new TemporalCouplingPair
            {
                FileA = fileA,
                FileB = fileB,
                CoChangeCount = coChanges,
                TotalCommitsA = commitsA,
                TotalCommitsB = commitsB,
                JaccardIndex = jaccard,
                HasStaticDependency = hasStaticDep
            });
        }

        // =========================================================================
        // PASSO 4: Ordinamento per indice di similarità Jaccard decrescente
        // =========================================================================
        dataset.Pairs.Sort((a, b) =>
        {
            int j = b.JaccardIndex.CompareTo(a.JaccardIndex);
            if (j != 0) return j;
            return b.CoChangeCount.CompareTo(a.CoChangeCount);
        });

        return dataset;
    }
}
