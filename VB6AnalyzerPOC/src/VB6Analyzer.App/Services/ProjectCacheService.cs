using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using VB6Analyzer.App.Layout;
using VB6Analyzer.App.Models;
using VB6Analyzer.Semantic.Graph;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.App.Services;

/// <summary>
/// Servizio ad alte prestazioni per il caching persistente dei progetti VB6 analizzati (%APPDATA%\VB6Analyzer\cache\).
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
            "VB6Analyzer",
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
    /// Percorso del file di cache associato a una determinata cartella di progetto.
    /// </summary>
    public string GetCacheFilePath(string folderPath)
    {
        var normalized = Path.GetFullPath(folderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hashHex = Convert.ToHexString(hashBytes)[..16];
        return Path.Combine(_cacheDirectory, $"cache_{hashHex}.json");
    }

    /// <summary>
    /// Verifica se esiste una cache su disco per la cartella specificata.
    /// </summary>
    public bool HasCache(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        var path = GetCacheFilePath(folderPath);
        return File.Exists(path);
    }

    /// <summary>
    /// Tenta di caricare lo snapshot di cache per la cartella specificata.
    /// Restituisce null se la cache non esiste o non è valida.
    /// </summary>
    public async Task<ProjectCacheSnapshot?> TryLoadSnapshotAsync(string folderPath)
    {
        try
        {
            var cacheFile = GetCacheFilePath(folderPath);
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
    /// Salva in background uno snapshot completo di cache per la cartella analizzata.
    /// </summary>
    public async Task SaveSnapshotAsync(string folderPath, VisualCodeGraph graph, IReadOnlyList<BlastRadiusGridRow> rows)
    {
        if (graph.SemanticGraph is null || !Directory.Exists(folderPath))
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
                LayoutNodes = graph.Nodes.Select(n => new NodeLayoutSnapshot
                {
                    Id = n.Id,
                    X = n.X,
                    Y = n.Y,
                    IsExpanded = n.IsExpanded
                }).ToList(),
                BlastRadiusRows = rows.Select(r => new BlastRadiusRowSnapshot
                {
                    NodeId = r.NodeId,
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

            var cacheFile = GetCacheFilePath(folderPath);
            var tempFile = cacheFile + ".tmp";

            await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(fs, snapshot, _jsonOptions);
            }

            File.Move(tempFile, cacheFile, overwrite: true);
        }
        catch
        {
            // Il fallimento di una scrittura cache non deve mai bloccare l'interfaccia utente
        }
    }

    /// <summary>
    /// Controlla se i file sorgente su disco sono cambiati rispetto allo snapshot della cache.
    /// </summary>
    public bool CheckStaleness(ProjectCacheSnapshot snapshot, string folderPath)
    {
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

    private static (int FileCount, DateTime LatestWriteUtc) GetFolderSourceStats(string folderPath)
    {
        var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cls", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".frm", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".ctl", StringComparison.OrdinalIgnoreCase))
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
