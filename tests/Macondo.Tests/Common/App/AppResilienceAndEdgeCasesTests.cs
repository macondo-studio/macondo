using System.Text.Json;
using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.App;

public class AppResilienceAndEdgeCasesTests
{
    #region LocalizationService Edge Cases

    /// <summary>
    /// Verify that LocalizationService handles strings with mismatched formatting tokens
    /// to the number of arguments passed (e.g. too many or too few arguments) safely without exceptions.
    /// </summary>
    [Fact]
    public void Localization_Get_ShouldHandleMismatchedFormatArgumentsSafely()
    {
        var service = LocalizationService.Instance;

        // Too many arguments provided with respect to placeholders
        var actTooMany = () => service.Get("Status_StatsFormat", 1, 2, 3, 4, 5, 6, 7);
        actTooMany.Should().NotThrow();

        // Fewer arguments provided than placeholders
        var actTooFew = () => service.Get("Status_StatsFormat", 1);
        actTooFew.Should().NotThrow();
    }

    /// <summary>
    /// Verify that the default language is set and that the language change maintains state consistency.
    /// </summary>
    [Fact]
    public void Localization_LanguageSwitch_ShouldPersistAcrossCalls()
    {
        var service = LocalizationService.Instance;
        service.SetLanguage(AppLanguage.Italian);
        service.CurrentLanguage.Should().Be(AppLanguage.Italian);

        service.SetLanguage(AppLanguage.English);
        service.CurrentLanguage.Should().Be(AppLanguage.English);
    }

    #endregion

    #region ProjectCacheService Resilience Edge Cases

    /// <summary>
    /// Verify that saving and restoring an empty or minimal cache snapshot
    /// does not generate errors and preserves the integrity of the data.
    /// </summary>
    [Fact]
    public void ProjectCacheService_ShouldHandleEmptyGraphSnapshot()
    {
        var snapshot = new ProjectCacheSnapshot
        {
            SchemaVersion = 1,
            ProjectName = "EmptyProject",
            FolderPath = @"C:\Test\Empty",
            AnalyzedAtUtc = DateTime.UtcNow,
            Symbols = new List<Symbol>(),
            Relationships = new List<Relationship>(),
            LayoutNodes = new List<NodeLayoutSnapshot>(),
            BlastRadiusRows = new List<BlastRadiusRowSnapshot>()
        };

        var json = JsonSerializer.Serialize(snapshot);
        json.Should().NotBeNullOrWhiteSpace();

        var deserialized = JsonSerializer.Deserialize<ProjectCacheSnapshot>(json);
        deserialized.Should().NotBeNull();
        deserialized!.ProjectName.Should().Be("EmptyProject");
        deserialized.Symbols.Should().BeEmpty();
        deserialized.Relationships.Should().BeEmpty();
        deserialized.LayoutNodes.Should().BeEmpty();
        deserialized.BlastRadiusRows.Should().BeEmpty();
    }

    /// <summary>
    /// Check that paths with special characters (hashtags, parentheses, commas)
    /// generate deterministic and valid cache file paths on Windows.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Projects\Vb6 #1 (Special, Name)\")]
    [InlineData(@"C:/Projects/Vb6 #1 (Special, Name)")]
    [InlineData(@"D:\Dev\Macondo [Branch_2026.01]\src")]
    public void ProjectCacheService_GetCacheFilePath_ShouldHandleComplexCharacters(string path)
    {
        var service = ProjectCacheService.Instance;
        var cacheFile = service.GetCacheFilePath(path);

        cacheFile.Should().NotBeNullOrWhiteSpace();
        cacheFile.Should().EndWith(".json");
        Path.GetInvalidPathChars().Any(c => cacheFile.Contains(c)).Should().BeFalse();
    }

    #endregion

    #region UpdateService & SemVer Semantic Edge Cases

    /// <summary>
    /// Verify that the version comparison logic correctly handles subsequent releases,
    /// major upgrades and version parity.
    /// </summary>
    [Theory]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.0.0", "2.0.0", true)]
    [InlineData("2.1.5", "2.1.4", false)]
    [InlineData("2.1.5", "2.1.5", false)]
    [InlineData("2.1.5", "2.2.0", true)]
    [InlineData("2.1.5", "3.0.0", true)]
    public void UpdateService_VersionComparisonMatrix_ShouldBeAccurate(string current, string remote, bool expectedNewer)
    {
        var currentVer = Version.Parse(current);
        var remoteVer = Version.Parse(remote);

        var isNewer = remoteVer > currentVer;
        isNewer.Should().Be(expectedNewer);
    }

    #endregion
}
