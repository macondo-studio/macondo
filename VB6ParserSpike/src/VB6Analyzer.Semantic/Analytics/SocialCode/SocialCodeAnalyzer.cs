using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.SocialCode;

/// <summary>
/// Analizzatore poliglotto di Social Code Analysis, Bus Factor e Conway's Law (Adam Tornhill / CodeScene methodology).
/// </summary>
public static class SocialCodeAnalyzer
{
    /// <summary>
    /// Esegue l'analisi sociale del codice incrociando i file del CodeGraph con la storia dei commit VCS.
    /// </summary>
    public static SocialCodeDataset Analyze(CodeGraph graph, IEnumerable<FileCommitRecord> commitHistory, HashSet<string>? activeAuthors = null)
    {
        var dataset = new SocialCodeDataset();
        var commits = commitHistory.ToList();

        // Raggruppa i commit per file (con path normalizzato)
        var commitsByFile = commits
            .GroupBy(c => NormalizePath(c.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Trova tutti i file noti nel CodeGraph
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
                // Nessun commit registrato nel VCS (assumiamo default 1 autore sconosciuto)
                report.PrimaryAuthor = "Unknown";
                report.PrimaryOwnership = 100.0;
                report.BusFactor = 1;
                report.Risk = BusFactorRisk.Low;
                dataset.Files.Add(report);
                continue;
            }

            // Calcola le contribuzioni per ciascun autore
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

            // Ordina per ownership decrescente
            report.AuthorContributions.Sort((a, b) => b.TotalLinesChanged.CompareTo(a.TotalLinesChanged));

            var primary = report.AuthorContributions[0];
            report.PrimaryAuthor = primary.Author;
            report.PrimaryOwnership = primary.OwnershipPercentage;

            // Calcolo del Bus Factor: numero minimo di autori per superare l'80% di modifiche
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

            // Calcolo dell'Entropia di Shannon (dispersione della conoscenza)
            double entropy = 0.0;
            foreach (var contrib in report.AuthorContributions)
            {
                double p = contrib.OwnershipPercentage / 100.0;
                if (p > 0.0)
                {
                    entropy -= p * Math.Log2(p);
                }
            }
            report.KnowledgeEntropy = Math.Round(entropy, 2);

            // Assegnazione del rischio
            bool isPrimaryInactive = activeAuthors != null && activeAuthors.Count > 0 && !activeAuthors.Contains(primary.Author);
            if (isPrimaryInactive)
            {
                report.Risk = BusFactorRisk.Orphan;
            }
            else if (report.BusFactor == 1 && report.PrimaryOwnership >= 80.0)
            {
                report.Risk = BusFactorRisk.High;
            }
            else if (report.BusFactor == 2)
            {
                report.Risk = BusFactorRisk.Medium;
            }
            else
            {
                report.Risk = BusFactorRisk.Low;
            }

            dataset.Files.Add(report);

            if (!dataset.PrimaryOwnershipCountByAuthor.TryGetValue(primary.Author, out var count))
            {
                count = 0;
            }
            dataset.PrimaryOwnershipCountByAuthor[primary.Author] = count + 1;
        }

        // Ordina mettendo per primi i file orfani o a rischio più alto
        dataset.Files.Sort((a, b) =>
        {
            int cmp = b.Risk.CompareTo(a.Risk);
            if (cmp != 0) return cmp;
            return a.BusFactor.CompareTo(b.BusFactor);
        });

        return dataset;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return path.Replace('\\', '/').Trim('/');
    }
}
