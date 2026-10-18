using FluentAssertions;
using Macondo.App.Models;
using Xunit;

namespace Macondo.Tests.VisualBasicNetSpecific;

public class VisualBasicNetProjectDiscoveryTests
{
    /// <summary>
    /// Verifica che ProjectDescriptor.DiscoverFromPaths riconosca i file di progetto '.vbproj',
    /// ne estragga il nome e i sorgenti '.vb', escludendo correttamente le cartelle di build ('bin', 'obj').
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverFromVbprojFile_AndFilterOutBinAndObj()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_Vbproj_" + Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(tempDir, "bin", "Debug", "net8.0");
        var objDir = Path.Combine(tempDir, "obj", "Debug", "net8.0");
        var srcDir = Path.Combine(tempDir, "Services");

        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(objDir);
        Directory.CreateDirectory(srcDir);

        try
        {
            var vbprojPath = Path.Combine(tempDir, "MyVbService.vbproj");
            File.WriteAllText(vbprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");

            var validSrc = Path.Combine(srcDir, "OrderService.vb");
            File.WriteAllText(validSrc, "Public Class OrderService\nEnd Class");

            var ignoredBin = Path.Combine(binDir, "Generated.vb");
            File.WriteAllText(ignoredBin, "Public Class IgnoredBin\nEnd Class");

            var ignoredObj = Path.Combine(objDir, "AssemblyAttributes.vb");
            File.WriteAllText(ignoredObj, "Public Class IgnoredObj\nEnd Class");

            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { vbprojPath });

            // Assert
            descriptors.Should().HaveCount(1);
            var desc = descriptors.First();
            desc.Name.Should().Be("MyVbService");
            desc.VbprojPath.Should().Be(vbprojPath);
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
    /// Verifica che ProjectDescriptor riconosca un singolo file sorgente '.vb' indipendente passato come percorso.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverSingleVbFile()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), "SingleModule_" + Guid.NewGuid().ToString("N") + ".vb");
        File.WriteAllText(tempFile, "Public Class SingleModule\nEnd Class");

        try
        {
            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { tempFile });

            // Assert
            descriptors.Should().HaveCount(1);
            var desc = descriptors.First();
            desc.SourceFiles.Should().ContainSingle(f => f == tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifica che ProjectDescriptor scansioni correttamente una Solution '.sln' contenente progetti VB.NET
    /// (identificati dal GUID di progetto standard di Visual Basic: '{F184B08F-C81C-45F6-A57F-5ABD9991F28F}').
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverVbNetProjectsFromSlnFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_VbSln_" + Guid.NewGuid().ToString("N"));
        var projDir = Path.Combine(tempDir, "VbApp");
        Directory.CreateDirectory(projDir);

        try
        {
            var vbprojPath = Path.Combine(projDir, "VbApp.vbproj");
            File.WriteAllText(vbprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

            var srcFile = Path.Combine(projDir, "Module1.vb");
            File.WriteAllText(srcFile, "Public Module Module1\nEnd Module");

            var slnPath = Path.Combine(tempDir, "TestApp.sln");
            var slnContent = """
                Microsoft Visual Studio Solution File, Format Version 12.00
                Project("{F184B08F-C81C-45F6-A57F-5ABD9991F28F}") = "VbApp", "VbApp\VbApp.vbproj", "{11111111-2222-3333-4444-555555555555}"
                EndProject
                Global
                EndGlobal
                """;
            File.WriteAllText(slnPath, slnContent);

            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { slnPath });

            // Assert
            descriptors.Should().HaveCount(1);
            var desc = descriptors.First();
            desc.Name.Should().Be("VbApp");
            desc.VbprojPath.Should().Be(vbprojPath);
            desc.SourceFiles.Should().Contain(srcFile);
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
