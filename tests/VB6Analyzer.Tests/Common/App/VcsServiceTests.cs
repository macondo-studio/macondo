using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class VcsServiceTests
{
    /// <summary>
    /// Verifica che VcsService individui la presenza di un repository Git tramite la cartella '.git'.
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
    /// Verifica che VcsService individui la presenza di un repository SVN (Subversion) tramite la cartella '.svn'.
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
    /// Verifica che percorsi nulli, vuoti o non esistenti restituiscano VcsType.None senza eccezioni.
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
    /// Verifica che VcsService individui la presenza di un repository Git anche quando interrogato
    /// da una sottocartella nidificata (deep subdirectory) risalendo la gerarchia delle cartelle genitrici.
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
    /// Verifica che VcsService gestisca un percorso puntato direttamente a un file sorgente all'interno del repository.
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
