using FluentAssertions;
using Macondo.App.Models;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6ProjectDiscoveryTests
{
    /// <summary>
    /// Verifica che ProjectDescriptor.DiscoverFromPaths scansioni ricorsivamente una cartella fisica
    /// e raggruppi tutti i sorgenti VB6 (.bas, .cls, .frm) in un descrittore di progetto.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverFromDirectory_WithVb6Files()
    {
        // Arrange: Use temp directory with simulated VB6 files
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_Discovery_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Module1.bas"), "Public Sub Test() : End Sub");
            File.WriteAllText(Path.Combine(tempDir, "Class1.cls"), "Public Sub Run() : End Sub");
            File.WriteAllText(Path.Combine(tempDir, "Form1.frm"), "VERSION 5.00\nBegin VB.Form Form1\nEnd");

            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { tempDir });

            // Assert
            descriptors.Should().NotBeEmpty();
            var desc = descriptors.First();
            desc.SourceFiles.Should().HaveCount(3);
            desc.SourceFiles.Should().Contain(f => f.EndsWith("Module1.bas"));
            desc.SourceFiles.Should().Contain(f => f.EndsWith("Class1.cls"));
            desc.SourceFiles.Should().Contain(f => f.EndsWith("Form1.frm"));
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
    /// Verifica l'estrazione del nome progetto e dei percorsi dei file sorgente dichiarati
    /// all'interno di un file di progetto VB6 (.vbp).
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverFromVbpFile()
    {
        // Arrange: Create simulated .vbp file
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_Vbp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clsPath = Path.Combine(tempDir, "Order.cls");
            File.WriteAllText(clsPath, "Public Sub Process() : End Sub");

            var vbpContent = """
                Type=Exe
                Name="BillingSystem"
                Class=Order; Order.cls
                Startup="Sub Main"
                """;
            var vbpPath = Path.Combine(tempDir, "Billing.vbp");
            File.WriteAllText(vbpPath, vbpContent);

            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { vbpPath });

            // Assert
            descriptors.Should().HaveCount(1);
            var desc = descriptors.First();
            desc.Name.Should().Be("BillingSystem");
            desc.SourceFiles.Should().Contain(clsPath);
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
    /// Verifica il parsing di file di gruppo Visual Basic (.vbg) contenenti molteplici riferimenti a progetti .vbp.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverFromVbgGroupFile()
    {
        // Arrange: Create simulated .vbg file referencing 2 .vbp projects
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_Vbg_" + Guid.NewGuid().ToString("N"));
        var dirProj1 = Path.Combine(tempDir, "Core");
        var dirProj2 = Path.Combine(tempDir, "UI");
        Directory.CreateDirectory(dirProj1);
        Directory.CreateDirectory(dirProj2);

        try
        {
            File.WriteAllText(Path.Combine(dirProj1, "Core.cls"), "Public Sub Init() : End Sub");
            File.WriteAllText(Path.Combine(dirProj1, "Core.vbp"), "Name=\"CoreLib\"\nClass=Core; Core.cls");

            File.WriteAllText(Path.Combine(dirProj2, "Main.frm"), "VERSION 5.00\nBegin VB.Form Main\nEnd");
            File.WriteAllText(Path.Combine(dirProj2, "App.vbp"), "Name=\"AppUI\"\nForm=Main.frm");

            var vbgContent = """
                VBGROUP 5.0
                StartupProject=UI\App.vbp
                Project=Core\Core.vbp
                """;
            var vbgPath = Path.Combine(tempDir, "Workspace.vbg");
            File.WriteAllText(vbgPath, vbgContent);

            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { vbgPath });

            // Assert
            descriptors.Should().HaveCount(2);
            descriptors.Select(d => d.Name).Should().Contain(new[] { "CoreLib", "AppUI" });
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
