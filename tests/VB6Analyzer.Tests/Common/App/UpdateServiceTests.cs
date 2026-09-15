using FluentAssertions;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class UpdateServiceTests
{
    /// <summary>
    /// Verifica che la proprietà UpdateService.CurrentVersion sia formattata come una stringa SemVer valida (X.Y.Z).
    /// </summary>
    [Fact]
    public void CurrentVersion_ShouldBeValidSemVer()
    {
        UpdateService.CurrentVersion.Should().NotBeNull();
        UpdateService.CurrentVersion.Major.Should().BeGreaterThanOrEqualTo(1);
        UpdateService.CurrentVersionString.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    /// <summary>
    /// Verifica che la comparazione semantica tra versioni (SemVer) identifichi correttamente se una versione remota è più recente.
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
