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
/// High-performance service for persistent caching of analyzed VB6 projects (%APPDATA%\Macondo\cache\).
/// Provides instant boot from disk, staleness detection against source files
/// and reconstruction of the Blast Radius graph and table.
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
    /// Normalizes a folder or project file path (.vbp, .csproj, .sln) by returning the absolute path of the referenced folder in lowercase.
    /// </summary>
    public static string NormalizeFolderOrFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))

            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;

        return fullPath
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
    }

    /// <summary>
    /// Path to the cache file associated with a specific folder or project file.
    /// </summary>
    public string GetCacheFilePath(string folderOrFilePath)
    {
        var normalized = NormalizeFolderOrFilePath(folderOrFilePath);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hashHex = Convert.ToHexString(hashBytes)[..16];
        return Path.Combine(_cacheDirectory, $"cache_{hashHex}.json");
    }

    /// <summary>
    /// Checks whether a disk cache exists for the specified folder or project file.
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
    /// Attempts to load the cache snapshot for the specified project file or folder.
    /// Returns null if the cache does not exist or is invalid.
    /// </summary>
    /// <param name="folderOrFilePath">Path to the project folder or file.</param>
    /// <param name="cancellationToken">Optional deletion token.</param>
    public async Task<ProjectCacheSnapshot?> TryLoadSnapshotAsync(string folderOrFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(folderOrFilePath))
                return null;

            var cacheFile = GetCacheFilePath(folderOrFilePath);
            if (!File.Exists(cacheFile))
                return null;

            await using var stream = File.OpenRead(cacheFile);
            var snapshot = await JsonSerializer.DeserializeAsync<ProjectCacheSnapshot>(stream, _jsonOptions, cancellationToken);

            if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.Symbols.Count == 0)
                return null;

            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fault tolerance: If the file is corrupt or inaccessible, fallback to null
            return null;
        }
    }

    /// <summary>
    /// Rebuilds the VisualCodeGraph (with semantic graph and active queries) and the rows of the Blast Radius table.
    /// </summary>
    /// <param name="snapshot">Cache snapshot loaded.</param>
    /// <param name="cancellationToken">Optional deletion token.</param>
    public (VisualCodeGraph Graph, List<BlastRadiusGridRow> Rows) RestoreGraphAndRows(ProjectCacheSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Reconstruction of the semantic CodeGraph
        var semanticGraph = new CodeGraph();
        semanticGraph.Symbols.AddRange(snapshot.Symbols);
        semanticGraph.Relationships.AddRange(snapshot.Relationships);

        cancellationToken.ThrowIfCancellationRequested();

        // 2. Creating the visual adapter
        var visualGraph = GraphVisualAdapter.FromSemanticGraph(semanticGraph, snapshot.ProjectName);

        cancellationToken.ThrowIfCancellationRequested();

        // 3. Restoration of saved geometric coordinates
        if (snapshot.LayoutNodes.Count > 0)
        {
            var layoutMap = snapshot.LayoutNodes.ToDictionary(n => n.Id);
            foreach (var node in visualGraph.Nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (layoutMap.TryGetValue(node.Id, out var layout))
                {
                    node.X = layout.X;
                    node.Y = layout.Y;
                    node.IsExpanded = layout.IsExpanded;
                }
            }
            visualGraph.Invalidate();
        }

        // 4. Restoring Blast Radius lines
        var rows = new List<BlastRadiusGridRow>(snapshot.BlastRadiusRows.Count);
        foreach (var r in snapshot.BlastRadiusRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
    /// Instantly and synchronously save a full cache snapshot for the scanned folder or file.
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
                tabsToSave = enabledAnalyticsTabs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            else if (File.Exists(cacheFile))
            {
                try
                {
                    using var readFs = File.OpenRead(cacheFile);
                    var existingSnapshot = JsonSerializer.Deserialize<ProjectCacheSnapshot>(readFs, _jsonOptions);
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

            using (var writeFs = File.Create(tempFile))
            {
                JsonSerializer.Serialize(writeFs, snapshot, _jsonOptions);
            }
            File.Move(tempFile, cacheFile, overwrite: true);
        }
        catch
        {
            // Failure of a cache write should never crash the UI
        }
    }

    /// <summary>
    /// Saves a full cache snapshot for the analyzed folder or file in the background on a dedicated thread.
    /// </summary>
    public Task SaveSnapshotAsync(
        string folderOrFilePath,
        VisualCodeGraph graph,
        IReadOnlyList<BlastRadiusGridRow>? rows,
        bool hasCustomUserLayout = false,
        IReadOnlyCollection<string>? enabledAnalyticsTabs = null)
        => Task.Run(() => SaveSnapshot(folderOrFilePath, graph, rows, hasCustomUserLayout, enabledAnalyticsTabs));

    /// <summary>
    /// Updates the geometric coordinates of the layout in the existing cache snapshot on disk.
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

            ProjectCacheSnapshot? snapshot;
            using (var readFs = File.OpenRead(cacheFile))
            {
                snapshot = JsonSerializer.Deserialize<ProjectCacheSnapshot>(readFs, _jsonOptions);
            }
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
            using (var writeFs = File.Create(tempFile))
            {
                JsonSerializer.Serialize(writeFs, snapshot, _jsonOptions);
            }
            File.Move(tempFile, cacheFile, overwrite: true);
        }
        catch
        {
            // Fault tolerance
        }
    }

    /// <summary>
    /// Asynchronous version that performs layout updates on background threads.
    /// </summary>
    public Task UpdateLayoutSnapshotAsync(string folderOrFilePath, VisualCodeGraph graph, bool hasCustomUserLayout = false)
        => Task.Run(() => UpdateLayoutSnapshot(folderOrFilePath, graph, hasCustomUserLayout));

    /// <summary>
    /// Updates the list of enabled or launched analytic cards in the existing on-disk cache snapshot.
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

            ProjectCacheSnapshot? snapshot;
            using (var readFs = File.OpenRead(cacheFile))
            {
                snapshot = JsonSerializer.Deserialize<ProjectCacheSnapshot>(readFs, _jsonOptions);
            }
            if (snapshot is null) return;

            snapshot.EnabledAnalyticsTabs = enabledTabs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var tempFile = cacheFile + ".tmp";
            using (var writeFs = File.Create(tempFile))
            {
                JsonSerializer.Serialize(writeFs, snapshot, _jsonOptions);
            }
            File.Move(tempFile, cacheFile, overwrite: true);
        }
        catch
        {
            // Fault tolerance
        }
    }

    /// <summary>
    /// Asynchronous version that updates the list of enabled analytical cards in the background.
    /// </summary>
    public Task UpdateEnabledAnalyticsTabsAsync(string folderOrFilePath, IEnumerable<string> enabledTabs)
        => Task.Run(() => UpdateEnabledAnalyticsTabs(folderOrFilePath, enabledTabs));

    /// <summary>
    /// Checks whether the source files on disk have changed since the cache snapshot.
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
    /// Delete all persistent cache snapshot files in the cache directory (%APPDATA%\Macondo\cache\).
    /// Returns the number of cache files deleted.
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
    /// Inspects VB6, C# and Java source files (.cls, .bas, .frm, .ctl, .cs, .csproj, .sln, .vbp, .vbg, .vb, .java, pom.xml, gradle) in the specified directory to calculate
    /// the total number of files and the last UTC modification timestamp, useful for checking the staleness status of the cache.
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
                        f.EndsWith(".vb", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".java", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".kt", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith("pom.xml", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".gradle", StringComparison.OrdinalIgnoreCase))
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
