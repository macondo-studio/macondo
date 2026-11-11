using FluentAssertions;
using Macondo.App.Models;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpProjectDiscoveryTests
{
    /// <summary>
    /// Verify that ProjectDescriptor.DiscoverFromPaths recognizes '.csproj' project files,
    /// extract the name and the '.cs' sources, correctly excluding the build folders ('bin', 'obj').
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverFromCsprojFile_AndFilterOutBinAndObj()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_Csproj_" + Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(tempDir, "bin", "Debug", "net8.0");
        var objDir = Path.Combine(tempDir, "obj", "Debug", "net8.0");
        var srcDir = Path.Combine(tempDir, "Services");

        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(objDir);
        Directory.CreateDirectory(srcDir);

        try
        {
            var csprojPath = Path.Combine(tempDir, "MyService.csproj");
            File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");

            var validSrc = Path.Combine(srcDir, "OrderService.cs");
            File.WriteAllText(validSrc, "public class OrderService {}");

            var ignoredBin = Path.Combine(binDir, "Generated.cs");
            File.WriteAllText(ignoredBin, "public class IgnoredBin {}");

            var ignoredObj = Path.Combine(objDir, "AssemblyAttributes.cs");
            File.WriteAllText(ignoredObj, "public class IgnoredObj {}");

            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { csprojPath });

            // Assert
            descriptors.Should().HaveCount(1);
            var desc = descriptors.First();
            desc.Name.Should().Be("MyService");
            desc.CsprojPath.Should().Be(csprojPath);
            desc.SourceFiles.Should().ContainSingle(f => f == validSrc);
            desc.SourceFiles.Should().NotContain(ignoredBin);
            desc.SourceFiles.Should().NotContain(ignoredObj);
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
    /// Verify that the Visual Studio solution file (.sln) parser locates all .csproj projects
    /// referenced and upload the respective project descriptors.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverFromSlnFile_ContainingCsprojProjects()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_Sln_" + Guid.NewGuid().ToString("N"));
        var proj1Dir = Path.Combine(tempDir, "Core");
        var proj2Dir = Path.Combine(tempDir, "Web");
        Directory.CreateDirectory(proj1Dir);
        Directory.CreateDirectory(proj2Dir);

        try
        {
            var proj1Csproj = Path.Combine(proj1Dir, "Core.csproj");
            File.WriteAllText(proj1Csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(proj1Dir, "CoreClass.cs"), "public class CoreClass {}");

            var proj2Csproj = Path.Combine(proj2Dir, "Web.csproj");
            File.WriteAllText(proj2Csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(proj2Dir, "WebController.cs"), "public class WebController {}");

            var slnContent = """
                Microsoft Visual Studio Solution File, Format Version 12.00
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Core", "Core\Core.csproj", "{11111111-1111-1111-1111-111111111111}"
                EndProject
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Web", "Web\Web.csproj", "{22222222-2222-2222-2222-222222222222}"
                EndProject
                """;
            var slnPath = Path.Combine(tempDir, "MySolution.sln");
            File.WriteAllText(slnPath, slnContent);

            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { slnPath });

            // Assert
            descriptors.Should().HaveCount(2);
            descriptors.Select(d => d.Name).Should().Contain(new[] { "Core", "Web" });
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
    /// Tests discovery of a single standalone C# source (.cs) file passed as a direct path.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverSingleCsFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_SingleCs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var csPath = Path.Combine(tempDir, "StandaloneClass.cs");
            File.WriteAllText(csPath, "namespace Test { public class StandaloneClass {} }");

            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { csPath });

            // Assert
            descriptors.Should().HaveCount(1);
            var desc = descriptors.First();
            desc.Name.Should().Be("StandaloneClass");
            desc.SourceFiles.Should().ContainSingle(f => f == csPath);
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
