using FluentAssertions;
using Macondo.Semantic.Analytics.DataMatrix;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CrudMatrixExtendedTests
{
    /// <summary>
    /// Verifica che un servizio di sola lettura (Query/Reporting) venga mappato con
    /// ReadCount positivo e 0 operazioni Create, Update e Delete per l'entità target.
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
    /// Verifica che routine con nomi specifici di rimozione (Delete, Remove, Purge)
    /// vengano categorizzate come operazioni di cancellazione (Delete).
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
    /// Verifica che un CodeGraph privo di entità o metodi ritorni un dataset vuoto coerente.
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
