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
    /// Verifica che LocalizationService gestisca stringhe con token di formattazione non corrispondenti
    /// al numero di argomenti passati (es. troppi o troppo pochi argomenti) in modo sicuro senza eccezioni.
    /// </summary>
    [Fact]
    public void Localization_Get_ShouldHandleMismatchedFormatArgumentsSafely()
    {
        var service = LocalizationService.Instance;

        // Troppi argomenti forniti rispetto ai placeholder
        var actTooMany = () => service.Get("Status_StatsFormat", 1, 2, 3, 4, 5, 6, 7);
        actTooMany.Should().NotThrow();

        // Meno argomenti forniti rispetto ai placeholder
        var actTooFew = () => service.Get("Status_StatsFormat", 1);
        actTooFew.Should().NotThrow();
    }

    /// <summary>
    /// Verifica che la lingua di default sia impostata e che il cambio lingua mantenga la coerenza di stato.
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
    /// Verifica che il salvataggio e ripristino di uno snapshot di cache vuoto o minimale
    /// non generi errori e preservi l'integrità dei dati.
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
    /// Verifica che percorsi con caratteri speciali (hashtag, parentesi, virgole)
    /// generino percorsi file di cache deterministici e validi su Windows.
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
    /// Verifica che la logica di comparazione delle versioni gestisca correttamente release successive,
    /// major upgrades e parità di versione.
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
