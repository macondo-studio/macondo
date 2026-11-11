using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class ThemeServiceTests
{
    /// <summary>
    /// Verify that each AppTheme enum value has a registered ThemeDefinition
    /// in the ThemeService with valid primary, secondary, accent and graph colors.
    /// </summary>
    [Fact]
    public void ThemeService_ShouldHaveCompleteDefinitions_ForAllAppThemes()
    {
        // Arrange
        var service = ThemeService.Instance;
        var allThemes = Enum.GetValues<AppTheme>();

        // Assert
        service.Themes.Should().NotBeEmpty();

        foreach (var theme in allThemes)
        {
            service.Themes.Should().ContainKey(theme);
            var def = service.Themes[theme];

            def.Theme.Should().Be(theme);
            def.BackgroundPrimary.Should().StartWith("#");
            def.BackgroundSecondary.Should().StartWith("#");
            def.BackgroundSurface.Should().StartWith("#");
            def.AccentColor.Should().StartWith("#");
            def.TextPrimary.Should().StartWith("#");
            def.GraphBackground.Should().StartWith("#");
            def.GraphNodeFill.Should().StartWith("#");
        }
    }

    /// <summary>
    /// Verify that the default current theme is consistent and that the CurrentDefinition property matches.
    /// </summary>
    [Fact]
    public void CurrentDefinition_ShouldMatchCurrentTheme()
    {
        // Arrange
        var service = ThemeService.Instance;

        // Assert
        service.CurrentDefinition.Should().NotBeNull();
        service.CurrentDefinition.Theme.Should().Be(service.CurrentTheme);
    }

    /// <summary>
    /// Verify that each theme registered in the Themes dictionary has valid values for all color fields.
    /// </summary>
    [Fact]
    public void Themes_ShouldContainValidColorHexCodes_ForAllRegisteredThemes()
    {
        // Arrange
        var service = ThemeService.Instance;

        foreach (var (theme, def) in service.Themes)
        {
            def.Theme.Should().Be(theme);
            def.BackgroundPrimary.Should().MatchRegex(@"^#[0-9A-Fa-f]{6}$");
            def.BackgroundSecondary.Should().MatchRegex(@"^#[0-9A-Fa-f]{6}$");
            def.BackgroundSurface.Should().MatchRegex(@"^#[0-9A-Fa-f]{6}$");
            def.AccentColor.Should().MatchRegex(@"^#[0-9A-Fa-f]{6}$");
            def.TextPrimary.Should().MatchRegex(@"^#[0-9A-Fa-f]{6}$");
            def.GraphBackground.Should().MatchRegex(@"^#[0-9A-Fa-f]{6}$");
            def.GraphNodeFill.Should().MatchRegex(@"^#[0-9A-Fa-f]{6}$");
        }
    }
}
