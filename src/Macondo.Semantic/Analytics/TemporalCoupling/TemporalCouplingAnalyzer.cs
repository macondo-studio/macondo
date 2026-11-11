using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.TemporalCoupling;

/// <summary>
/// Agnostic Temporal Coupling analyzer based on commit history (Repository Mining).
/// Compute historical co-occurrence of changes and Jaccard index between files.
/// </summary>
public static class TemporalCouplingAnalyzer
{
    private static readonly HashSet<string> RelevantExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cls", ".frm", ".bas", ".ctl", ".cs", ".vb", ".cpp", ".c", ".h", ".py", ".ts", ".js"
    };

    /// <summary>
    /// Performs temporal coupling analysis from the changelist of each VCS commit.
    /// Calculate the joint co-modification frequency and Jaccard similarity index to identify hidden couplings.
    /// </summary>
    /// <param name="commitChangelists">List of files modified for each commit extracted from the repository.</param>
    /// <param name="graph">The optional <see cref="CodeGraph"/> to check whether an explicit static dependency already exists between files.</param>
    /// <param name="gitRoot">Root path to the Git repository.</param>
    /// <returns>The <see cref="TemporalCouplingDataset"/> dataset with all temporally coupled file pairs.</returns>
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
        // STEP 1: Processing and filtering the changelists of individual VCS commits
        // =========================================================================
        foreach (var changelist in commitChangelists)
        {
            // Filter only relevant source files (excluding build artifacts, temporary files, configurations)
            var relevantFiles = changelist
                .Select(f => f.Replace('\\', '/').Trim())
                .Where(f => RelevantExtensions.Contains(Path.GetExtension(f)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (relevantFiles.Count > 35)
                continue;

            // Increment the overall commit count for each file
            foreach (var file in relevantFiles)
            {
                fileCommitCounts[file] = fileCommitCounts.GetValueOrDefault(file, 0) + 1;
            }

            // Ignore commits with only 1 file for joint co-editing pairs
            if (relevantFiles.Count < 2)
                continue;

            // Increment joint co-occurrence counts for each distinct pair (A, B)
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
        // STEP 2: Mapping explicit static dependencies from the CodeGraph
        // Identifies whether the two files are already linked by known calls or instantiations:
        // if NO but they always change together, it is a critical implicit/hidden coupling (Hidden Coupling).
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
        // STEP 3: Calculation of the Jaccard index and construction of the pairs
        // J(A, B) = CoChange(A, B) / (Commits(A) + Commits(B) - CoChange(A, B))
        // =========================================================================
        foreach (var (pairKey, coChanges) in coChangeCounts)
        {
            var (fileA, fileB) = pairKey;
            var commitsA = fileCommitCounts.GetValueOrDefault(fileA, coChanges);
            var commitsB = fileCommitCounts.GetValueOrDefault(fileB, coChanges);

            // Calculating the union of commit sets
            int unionCommits = commitsA + commitsB - coChanges;
            double jaccard = unionCommits > 0 ? (double)coChanges / unionCommits : 0.0;

            // Filter with minimum statistical significance threshold (Jaccard >= 0.20 or at least 3 co-modifications)
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
        // STEP 4: Sorting by decreasing Jaccard similarity index
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
