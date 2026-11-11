using FluentAssertions;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class UpdateServiceTests
{
    /// <summary>
    /// Verify that the UpdateService.CurrentVersion property is formatted as a valid SemVer (X.Y.Z) string.
    /// </summary>
    [Fact]
    public void CurrentVersion_ShouldBeValidSemVer()
    {
        UpdateService.CurrentVersion.Should().NotBeNull();
        UpdateService.CurrentVersion.Major.Should().BeGreaterThanOrEqualTo(1);
        UpdateService.CurrentVersionString.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    /// <summary>
    /// Verify that semantic version comparison (SemVer) correctly identifies whether a remote version is newer.
    /// </summary>
    [Theory]
    [InlineData("1.0.0", false)]
    [InlineData("1.1.0", false)]
    [InlineData("1.1.1", true)]
    [InlineData("1.2.0", true)]
    [InlineData("2.0.0", true)]
    public void VersionComparison_ShouldCorrectlyIdentifyNewerVersions(string remoteVersionStr, bool isNewer)
    {
        var baseVersion = new Version(1, 1, 0);
        var parsedRemote = Version.Parse(remoteVersionStr);

        var result = parsedRemote > baseVersion;
        result.Should().Be(isNewer);
    }
}
