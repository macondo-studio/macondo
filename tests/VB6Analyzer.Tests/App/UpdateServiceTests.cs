using FluentAssertions;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.App;

public class UpdateServiceTests
{
    [Fact]
    public void CurrentVersion_ShouldBeValidSemVer()
    {
        UpdateService.CurrentVersion.Should().NotBeNull();
        UpdateService.CurrentVersion.Major.Should().BeGreaterThanOrEqualTo(1);
        UpdateService.CurrentVersionString.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

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
