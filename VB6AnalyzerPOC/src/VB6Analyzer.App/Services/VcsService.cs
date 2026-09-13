using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Macondo.App.Models;

namespace Macondo.App.Services;

/// <summary>
/// Servizio unificato di gestione Version Control (Git e Subversion/SVN).
/// Fornisce un'interfaccia unica all'applicazione per il rilevamento del repository,
/// la lettura del branch attivo, il listing dei branch e il cambio branch.
/// </summary>
public sealed class VcsService
{
    private static readonly Lazy<VcsService> _instance = new(() => new VcsService());
    public static VcsService Instance => _instance.Value;

    private VcsService() { }

    /// <summary>
    /// Rileva se la cartella specificata è tracciata sotto Git o SVN.
    /// </summary>
    public VcsType DetectVcsType(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return VcsType.None;

        if (GitService.Instance.IsGitRepository(folderPath))
        {
            return VcsType.Git;
        }

        if (SvnService.Instance.IsSvnRepository(folderPath))
        {
            return VcsType.Svn;
        }

        return VcsType.None;
    }

    /// <summary>
    /// Verifica se la cartella appartiene a un qualsiasi repository controllato da versione (Git o SVN).
    /// </summary>
    public bool IsVcsRepository(string? folderPath) =>
        DetectVcsType(folderPath) != VcsType.None;

    /// <summary>
    /// Restituisce il nome del branch attivo e il tipo di VCS associato alla cartella.
    /// </summary>
    public string? GetCurrentBranch(string? folderPath, out VcsType vcsType)
    {
        vcsType = DetectVcsType(folderPath);
        return vcsType switch
        {
            VcsType.Git => GitService.Instance.GetCurrentBranch(folderPath),
            VcsType.Svn => SvnService.Instance.GetCurrentBranch(folderPath),
            _ => null
        };
    }

    /// <summary>
    /// Restituisce l'elenco dei branch disponibili per il repository attivo.
    /// </summary>
    public List<VcsBranchInfo> GetBranches(string? folderPath)
    {
        var vcsType = DetectVcsType(folderPath);

        if (vcsType == VcsType.Git)
        {
            return GitService.Instance.GetBranches(folderPath)
                .Select(b => new VcsBranchInfo
                {
                    Name = b.Name,
                    Target = b.Name,
                    IsCurrent = b.IsCurrent,
                    VcsType = VcsType.Git
                }).ToList();
        }

        if (vcsType == VcsType.Svn)
        {
            return SvnService.Instance.GetBranches(folderPath)
                .Select(b => new VcsBranchInfo
                {
                    Name = b.Name,
                    Target = b.TargetUrl,
                    IsCurrent = b.IsCurrent,
                    VcsType = VcsType.Svn
                }).ToList();
        }

        return new List<VcsBranchInfo>();
    }

    /// <summary>
    /// Esegue il checkout (Git) o lo switch (SVN) verso il branch selezionato.
    /// </summary>
    public async Task<(bool Success, string? Error)> SwitchBranchAsync(string? folderPath, VcsBranchInfo branch)
    {
        if (branch.VcsType == VcsType.Git)
        {
            return await GitService.Instance.CheckoutBranchAsync(folderPath, branch.Target);
        }

        if (branch.VcsType == VcsType.Svn)
        {
            return await SvnService.Instance.SwitchBranchAsync(folderPath, branch.Target);
        }

        return (false, "Sistema di controllo versione non supportato.");
    }
}
