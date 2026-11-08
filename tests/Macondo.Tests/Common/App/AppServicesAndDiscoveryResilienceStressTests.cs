using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.App;

public class AppServicesAndDiscoveryResilienceStressTests
{
    #region Project Discovery Resilience and Mixed Path Formats

    /// <summary>
    /// Verifica che ProjectDescriptor.DiscoverFromPaths gestisca in modo resiliente percorsi misti
    /// contenenti directory inesistenti, file non validi e separatori / o \ alternati.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldBeResilientToInvalidPathsAndMixedSeparators()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Discovery_Stress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var validFile = Path.Combine(tempDir, "ValidModule.bas");
            File.WriteAllText(validFile, "Public Sub DoWork() : End Sub");

            var nonExistentPath = Path.Combine(tempDir, "DoesNotExist", "Missing.vbp");
            var mixedSlashPath = tempDir.Replace('\\', '/') + "/ValidModule.bas";

            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { nonExistentPath, mixedSlashPath });

            // Assert
            descriptors.Should().NotBeNull();
            descriptors.Should().HaveCount(1);
            descriptors[0].SourceFiles.Should().Contain(validFile);
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
    /// Verifica che la scansione di una cartella contenente file di controllo utente (.ctl) e proprietà (.pag)
    /// includa correttamente tutti i file sorgente VB6 rilevanti.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverAllVb6SourceExtensions_IncludingUserControls()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Vb6_Ext_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "CustomControl.ctl"), "VERSION 5.00\nBegin VB.UserControl CustomControl\nEnd");
            File.WriteAllText(Path.Combine(tempDir, "StandardMod.bas"), "Public Sub Run() : End Sub");
            File.WriteAllText(Path.Combine(tempDir, "ClassMod.cls"), "Public Sub Execute() : End Sub");
            File.WriteAllText(Path.Combine(tempDir, "FormUI.frm"), "VERSION 5.00\nBegin VB.Form FormUI\nEnd");

            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { tempDir });

            // Assert
            descriptors.Should().HaveCount(1);
            var desc = descriptors[0];
            desc.SourceFiles.Should().HaveCount(4);
            desc.SourceFiles.Should().Contain(f => f.EndsWith("CustomControl.ctl"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    #endregion

    #region Large Project Cache Snapshot Stress Test

    /// <summary>
    /// Verifica che il ripristino di uno snapshot di cache di grandi dimensioni (500 simboli, 200 righe Blast Radius)
    /// conservi tutte le coordinate, i livelli di rischio e le gerarchie di nodi.
    /// </summary>
    [Fact]
    public void ProjectCacheService_ShouldRestoreLargeSnapshotsAccurately()
    {
        // Arrange
        var snapshot = new ProjectCacheSnapshot
        {
            SchemaVersion = 1,
            ProjectName = "EnterpriseApp",
            FolderPath = @"C:\Fake\EnterpriseApp",
            AnalyzedAtUtc = DateTime.UtcNow
        };

        for (int i = 0; i < 200; i++)
        {
            var symId = Guid.NewGuid();
            var sym = new Symbol
            {
                Id = symId,
                Name = $"Service_{i}",
                QualifiedName = $"Enterprise.Service_{i}",
                FilePath = $"Service_{i}.cls",
                Kind = SymbolKind.Class
            };
            snapshot.Symbols.Add(sym);

            snapshot.LayoutNodes.Add(new NodeLayoutSnapshot
            {
                Id = symId,
                X = i * 10.5,
                Y = i * 5.2,
                IsExpanded = (i % 2 == 0)
            });

            snapshot.BlastRadiusRows.Add(new BlastRadiusRowSnapshot
            {
                NodeId = symId,
                ProjectName = "EnterpriseApp",
                Name = $"Service_{i}",
                QualifiedName = $"Enterprise.Service_{i}",
                ContainerName = "EnterpriseApp",
                Kind = "Class",
                DirectCallersCount = i,
                IndirectCallersCount = i * 2,
                MaxDepth = 3,
                RiskLevel = (i % 3 == 0) ? ImpactRiskLevel.Critical : ImpactRiskLevel.Low,
                TotalImpactedCount = i * 3
            });
        }

        // Act
        var (graph, rows) = ProjectCacheService.Instance.RestoreGraphAndRows(snapshot);

        // Assert
        graph.Should().NotBeNull();
        graph.Nodes.Should().HaveCount(201); // 1 root + 200 class nodes
        rows.Should().HaveCount(200);

        var firstRow = rows.First(r => r.DirectCallersCount == 0);
        firstRow.RiskLevel.Should().Be(ImpactRiskLevel.Critical);

        var node50 = graph.Nodes.First(n => n.Name == "Service_50");
        node50.X.Should().Be(50 * 10.5);
        node50.Y.Should().Be(50 * 5.2);
        node50.IsExpanded.Should().BeTrue();
    }

    #endregion

    #region Localization Dynamic Argument Robustness

    /// <summary>
    /// Verifica che LocalizationService.Get gestisca stringhe con parametri posizionali complessi,
    /// formati multipli e valori nulli negli argomenti senza lanciare eccezioni.
    /// </summary>
    [Fact]
    public void Localization_ShouldHandleComplexArgsAndNullValues_Safely()
    {
        var service = LocalizationService.Instance;

        // Act & Assert
        var act1 = () => service.Get("Status_StatsFormat", null!, null!, null!, null!);
        act1.Should().NotThrow();

        var act2 = () => service.Get("Status_StatsFormat", 1, 2, 3, 4, "Extra5", "Extra6");
        act2.Should().NotThrow();
    }

    #endregion
}
