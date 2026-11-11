using FluentAssertions;
using Macondo.Semantic.Analytics.DataMatrix;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CrudMatrixExtendedTests
{
    /// <summary>
    /// Verify that a read-only service (Query/Reporting) is mapped to
    /// Positive ReadCount and 0 Create, Update, and Delete operations for the target entity.
    /// </summary>
    [Fact]
    public void CrudMatrix_ShouldIdentifyReadOnlyReportingService()
    {
        // Arrange
        var graph = new CodeGraph();
        var productEntity = new Symbol { Id = Guid.NewGuid(), Name = "Product", QualifiedName = "Product", FilePath = "Product.cs", Kind = SymbolKind.Class };

        var findById = new Symbol { Id = Guid.NewGuid(), Name = "FindProductById", QualifiedName = "ProductQueryService.FindProductById", FilePath = "ProductQueryService.cs", Kind = SymbolKind.Method };
        var listAll = new Symbol { Id = Guid.NewGuid(), Name = "GetActiveProducts", QualifiedName = "ProductQueryService.GetActiveProducts", FilePath = "ProductQueryService.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { productEntity, findById, listAll });

        // Act
        var dataset = CrudMatrixAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        var summary = dataset.EntitySummaries.FirstOrDefault(s => s.EntityName == "Product");
        summary.Should().NotBeNull();
        summary!.ReadCount.Should().Be(2);
        summary.CreateCount.Should().Be(0);
        summary.UpdateCount.Should().Be(0);
        summary.DeleteCount.Should().Be(0);
    }

    /// <summary>
    /// Check for routines with specific purge names (Delete, Remove, Purge)
    /// are categorized as deletion operations (Delete).
    /// </summary>
    [Fact]
    public void CrudMatrix_ShouldIdentifyDeleteAndPurgeOperations()
    {
        // Arrange
        var graph = new CodeGraph();
        var logEntity = new Symbol { Id = Guid.NewGuid(), Name = "AuditLog", QualifiedName = "AuditLog", FilePath = "AuditLog.cs", Kind = SymbolKind.Class };

        var purgeMethod = new Symbol { Id = Guid.NewGuid(), Name = "PurgeAuditLogs", QualifiedName = "AuditService.PurgeAuditLogs", FilePath = "AuditService.cs", Kind = SymbolKind.Method };
        var removeMethod = new Symbol { Id = Guid.NewGuid(), Name = "RemoveAuditLogEntry", QualifiedName = "AuditService.RemoveAuditLogEntry", FilePath = "AuditService.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { logEntity, purgeMethod, removeMethod });

        // Act
        var dataset = CrudMatrixAnalyzer.Analyze(graph);

        // Assert
        var summary = dataset.EntitySummaries.FirstOrDefault(s => s.EntityName == "AuditLog");
        summary.Should().NotBeNull();
        summary!.DeleteCount.Should().Be(2);
    }

    /// <summary>
    /// Verify that a CodeGraph with no entities or methods returns a consistent empty dataset.
    /// </summary>
    [Fact]
    public void CrudMatrix_ShouldHandleEmptyGraph_Gracefully()
    {
        // Arrange
        var emptyGraph = new CodeGraph();

        // Act
        var dataset = CrudMatrixAnalyzer.Analyze(emptyGraph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Entities.Should().BeEmpty();
        dataset.EntitySummaries.Should().BeEmpty();
    }
}
