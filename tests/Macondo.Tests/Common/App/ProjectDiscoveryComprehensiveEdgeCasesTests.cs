using FluentAssertions;
using Macondo.App.Models;
using Xunit;

namespace Macondo.Tests.Common.App;

public class ProjectDiscoveryComprehensiveEdgeCasesTests
{
    /// <summary>
    /// Verifica che ProjectDescriptor.DiscoverFromPaths gestisca cartelle vuote o prive di file sorgente supportati
    /// restituendo una lista vuota senza sollevare eccezioni.
    /// </summary>
    [Fact]
    public void ProjectDiscovery_ShouldReturnEmptyList_ForDirectoryWithoutSupportedFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_EmptyDisc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "readme.txt"), "This is a text file.");
            File.WriteAllText(Path.Combine(tempDir, "config.json"), "{}");

            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { tempDir });

            // Assert
            descriptors.Should().BeEmpty();
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
    /// Verifica che percorsi nulli, vuoti o non esistenti vengano gestiti in modo sicuro senza errori.
    /// </summary>
    [Theory]
    [InlineData(@"C:\NonExistent_Path_ABC_123456")]
    [InlineData("")]
    public void ProjectDiscovery_ShouldHandleInvalidOrNonExistentPaths_Safely(string path)
    {
        var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { path });
        descriptors.Should().BeEmpty();
    }
}
