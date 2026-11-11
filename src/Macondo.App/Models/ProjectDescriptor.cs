namespace Macondo.App.Models;

/// <summary>
/// Descriptor of a project (VB6, C# or Java) within a Workspace (single or multi-project).
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
    /// Inspect a set of paths (folders, .vbp, .vbg, .csproj, .sln, pom.xml, build.gradle, or source files)
    /// and returns the descriptors of all the identified projects.
    /// </summary>
    /// <param name="paths">Paths to folders or project files.</param>
    /// <param name="cancellationToken">Optional deletion token.</param>
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
                else if (fileName is "build.gradle" or "build.gradle.kts" or "settings.gradle" or "settings.gradle.kts")
                {
                    if (seenProjectFiles.Add(fullPath))
                    {
                        var proj = ParseGradleFile(fullPath, cancellationToken);
                        descriptors.Add(proj);
                    }
                }
                else if (ext is ".java" or ".cs" or ".cls" or ".bas" or ".frm" or ".ctl" || (ext == ".kt" || (ext == ".kts" && !fileName.EndsWith(".gradle.kts") && fileName != "settings.gradle.kts")))
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

                // 1. Search for .sln or .vbg files in the root folder
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

                // 2. Search for .csproj, .vbp, pom.xml or build.gradle files in the folder or subfolders
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
                var gradleSettingsFiles = Directory.GetFiles(fullPath, "settings.gradle*", SearchOption.AllDirectories)
                    .Where(f => !IsInIgnoredFolder(f) && (f.EndsWith("settings.gradle", StringComparison.OrdinalIgnoreCase) || f.EndsWith("settings.gradle.kts", StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                cancellationToken.ThrowIfCancellationRequested();
                var gradleBuildFiles = Directory.GetFiles(fullPath, "build.gradle*", SearchOption.AllDirectories)
                    .Where(f => !IsInIgnoredFolder(f) && (f.EndsWith("build.gradle", StringComparison.OrdinalIgnoreCase) || f.EndsWith("build.gradle.kts", StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                if (csprojFiles.Length > 0 || vbpFiles.Length > 0 || pomFiles.Length > 0 || gradleSettingsFiles.Length > 0 || gradleBuildFiles.Length > 0)
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

                    // 2a. Advanced Maven Multi-Module Management:
                    // Collects the list of all folders containing a pom.xml file in advance
                    // to prevent a root (or parent) module from recursively including child submodule files.
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

                                // Isolates submodule folders that physically reside within this pom.xml folder
                                var childFolders = pomFolders
                                    .Where(d => !string.Equals(d, folder, StringComparison.OrdinalIgnoreCase) && d.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                                descriptors.Add(ParsePomXmlFile(pomFile, childFolders, cancellationToken));
                            }
                        }
                    }

                    // 2b. Advanced Gradle Multi-Project Management (e.g. Android / Kotlin / Spring / Robolectric):
                    // Deterministically identifies the root directories of Gradle projects to preserve the real architecture:
                    // 1) In standard or multi-module Android projects, 'settings.gradle' or 'settings.gradle.kts' resides in
                    // main parent folder (e.g. 'BroadcastApp'), while the application code resides conventionally
                    // in the 'app/' submodule (which includes your own 'build.gradle[.kts]').
                    // 2) By assigning the entire subfolder hierarchy to the owning root, you ensure that all sources
                    // (.kt, .java) appear grouped under the respective application project in the Project Explorer,
                    // avoiding the proliferation of orphaned or duplicate projects generically called 'apps'.
                    if (gradleSettingsFiles.Length > 0 || gradleBuildFiles.Length > 0)
                    {
                        var gradleProjectRoots = new List<string>();

                        // Step 1: Census of official roots with settings file 'settings.gradle' or 'settings.gradle.kts'
                        var settingsDirs = gradleSettingsFiles
                            .Select(Path.GetDirectoryName)
                            .Where(d => !string.IsNullOrEmpty(d))
                            .Cast<string>()
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(d => d.Length) // Priorità alle directory di livello gerarchico superiore
                            .ToList();

                        foreach (var sDir in settingsDirs)
                        {
                            var normalized = sDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                            if (!gradleProjectRoots.Any(r => normalized.StartsWith(r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                                gradleProjectRoots.Add(sDir);
                        }

                        // Phase 2: Census of additional standalone Gradle projects (with 'build.gradle[.kts]' but without 'settings.gradle[.kts]')
                        // that are not already part of a root hierarchy surveyed in Phase 1
                        var buildDirs = gradleBuildFiles
                            .Select(Path.GetDirectoryName)
                            .Where(d => !string.IsNullOrEmpty(d))
                            .Cast<string>()
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(d => d.Length)
                            .ToList();

                        foreach (var bDir in buildDirs)
                        {
                            var normalized = bDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                            if (!gradleProjectRoots.Any(r => normalized.StartsWith(r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                                gradleProjectRoots.Add(bDir);
                        }

                        // Phase 3: Creation of project descriptors with complete aggregation of the related sources
                        foreach (var gRoot in gradleProjectRoots)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var gradleKey = "gradle:" + gRoot;
                            if (seenProjectFiles.Add(gradleKey))
                            {
                                var proj = ParseGradleProjectFolder(gRoot, cancellationToken);
                                if (proj.SourceFiles.Count > 0 || gradleProjectRoots.Count == 1)
                                    descriptors.Add(proj);
                            }
                        }
                    }

                    // 2c. Cleaning aggregator modules without sources:
                    // If multiple projects have been discovered in the workspace and at least one contains actual source files,
                    // discard purely configurative/aggregatory root projects (e.g. root build.gradle without its own Java/Kotlin code)
                    // avoiding generating empty projects or orphan nodes in the visual graph.
                    if (descriptors.Count > 1 && descriptors.Any(d => d.SourceFiles.Count > 0))
                        descriptors.RemoveAll(d => d.SourceFiles.Count == 0);
                }
                else
                {
                    // 3. No project files: scan source files (VB6, C#, Java)
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
        if (!Directory.Exists(dir))
            return new List<string>();

        cancellationToken.ThrowIfCancellationRequested();
        return Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => !IsInIgnoredFolder(f) && IsValidSourceFile(f))
            .OrderBy(f => Path.GetFileName(f))
            .ToList();
    }

    /// <summary>
    /// Tests whether a file is a valid source of parsable application code (VB6, C#, Java, Kotlin).
    /// Explicitly exclude configuration files or build scripts such as 'build.gradle.kts' or 'settings.gradle.kts'.
    /// </summary>
    private static bool IsValidSourceFile(string f)
    {
        if (f.EndsWith(".java", StringComparison.OrdinalIgnoreCase))
            return true;
        if (f.EndsWith(".kt", StringComparison.OrdinalIgnoreCase))
            return true;
        if (f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return true;
        if (f.EndsWith(".cls", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".frm", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".ctl", StringComparison.OrdinalIgnoreCase))
            return true;
        if (f.EndsWith(".kts", StringComparison.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(f);
            if (name.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("settings.gradle.kts", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".gradle.kts", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
        return false;
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
    /// Checks whether the specified source file belongs to one of the nested submodules surveyed.
    /// In that case, the file must be excluded from the parent module to ensure that every source
    /// is attributed exclusively and univocally to its own proprietary submodule.
    /// </summary>
    /// <param name="filePath">Absolute path of the source file to verify.</param>
    /// <param name="childProjectFolders">Set of absolute folders of nested submodules.</param>
    /// <returns><c>true</c> if the file resides inside a submodule folder; otherwise <c>false</c>.</returns>
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
    /// Parses a Maven configuration file ('pom.xml') and locates any associated Java source (.java) files.
    /// Automatically detects any nested Maven submodules to exclude their source files and avoid duplication.
    /// </summary>
    public static ProjectDescriptor ParsePomXmlFile(string pomXmlPath, CancellationToken cancellationToken = default)
        => ParsePomXmlFile(pomXmlPath, childProjectFolders: null, cancellationToken);

    /// <summary>
    /// Parses a Maven configuration file ('pom.xml') and locates any associated Java source (.java) files.
    /// Extracts the project name from <artifactId> or <name>, or uses the parent folder name as a fallback.
    /// If the list of submodules is specified, it recursively excludes the files of the latter.
    /// </summary>
    /// <param name="pomXmlPath">Absolute path to the pom.xml file.</param>
    /// <param name="childProjectFolders">Optional folders of nested submodules whose source files should not be included in the current module.</param>
    /// <param name="cancellationToken">Optional delete token to stop scanning.</param>
    /// <returns>The Maven project descriptor populated with the list of Java source files.</returns>
    public static ProjectDescriptor ParsePomXmlFile(
        string pomXmlPath,
        IReadOnlyCollection<string>? childProjectFolders,
        CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(pomXmlPath) ?? "";
        var projName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // Try reading <artifactId> or <name> from the pom.xml file
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
            // Ignore pom.xml parsing errors and use the folder name as a resilient fallback
        }

        cancellationToken.ThrowIfCancellationRequested();

        // If not explicitly specified, it independently identifies any Maven submodules nested in the folder
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

        // Recovers Java and Kotlin source files (.java, .kt, .kts) belonging to the current module, excluding ignored folders and submodules
        var sourceFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => !IsInIgnoredFolder(f) &&
                        !IsInChildProjectFolder(f, childProjectFolders) &&
                        IsValidSourceFile(f))
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        var defaultName = sourceFiles.Any(f => f.EndsWith(".kt", StringComparison.OrdinalIgnoreCase)) ? "Kotlin Maven Project" : "Java Maven Project";

        return new ProjectDescriptor
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrEmpty(projName) ? defaultName : projName,
            FolderPath = folder,
            PomXmlPath = pomXmlPath,
            SourceFiles = sourceFiles
        };
    }

    /// <summary>
    /// Parses a Gradle build script ('build.gradle', 'build.gradle.kts', 'settings.gradle', or 'settings.gradle.kts')
    /// and collects all associated Java and Kotlin source files.
    /// Automatically resolves the project root if invoked within a standard module such as 'app'.
    /// </summary>
    public static ProjectDescriptor ParseGradleFile(string gradlePath, CancellationToken cancellationToken = default)
        => ParseGradleFile(gradlePath, childProjectFolders: null, cancellationToken);

    /// <summary>
    /// Parses a Gradle build script ('build.gradle' or 'build.gradle.kts') and collects all Java and Kotlin files.
    /// If the list of submodules is specified, it recursively excludes the files of the latter.
    /// </summary>
    /// <param name="gradlePath">Absolute path to the build.gradle or build.gradle.kts file.</param>
    /// <param name="childProjectFolders">Optional folders of nested submodules whose source files should not be included in the current module.</param>
    /// <param name="cancellationToken">Optional deletion token.</param>
    /// <returns>The Gradle project descriptor with the complete list of source files found.</returns>
    public static ProjectDescriptor ParseGradleFile(
        string gradlePath,
        IReadOnlyCollection<string>? childProjectFolders,
        CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(gradlePath) ?? "";

        // If the given folder is 'app' and the parent contains settings.gradle or build.gradle,
        // resolves directly to the root of the parent project
        if (string.Equals(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "app", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(folder);
            if (!string.IsNullOrEmpty(parent))
            {
                var parentSettings = Directory.GetFiles(parent, "settings.gradle*", SearchOption.TopDirectoryOnly);
                var parentBuild = Directory.GetFiles(parent, "build.gradle*", SearchOption.TopDirectoryOnly);
                if (parentSettings.Length > 0 || parentBuild.Length > 0)
                    folder = parent;
            }
        }

        if (childProjectFolders != null && childProjectFolders.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var sourceFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => !IsInIgnoredFolder(f) &&
                            !IsInChildProjectFolder(f, childProjectFolders) &&
                            IsValidSourceFile(f))
                .OrderBy(f => Path.GetFileName(f))
                .ToList();

            var defaultName = sourceFiles.Any(f => f.EndsWith(".kt", StringComparison.OrdinalIgnoreCase)) ? "Kotlin Gradle Project" : "Java Gradle Project";

            return new ProjectDescriptor
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrEmpty(projName) ? defaultName : projName,
                FolderPath = folder,
                GradlePath = gradlePath,
                SourceFiles = sourceFiles
            };
        }

        return ParseGradleProjectFolder(folder, cancellationToken);
    }

    /// <summary>
    /// Parses a Gradle project root directory (containing settings.gradle[.kts] or build.gradle[.kts]),
    /// detecting the actual project name and including all Java and Kotlin sources (including internal submodules like 'app').
    /// </summary>
    /// <param name="folder">Absolute path to the root folder of the Gradle project.</param>
    /// <param name="cancellationToken">Optional delete token to stop analysis.</param>
    /// <returns>The Gradle project descriptor configured with all source files belonging to the application hierarchy.</returns>
    public static ProjectDescriptor ParseGradleProjectFolder(string folder, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Locate the main configuration file
        string? gradlePath = null;
        var candBuildKts = Path.Combine(folder, "build.gradle.kts");
        var candBuildGroovy = Path.Combine(folder, "build.gradle");
        var candSettingsKts = Path.Combine(folder, "settings.gradle.kts");
        var candSettingsGroovy = Path.Combine(folder, "settings.gradle");

        if (File.Exists(candBuildKts))
            gradlePath = candBuildKts;
        else if (File.Exists(candBuildGroovy))
            gradlePath = candBuildGroovy;
        else if (File.Exists(candSettingsKts))
            gradlePath = candSettingsKts;
        else if (File.Exists(candSettingsGroovy))
            gradlePath = candSettingsGroovy;
        else
        {
            var subBuild = Directory.GetFiles(folder, "build.gradle*", SearchOption.AllDirectories)
                .FirstOrDefault(f => !IsInIgnoredFolder(f) && (f.EndsWith("build.gradle", StringComparison.OrdinalIgnoreCase) || f.EndsWith("build.gradle.kts", StringComparison.OrdinalIgnoreCase)));
            gradlePath = subBuild;
        }

        var folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var projName = folderName;

        // If the folder is generically "app", try reading the name of the parent folder
        if (string.Equals(projName, "app", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(folder);
            if (!string.IsNullOrEmpty(parent))
            {
                var parentName = Path.GetFileName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrEmpty(parentName))
                    projName = parentName;
            }
        }

        // If available settings.gradle[.kts], checks whether a custom rootProject.name is specified
        var settingsPath = File.Exists(candSettingsKts) ? candSettingsKts : (File.Exists(candSettingsGroovy) ? candSettingsGroovy : null);
        if (settingsPath != null && File.Exists(settingsPath))
        {
            try
            {
                var content = File.ReadAllText(settingsPath);
                var match = System.Text.RegularExpressions.Regex.Match(content, @"rootProject\.name\s*=\s*\(?[""']([^""'\r\n]+)[""']\)?");
                if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                    projName = match.Groups[1].Value.Trim();
            }
            catch
            {
                // Ignore settings file parsing errors
            }
        }

        // Collects all Java and Kotlin source files in the project (including all submodules like app/)
        var sourceFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => !IsInIgnoredFolder(f) && IsValidSourceFile(f))
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        var defaultName = sourceFiles.Any(f => f.EndsWith(".kt", StringComparison.OrdinalIgnoreCase)) ? "Kotlin Gradle Project" : "Java Gradle Project";

        return new ProjectDescriptor
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrEmpty(projName) ? defaultName : projName,
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

        // If no file was found specifically from the .vbp, scan the folder
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
