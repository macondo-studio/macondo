using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Macondo.App.Services;

/// <summary>
/// Informazioni su un branch o percorso di switch SVN (Subversion).
/// </summary>
public sealed class SvnBranchInfo
{
    /// <summary>
    /// Nome leggibile per l'interfaccia (es. 'trunk', 'feature-auth', 'tags/v1.0').
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Percorso relativo o URL per 'svn switch' (es. '^/trunk', '^/branches/feature-auth').
    /// </summary>
    public required string TargetUrl { get; init; }

    /// <summary>
    /// Indica se questo è il branch/percorso attualmente attivo nel working copy.
    /// </summary>
    public bool IsCurrent { get; init; }
}

/// <summary>
/// Servizio per il rilevamento e la gestione delle operazioni SVN (Subversion) di base (branch corrente, lista branch, svn switch).
/// Non richiede librerie terze binarie, sfruttando la CLI standard di Subversion (svn).
/// </summary>
public sealed class SvnService
{
    private static readonly Lazy<SvnService> _instance = new(() => new SvnService());
    public static SvnService Instance => _instance.Value;

    private SvnService() { }

    /// <summary>
    /// Trova la cartella radice della copia di lavoro SVN (.svn) risalendo la gerarchia delle directory.
    /// </summary>
    public string? FindSvnRoot(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return null;

        try
        {
            var dir = new DirectoryInfo(folderPath);
            while (dir != null && dir.Exists)
            {
                var svnPath = Path.Combine(dir.FullName, ".svn");
                if (Directory.Exists(svnPath) || File.Exists(svnPath))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
        }
        catch
        {
            // Ignora errori di accesso ai permessi
        }

        return null;
    }

    /// <summary>
    /// Verifica se la cartella specificata appartiene a una copia di lavoro Subversion.
    /// </summary>
    public bool IsSvnRepository(string? folderPath) =>
        FindSvnRoot(folderPath) != null;

    /// <summary>
    /// Restituisce il nome del branch o percorso relativo SVN attivo nel working copy (es. 'trunk', 'feature-x').
    /// </summary>
    public string? GetCurrentBranch(string? folderPath)
    {
        var root = FindSvnRoot(folderPath);
        if (root == null) return null;

        // 1. Prova svn info --show-item relative-url
        var (code, stdout, _) = RunSvnCommand(root, "info --show-item relative-url --non-interactive");
        if (code == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            var rel = stdout.Trim();
            return SimplifySvnRelativeUrl(rel);
        }

        // 2. Fallback su svn info standard
        var (infoCode, infoStdout, _) = RunSvnCommand(root, "info --non-interactive");
        if (infoCode == 0 && !string.IsNullOrWhiteSpace(infoStdout))
        {
            var lines = infoStdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string? relativeUrl = null;
            string? url = null;
            string? repoRoot = null;

            foreach (var line in lines)
            {
                if (line.StartsWith("Relative URL:", StringComparison.OrdinalIgnoreCase))
                {
                    relativeUrl = line.Substring("Relative URL:".Length).Trim();
                }
                else if (line.StartsWith("URL:", StringComparison.OrdinalIgnoreCase))
                {
                    url = line.Substring("URL:".Length).Trim();
                }
                else if (line.StartsWith("Repository Root:", StringComparison.OrdinalIgnoreCase))
                {
                    repoRoot = line.Substring("Repository Root:".Length).Trim();
                }
            }

            if (!string.IsNullOrEmpty(relativeUrl))
            {
                return SimplifySvnRelativeUrl(relativeUrl);
            }

            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(repoRoot) && url.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                var rel = "^" + url.Substring(repoRoot.Length);
                return SimplifySvnRelativeUrl(rel);
            }

            if (!string.IsNullOrEmpty(url))
            {
                return Path.GetFileName(url.TrimEnd('/'));
            }
        }

        return "SVN";
    }

