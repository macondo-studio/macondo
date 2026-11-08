namespace Macondo.App.Models;

/// <summary>
/// Rappresenta una voce di scorciatoia da tastiera con combinazione di tasti, categoria e descrizione.
/// </summary>
public sealed class ShortcutItem
{
    public required IReadOnlyList<string> KeyBadges { get; init; }
    public required string ActionName { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }

    public string KeyChord => string.Join(" + ", KeyBadges);
}
