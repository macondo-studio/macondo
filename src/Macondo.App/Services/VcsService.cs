using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Macondo.App.Models;

namespace Macondo.App.Services;

/// <summary>
/// Unified Version Control management service (Git and Subversion/SVN).
/// Provides a unique interface to the repository discovery application,
/// reading the active branch, branch listing and branch switching.
/// </summary>
public sealed class VcsService
{
    private static readonly Lazy<VcsService> _instance = new(() => new VcsService());
    public static VcsService Instance => _instance.Value;

    private VcsService() { }

    /// <summary>
    /// Detects whether the specified folder is tracked under Git or SVN.
    /// </summary>
    public VcsType DetectVcsType(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return VcsType.None;

        if (GitService.Instance.IsGitRepository(folderPath))

            return VcsType.Git;

        if (SvnService.Instance.IsSvnRepository(folderPath))

            return VcsType.Svn;

        return VcsType.None;
    }

    /// <summary>
    /// Check if the folder belongs to any version-controlled repository (Git or SVN).
    /// </summary>
    public bool IsVcsRepository(string? folderPath) =>
        DetectVcsType(folderPath) != VcsType.None;

    /// <summary>
    /// Returns the name of the active branch and the type of VCS associated with the folder.
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
    /// Returns the list of branches available for the active repository.
    /// </summary>
    public List<VcsBranchInfo> GetBranches(string? folderPath)
    {
        var vcsType = DetectVcsType(folderPath);

        if (vcsType == VcsType.Git)

            return GitService.Instance.GetBranches(folderPath)
                .Select(b => new VcsBranchInfo
                {
                    Name = b.Name,
                    Target = b.Name,
                    IsCurrent = b.IsCurrent,
                    VcsType = VcsType.Git
                }).ToList();

        if (vcsType == VcsType.Svn)

            return SvnService.Instance.GetBranches(folderPath)
                .Select(b => new VcsBranchInfo
                {
                    Name = b.Name,
                    Target = b.TargetUrl,
                    IsCurrent = b.IsCurrent,
                    VcsType = VcsType.Svn
                }).ToList();

        return new List<VcsBranchInfo>();
    }

    /// <summary>
    /// Performs checkout (Git) or switch (SVN) to the selected branch.
    /// </summary>
    public async Task<(bool Success, string? Error)> SwitchBranchAsync(string? folderPath, VcsBranchInfo branch)
    {
        if (branch.VcsType == VcsType.Git)

            return await GitService.Instance.CheckoutBranchAsync(folderPath, branch.Target);

        if (branch.VcsType == VcsType.Svn)

            return await SvnService.Instance.SwitchBranchAsync(folderPath, branch.Target);

        return (false, "Sistema di controllo versione non supportato.");
    }
}
