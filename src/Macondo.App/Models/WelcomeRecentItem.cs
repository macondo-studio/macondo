using System.Collections.Generic;

namespace Macondo.App.Models;

/// <summary>
/// Modello visuale per un elemento di progetto recente nella schermata iniziale (Welcome View).
/// Include lo stato di cache calcolato dinamicamente per dare feedback immediato all'utente.
/// </summary>
public sealed class WelcomeRecentItem
{
    public required string DisplayName { get; init; }
    public required string PathSummary { get; init; }
    public required string LastOpenedText { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public bool IsFolder { get; init; }
    public bool IsCached { get; init; }
    public required string CacheBadgeText { get; init; }
    public required string CacheBadgeBackground { get; init; }
    public required string CacheBadgeForeground { get; init; }
    public required string CacheBadgeTooltip { get; init; }
}
