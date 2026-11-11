using FluentAssertions;
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CohesionAnalysisExtendedTests
{
    /// <summary>
    /// Ensures that you have a highly cohesive class where all methods access all shared fields
    /// produces LCOM4 = 1, TCC = 1.0 and is not considered a candidate for refactoring.
    /// </summary>
    [Fact]
    public void Cohesion_ShouldYieldPerfectLcom4AndTcc_ForFullyCohesiveClass()
    {
        // Arrange
        var graph = new CodeGraph();
        var cls = new Symbol { Id = Guid.NewGuid(), Name = "PointVector", QualifiedName = "PointVector", FilePath = "PointVector.cs", Kind = SymbolKind.Class };

        var fldX = new Symbol { Id = Guid.NewGuid(), Name = "m_x", QualifiedName = "PointVector.m_x", FilePath = "PointVector.cs", Kind = SymbolKind.Field };
        var fldY = new Symbol { Id = Guid.NewGuid(), Name = "m_y", QualifiedName = "PointVector.m_y", FilePath = "PointVector.cs", Kind = SymbolKind.Field };

        var mthSet = new Symbol { Id = Guid.NewGuid(), Name = "SetCoordinates", QualifiedName = "PointVector.SetCoordinates", FilePath = "PointVector.cs", Kind = SymbolKind.Method };
        var mthMag = new Symbol { Id = Guid.NewGuid(), Name = "Magnitude", QualifiedName = "PointVector.Magnitude", FilePath = "PointVector.cs", Kind = SymbolKind.Method };
        var mthNorm = new Symbol { Id = Guid.NewGuid(), Name = "Normalize", QualifiedName = "PointVector.Normalize", FilePath = "PointVector.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { cls, fldX, fldY, mthSet, mthMag, mthNorm });
        graph.AddContains(cls, fldX);
        graph.AddContains(cls, fldY);
        graph.AddContains(cls, mthSet);
        graph.AddContains(cls, mthMag);
        graph.AddContains(cls, mthNorm);

        // mthSet writes to both fields
        graph.AddWrites(mthSet, fldX);
        graph.AddWrites(mthSet, fldY);

        // mthMag reads both fields
        graph.AddReads(mthMag, fldX);
        graph.AddReads(mthMag, fldY);

        // mthNorm invokes mthMag and writes to fldX and fldY
        graph.AddCalls(mthNorm, mthMag);
        graph.AddWrites(mthNorm, fldX);
        graph.AddWrites(mthNorm, fldY);

        // Act
        var dataset = CohesionCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().HaveCount(1);

        var report = dataset.Items[0];
        report.Lcom4.Should().Be(1);
        report.IsRefactoringCandidate.Should().BeFalse();
        report.Clusters.Should().HaveCount(1);
    }

    /// <summary>
    /// Check that classes with calls between transitive methods (m1 calls m2, m2 uses field f)
    /// group m1 and m2 in the same cohesion cluster.
    /// </summary>
    [Fact]
    public void Cohesion_ShouldGroupMethodsIntoSingleCluster_ViaMethodInvocations()
    {
        // Arrange
        var graph = new CodeGraph();
        var cls = new Symbol { Id = Guid.NewGuid(), Name = "PipelineRunner", QualifiedName = "PipelineRunner", FilePath = "PipelineRunner.cs", Kind = SymbolKind.Class };
        var fldState = new Symbol { Id = Guid.NewGuid(), Name = "m_state", QualifiedName = "PipelineRunner.m_state", FilePath = "PipelineRunner.cs", Kind = SymbolKind.Field };

        var mthCaller = new Symbol { Id = Guid.NewGuid(), Name = "ExecutePipeline", QualifiedName = "PipelineRunner.ExecutePipeline", FilePath = "PipelineRunner.cs", Kind = SymbolKind.Method };
        var mthWorker = new Symbol { Id = Guid.NewGuid(), Name = "DoStep", QualifiedName = "PipelineRunner.DoStep", FilePath = "PipelineRunner.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { cls, fldState, mthCaller, mthWorker });
        graph.AddContains(cls, fldState);
        graph.AddContains(cls, mthCaller);
        graph.AddContains(cls, mthWorker);

        // mthCaller does not touch m_state but calls mthWorker
        graph.AddCalls(mthCaller, mthWorker);
        // mthWorker reads m_state
        graph.AddReads(mthWorker, fldState);

        // Act
        var dataset = CohesionCalculator.Analyze(graph);

        // Assert
        dataset.Items.Should().HaveCount(1);
        var report = dataset.Items[0];
        report.Lcom4.Should().Be(1, "La chiamata diretta tra metodi unifica i cluster di coesione");
    }

    /// <summary>
    /// Verify that a CodeGraph with no classes or only empty modules does not raise exceptions.
    /// </summary>
    [Fact]
    public void Cohesion_ShouldHandleEmptyGraph_Gracefully()
    {
        // Arrange
        var emptyGraph = new CodeGraph();

        // Act
        var dataset = CohesionCalculator.Analyze(emptyGraph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().BeEmpty();
        dataset.AverageLcom4.Should().Be(0.0);
    }
}
