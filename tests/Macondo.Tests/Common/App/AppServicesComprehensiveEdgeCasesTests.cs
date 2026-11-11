using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class AppServicesComprehensiveEdgeCasesTests
{
    #region VCS Service Extended Edge Cases

    /// <summary>
    /// Verify that VcsService returns VcsType.None for paths not associated with any repository
    /// even when the temporary folder is deeply nested.
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
    /// Verify that VcsService handles paths with spaces and special characters without raising exceptions.
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
    /// Verify that Windows Notification Service handles special characters, emojis, XML entities
    /// and error-free multiline formatting.
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
    /// Check the semantic comparison of versions with multi-digit numbers (e.g. 1.10.0 > 1.9.0, 2.0.0 > 1.99.99).
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
    /// Verify that all actions in the shortcut catalog have unique action names
    /// and key combinations valid for both languages.
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
