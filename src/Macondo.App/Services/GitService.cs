using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Macondo.App.Models;

namespace Macondo.App.Services;

/// <summary>
/// Service for tracking and managing basic Git operations (current branch, branch list, checkout).
/// It does not require third-party binary libraries, taking advantage of direct reading of .git and the standard Git CLI interface.
/// </summary>
public sealed class GitService
{
    private static readonly Lazy<GitService> _instance = new(() => new GitService());
    public static GitService Instance => _instance.Value;

    private GitService() { }

    /// <summary>
    /// Find the root folder of the Git repository (.git) by going up the hierarchy starting from the given folder.
    /// </summary>
    public string? FindGitRoot(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return null;

        try
        {
            if (File.Exists(folderPath))
            {
                folderPath = Path.GetDirectoryName(folderPath);
                if (string.IsNullOrWhiteSpace(folderPath)) return null;
            }

            var dir = new DirectoryInfo(folderPath);
            while (dir != null && dir.Exists)
            {
                var gitPath = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))

                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch
        {
            // Ignore permissions or I/O errors
        }

        return null;
    }

    /// <summary>
    /// Checks whether the specified folder is inside a Git repository.
    /// </summary>
    public bool IsGitRepository(string? folderPath) =>
        FindGitRoot(folderPath) != null;

