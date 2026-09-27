using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.SocialCode;

/// <summary>
/// Analizzatore poliglotto di Social Code Analysis, Bus Factor, Ownership del codice ed Entropia di Shannon
/// (metodologia Adam Tornhill / CodeScene / Software Design X-Rays).
/// 
/// METRICHE CHIAVE:
/// 1. Primary Ownership: percentuale di linee di codice modificate dall'autore principale su un dato file.
/// 2. Bus Factor (Truck Factor): numero minimo di sviluppatori necessari per coprire l'80% della conoscenza storica del file.
/// 3. Knowledge Entropy (Shannon Entropy): misura del grado di dispersione della conoscenza tra molteplici autori:
///    H = - ∑ (p_i * log2(p_i)), dove p_i è la quota di ownership dell'autore i.
/// 4. Orphan Risk: file in cui l'autore principale non fa più parte degli sviluppatori attivi del team.
/// </summary>
public static class SocialCodeAnalyzer
{
    /// <summary>
    /// Esegue l'analisi sociale del codice incrociando i file del CodeGraph con la cronologia dei commit VCS.
    /// Calcola metriche di Primary Ownership, Bus Factor (Truck Factor), Entropia di Shannon e Rischio di abbandono (Orphan).
    /// </summary>
    /// <param name="graph">Il CodeGraph contenente i simboli del progetto.</param>
    /// <param name="commitHistory">Elenco dei record di commit estratti dal repository Git/VCS.</param>
    /// <param name="activeAuthors">Insieme opzionale degli autori attualmente attivi nel team (usato per rilevare file orfani).</param>
    /// <returns>Il dataset <see cref="SocialCodeDataset"/> con il dettaglio sociale per file e per autore.</returns>
    public static SocialCodeDataset Analyze(CodeGraph graph, IEnumerable<FileCommitRecord> commitHistory, HashSet<string>? activeAuthors = null)
    {
        var dataset = new SocialCodeDataset();
        var commits = commitHistory.ToList();

        // ---------------------------------------------------------------------
        // FASE 1: Raggruppa i commit per percorso file normalizzato
        // ---------------------------------------------------------------------
        // Uniforma i separatori di directory (slash) e garantisce confronti case-insensitive
        var commitsByFile = commits
            .GroupBy(c => NormalizePath(c.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // ---------------------------------------------------------------------
        // FASE 2: Mappatura di tutti i file sorgente presenti nel CodeGraph
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
                // Nessun commit registrato nel VCS (assumiamo default 1 autore sconosciuto con ownership 100%)
                report.PrimaryAuthor = "Unknown";
                report.PrimaryOwnership = 100.0;
                report.BusFactor = 1;
                report.Risk = BusFactorRisk.Low;
                dataset.Files.Add(report);
                continue;
            }

            // -----------------------------------------------------------------
            // FASE 3: Calcolo delle linee totali aggiunte/cancellate per autore
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

            // Ordina gli autori per volume decrescente di righe modificate
            report.AuthorContributions.Sort((a, b) => b.TotalLinesChanged.CompareTo(a.TotalLinesChanged));

            var primary = report.AuthorContributions[0];
            report.PrimaryAuthor = primary.Author;
            report.PrimaryOwnership = primary.OwnershipPercentage;

            // -----------------------------------------------------------------
            // FASE 4: Calcolo del Bus Factor (Truck Factor)
            // -----------------------------------------------------------------
            // Determina il numero minimo di sviluppatori che cumulativamente detengono almeno l'80% delle modifiche
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
            // FASE 5: Calcolo dell'Entropia di Shannon (Knowledge Dispersion)
            // -----------------------------------------------------------------
            // H = - ∑ (p_i * log2(p_i)), dove p_i è la probabilità / quota di ownership dell'autore
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

            // -----------------------------------------------------------------
            // FASE 6: Valutazione del livello di rischio
            // -----------------------------------------------------------------
            // - Orphan: l'autore principale non è più presente nella lista degli autori attivi del team
            // - High: Bus Factor pari a 1 con ownership >= 80% (single point of failure umano)
            // - Medium: Bus Factor pari a 2
            // - Low: Bus Factor distribuito su 3 o più sviluppatori
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

        // Ordina mettendo per primi i file a rischio più elevato (Orphan / High) e minor Bus Factor
        dataset.Files.Sort((a, b) =>
        {
            int cmp = b.Risk.CompareTo(a.Risk);
            if (cmp != 0) return cmp;
            return a.BusFactor.CompareTo(b.BusFactor);
        });

        return dataset;
    }

    /// <summary>
    /// Normalizza un percorso file convertendo i backslash in slash e rimuovendo spazi superflui.
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return path.Replace('\\', '/').Trim('/');
    }
}

