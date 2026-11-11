using System.Collections.Generic;

namespace Macondo.App.Models;

/// <summary>
/// Visual template for a recent project item on the Welcome View.
/// Includes dynamically calculated cache state to give immediate feedback to the user.
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
