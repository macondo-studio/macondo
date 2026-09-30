using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Macondo.App.Layout;
using Macondo.App.Models;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;

namespace Macondo.App.Services;

/// <summary>
/// Servizio ad alte prestazioni per il caching persistente dei progetti VB6 analizzati (%APPDATA%\Macondo\cache\).
/// Fornisce avvio istantaneo da disco, rilevamento dello stato di staleness rispetto ai file sorgente
/// e ricostruzione del grafo e della tabella Blast Radius.
/// </summary>
public sealed class ProjectCacheService
{
    private static readonly Lazy<ProjectCacheService> _instance = new(() => new ProjectCacheService());
    public static ProjectCacheService Instance => _instance.Value;

    private readonly string _cacheDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    private ProjectCacheService()
    {
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Macondo",
            "cache");

        Directory.CreateDirectory(_cacheDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false, // Compatto e veloce da salvare/caricare
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    /// <summary>
    /// Normalizza un percorso di cartella o di file di progetto (.vbp, .csproj, .sln) restituendo il percorso assoluto della cartella di riferimento in minuscolo.
    /// </summary>
    public static string NormalizeFolderOrFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
        }

        return fullPath
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
    }

    /// <summary>
    /// Percorso del file di cache associato a una determinata cartella o file di progetto.
    /// </summary>
    public string GetCacheFilePath(string folderOrFilePath)
    {
        var normalized = NormalizeFolderOrFilePath(folderOrFilePath);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hashHex = Convert.ToHexString(hashBytes)[..16];
        return Path.Combine(_cacheDirectory, $"cache_{hashHex}.json");
    }

    /// <summary>
    /// Verifica se esiste una cache su disco per la cartella o file di progetto specificato.
    /// </summary>
    public bool HasCache(string folderOrFilePath)
    {
        if (string.IsNullOrWhiteSpace(folderOrFilePath))
            return false;

        var normalized = NormalizeFolderOrFilePath(folderOrFilePath);
        if (!Directory.Exists(normalized))
            return false;

        var path = GetCacheFilePath(folderOrFilePath);
        return File.Exists(path);
    }

    /// <summary>
    /// Tenta di caricare lo snapshot di cache per la cartella o file di progetto specificato.
    /// Restituisce null se la cache non esiste o non è valida.
    /// </summary>
    public async Task<ProjectCacheSnapshot?> TryLoadSnapshotAsync(string folderOrFilePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folderOrFilePath))
                return null;

            var cacheFile = GetCacheFilePath(folderOrFilePath);
            if (!File.Exists(cacheFile))
                return null;

            await using var stream = File.OpenRead(cacheFile);
            var snapshot = await JsonSerializer.DeserializeAsync<ProjectCacheSnapshot>(stream, _jsonOptions);

            if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.Symbols.Count == 0)
                return null;

            return snapshot;
        }
        catch
        {
            // Tolleranza agli errori: se il file è corrotto o inaccessibile, fallback su null
            return null;
        }
    }

    /// <summary>
    /// Ricostruisce il VisualCodeGraph (con semantic graph e queries attive) e le righe della tabella Blast Radius.
    /// </summary>
    public (VisualCodeGraph Graph, List<BlastRadiusGridRow> Rows) RestoreGraphAndRows(ProjectCacheSnapshot snapshot)
    {
        // 1. Ricostruzione del CodeGraph semantico
        var semanticGraph = new CodeGraph();
        semanticGraph.Symbols.AddRange(snapshot.Symbols);
        semanticGraph.Relationships.AddRange(snapshot.Relationships);

        // 2. Creazione dell'adattatore visuale
        var visualGraph = GraphVisualAdapter.FromSemanticGraph(semanticGraph, snapshot.ProjectName);

        // 3. Ripristino delle coordinate geometriche salvate
        if (snapshot.LayoutNodes.Count > 0)
        {
            var layoutMap = snapshot.LayoutNodes.ToDictionary(n => n.Id);
            foreach (var node in visualGraph.Nodes)
            {
                if (layoutMap.TryGetValue(node.Id, out var layout))
                {
                    node.X = layout.X;
                    node.Y = layout.Y;
                    node.IsExpanded = layout.IsExpanded;
                }
            }
            visualGraph.Invalidate();
        }

        // 4. Ripristino delle righe Blast Radius
        var rows = new List<BlastRadiusGridRow>(snapshot.BlastRadiusRows.Count);
        foreach (var r in snapshot.BlastRadiusRows)
        {
            var targetSym = semanticGraph.FindSymbolById(r.NodeId);
            if (targetSym is null) continue;

            rows.Add(new BlastRadiusGridRow
            {
                TargetSymbol = targetSym,
                NodeId = r.NodeId,
                ProjectName = string.IsNullOrEmpty(r.ProjectName) ? snapshot.ProjectName : r.ProjectName,
                Name = r.Name,
                QualifiedName = r.QualifiedName,
                ContainerName = r.ContainerName,
                Kind = r.Kind,
                DirectCallersCount = r.DirectCallersCount,
                IndirectCallersCount = r.IndirectCallersCount,
                MaxDepth = r.MaxDepth,
                RiskLevel = r.RiskLevel,
                AffectedFormsCount = r.AffectedFormsCount,
                TotalImpactedCount = r.TotalImpactedCount,
                AffectedFormsSummary = r.AffectedFormsSummary
            });
        }

        return (visualGraph, rows);
    }

    /// <summary>
    /// Salva immediatamente e in modo sincrono uno snapshot completo di cache per la cartella o file analizzato.
    /// </summary>
    public void SaveSnapshot(
        string folderOrFilePath,
        VisualCodeGraph graph,
        IReadOnlyList<BlastRadiusGridRow>? rows,
        bool hasCustomUserLayout = false,
        IReadOnlyCollection<string>? enabledAnalyticsTabs = null)
    {
        if (graph?.SemanticGraph is null || string.IsNullOrWhiteSpace(folderOrFilePath))
            return;

        var folderPath = File.Exists(folderOrFilePath) ? (Path.GetDirectoryName(folderOrFilePath) ?? folderOrFilePath) : folderOrFilePath;
        if (!Directory.Exists(folderPath))
            return;

        try
        {
            var projectName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var (fileCount, latestWriteUtc) = GetFolderSourceStats(folderPath);

            var snapshot = new ProjectCacheSnapshot
            {
                SchemaVersion = 1,
                FolderPath = folderPath,
                ProjectName = projectName,
                AnalyzedAtUtc = DateTime.UtcNow,
                LastFileWriteTimeUtc = latestWriteUtc,
                FileCount = fileCount,
                Symbols = graph.SemanticGraph.Symbols,
                Relationships = graph.SemanticGraph.Relationships,
                HasCustomUserLayout = hasCustomUserLayout,
                LayoutNodes = graph.Nodes.Select(n => new NodeLayoutSnapshot
                {
                    Id = n.Id,
                    X = n.X,
                    Y = n.Y,
                    IsExpanded = n.IsExpanded
                }).ToList(),
                BlastRadiusRows = (rows ?? Array.Empty<BlastRadiusGridRow>()).Select(r => new BlastRadiusRowSnapshot
                {
                    NodeId = r.NodeId,
                    ProjectName = r.ProjectName,
                    Name = r.Name,
                    QualifiedName = r.QualifiedName,
                    ContainerName = r.ContainerName,
                    Kind = r.Kind,
                    DirectCallersCount = r.DirectCallersCount,
                    IndirectCallersCount = r.IndirectCallersCount,
                    MaxDepth = r.MaxDepth,
                    RiskLevel = r.RiskLevel,
                    AffectedFormsCount = r.AffectedFormsCount,
                    TotalImpactedCount = r.TotalImpactedCount,
                    AffectedFormsSummary = r.AffectedFormsSummary
                }).ToList()
            };

            var cacheFile = GetCacheFilePath(folderOrFilePath);

            List<string> tabsToSave;
            if (enabledAnalyticsTabs != null)
            {
                tabsToSave = enabledAnalyticsTabs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            else if (File.Exists(cacheFile))
            {
                try
                {
                    var existingJson = File.ReadAllText(cacheFile);
                    var existingSnapshot = JsonSerializer.Deserialize<ProjectCacheSnapshot>(existingJson, _jsonOptions);
                    tabsToSave = existingSnapshot?.EnabledAnalyticsTabs ?? new List<string>();
                }
                catch
                {
                    tabsToSave = new List<string>();
                }
            }
            else
            {
                tabsToSave = new List<string>();
            }

            snapshot.EnabledAnalyticsTabs = tabsToSave;

            var tempFile = cacheFile + ".tmp";

            var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, cacheFile, overwrite: true);
        }
        catch
        {
            // Il fallimento di una scrittura cache non deve mai bloccare l'interfaccia utente
        }
    }

    /// <summary>
    /// Salva in background su thread dedicato uno snapshot completo di cache per la cartella o file analizzato.
    /// </summary>
    public Task SaveSnapshotAsync(
        string folderOrFilePath,
        VisualCodeGraph graph,
        IReadOnlyList<BlastRadiusGridRow>? rows,
        bool hasCustomUserLayout = false,
        IReadOnlyCollection<string>? enabledAnalyticsTabs = null)
        => Task.Run(() => SaveSnapshot(folderOrFilePath, graph, rows, hasCustomUserLayout, enabledAnalyticsTabs));

    /// <summary>
    /// Aggiorna le coordinate geometriche del layout nello snapshot di cache esistente su disco.
    /// </summary>
    public void UpdateLayoutSnapshot(string folderOrFilePath, VisualCodeGraph graph, bool hasCustomUserLayout = false)
    {
        if (string.IsNullOrWhiteSpace(folderOrFilePath) || graph is null)
            return;

        var folderPath = File.Exists(folderOrFilePath) ? (Path.GetDirectoryName(folderOrFilePath) ?? folderOrFilePath) : folderOrFilePath;
        if (!Directory.Exists(folderPath))
            return;

        try
        {
            var cacheFile = GetCacheFilePath(folderOrFilePath);
            if (!File.Exists(cacheFile))
                return;

            var json = File.ReadAllText(cacheFile);
            var snapshot = JsonSerializer.Deserialize<ProjectCacheSnapshot>(json, _jsonOptions);
            if (snapshot is null) return;

            snapshot.HasCustomUserLayout = hasCustomUserLayout;
            snapshot.LayoutNodes = graph.Nodes.Select(n => new NodeLayoutSnapshot
            {
                Id = n.Id,
                X = n.X,
                Y = n.Y,
                IsExpanded = n.IsExpanded
            }).ToList();

            var tempFile = cacheFile + ".tmp";
            var updatedJson = JsonSerializer.Serialize(snapshot, _jsonOptions);
            File.WriteAllText(tempFile, updatedJson);
            File.Move(tempFile, cacheFile, overwrite: true);
        }
        catch
        {
            // Tolleranza agli errori
        }
    }

    /// <summary>
    /// Versione asincrona che esegue l'aggiornamento del layout su thread di background.
    /// </summary>
    public Task UpdateLayoutSnapshotAsync(string folderOrFilePath, VisualCodeGraph graph, bool hasCustomUserLayout = false)
        => Task.Run(() => UpdateLayoutSnapshot(folderOrFilePath, graph, hasCustomUserLayout));

    /// <summary>
    /// Aggiorna l'elenco delle schede analitiche abilitate o avviate nello snapshot di cache esistente su disco.
    /// </summary>
    public void UpdateEnabledAnalyticsTabs(string folderOrFilePath, IEnumerable<string> enabledTabs)
    {
        if (string.IsNullOrWhiteSpace(folderOrFilePath))
            return;

        var folderPath = File.Exists(folderOrFilePath) ? (Path.GetDirectoryName(folderOrFilePath) ?? folderOrFilePath) : folderOrFilePath;
        if (!Directory.Exists(folderPath))
            return;

        try
        {
            var cacheFile = GetCacheFilePath(folderOrFilePath);
            if (!File.Exists(cacheFile))
                return;

            var json = File.ReadAllText(cacheFile);
            var snapshot = JsonSerializer.Deserialize<ProjectCacheSnapshot>(json, _jsonOptions);
            if (snapshot is null) return;

            snapshot.EnabledAnalyticsTabs = enabledTabs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var tempFile = cacheFile + ".tmp";
            var updatedJson = JsonSerializer.Serialize(snapshot, _jsonOptions);
            File.WriteAllText(tempFile, updatedJson);
            File.Move(tempFile, cacheFile, overwrite: true);
        }
        catch
        {
            // Tolleranza agli errori
        }
    }

    /// <summary>
    /// Versione asincrona che aggiorna l'elenco delle schede analitiche abilitate in background.
    /// </summary>
    public Task UpdateEnabledAnalyticsTabsAsync(string folderOrFilePath, IEnumerable<string> enabledTabs)
        => Task.Run(() => UpdateEnabledAnalyticsTabs(folderOrFilePath, enabledTabs));

    /// <summary>
    /// Controlla se i file sorgente su disco sono cambiati rispetto allo snapshot della cache.
    /// </summary>
    public bool CheckStaleness(ProjectCacheSnapshot snapshot, string folderOrFilePath)
    {
        var folderPath = File.Exists(folderOrFilePath) ? (Path.GetDirectoryName(folderOrFilePath) ?? folderOrFilePath) : folderOrFilePath;
        if (!Directory.Exists(folderPath))
            return false;

        try
        {
            var (fileCount, latestWriteUtc) = GetFolderSourceStats(folderPath);
            return fileCount != snapshot.FileCount || latestWriteUtc > snapshot.LastFileWriteTimeUtc;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Elimina tutti i file snapshot di cache persistenti presenti nella directory di cache (%APPDATA%\Macondo\cache\).
    /// Restituisce il numero di file di cache eliminati.
    /// </summary>
    public int ClearAllCache()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                return 0;

            var files = Directory.GetFiles(_cacheDirectory, "*.json");
            int deleted = 0;
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch { }
            }
            return deleted;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Ispeziona i file sorgente VB6 e C# (.cls, .bas, .frm, .ctl, .cs, .csproj, .sln, .vbp, .vbg, .vb) nella directory specificata per calcolare
    /// il numero totale di file e l'ultimo timestamp di modifica UTC, utile a verificare lo stato di staleness della cache.
    /// </summary>
    private static (int FileCount, DateTime LatestWriteUtc) GetFolderSourceStats(string folderPath)
    {
        var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cls", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".frm", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".ctl", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".vbp", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".vbg", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var maxTime = DateTime.MinValue;
        foreach (var file in files)
        {
            var writeTime = File.GetLastWriteTimeUtc(file);
            if (writeTime > maxTime)
                maxTime = writeTime;
        }

        return (files.Count, maxTime);
    }
}
