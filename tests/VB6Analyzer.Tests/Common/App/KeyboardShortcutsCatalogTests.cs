using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class KeyboardShortcutsCatalogTests
{
    /// <summary>
    /// Verifica che il catalogo delle scorciatoie restituisca una lista non vuota di shortcut
    /// per entrambe le lingue supportate (Italiano e Inglese).
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
    /// Verifica che gli shortcut essenziali (Ctrl+O, Ctrl+F, Ctrl+B, Ctrl+I, Ctrl+R, F1)
    /// siano presenti nel catalogo delle scorciatoie.
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
