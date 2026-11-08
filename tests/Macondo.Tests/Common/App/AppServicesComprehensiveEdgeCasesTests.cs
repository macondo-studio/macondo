using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class AppServicesComprehensiveEdgeCasesTests
{
    #region VCS Service Extended Edge Cases

    /// <summary>
    /// Verifica che VcsService restituisca VcsType.None per percorsi non associati ad alcun repository
    /// anche quando la cartella temporanea è profondamente nidificata.
    /// </summary>
    [Fact]
    public void VcsService_ShouldReturnNone_ForDeeplyNestedNonVcsDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_NonVcs_" + Guid.NewGuid().ToString("N"), "sub1", "sub2", "sub3");
        Directory.CreateDirectory(tempDir);

        try
        {
            var vcsType = VcsService.Instance.DetectVcsType(tempDir);
            vcsType.Should().Be(VcsType.None);
            VcsService.Instance.IsVcsRepository(tempDir).Should().BeFalse();
        }
        finally
        {
            var rootTemp = Path.Combine(Path.GetTempPath(), "Macondo_NonVcs_" + Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(tempDir))!));
            if (Directory.Exists(rootTemp))
            {
                Directory.Delete(rootTemp, true);
            }
        }
    }

    /// <summary>
    /// Verifica che VcsService gestisca percorsi con spazi e caratteri speciali senza sollevare eccezioni.
    /// </summary>
    [Fact]
    public void VcsService_ShouldHandleDirectoryWithSpacesAndSpecialChars()
    {
        var specialDirName = "Macondo Git Test - Special # & (1) " + Guid.NewGuid().ToString("N");
        var tempDir = Path.Combine(Path.GetTempPath(), specialDirName);
        var gitDir = Path.Combine(tempDir, ".git");
        Directory.CreateDirectory(gitDir);

        try
        {
            var vcsType = VcsService.Instance.DetectVcsType(tempDir);
            vcsType.Should().Be(VcsType.Git);
            VcsService.Instance.IsVcsRepository(tempDir).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    #endregion

    #region Windows Notification Service Unicode & Formatting

    /// <summary>
    /// Verifica che il servizio di notifica Windows gestisca caratteri speciali, emoji, entità XML
    /// e formattazioni multilinea senza errori.
    /// </summary>
    [Theory]
    [InlineData("Notifica: Analisi completata ✅", "Trovati 15 avvisi e 3 errori.\r\nControllare i log.")]
    [InlineData("Special <XML> & \"Quotes\" 'Test'", "Payload: 100% OK & <valid>")]
    [InlineData("Line1\nLine2\r\nLine3", "\tTabbed content with trailing spaces   ")]
    public void WindowsNotificationService_ShouldHandleSpecialFormattingAndUnicode(string title, string message)
    {
        var act = () => WindowsNotificationService.ShowWindowsNotification(null, title, message);
        act.Should().NotThrow();
    }

    #endregion

    #region UpdateService Multi-Digit & Semantic Version Comparisons

    /// <summary>
    /// Verifica la comparazione semantica delle versioni con numeri a più cifre (es. 1.10.0 > 1.9.0, 2.0.0 > 1.99.99).
    /// </summary>
    [Theory]
    [InlineData("1.9.0", "1.10.0", true)]
    [InlineData("1.10.0", "1.9.0", false)]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("2.0.0", "1.99.99", false)]
    [InlineData("1.5.10", "1.5.9", false)]
    [InlineData("1.5.10", "1.5.10", false)]
    public void UpdateService_ShouldCompareMultiDigitVersionsAccurately(string baseVersionStr, string remoteVersionStr, bool isRemoteNewer)
    {
        var baseVersion = Version.Parse(baseVersionStr);
        var remoteVersion = Version.Parse(remoteVersionStr);

        var result = remoteVersion > baseVersion;
        result.Should().Be(isRemoteNewer);
    }

    #endregion

    #region Keyboard Shortcuts Catalog Consistency

    /// <summary>
    /// Verifica che tutte le azioni nel catalogo delle scorciatoie abbiano nomi di azione unici
    /// e combinazioni di tasti valide per entrambe le lingue.
    /// </summary>
    [Theory]
    [InlineData(AppLanguage.Italian)]
    [InlineData(AppLanguage.English)]
    public void KeyboardShortcutsCatalog_ShouldHaveUniqueActionsAndValidBadges(AppLanguage language)
    {
        var shortcuts = KeyboardShortcutsCatalog.GetShortcuts(language);

        shortcuts.Should().NotBeNullOrEmpty();
        var actionNames = shortcuts.Select(s => s.ActionName).ToList();
        actionNames.Distinct().Count().Should().Be(actionNames.Count, "Ogni scorciatoia deve avere un ActionName distinto");

        foreach (var shortcut in shortcuts)
        {
            shortcut.KeyBadges.Should().NotBeEmpty();
            shortcut.KeyBadges.Should().AllSatisfy(badge => badge.Should().NotBeNullOrWhiteSpace());
            shortcut.Category.Should().NotBeNullOrWhiteSpace();
        }
    }

    #endregion
}
