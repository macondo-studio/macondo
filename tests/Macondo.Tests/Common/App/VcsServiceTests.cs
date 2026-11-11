using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class VcsServiceTests
{
    /// <summary>
    /// Verify that VcsService detects the presence of a Git repository via the '.git' folder.
    /// </summary>
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

    /// <summary>
    /// Verify that VcsService detects the presence of an SVN (Subversion) repository via the '.svn' folder.
    /// </summary>
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

    /// <summary>
    /// Checks that null, empty, or non-existent paths return VcsType.None without exceptions.
    /// </summary>
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

    /// <summary>
    /// Verify that VcsService detects the presence of a Git repository even when queried
    /// from a nested subfolder (deep subdirectory) going up the hierarchy of parent folders.
    /// </summary>
    [Fact]
    public void VcsService_ShouldDetectGitRepository_FromNestedSubdirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Git_Subdir_" + Guid.NewGuid().ToString("N"));
        var gitDir = Path.Combine(tempDir, ".git");
        var subDir = Path.Combine(tempDir, "src", "Core", "Services");
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(subDir);

        try
        {
            var vcsType = VcsService.Instance.DetectVcsType(subDir);
            vcsType.Should().Be(VcsType.Git);
            VcsService.Instance.IsVcsRepository(subDir).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Verify that VcsService maintains a path pointing directly to a source file within the repository.
    /// </summary>
    [Fact]
    public void VcsService_ShouldDetectRepository_FromFilePath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Git_File_" + Guid.NewGuid().ToString("N"));
        var gitDir = Path.Combine(tempDir, ".git");
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(srcDir);

        var filePath = Path.Combine(srcDir, "Order.cls");
        File.WriteAllText(filePath, "Public Sub Run() : End Sub");

        try
        {
            var vcsType = VcsService.Instance.DetectVcsType(filePath);
            vcsType.Should().Be(VcsType.Git);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
