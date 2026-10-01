using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Macondo.App.Models;

namespace Macondo.App.Services;

/// <summary>
/// Servizio per il rilevamento e la gestione delle operazioni Git di base (branch corrente, lista branch, checkout).
/// Non richiede librerie terze binarie, sfruttando la lettura diretta di .git e l'interfaccia CLI standard di Git.
/// </summary>
public sealed class GitService
{
    private static readonly Lazy<GitService> _instance = new(() => new GitService());
    public static GitService Instance => _instance.Value;

    private GitService() { }

    /// <summary>
    /// Trova la cartella radice del repository Git (.git) risalendo la gerarchia a partire dalla cartella data.
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
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
        }
        catch
        {
            // Ignora errori di permessi o I/O
        }

        return null;
    }

    /// <summary>
    /// Verifica se la cartella specificata è all'interno di un repository Git.
    /// </summary>
    public bool IsGitRepository(string? folderPath) =>
        FindGitRoot(folderPath) != null;

    /// <summary>
    /// Restituisce il nome del branch Git attivo nel repository associato alla cartella.
    /// </summary>
    public string? GetCurrentBranch(string? folderPath)
    {
        var root = FindGitRoot(folderPath);
        if (root == null) return null;

        // 1. Lettura ultra-rapida da .git/HEAD
        try
        {
            var headFile = Path.Combine(root, ".git", "HEAD");
            if (File.Exists(headFile))
            {
                var content = File.ReadAllText(headFile).Trim();
                if (content.StartsWith("ref: refs/heads/"))
                {
                    return content.Substring("ref: refs/heads/".Length);
                }
            }
        }
        catch
        {
            // Fallback su comando CLI se il file .git/HEAD è bloccato o worktree
        }

        // 2. Fallback su comando CLI git rev-parse
        var (exitCode, stdout, _) = RunGitCommand(root, "rev-parse --abbrev-ref HEAD");
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            return stdout.Trim();
        }

        return null;
    }

    /// <summary>
    /// Restituisce l'elenco di tutti i branch locali disponibili nel repository.
    /// </summary>
    public List<GitBranchInfo> GetBranches(string? folderPath)
    {
        var root = FindGitRoot(folderPath);
        if (root == null) return new List<GitBranchInfo>();

        var branches = new List<GitBranchInfo>();
        var currentBranch = GetCurrentBranch(folderPath);

        // 1. Esecuzione comando git branch --list
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
                {
                    branches.Add(new GitBranchInfo
                    {
                        Name = branchName,
                        IsCurrent = isCurrent || string.Equals(branchName, currentBranch, StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
        }

        // 2. Fallback su filesystem .git/refs/heads se git CLI non fosse installato nel PATH
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
                // Ignora errori di accesso
            }
        }

        return branches.OrderByDescending(b => b.IsCurrent).ThenBy(b => b.Name).ToList();
    }

    /// <summary>
    /// Estrae l'elenco dei file modificati per ciascun commit recente (fino a maxCommits) per l'analisi di Temporal Coupling.
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
                {
                    result.Add(currentCommitFiles);
                }
                currentCommitFiles = new List<string>();
            }
            else if (currentCommitFiles != null)
            {
                currentCommitFiles.Add(trimmed);
            }
        }

        if (currentCommitFiles != null && currentCommitFiles.Count > 0)
        {
            result.Add(currentCommitFiles);
        }

        return result;
    }

    /// <summary>
    /// Estrae la cronologia dettagliata dei commit per file (Autore, percorso file, data, righe aggiunte/rimosse) per l'analisi Social Code e Bus Factor.
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
                {
                    currentTimestamp = dt;
                }
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
    /// Esegue il checkout asincrono del branch specificato.
    /// Restituisce Success = true se completato, altrimenti Success = false con il messaggio di errore di Git.
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
        {
            return (true, null);
        }

        var error = !string.IsNullOrWhiteSpace(result.StdErr) ? result.StdErr.Trim() : result.StdOut.Trim();
        return (false, error);
    }

    /// <summary>
    /// Esegue un comando CLI 'git' con timeout di sicurezza (5s), catturando stdout e stderr in codifica UTF-8.
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