    /// <summary>
    /// Restituisce l'elenco dei branch/trunk/tags disponibili per il comando 'svn switch'.
    /// </summary>
    public List<SvnBranchInfo> GetBranches(string? folderPath)
    {
        var root = FindSvnRoot(folderPath);
        if (root == null) return new List<SvnBranchInfo>();

        var branches = new List<SvnBranchInfo>();
        var currentBranchName = GetCurrentBranch(folderPath) ?? string.Empty;

        // 1. Verifica se esiste trunk
        var (trunkCode, _, _) = RunSvnCommand(root, "info ^/trunk --non-interactive");
        if (trunkCode == 0)
        {
            var isCurrent = string.Equals(currentBranchName, "trunk", StringComparison.OrdinalIgnoreCase);
            branches.Add(new SvnBranchInfo
            {
                Name = "trunk",
                TargetUrl = "^/trunk",
                IsCurrent = isCurrent
            });
        }

        // 2. Elenca branch in ^/branches
        var (branchesCode, branchesStdout, _) = RunSvnCommand(root, "list ^/branches --non-interactive");
        if (branchesCode == 0 && !string.IsNullOrWhiteSpace(branchesStdout))
        {
            var lines = branchesStdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var name = line.Trim().TrimEnd('/');
                if (!string.IsNullOrEmpty(name))
                {
                    var isCurrent = string.Equals(currentBranchName, name, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(currentBranchName, $"branches/{name}", StringComparison.OrdinalIgnoreCase);

                    branches.Add(new SvnBranchInfo
                    {
                        Name = name,
                        TargetUrl = $"^/branches/{name}",
                        IsCurrent = isCurrent
                    });
                }
            }
        }

        // 3. Se non abbiamo trovato né trunk né branches, elenca la radice del repository (^/)
        if (branches.Count == 0)
        {
            var (rootListCode, rootListStdout, _) = RunSvnCommand(root, "list ^/ --non-interactive");
            if (rootListCode == 0 && !string.IsNullOrWhiteSpace(rootListStdout))
            {
                var lines = rootListStdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var name = line.Trim().TrimEnd('/');
                    if (!string.IsNullOrEmpty(name))
                    {
                        var isCurrent = string.Equals(currentBranchName, name, StringComparison.OrdinalIgnoreCase);
                        branches.Add(new SvnBranchInfo
                        {
                            Name = name,
                            TargetUrl = $"^/{name}",
                            IsCurrent = isCurrent
                        });
                    }
                }
            }
        }

        // Se la lista è ancora vuota ma siamo in un repo SVN, offri almeno il branch corrente
        if (branches.Count == 0 && !string.IsNullOrEmpty(currentBranchName))
        {
            branches.Add(new SvnBranchInfo
            {
                Name = currentBranchName,
                TargetUrl = currentBranchName.StartsWith("^/") ? currentBranchName : $"^/{currentBranchName}",
                IsCurrent = true
            });
        }

        return branches.OrderByDescending(b => b.IsCurrent)
                       .ThenBy(b => b.Name.Equals("trunk", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                       .ThenBy(b => b.Name)
                       .ToList();
    }

    /// <summary>
    /// Esegue il comando 'svn switch' verso il branch o percorso specificato.
    /// </summary>
    public async Task<(bool Success, string? Error)> SwitchBranchAsync(string? folderPath, string targetUrlOrName)
    {
        var root = FindSvnRoot(folderPath);
        if (root == null)
        {
            return (false, "Cartella non appartenente a una copia di lavoro SVN.");
        }

        var target = targetUrlOrName.Trim();
        if (!target.StartsWith("^/") && !target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !target.StartsWith("svn://", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(target, "trunk", StringComparison.OrdinalIgnoreCase))
            {
                target = "^/trunk";
            }
            else
            {
                target = $"^/branches/{target}";
            }
        }

        return await Task.Run<(bool Success, string? Error)>(() =>
        {
            var (exitCode, stdout, stderr) = RunSvnCommand(root, $"switch \"{target}\" --non-interactive", timeoutMs: 30000);
            if (exitCode == 0)
            {
                return (true, null);
            }

            var err = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
            if (string.IsNullOrWhiteSpace(err))
            {
                err = $"svn switch è terminato con codice di errore {exitCode}.";
            }
            return (false, err.Trim());
        });
    }

    /// <summary>
    /// Semplifica un URL relativo SVN (es. '^/branches/feature-x' -> 'feature-x', '^/trunk' -> 'trunk').
    /// </summary>
    private static string SimplifySvnRelativeUrl(string relativeUrl)
    {
        var clean = relativeUrl.Trim();
        if (clean.StartsWith("^/"))
        {
            clean = clean.Substring(2);
        }
        else if (clean.StartsWith("^"))
        {
            clean = clean.Substring(1);
        }

        clean = clean.TrimStart('/');

        if (clean.StartsWith("branches/", StringComparison.OrdinalIgnoreCase))
        {
            return clean.Substring("branches/".Length);
        }

        return clean;
    }

    /// <summary>
    /// Esegue un comando CLI Subversion ('svn') con timeout e cattura sincrona di stdout/stderr.
    /// </summary>
    private static (int ExitCode, string StdOut, string StdErr) RunSvnCommand(string workingDir, string arguments, int timeoutMs = 4000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "svn",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) stderr.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(); } catch { }
                return (-1, stdout.ToString(), "Timeout durante l'esecuzione del comando svn.");
            }

            return (process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
