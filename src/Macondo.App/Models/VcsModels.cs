using System;

namespace Macondo.App.Models;

/// <summary>
/// Type of Version Control System detected for the project.
/// </summary>
public enum VcsType
{
    None,
    Git,
    Svn
}

/// <summary>
/// Unified information about a branch / tag / VCS path (Git or SVN).
/// </summary>
public sealed class VcsBranchInfo
{
    /// <summary>
    /// Name of the branch to display in the interface (e.g. 'main', 'trunk', 'feature/login').
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Target for the checkout or switch command (Git branch name or SVN URL target).
    /// </summary>
    public required string Target { get; init; }

    /// <summary>
    /// Indicates whether this is the currently active branch.
    /// </summary>
    public bool IsCurrent { get; init; }

    /// <summary>
    /// Source VCS system (Git or SVN).
    /// </summary>
    public VcsType VcsType { get; init; }
}

/// <summary>
/// Information about a Git branch of the repository.
/// </summary>
public sealed class GitBranchInfo
{
    public required string Name { get; init; }
    public bool IsCurrent { get; init; }
}

/// <summary>
/// Information about an SVN (Subversion) branch or switch path.
/// </summary>
public sealed class SvnBranchInfo
{
    /// <summary>
    /// Human-readable name for the interface (e.g. 'trunk', 'feature-auth', 'tags/v1.0').
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Relative path or URL for 'svn switch' (e.g. '^/trunk', '^/branches/feature-auth').
    /// </summary>
    public required string TargetUrl { get; init; }

    /// <summary>
    /// Indicates whether this is the branch/path currently active in the working copy.
    /// </summary>
    public bool IsCurrent { get; init; }
}
