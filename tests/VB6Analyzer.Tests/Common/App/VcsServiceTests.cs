using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class VcsServiceTests
{
    [Fact]
    public void VcsService_ShouldDetectGitRepository()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Git_Test_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void VcsService_ShouldDetectSvnRepository()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Svn_Test_" + Guid.NewGuid().ToString("N"));
        var svnDir = Path.Combine(tempDir, ".svn");
        Directory.CreateDirectory(svnDir);

        try
        {
            var vcsType = VcsService.Instance.DetectVcsType(tempDir);
            vcsType.Should().Be(VcsType.Svn);
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\NonExistent_Directory_XYZ_123456")]
    public void VcsService_ShouldReturnNone_ForInvalidOrNonExistentPath(string? path)
    {
        var vcsType = VcsService.Instance.DetectVcsType(path);
        vcsType.Should().Be(VcsType.None);
        VcsService.Instance.IsVcsRepository(path).Should().BeFalse();
    }
}
