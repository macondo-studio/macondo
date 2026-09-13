using System;

namespace Macondo.App.Models;

/// <summary>
/// Tipologia di Version Control System rilevato per il progetto.
/// </summary>
public enum VcsType
{
    None,
    Git,
    Svn
}

/// <summary>
/// Informazioni unificate su un branch / tag / percorso VCS (Git o SVN).
/// </summary>
public sealed class VcsBranchInfo
{
    /// <summary>
    /// Nome del branch da visualizzare nell'interfaccia (es. 'main', 'trunk', 'feature/login').
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Bersaglio per il comando di checkout o switch (nome branch Git o target URL SVN).
    /// </summary>
    public required string Target { get; init; }

    /// <summary>
    /// Indica se questo è il branch correntemente attivo.
    /// </summary>
    public bool IsCurrent { get; init; }

    /// <summary>
    /// Sistema VCS di provenienza (Git o SVN).
    /// </summary>
    public VcsType VcsType { get; init; }
}

/// <summary>
/// Informazioni su un branch Git del repository.
/// </summary>
public sealed class GitBranchInfo
{
    public required string Name { get; init; }
    public bool IsCurrent { get; init; }
}

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
