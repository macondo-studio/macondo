using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.SocialCode;

/// <summary>
/// Polyglot analyzer of Social Code Analysis, Bus Factor, Code Ownership and Shannon Entropy
/// (metodologia Adam Tornhill / CodeScene / Software Design X-Rays).
/// 
/// METRICHE CHIAVE:
/// 1. Primary Ownership: Percentage of lines of code modified by the primary author on a given file.
/// 2. Bus Factor (Truck Factor): minimum number of developers needed to cover 80% of the historical knowledge of the file.
/// 3. Knowledge Entropy (Shannon Entropy): measure of the degree of dispersion of knowledge among multiple authors:
/// H = - ∑ (p_i * log2(p_i)), where p_i is the ownership share of author i.
/// 4. Orphan Risk: Files where the lead author is no longer part of the active developers on the team.
/// </summary>
public static class SocialCodeAnalyzer
{
    /// <summary>
    /// Performs social analysis of code by cross-referencing CodeGraph files with VCS commit history.
    /// Calculate Primary Ownership, Bus Factor (Truck Factor), Shannon Entropy and Dropout Risk (Orphan) metrics.
    /// </summary>
    /// <param name="graph">The CodeGraph containing the project symbols.</param>
    /// <param name="commitHistory">List of commit records extracted from the Git/VCS repository.</param>
    /// <param name="activeAuthors">Optional set of authors currently active in the team (used to detect orphaned files).</param>
    /// <returns>The <see cref="SocialCodeDataset"/> dataset with the social detail by file and author.</returns>
    public static SocialCodeDataset Analyze(CodeGraph graph, IEnumerable<FileCommitRecord> commitHistory, HashSet<string>? activeAuthors = null)
    {
        var dataset = new SocialCodeDataset();
        var commits = commitHistory.ToList();
        if (commits.Count == 0)
            return dataset;

        // ---------------------------------------------------------------------
        // STEP 1: Group commits by normalized file path
        // ---------------------------------------------------------------------
        // Standardizes directory separators (slashes) and ensures case-insensitive comparisons
        var commitsByFile = commits
            .GroupBy(c => NormalizePath(c.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // ---------------------------------------------------------------------
        // PHASE 2: Mapping of all the source files present in the CodeGraph
        // ---------------------------------------------------------------------
        var knownFiles = graph.Symbols
            .Where(s => !string.IsNullOrWhiteSpace(s.FilePath))
            .GroupBy(s => NormalizePath(s.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var allFileKeys = commitsByFile.Keys.Union(knownFiles.Keys).ToList();

        foreach (var fileKey in allFileKeys)
        {
            var fileCommits = commitsByFile.TryGetValue(fileKey, out var list) ? list : new List<FileCommitRecord>();
            var symbol = knownFiles.TryGetValue(fileKey, out var sym) ? sym : null;

            var fileName = Path.GetFileName(fileKey);
            var report = new FileSocialReport
            {
                FilePath = fileKey,
                FileName = string.IsNullOrEmpty(fileName) ? fileKey : fileName,
                SymbolId = symbol?.Id
            };

            if (fileCommits.Count == 0)
            {
                // No commits recorded in the VCS (we assume default 1 unknown author with 100% ownership)
                report.PrimaryAuthor = "Unknown";
                report.PrimaryOwnership = 100.0;
                report.BusFactor = 1;
                report.Risk = BusFactorRisk.Low;
                dataset.Files.Add(report);
                continue;
            }

            // -----------------------------------------------------------------
            // STEP 3: Calculation of total lines added/deleted by author
            // -----------------------------------------------------------------
            var authorGroups = fileCommits
                .GroupBy(c => string.IsNullOrWhiteSpace(c.Author) ? "Unknown" : c.Author.Trim())
                .ToList();

            int totalChanges = fileCommits.Sum(c => Math.Max(1, c.LinesAdded + c.LinesDeleted));

            foreach (var ag in authorGroups)
            {
                int authorChanges = ag.Sum(c => Math.Max(1, c.LinesAdded + c.LinesDeleted));
                double ownership = totalChanges > 0 ? (double)authorChanges / totalChanges * 100.0 : 0.0;

                report.AuthorContributions.Add(new AuthorContribution
                {
                    Author = ag.Key,
                    CommitCount = ag.Count(),
                    TotalLinesChanged = authorChanges,
                    OwnershipPercentage = Math.Round(ownership, 1),
                    LastCommitDate = ag.Max(c => c.Timestamp)
                });
            }

            // Sort authors by decreasing volume of edited lines
            report.AuthorContributions.Sort((a, b) => b.TotalLinesChanged.CompareTo(a.TotalLinesChanged));

            var primary = report.AuthorContributions[0];
            report.PrimaryAuthor = primary.Author;
            report.PrimaryOwnership = primary.OwnershipPercentage;

            // -----------------------------------------------------------------
            // PHASE 4: Calculation of the Bus Factor (Truck Factor)
            // -----------------------------------------------------------------
            // Determines the minimum number of developers who cumulatively own at least 80% of the changes
            int coveredLines = 0;
            int busFactor = 0;
            int targetThreshold = (int)(totalChanges * 0.8);

            foreach (var contrib in report.AuthorContributions)
            {
                busFactor++;
                coveredLines += contrib.TotalLinesChanged;
                if (coveredLines >= targetThreshold)
                    break;
            }
            report.BusFactor = Math.Max(1, busFactor);

            // -----------------------------------------------------------------
            // PHASE 5: Calculation of Shannon Entropy (Knowledge Dispersion)
            // -----------------------------------------------------------------
            // H = - ∑ (p_i * log2(p_i)), where p_i is the author's probability / share of ownership
            double entropy = 0.0;
            foreach (var contrib in report.AuthorContributions)
            {
                double p = contrib.OwnershipPercentage / 100.0;
                if (p > 0.0)

                    entropy -= p * Math.Log2(p);
            }
            report.KnowledgeEntropy = Math.Round(entropy, 2);

            // -----------------------------------------------------------------
            // PHASE 6: Assessment of the risk level
            // -----------------------------------------------------------------
            // - Orphan: the main author is no longer present in the team's list of active authors
            // - High: Bus Factor equal to 1 with ownership >= 80% (human single point of failure)
            // - Medium: Bus Factor equal to 2
            // - Low: Bus Factor distributed across 3 or more developers
            bool isPrimaryInactive = activeAuthors != null && activeAuthors.Count > 0 && !activeAuthors.Contains(primary.Author);
            if (isPrimaryInactive)

                report.Risk = BusFactorRisk.Orphan;

            else if (report.BusFactor == 1 && report.PrimaryOwnership >= 80.0)

    report.Risk = BusFactorRisk.High;

else if (report.BusFactor == 2)

    report.Risk = BusFactorRisk.Medium;

else
            {
                report.Risk = BusFactorRisk.Low;
            }

            dataset.Files.Add(report);

            if (!dataset.PrimaryOwnershipCountByAuthor.TryGetValue(primary.Author, out var count))

                count = 0;
            dataset.PrimaryOwnershipCountByAuthor[primary.Author] = count + 1;
        }

        // Sort by putting files with the highest risk (Orphan / High) and lowest Bus Factor first
        dataset.Files.Sort((a, b) =>
        {
            int cmp = b.Risk.CompareTo(a.Risk);
            if (cmp != 0) return cmp;
            return a.BusFactor.CompareTo(b.BusFactor);
        });

        return dataset;
    }

    /// <summary>
    /// Normalize a file path by converting backslashes to slashes and removing unnecessary spaces.
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return path.Replace('\\', '/').Trim('/');
    }
}

