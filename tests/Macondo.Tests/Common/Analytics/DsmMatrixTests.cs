using FluentAssertions;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class DsmMatrixTests
{
    /// <summary>
    /// Check the construction of the dependency structure matrix (Design Structure Matrix - DSM)
    /// starting from the nodes and relations of the CodeGraph.
    /// </summary>
    [Fact]
    public void DsmMatrix_ShouldBuildCorrectMatrix_FromGraph()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var dsm = DsmMatrixBuilder.Build(graph);

        // Assert
        dsm.Should().NotBeNull();
        dsm.Size.Should().BeGreaterThan(0);
        dsm.Items.Should().NotBeEmpty();
        dsm.TotalDependencies.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Verify that Tarjan's algorithm for Strongly Connected Components (SCC) identifies
    /// carefully loop dependencies between modules and mark the HasCircularDependencies flag.
    /// </summary>
    [Fact]
    public void DsmMatrix_ShouldDetectTarjanStronglyConnectedComponents_AndMarkCycles()
    {
        // Arrange: Create synthetic cycle A -> B -> C -> A
        var graph = new CodeGraph();
        var modA = new Symbol { Id = Guid.NewGuid(), Name = "ModA", QualifiedName = "ModA", FilePath = "ModA.bas", Kind = SymbolKind.Module };
        var modB = new Symbol { Id = Guid.NewGuid(), Name = "ModB", QualifiedName = "ModB", FilePath = "ModB.bas", Kind = SymbolKind.Module };
        var modC = new Symbol { Id = Guid.NewGuid(), Name = "ModC", QualifiedName = "ModC", FilePath = "ModC.bas", Kind = SymbolKind.Module };
        var modD = new Symbol { Id = Guid.NewGuid(), Name = "ModD", QualifiedName = "ModD", FilePath = "ModD.bas", Kind = SymbolKind.Module }; // isolated

        graph.Symbols.AddRange(new[] { modA, modB, modC, modD });

        graph.AddCalls(modA, modB, 10);
        graph.AddCalls(modB, modC, 20);
        graph.AddCalls(modC, modA, 30);
        graph.AddCalls(modD, modA, 40);

        // Act
        var dsm = DsmMatrixBuilder.Build(graph);

        // Assert
        dsm.StronglyConnectedComponents.Should().HaveCount(1);
        dsm.StronglyConnectedComponents[0].Should().HaveCount(3);
        dsm.StronglyConnectedComponents[0].Should().Contain(new[] { modA.Id, modB.Id, modC.Id });
        dsm.StronglyConnectedComponents[0].Should().NotContain(modD.Id);

        dsm.Items.First(i => i.Id == modA.Id).HasCircularDependencies.Should().BeTrue();
        dsm.Items.First(i => i.Id == modB.Id).HasCircularDependencies.Should().BeTrue();
        dsm.Items.First(i => i.Id == modC.Id).HasCircularDependencies.Should().BeTrue();
        dsm.Items.First(i => i.Id == modD.Id).HasCircularDependencies.Should().BeFalse();

        dsm.TotalCyclicDependencies.Should().Be(3);
    }

    /// <summary>
    /// Verifies that any sorting applied to the DSM matrix (alphabetical, by type, or topological)
    /// keeps the total sum of the dependency weights between cells unchanged.
    /// </summary>
    [Theory]
    [InlineData(DsmOrdering.Alphabetical)]
    [InlineData(DsmOrdering.ByKindAndName)]
    [InlineData(DsmOrdering.TopologicalOrPartitioned)]
    public void DsmMatrix_ShouldPreserveDependencies_WhenSorted(DsmOrdering ordering)
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var dsm = DsmMatrixBuilder.Build(graph, ordering);

        // Assert
        dsm.Should().NotBeNull();
        dsm.TotalDependencies.Should().BeGreaterThan(0);

        int cellWeightSum = 0;
        for (int r = 0; r < dsm.Size; r++)
        {
            for (int c = 0; c < dsm.Size; c++)
            {
                cellWeightSum += dsm.GetCell(r, c).Weight;
            }
        }

        cellWeightSum.Should().Be(dsm.TotalDependencies, "The sum of all cell weights must equal the total number of dependencies");
    }
}
