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
    public string? CsprojPath { get; init; }
    public string? ProjectFilePath => VbpPath ?? CsprojPath;
    public List<string> SourceFiles { get; init; } = new();

    /// <summary>
    /// Ispeziona un insieme di percorsi (cartelle, file .vbp, .vbg, .csproj, .sln o file sorgente)
    /// e restituisce i descrittori di tutti i progetti individuati.
    /// </summary>
    /// <param name="paths">Percorsi di cartelle o file di progetto.</param>
    /// <param name="cancellationToken">Token opzionale di cancellazione.</param>
    public static List<ProjectDescriptor> DiscoverFromPaths(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var descriptors = new List<ProjectDescriptor>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenProjectFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(rawPath)) continue;
            var fullPath = Path.GetFullPath(rawPath);

            if (File.Exists(fullPath))
            {
                var ext = Path.GetExtension(fullPath).ToLowerInvariant();
                if (ext == ".vbg")
                {
                    // Visual Basic Group file
                    var vbgProjects = ParseVbgFile(fullPath, cancellationToken);
                    foreach (var vbgProj in vbgProjects)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (seenFolders.Add(vbgProj.FolderPath))
                            descriptors.Add(vbgProj);
                    }
                }
                else if (ext == ".sln")
                {
                    // Visual Studio Solution file
                    var slnProjects = ParseSlnFile(fullPath, cancellationToken);
                    foreach (var slnProj in slnProjects)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var key = slnProj.ProjectFilePath ?? slnProj.FolderPath;
                        if (seenProjectFiles.Add(key))
                            descriptors.Add(slnProj);
                    }
                }
                else if (ext == ".vbp")
                {
                    if (seenProjectFiles.Add(fullPath))
                    {
                        var proj = ParseVbpFile(fullPath, cancellationToken);
                        descriptors.Add(proj);
                    }
                }
                else if (ext == ".csproj")
                {
                    if (seenProjectFiles.Add(fullPath))
                    {
                        var proj = ParseCsprojFile(fullPath, cancellationToken);
                        descriptors.Add(proj);
                    }
                }
                else if (ext is ".cs" or ".cls" or ".bas" or ".frm" or ".ctl")
                {
                    // File singolo
                    var folder = Path.GetDirectoryName(fullPath) ?? "";
                    var fileName = Path.GetFileNameWithoutExtension(fullPath);
                    descriptors.Add(new ProjectDescriptor
                    {
                        Id = Guid.NewGuid(),
                        Name = fileName,
                        FolderPath = folder,
                        VbpPath = ext != ".cs" ? null : null,
                        CsprojPath = null,
                        SourceFiles = new List<string> { fullPath }
                    });
                }
            }
            else if (Directory.Exists(fullPath))
            {
                if (!seenFolders.Add(fullPath)) continue;

                // 1. Cerca file .sln o .vbg nella cartella principale
                var slnFiles = Directory.GetFiles(fullPath, "*.sln", SearchOption.TopDirectoryOnly);
                if (slnFiles.Length > 0)
                {
                    foreach (var slnFile in slnFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var slnProjects = ParseSlnFile(slnFile, cancellationToken);
                        foreach (var slnProj in slnProjects)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var key = slnProj.ProjectFilePath ?? slnProj.FolderPath;
                            if (seenProjectFiles.Add(key))
                                descriptors.Add(slnProj);
                        }
                    }
                    if (descriptors.Count > 0) continue;
                }

                var vbgFiles = Directory.GetFiles(fullPath, "*.vbg", SearchOption.TopDirectoryOnly);
                if (vbgFiles.Length > 0)
                {
                    foreach (var vbgFile in vbgFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var vbgProjects = ParseVbgFile(vbgFile, cancellationToken);
                        foreach (var vbgProj in vbgProjects)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (seenFolders.Add(vbgProj.FolderPath))
                                descriptors.Add(vbgProj);
                        }
                    }
                    if (descriptors.Count > 0) continue;
                }

                // 2. Cerca file .csproj o .vbp nella cartella o sottocartelle
                cancellationToken.ThrowIfCancellationRequested();
                var csprojFiles = Directory.GetFiles(fullPath, "*.csproj", SearchOption.AllDirectories)
                    .Where(f => !IsInIgnoredFolder(f))
                    .ToArray();

                cancellationToken.ThrowIfCancellationRequested();
                var vbpFiles = Directory.GetFiles(fullPath, "*.vbp", SearchOption.AllDirectories);

                if (csprojFiles.Length > 0 || vbpFiles.Length > 0)
                {
                    foreach (var csprojFile in csprojFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (seenProjectFiles.Add(csprojFile))
                            descriptors.Add(ParseCsprojFile(csprojFile, cancellationToken));
                    }

                    foreach (var vbpFile in vbpFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (seenProjectFiles.Add(vbpFile))
                            descriptors.Add(ParseVbpFile(vbpFile, cancellationToken));
                    }
                }
                else
                {
                    // 3. Nessun file di progetto (.csproj/.vbp/.sln/.vbg): scansione dei file sorgente
                    cancellationToken.ThrowIfCancellationRequested();
                    var subDirs = Directory.GetDirectories(fullPath).Where(d => !IsInIgnoredFolder(d)).ToArray();
                    var foundInSubDirs = false;

                    if (subDirs.Length > 0)
                    {
                        foreach (var subDir in subDirs)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var subFiles = GetAllSourceFilesInDirectory(subDir, cancellationToken);
                            if (subFiles.Count > 0)
                            {
                                foundInSubDirs = true;
                                var subName = Path.GetFileName(subDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                                descriptors.Add(new ProjectDescriptor
                                {
                                    Id = Guid.NewGuid(),
                                    Name = string.IsNullOrEmpty(subName) ? "Source Project" : subName,
                                    FolderPath = subDir,
                                    VbpPath = null,
                                    CsprojPath = null,
                                    SourceFiles = subFiles
                                });
                            }
                        }
                    }

                    if (!foundInSubDirs)
                    {
                        var folderName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        var files = GetAllSourceFilesInDirectory(fullPath, cancellationToken);

                        if (files.Count > 0)
                            descriptors.Add(new ProjectDescriptor
                            {
                                Id = Guid.NewGuid(),
                                Name = string.IsNullOrEmpty(folderName) ? "Source Project" : folderName,
                                FolderPath = fullPath,
                                VbpPath = null,
                                CsprojPath = null,
                                SourceFiles = files
                            });
                    }
                }
            }
        }

        return descriptors;
    }

    private static List<string> GetAllSourceFilesInDirectory(string dir, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(dir)) return new List<string>();

        cancellationToken.ThrowIfCancellationRequested();
        return Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => !IsInIgnoredFolder(f) &&
                        (f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                         f.EndsWith(".cls", StringComparison.OrdinalIgnoreCase) ||
                         f.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                         f.EndsWith(".frm", StringComparison.OrdinalIgnoreCase) ||
                         f.EndsWith(".ctl", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => Path.GetFileName(f))
            .ToList();
    }

    private static bool IsInIgnoredFolder(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                              p.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                              p.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                              p.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                              p.Equals(".idea", StringComparison.OrdinalIgnoreCase) ||
                              p.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    public static ProjectDescriptor ParseCsprojFile(string csprojPath, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(csprojPath) ?? "";
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(csprojPath);
        var projName = fileNameWithoutExt;

        cancellationToken.ThrowIfCancellationRequested();
        var sourceFiles = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsInIgnoredFolder(f))
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        return new ProjectDescriptor
        {
            Id = Guid.NewGuid(),
            Name = projName,
            FolderPath = folder,
            CsprojPath = csprojPath,
            SourceFiles = sourceFiles
        };
    }

    public static List<ProjectDescriptor> ParseSlnFile(string slnPath, CancellationToken cancellationToken = default)
    {
        var result = new List<ProjectDescriptor>();
        var slnFolder = Path.GetDirectoryName(slnPath) ?? "";

        if (File.Exists(slnPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = File.ReadAllLines(slnPath);
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Project(", StringComparison.OrdinalIgnoreCase))
                {
                    // Esempio: Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MyApp", "src\MyApp\MyApp.csproj", "{...}"
                    var eqIdx = trimmed.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        var right = trimmed[(eqIdx + 1)..].Trim();
                        var parts = right.Split(',');
                        if (parts.Length >= 2)
                        {
                            var relPath = parts[1].Trim(' ', '"', '\'');
                            var absPath = Path.IsPathRooted(relPath) ? relPath : Path.GetFullPath(Path.Combine(slnFolder, relPath));
                            if (File.Exists(absPath))
                            {
                                var ext = Path.GetExtension(absPath).ToLowerInvariant();
                                if (ext == ".csproj")
                                    result.Add(ParseCsprojFile(absPath, cancellationToken));
                                else if (ext == ".vbp")
                                    result.Add(ParseVbpFile(absPath, cancellationToken));
                            }
                        }
                    }
                }
            }
        }

        return result;
    }

    private static ProjectDescriptor ParseVbpFile(string vbpPath, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(vbpPath) ?? "";
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(vbpPath);
        var projName = fileNameWithoutExt;
        var sourceFiles = new List<string>();

        if (File.Exists(vbpPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = File.ReadAllLines(vbpPath);
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
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

    private static List<ProjectDescriptor> ParseVbgFile(string vbgPath, CancellationToken cancellationToken = default)
    {
        var result = new List<ProjectDescriptor>();
        var vbgFolder = Path.GetDirectoryName(vbgPath) ?? "";

        if (File.Exists(vbgPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = File.ReadAllLines(vbgPath);
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                            result.Add(ParseVbpFile(absVbp, cancellationToken));
                    }
                }
            }
        }

        return result;
    }
}
