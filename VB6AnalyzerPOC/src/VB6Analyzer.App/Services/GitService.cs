using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Macondo.App.Services;

/// <summary>
/// Informazioni su un branch Git del repository.
/// </summary>
public sealed class GitBranchInfo
{
    public required string Name { get; init; }
    public bool IsCurrent { get; init; }
}

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
