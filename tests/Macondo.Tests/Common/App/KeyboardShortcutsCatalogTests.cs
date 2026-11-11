using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class KeyboardShortcutsCatalogTests
{
    /// <summary>
    /// Verify that the shortcut catalog returns a non-empty list of shortcuts
    /// for both supported languages (Italian and English).
    /// </summary>
    [Theory]
    [InlineData(AppLanguage.Italian)]
    [InlineData(AppLanguage.English)]
    public void KeyboardShortcutsCatalog_ShouldReturnPopulatedList_ForBothLanguages(AppLanguage language)
    {
        // Act
        var shortcuts = KeyboardShortcutsCatalog.GetShortcuts(language);

        // Assert
        shortcuts.Should().NotBeNullOrEmpty();
        shortcuts.Count.Should().BeGreaterThanOrEqualTo(8);

        foreach (var item in shortcuts)
        {
            item.ActionName.Should().NotBeNullOrWhiteSpace();
            item.Category.Should().NotBeNullOrWhiteSpace();
            item.Description.Should().NotBeNullOrWhiteSpace();
            item.KeyBadges.Should().NotBeEmpty();
        }
    }

    /// <summary>
    /// Check that the essential shortcuts (Ctrl+O, Ctrl+F, Ctrl+B, Ctrl+I, Ctrl+R, F1)
    /// are present in the shortcut catalog.
    /// </summary>
    [Fact]
    public void KeyboardShortcutsCatalog_ShouldContainEssentialShortcuts()
    {
        // Act
        var shortcuts = KeyboardShortcutsCatalog.GetShortcuts(AppLanguage.English);

        // Assert
        shortcuts.Should().Contain(s => s.KeyBadges.SequenceEqual(new[] { "Ctrl", "O" }));
        shortcuts.Should().Contain(s => s.KeyBadges.SequenceEqual(new[] { "Ctrl", "F" }));
        shortcuts.Should().Contain(s => s.KeyBadges.SequenceEqual(new[] { "Ctrl", "B" }));
        shortcuts.Should().Contain(s => s.KeyBadges.SequenceEqual(new[] { "Ctrl", "I" }));
        shortcuts.Should().Contain(s => s.KeyBadges.SequenceEqual(new[] { "Ctrl", "R" }));
        shortcuts.Should().Contain(s => s.KeyBadges.SequenceEqual(new[] { "F1" }));
    }
}
