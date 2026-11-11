namespace Macondo.App.Models;

/// <summary>
/// Represents a keyboard shortcut entry with key combination, category, and description.
/// </summary>
public sealed class ShortcutItem
{
    public required IReadOnlyList<string> KeyBadges { get; init; }
    public required string ActionName { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }

    public string KeyChord => string.Join(" + ", KeyBadges);
}
