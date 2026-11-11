using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpProjectAnalysisServiceTests
{
    /// <summary>
    /// Tests the complete analysis pipeline (ProjectAnalysisService.AnalyzePaths) applied to a project
    /// Pure C# (.csproj), verifying the creation of project nodes, types, interfaces and IMPLEMENTS relationships.
    /// </summary>
    [Fact]
    public void ProjectAnalysisService_ShouldAnalyzePureCSharpProject()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_Analysis_CS_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var csprojPath = Path.Combine(tempDir, "PaymentCore.csproj");
            File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

            var csFile1 = Path.Combine(tempDir, "IPaymentGateway.cs");
            File.WriteAllText(csFile1, """
                namespace PaymentCore;
                public interface IPaymentGateway
                {
                    bool Pay(decimal amount);
                }
                """);

            var csFile2 = Path.Combine(tempDir, "StripeGateway.cs");
            File.WriteAllText(csFile2, """
                namespace PaymentCore;
                public class StripeGateway : IPaymentGateway
                {
                    public bool Pay(decimal amount) => true;
                }
                """);

            var progressReports = new List<ProjectAnalysisProgress>();
            var progress = new Progress<ProjectAnalysisProgress>(p => progressReports.Add(p));

            // Act
            var visualGraph = ProjectAnalysisService.AnalyzePaths(new[] { csprojPath }, progress);

            // Assert
            visualGraph.Should().NotBeNull();
            visualGraph.Nodes.Should().NotBeEmpty();

            // Project root node should be present
            var projNode = visualGraph.Nodes.FirstOrDefault(n => n.Kind == SymbolKind.Project && n.Name == "PaymentCore");
            projNode.Should().NotBeNull();

            // Interface and class nodes should be present
            visualGraph.Nodes.Should().Contain(n => n.Name == "IPaymentGateway");
            visualGraph.Nodes.Should().Contain(n => n.Name == "StripeGateway");

            // Implements edge should be present
            visualGraph.Edges.Should().Contain(e => e.Kind == RelationshipKind.Implements);
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
    /// Test combined (hybrid) analysis of a Workspace simultaneously containing legacy VB6 projects (.vbp)
    /// and C# projects (.csproj), with correct generation of the unified visual graph containing both project roots.
    /// </summary>
    [Fact]
    public void ProjectAnalysisService_ShouldAnalyzeHybridWorkspace_WithBothVB6AndCSharpProjects()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_Hybrid_" + Guid.NewGuid().ToString("N"));
        var vb6Dir = Path.Combine(tempDir, "LegacyVb6");
        var csDir = Path.Combine(tempDir, "ModernCSharp");
        Directory.CreateDirectory(vb6Dir);
        Directory.CreateDirectory(csDir);

        try
        {
            // VB6 Project
            var vbpPath = Path.Combine(vb6Dir, "LegacyApp.vbp");
            File.WriteAllText(vbpPath, "Name=\"LegacyApp\"\nClass=LegacyHelper; LegacyHelper.cls");
            File.WriteAllText(Path.Combine(vb6Dir, "LegacyHelper.cls"), "Public Sub DoLegacy() : End Sub");

            // C# Project
            var csprojPath = Path.Combine(csDir, "ModernApp.csproj");
            File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(csDir, "ModernWorker.cs"), "public class ModernWorker { public void DoModern() {} }");

            // Act
            var visualGraph = ProjectAnalysisService.AnalyzePaths(new[] { vbpPath, csprojPath });

            // Assert
            visualGraph.Should().NotBeNull();

            var nodes = visualGraph.Nodes.ToList();
            nodes.Should().Contain(n => n.Name == "LegacyApp" && n.Kind == SymbolKind.Project);
            nodes.Should().Contain(n => n.Name == "ModernApp" && n.Kind == SymbolKind.Project);
            nodes.Should().Contain(n => n.Name == "LegacyHelper");
            nodes.Should().Contain(n => n.Name == "ModernWorker");
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
    /// Tests fallback behavior when passed an empty list of routes,
    /// ensuring that an empty VisualCodeGraph is returned without raising exceptions.
    /// </summary>
    [Fact]
    public void ProjectAnalysisService_ShouldReturnEmptyGraph_WhenNoProjectsFound()
    {
        // Act
        var visualGraph = ProjectAnalysisService.AnalyzePaths(Array.Empty<string>());

        // Assert
        visualGraph.Should().NotBeNull();
        visualGraph.Nodes.Should().BeEmpty();
    }
}
