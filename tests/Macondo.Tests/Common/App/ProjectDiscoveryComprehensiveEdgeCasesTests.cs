using FluentAssertions;
using Macondo.App.Models;
using Xunit;

namespace Macondo.Tests.Common.App;

public class ProjectDiscoveryComprehensiveEdgeCasesTests
{
    /// <summary>
    /// Verify that ProjectDescriptor.DiscoverFromPaths handles folders that are empty or have no supported source files
    /// returning an empty list without raising exceptions.
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
    /// Verify that null, empty, or non-existent paths are handled safely without errors.
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