    /// <summary>
    /// Returns the name of the active Git branch in the repository associated with the folder.
    /// </summary>
    public string? GetCurrentBranch(string? folderPath)
    {
        var root = FindGitRoot(folderPath);
        if (root == null) return null;

        // 1. Ultra-fast reading from .git/HEAD
        try
        {
            var headFile = Path.Combine(root, ".git", "HEAD");
            if (File.Exists(headFile))
            {
                var content = File.ReadAllText(headFile).Trim();
                if (content.StartsWith("ref: refs/heads/"))

                    return content.Substring("ref: refs/heads/".Length);
            }
        }
        catch
        {
            // Fallback to CLI command if .git/HEAD file is locked or worktree
        }

        // 2. Fallback to git rev-parse CLI command
        var (exitCode, stdout, _) = RunGitCommand(root, "rev-parse --abbrev-ref HEAD");
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))

            return stdout.Trim();

        return null;
    }

    /// <summary>
    /// Returns the list of all local branches available in the repository.
    /// </summary>
    public List<GitBranchInfo> GetBranches(string? folderPath)
    {
        var root = FindGitRoot(folderPath);
        if (root == null) return new List<GitBranchInfo>();

        var branches = new List<GitBranchInfo>();
        var currentBranch = GetCurrentBranch(folderPath);

        // 1. Execute git branch --list command
        var (exitCode, stdout, _) = RunGitCommand(root, "branch --list --no-color");
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var isCurrent = trimmed.StartsWith("*");
                var branchName = trimmed.TrimStart('*', ' ').Trim();

                if (!string.IsNullOrEmpty(branchName))

                    branches.Add(new GitBranchInfo
                    {
                        Name = branchName,
                        IsCurrent = isCurrent || string.Equals(branchName, currentBranch, StringComparison.OrdinalIgnoreCase)
                    });
            }
        }

        // 2. Fallback to .git/refs/heads filesystem if git CLI is not installed in the PATH
        if (branches.Count == 0)
        {
            try
            {
                var headsDir = Path.Combine(root, ".git", "refs", "heads");
                if (Directory.Exists(headsDir))
                {
                    var files = Directory.GetFiles(headsDir, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var relName = Path.GetRelativePath(headsDir, file).Replace('\\', '/');
                        branches.Add(new GitBranchInfo
                        {
                            Name = relName,
                            IsCurrent = string.Equals(relName, currentBranch, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
            }
            catch
            {
                // Ignore login errors
            }
        }

        return branches.OrderByDescending(b => b.IsCurrent).ThenBy(b => b.Name).ToList();
    }

    /// <summary>
    /// Extracts the list of modified files for each recent commit (up to maxCommits) for Temporal Coupling analysis.
    /// </summary>
    public List<List<string>> GetCommitFileChangelists(string? folderPath, int maxCommits = 300)
    {
        var root = FindGitRoot(folderPath);
        if (root == null) return new List<List<string>>();

        var (exitCode, stdout, _) = RunGitCommand(root, $"log -n {maxCommits} --name-only --pretty=format:--COMMIT--");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return new List<List<string>>();

        var result = new List<List<string>>();
        var lines = stdout.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        List<string>? currentCommitFiles = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed == "--COMMIT--")
            {
                if (currentCommitFiles != null && currentCommitFiles.Count > 0)

                    result.Add(currentCommitFiles);
                currentCommitFiles = new List<string>();
            }
            else if (currentCommitFiles != null)

    currentCommitFiles.Add(trimmed);
        }

        if (currentCommitFiles != null && currentCommitFiles.Count > 0)

            result.Add(currentCommitFiles);

        return result;
    }

    /// <summary>
    /// Extracts detailed commit history by file (Author, file path, date, added/removed lines) for Social Code and Bus Factor analysis.
    /// </summary>
    public List<Macondo.Semantic.Analytics.SocialCode.FileCommitRecord> GetFileCommitHistory(string? folderPath, int maxCommits = 500)
    {
        var root = FindGitRoot(folderPath);
        if (root == null) return new List<Macondo.Semantic.Analytics.SocialCode.FileCommitRecord>();

        var (exitCode, stdout, _) = RunGitCommand(root, $"log -n {maxCommits} --numstat --pretty=format:--COMMIT--%an%x09%aI");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return new List<Macondo.Semantic.Analytics.SocialCode.FileCommitRecord>();

        var result = new List<Macondo.Semantic.Analytics.SocialCode.FileCommitRecord>();
        var lines = stdout.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        string currentAuthor = "Unknown";
        DateTime currentTimestamp = DateTime.UtcNow;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.StartsWith("--COMMIT--"))
            {
                var meta = trimmed.Substring("--COMMIT--".Length).Split('\t');
                currentAuthor = meta.Length > 0 && !string.IsNullOrWhiteSpace(meta[0]) ? meta[0].Trim() : "Unknown";
                if (meta.Length > 1 && DateTime.TryParse(meta[1], out var dt))

                    currentTimestamp = dt;

                else
                {
                    currentTimestamp = DateTime.UtcNow;
                }
            }
            else
            {
                var parts = line.Split('\t');
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[0], out var added);
                    int.TryParse(parts[1], out var deleted);
                    var filePath = parts[2].Trim();
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        var fullPath = Path.GetFullPath(Path.Combine(root, filePath));
                        result.Add(new Macondo.Semantic.Analytics.SocialCode.FileCommitRecord
                        {
                            Author = currentAuthor,
                            FilePath = fullPath,
                            Timestamp = currentTimestamp,
                            LinesAdded = added,
                            LinesDeleted = deleted
                        });
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Performs asynchronous checkout of the specified branch.
    /// Returns Success = true if successful, otherwise Success = false with Git error message.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> CheckoutBranchAsync(string? folderPath, string branchName)
    {
        var root = FindGitRoot(folderPath);
        if (root == null)
            return (false, "Cartella non appartenente a un repository Git valido.");

        if (string.IsNullOrWhiteSpace(branchName))
            return (false, "Nome branch non valido.");

        var result = await Task.Run(() => RunGitCommand(root, $"checkout \"{branchName}\""));

        if (result.ExitCode == 0)

            return (true, null);

        var error = !string.IsNullOrWhiteSpace(result.StdErr) ? result.StdErr.Trim() : result.StdOut.Trim();
        return (false, error);
    }

    /// <summary>
    /// Runs a 'git' CLI command with safe timeout (5s), capturing stdout and stderr in UTF-8 encoding.
    /// </summary>
    private static (int ExitCode, string StdOut, string StdErr) RunGitCommand(string workingDir, string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { }
                return (-1, string.Empty, "Timeout comando Git superato (5s).");
            }

            return (process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, $"Impossibile eseguire comando Git: {ex.Message}");
        }
    }
}
