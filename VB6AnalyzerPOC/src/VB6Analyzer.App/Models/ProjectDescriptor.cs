namespace Macondo.App.Models;

/// <summary>
/// Descrittore di un progetto (VB6, C# o Java) all'interno di un Workspace (singolo o multi-progetto).
/// </summary>
public sealed class ProjectDescriptor
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string FolderPath { get; init; }
    public string? VbpPath { get; init; }
    public string? CsprojPath { get; init; }
    public string? PomXmlPath { get; init; }
    public string? GradlePath { get; init; }
    public string? ProjectFilePath => VbpPath ?? CsprojPath ?? PomXmlPath ?? GradlePath;
    public List<string> SourceFiles { get; init; } = new();

    /// <summary>
    /// Ispeziona un insieme di percorsi (cartelle, file .vbp, .vbg, .csproj, .sln, pom.xml, build.gradle o file sorgente)
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
                var fileName = Path.GetFileName(fullPath).ToLowerInvariant();
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
                else if (fileName == "pom.xml")
                {
                    if (seenProjectFiles.Add(fullPath))
                    {
                        var proj = ParsePomXmlFile(fullPath, cancellationToken);
                        descriptors.Add(proj);
                    }
                }
                else if (fileName is "build.gradle" or "build.gradle.kts")
                {
                    if (seenProjectFiles.Add(fullPath))
                    {
                        var proj = ParseGradleFile(fullPath, cancellationToken);
                        descriptors.Add(proj);
                    }
                }
                else if (ext is ".java" or ".cs" or ".cls" or ".bas" or ".frm" or ".ctl")
                {
                    // File singolo
                    var folder = Path.GetDirectoryName(fullPath) ?? "";
                    var baseName = Path.GetFileNameWithoutExtension(fullPath);
                    descriptors.Add(new ProjectDescriptor
                    {
                        Id = Guid.NewGuid(),
                        Name = baseName,
                        FolderPath = folder,
                        VbpPath = null,
                        CsprojPath = null,
                        PomXmlPath = null,
                        GradlePath = null,
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

                // 2. Cerca file .csproj, .vbp, pom.xml o build.gradle nella cartella o sottocartelle
                cancellationToken.ThrowIfCancellationRequested();
                var csprojFiles = Directory.GetFiles(fullPath, "*.csproj", SearchOption.AllDirectories)
                    .Where(f => !IsInIgnoredFolder(f))
                    .ToArray();

                cancellationToken.ThrowIfCancellationRequested();
                var vbpFiles = Directory.GetFiles(fullPath, "*.vbp", SearchOption.AllDirectories);

                cancellationToken.ThrowIfCancellationRequested();
                var pomFiles = Directory.GetFiles(fullPath, "pom.xml", SearchOption.AllDirectories)
                    .Where(f => !IsInIgnoredFolder(f))
                    .ToArray();

                cancellationToken.ThrowIfCancellationRequested();
                var gradleFiles = Directory.GetFiles(fullPath, "build.gradle*", SearchOption.AllDirectories)
                    .Where(f => !IsInIgnoredFolder(f) && (f.EndsWith("build.gradle", StringComparison.OrdinalIgnoreCase) || f.EndsWith("build.gradle.kts", StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                if (csprojFiles.Length > 0 || vbpFiles.Length > 0 || pomFiles.Length > 0 || gradleFiles.Length > 0)
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

                    // 2a. Gestione avanzata Maven Multi-Modulo:
                    // Raccoglie preventivamente l'elenco di tutte le cartelle contenenti un file pom.xml
                    // per evitare che un modulo root (o padre) includa ricorsivamente i file dei sottomoduli figli.
                    if (pomFiles.Length > 0)
                    {
                        var pomFolders = pomFiles
                            .Select(Path.GetDirectoryName)
                            .Where(d => !string.IsNullOrEmpty(d))
                            .Cast<string>()
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        foreach (var pomFile in pomFiles)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (seenProjectFiles.Add(pomFile))
                            {
                                var folder = Path.GetDirectoryName(pomFile) ?? "";
                                var normalizedFolder = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                                // Isola le cartelle dei sottomoduli che risiedono fisicamente all'interno della cartella di questo pom.xml
                                var childFolders = pomFolders
                                    .Where(d => !string.Equals(d, folder, StringComparison.OrdinalIgnoreCase) && d.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                                descriptors.Add(ParsePomXmlFile(pomFile, childFolders, cancellationToken));
                            }
                        }
                    }

                    // 2b. Gestione avanzata Gradle Multi-Project (es. Android / Robolectric / Spring Multi-Module):
                    // Identifica tutte le directory contenenti build.gradle o build.gradle.kts per circoscrivere
                    // l'assegnazione dei file Java al solo modulo proprietario diretto, prevenendo la duplicazione a catena.
                    if (gradleFiles.Length > 0)
                    {
                        var gradleFolders = gradleFiles
                            .Select(Path.GetDirectoryName)
                            .Where(d => !string.IsNullOrEmpty(d))
                            .Cast<string>()
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        foreach (var gradleFile in gradleFiles)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (seenProjectFiles.Add(gradleFile))
                            {
                                var folder = Path.GetDirectoryName(gradleFile) ?? "";
                                var normalizedFolder = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                                // Individua tutte le sottocartelle interne che ospitano un sottomodulo Gradle figlio
                                var childFolders = gradleFolders
                                    .Where(d => !string.Equals(d, folder, StringComparison.OrdinalIgnoreCase) && d.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                                descriptors.Add(ParseGradleFile(gradleFile, childFolders, cancellationToken));
                            }
                        }
                    }

                    // 2c. Pulizia moduli aggregatori privi di sorgenti:
                    // Se sono stati scoperti più progetti nel workspace e almeno uno contiene file sorgente effettivi,
                    // scarta i progetti radice puramente configurativi/aggregatori (es. root build.gradle senza codice Java proprio)
                    // evitando di generare progetti vuoti o nodi orfani nel grafo visuale.
                    if (descriptors.Count > 1 && descriptors.Any(d => d.SourceFiles.Count > 0))
                        descriptors.RemoveAll(d => d.SourceFiles.Count == 0);
                }
                else
                {
                    // 3. Nessun file di progetto: scansione dei file sorgente (VB6, C#, Java)
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
                                    PomXmlPath = null,
                                    GradlePath = null,
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
                                PomXmlPath = null,
                                GradlePath = null,
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
                        (f.EndsWith(".java", StringComparison.OrdinalIgnoreCase) ||
                         f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
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
                              p.Equals("target", StringComparison.OrdinalIgnoreCase) ||
                              p.Equals(".gradle", StringComparison.OrdinalIgnoreCase) ||
                              p.Equals(".mvn", StringComparison.OrdinalIgnoreCase) ||
                              p.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                              p.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                              p.Equals(".idea", StringComparison.OrdinalIgnoreCase) ||
                              p.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifica se il file sorgente specificato appartiene a uno dei sottomoduli annidati censiti.
    /// In tal caso, il file deve essere escluso dal modulo genitore per garantire che ogni sorgente
    /// venga attribuito esclusivamente e univocamente al proprio sottomodulo proprietario.
    /// </summary>
    /// <param name="filePath">Percorso assoluto del file sorgente da verificare.</param>
    /// <param name="childProjectFolders">Insieme delle cartelle assolute dei sottomoduli annidati.</param>
    /// <returns><c>true</c> se il file risiede all'interno di una cartella di un sottomodulo; altrimenti <c>false</c>.</returns>
    private static bool IsInChildProjectFolder(string filePath, IEnumerable<string>? childProjectFolders)
    {
        if (childProjectFolders == null)
            return false;

        foreach (var childFolder in childProjectFolders)
        {
            if (string.IsNullOrEmpty(childFolder)) continue;
            var normalizedChild = childFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (filePath.StartsWith(normalizedChild, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Esegue il parsing di un file di configurazione Maven ('pom.xml') e individua tutti i file sorgente Java (.java) associati.
    /// Rileva automaticamente eventuali sottomoduli Maven annidati per escluderne i file sorgente ed evitare duplicazioni.
    /// </summary>
    public static ProjectDescriptor ParsePomXmlFile(string pomXmlPath, CancellationToken cancellationToken = default)
        => ParsePomXmlFile(pomXmlPath, childProjectFolders: null, cancellationToken);

    /// <summary>
    /// Esegue il parsing di un file di configurazione Maven ('pom.xml') e individua tutti i file sorgente Java (.java) associati.
    /// Estrae il nome del progetto da &lt;artifactId&gt; o &lt;name&gt;, oppure ricorre al nome della cartella genitrice come fallback.
    /// Se specificato l'elenco dei sottomoduli, esclude ricorsivamente i file di questi ultimi.
    /// </summary>
    /// <param name="pomXmlPath">Percorso assoluto del file pom.xml.</param>
    /// <param name="childProjectFolders">Cartelle opzionali di sottomoduli annidati i cui file sorgente non devono essere inclusi nel modulo corrente.</param>
    /// <param name="cancellationToken">Token opzionale di cancellazione per interrompere la scansione.</param>
    /// <returns>Il descrittore del progetto Maven popolato con l'elenco dei file sorgente Java.</returns>
    public static ProjectDescriptor ParsePomXmlFile(
        string pomXmlPath,
        IReadOnlyCollection<string>? childProjectFolders,
        CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(pomXmlPath) ?? "";
        var projName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // Prova a leggere <artifactId> o <name> dal file pom.xml
        try
        {
            if (File.Exists(pomXmlPath))
            {
                var content = File.ReadAllText(pomXmlPath);
                var artMatch = System.Text.RegularExpressions.Regex.Match(content, @"<artifactId>\s*([^<\r\n]+)\s*</artifactId>");
                if (artMatch.Success && !string.IsNullOrWhiteSpace(artMatch.Groups[1].Value))
                    projName = artMatch.Groups[1].Value.Trim();
            }
        }
        catch
        {
            // Ignora errori di parsing del pom.xml ed usa il nome della cartella come fallback resiliente
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Se non specificato esplicitamente, individua in autonomia eventuali sottomoduli Maven annidati nella cartella
        if (childProjectFolders == null && Directory.Exists(folder))
        {
            childProjectFolders = Directory.GetFiles(folder, "pom.xml", SearchOption.AllDirectories)
                .Where(f => !IsInIgnoredFolder(f) && !string.Equals(f, pomXmlPath, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetDirectoryName)
                .Where(d => !string.IsNullOrEmpty(d))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Recupera solo i file .java appartenenti al modulo corrente, escludendo cartelle ignorate e sottomoduli
        var sourceFiles = Directory.GetFiles(folder, "*.java", SearchOption.AllDirectories)
            .Where(f => !IsInIgnoredFolder(f) && !IsInChildProjectFolder(f, childProjectFolders))
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        return new ProjectDescriptor
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrEmpty(projName) ? "Java Maven Project" : projName,
            FolderPath = folder,
            PomXmlPath = pomXmlPath,
            SourceFiles = sourceFiles
        };
    }

    /// <summary>
    /// Esegue il parsing di uno script di build Gradle ('build.gradle' o 'build.gradle.kts') e raccoglie tutti i file .java.
    /// Rileva automaticamente eventuali sottomoduli Gradle annidati per escluderne i file sorgente ed evitare duplicazioni.
    /// </summary>
    public static ProjectDescriptor ParseGradleFile(string gradlePath, CancellationToken cancellationToken = default)
        => ParseGradleFile(gradlePath, childProjectFolders: null, cancellationToken);

    /// <summary>
    /// Esegue il parsing di uno script di build Gradle ('build.gradle' o 'build.gradle.kts') e raccoglie tutti i file .java.
    /// Utilizza il nome della directory del modulo come nome del progetto.
    /// Se specificato l'elenco dei sottomoduli, esclude ricorsivamente i file di questi ultimi.
    /// </summary>
    /// <param name="gradlePath">Percorso assoluto del file build.gradle o build.gradle.kts.</param>
    /// <param name="childProjectFolders">Cartelle opzionali di sottomoduli annidati i cui file sorgente non devono essere inclusi nel modulo corrente.</param>
    /// <param name="cancellationToken">Token opzionale di cancellazione.</param>
    /// <returns>Il descrittore del progetto Gradle con l'elenco completo dei file sorgente Java trovati.</returns>
    public static ProjectDescriptor ParseGradleFile(
        string gradlePath,
        IReadOnlyCollection<string>? childProjectFolders,
        CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(gradlePath) ?? "";
        var projName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        cancellationToken.ThrowIfCancellationRequested();

        // Se non specificato esplicitamente, individua in autonomia eventuali sottomoduli Gradle annidati nella cartella
        if (childProjectFolders == null && Directory.Exists(folder))
        {
            childProjectFolders = Directory.GetFiles(folder, "build.gradle*", SearchOption.AllDirectories)
                .Where(f => !IsInIgnoredFolder(f) &&
                            !string.Equals(f, gradlePath, StringComparison.OrdinalIgnoreCase) &&
                            (f.EndsWith("build.gradle", StringComparison.OrdinalIgnoreCase) || f.EndsWith("build.gradle.kts", StringComparison.OrdinalIgnoreCase)))
                .Select(Path.GetDirectoryName)
                .Where(d => !string.IsNullOrEmpty(d))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Recupera solo i file .java appartenenti al modulo corrente, escludendo cartelle ignorate e sottomoduli
        var sourceFiles = Directory.GetFiles(folder, "*.java", SearchOption.AllDirectories)
            .Where(f => !IsInIgnoredFolder(f) && !IsInChildProjectFolder(f, childProjectFolders))
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        return new ProjectDescriptor
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrEmpty(projName) ? "Java Gradle Project" : projName,
            FolderPath = folder,
            GradlePath = gradlePath,
            SourceFiles = sourceFiles
        };
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
