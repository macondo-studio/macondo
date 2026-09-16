using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.TemporalCoupling;

/// <summary>
/// Coppia di file che mostrano un accoppiamento temporale (co-modifica frequente nei commit del VCS).
/// </summary>
public sealed class TemporalCouplingPair
{
    public required string FileA { get; init; }
    public required string FileB { get; init; }
    public string FileAName => Path.GetFileName(FileA);
    public string FileBName => Path.GetFileName(FileB);

    /// <summary>Numero di commit in cui entrambi i file sono stati modificati insieme.</summary>
    public int CoChangeCount { get; set; }

    /// <summary>Totale commit in cui compare il File A.</summary>
    public int TotalCommitsA { get; set; }

    /// <summary>Totale commit in cui compare il File B.</summary>
    public int TotalCommitsB { get; set; }

    /// <summary>
    /// Indice di similarità Jaccard J(A, B) = CoChange / (CommitsA + CommitsB - CoChange) in [0.0 .. 1.0].
    /// </summary>
    public double JaccardIndex { get; set; }

    /// <summary>Confidenza P(B|A) = CoChange / CommitsA in [0.0 .. 1.0].</summary>
    public double ConfidenceAToB => TotalCommitsA > 0 ? (double)CoChangeCount / TotalCommitsA : 0.0;

    /// <summary>Confidenza P(A|B) = CoChange / CommitsB in [0.0 .. 1.0].</summary>
    public double ConfidenceBToA => TotalCommitsB > 0 ? (double)CoChangeCount / TotalCommitsB : 0.0;

    /// <summary>
    /// Indica se tra i due file esiste già una dipendenza statica esplicita (chiamata o istanziazione).
    /// Se false e l'indice di Jaccard è alto, si tratta di un accoppiamento nascosto critico (Hidden Coupling).
    /// </summary>
    public bool HasStaticDependency { get; set; }
}

/// <summary>
/// Dataset completo dell'analisi di Temporal Coupling estratto dalla cronologia Git / VCS.
/// </summary>
public sealed class TemporalCouplingDataset
{
    public bool HasGitHistory { get; set; }
    public string? GitRoot { get; set; }
    public int TotalCommitsAnalyzed { get; set; }
    public List<TemporalCouplingPair> Pairs { get; init; } = new();

    public int TotalCoupledPairsCount => Pairs.Count;
    public int StrongCouplingCount => Pairs.Count(p => p.JaccardIndex >= 0.5);
    public int HiddenCouplingCount => Pairs.Count(p => p.JaccardIndex >= 0.4 && !p.HasStaticDependency);
}

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
    /// Esegue l'analisi di accoppiamento temporale a partire dall'elenco dei changelist di ciascun commit.
    /// </summary>
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

        // 1. Elaborazione changelist dei singoli commit
        foreach (var changelist in commitChangelists)
        {
            // Filtra solo file sorgente rilevanti ed evita commit con troppi file (es. merge o refactoring globali massivi)
            var relevantFiles = changelist
                .Select(f => f.Replace('\\', '/').Trim())
                .Where(f => RelevantExtensions.Contains(Path.GetExtension(f)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (relevantFiles.Count < 2 || relevantFiles.Count > 35)
                continue;

            // Incrementa conteggi singoli file
            foreach (var file in relevantFiles)
            {
                fileCommitCounts[file] = fileCommitCounts.GetValueOrDefault(file, 0) + 1;
            }

            // Incrementa conteggi co-occorrenza per ciascuna coppia (A, B)
            for (int i = 0; i < relevantFiles.Count; i++)
            {
                for (int j = i + 1; j < relevantFiles.Count; j++)
                {
                    var pairKey = (relevantFiles[i], relevantFiles[j]);
                    coChangeCounts[pairKey] = coChangeCounts.GetValueOrDefault(pairKey, 0) + 1;
                }
            }
        }

        // 2. Mappatura dipendenze statiche se il CodeGraph è disponibile
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

        // 3. Calcolo Jaccard Index e costruzione delle coppie
        foreach (var (pairKey, coChanges) in coChangeCounts)
        {
            // Considera solo coppie che cambiano insieme almeno 2 volte
            if (coChanges < 2)
                continue;

            var (fileA, fileB) = pairKey;
            var commitsA = fileCommitCounts.GetValueOrDefault(fileA, coChanges);
            var commitsB = fileCommitCounts.GetValueOrDefault(fileB, coChanges);

            int unionCommits = commitsA + commitsB - coChanges;
            double jaccard = unionCommits > 0 ? (double)coChanges / unionCommits : 0.0;

            // Filtro soglia minima di confidenza o jaccard >= 0.20
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

        // Ordina per Jaccard Index decrescente, poi per numero co-modifiche
        dataset.Pairs.Sort((a, b) =>
        {
            int j = b.JaccardIndex.CompareTo(a.JaccardIndex);
            if (j != 0) return j;
            return b.CoChangeCount.CompareTo(a.CoChangeCount);
        });

        return dataset;
    }
}
