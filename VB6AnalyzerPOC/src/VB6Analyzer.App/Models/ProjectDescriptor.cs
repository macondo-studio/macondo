namespace Macondo.App.Models;

/// <summary>
/// Descrittore di un progetto VB6 all'interno di un Workspace (singolo o multi-progetto).
/// </summary>
public sealed class ProjectDescriptor
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string FolderPath { get; init; }
    public string? VbpPath { get; init; }
    public List<string> SourceFiles { get; init; } = new();

    /// <summary>
    /// Ispeziona un insieme di percorsi (cartelle, file .vbp o file .vbg) e restituisce
    /// i descrittori di tutti i progetti VB6 individuati.
    /// </summary>
    public static List<ProjectDescriptor> DiscoverFromPaths(IEnumerable<string> paths)
    {
        var descriptors = new List<ProjectDescriptor>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in paths)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) continue;
            var fullPath = Path.GetFullPath(rawPath);

            if (File.Exists(fullPath))
            {
                var ext = Path.GetExtension(fullPath).ToLowerInvariant();
                if (ext == ".vbg")
                {
                    // Visual Basic Group file
                    var vbgProjects = ParseVbgFile(fullPath);
                    foreach (var vbgProj in vbgProjects)
                    {
                        if (seenFolders.Add(vbgProj.FolderPath))
                            descriptors.Add(vbgProj);
                    }
                }
                else if (ext == ".vbp")
                {
                    var proj = ParseVbpFile(fullPath);
                    if (seenFolders.Add(proj.FolderPath))
                        descriptors.Add(proj);
                }
            }
            else if (Directory.Exists(fullPath))
            {
                if (!seenFolders.Add(fullPath)) continue;

                // 1. Cerca file .vbg (Visual Basic Group) nella cartella
                var vbgFiles = Directory.GetFiles(fullPath, "*.vbg", SearchOption.TopDirectoryOnly);
                if (vbgFiles.Length > 0)
                {
                    foreach (var vbgFile in vbgFiles)
                    {
                        var vbgProjects = ParseVbgFile(vbgFile);
                        foreach (var vbgProj in vbgProjects)
                        {
                            if (seenFolders.Add(vbgProj.FolderPath))
                                descriptors.Add(vbgProj);
                        }
                    }
                    continue;
                }

                // 2. Cerca file .vbp nella cartella o sottocartelle
                var vbpFiles = Directory.GetFiles(fullPath, "*.vbp", SearchOption.AllDirectories);
                if (vbpFiles.Length > 0)
                {
                    foreach (var vbpFile in vbpFiles)
                    {
                        var parsed = ParseVbpFile(vbpFile);
                        if (seenFolders.Add(vbpFile))
                        {
                            descriptors.Add(parsed);
                        }
                    }
                }
                else
                {
                    // 3. Nessun file .vbp/.vbg: verifica se ci sono sottocartelle con file VB6
                    var subDirs = Directory.GetDirectories(fullPath);
                    var foundInSubDirs = false;

                    if (subDirs.Length > 0)
                    {
                        foreach (var subDir in subDirs)
                        {
                            var subFiles = Directory.GetFiles(subDir, "*.*", SearchOption.AllDirectories)
                                .Where(f => f.EndsWith(".cls", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".frm", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".ctl", StringComparison.OrdinalIgnoreCase))
                                .OrderBy(f => Path.GetFileName(f))
                                .ToList();

                            if (subFiles.Count > 0)
                            {
                                foundInSubDirs = true;
                                var subName = Path.GetFileName(subDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                                descriptors.Add(new ProjectDescriptor
                                {
                                    Id = Guid.NewGuid(),
                                    Name = string.IsNullOrEmpty(subName) ? "VB6 Project" : subName,
                                    FolderPath = subDir,
                                    VbpPath = null,
                                    SourceFiles = subFiles
                                });
                            }
                        }
                    }

                    if (!foundInSubDirs)
                    {
                        var folderName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        var files = Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories)
                            .Where(f => f.EndsWith(".cls", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".frm", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".ctl", StringComparison.OrdinalIgnoreCase))
                            .OrderBy(f => Path.GetFileName(f))
                            .ToList();

                        descriptors.Add(new ProjectDescriptor
                        {
                            Id = Guid.NewGuid(),
                            Name = string.IsNullOrEmpty(folderName) ? "VB6 Project" : folderName,
                            FolderPath = fullPath,
                            VbpPath = null,
                            SourceFiles = files
                        });
                    }
                }
            }
        }

        return descriptors;
    }

    private static ProjectDescriptor ParseVbpFile(string vbpPath)
    {
        var folder = Path.GetDirectoryName(vbpPath) ?? "";
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(vbpPath);
        var projName = fileNameWithoutExt;
        var sourceFiles = new List<string>();

        if (File.Exists(vbpPath))
        {
            var lines = File.ReadAllLines(vbpPath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = trimmed[5..].Trim('"', '\'', ' ');
                    if (!string.IsNullOrWhiteSpace(val))
                        projName = val;
                }
                else if (trimmed.StartsWith("Class=", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.StartsWith("Module=", StringComparison.OrdinalIgnoreCase))
                {
                    // Class=ClassName; RelativePath.cls o Module=ModuleName; RelativePath.bas
                    var semiIdx = trimmed.IndexOf(';');
                    if (semiIdx >= 0)
                    {
                        var relPath = trimmed[(semiIdx + 1)..].Trim();
                        var abs = Path.IsPathRooted(relPath) ? relPath : Path.Combine(folder, relPath);
                        if (File.Exists(abs)) sourceFiles.Add(abs);
                    }
                }
                else if (trimmed.StartsWith("Form=", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.StartsWith("UserControl=", StringComparison.OrdinalIgnoreCase))
                {
                    var eqIdx = trimmed.IndexOf('=');
                    if (eqIdx >= 0)
                    {
                        var relPath = trimmed[(eqIdx + 1)..].Trim();
                        var abs = Path.IsPathRooted(relPath) ? relPath : Path.Combine(folder, relPath);
                        if (File.Exists(abs)) sourceFiles.Add(abs);
                    }
                }
            }
        }

        // Se nessun file è stato trovato specificamente dal .vbp, effettua scansione cartella
        if (sourceFiles.Count == 0 && Directory.Exists(folder))
        {
            sourceFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".cls", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".frm", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".ctl", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f))
                .ToList();
        }

        return new ProjectDescriptor
        {
            Id = Guid.NewGuid(),
            Name = projName,
            FolderPath = folder,
            VbpPath = vbpPath,
            SourceFiles = sourceFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static List<ProjectDescriptor> ParseVbgFile(string vbgPath)
    {
        var result = new List<ProjectDescriptor>();
        var vbgFolder = Path.GetDirectoryName(vbgPath) ?? "";

        if (File.Exists(vbgPath))
        {
            var lines = File.ReadAllLines(vbgPath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("StartupProject=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Project=", StringComparison.OrdinalIgnoreCase))
                {
                    var eqIdx = trimmed.IndexOf('=');
                    if (eqIdx >= 0)
                    {
                        var relVbp = trimmed[(eqIdx + 1)..].Trim();
                        var absVbp = Path.IsPathRooted(relVbp) ? relVbp : Path.Combine(vbgFolder, relVbp);
                        if (File.Exists(absVbp))
                        {
                            result.Add(ParseVbpFile(absVbp));
                        }
                    }
                }
            }
        }

        return result;
    }
}
