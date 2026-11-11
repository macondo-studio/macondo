using FluentAssertions;
using Macondo.Semantic.Analytics.DataMatrix;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CrudMatrixTests
{
    /// <summary>
    /// Test detection and mapping of CRUD operations on entities and tables.
    /// </summary>
    [Fact]
    public void CrudMatrix_ShouldInferOperationsAndBuildMatrix()
    {
        // Arrange
        var graph = new CodeGraph();

        var customerEntity = new Symbol { Id = Guid.NewGuid(), Name = "CustomerEntity", QualifiedName = "CustomerEntity", FilePath = "CustomerEntity.cs", Kind = SymbolKind.Class };

        var createMethod = new Symbol { Id = Guid.NewGuid(), Name = "CreateCustomer", QualifiedName = "CustomerRepo.CreateCustomer", FilePath = "CustomerRepo.cs", Kind = SymbolKind.Method };
        var getMethod = new Symbol { Id = Guid.NewGuid(), Name = "GetCustomerById", QualifiedName = "CustomerRepo.GetCustomerById", FilePath = "CustomerRepo.cs", Kind = SymbolKind.Method };
        var updateMethod = new Symbol { Id = Guid.NewGuid(), Name = "UpdateCustomer", QualifiedName = "CustomerRepo.UpdateCustomer", FilePath = "CustomerRepo.cs", Kind = SymbolKind.Method };
        var deleteMethod = new Symbol { Id = Guid.NewGuid(), Name = "DeleteCustomer", QualifiedName = "CustomerRepo.DeleteCustomer", FilePath = "CustomerRepo.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { customerEntity, createMethod, getMethod, updateMethod, deleteMethod });

        // Act
        var dataset = CrudMatrixAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Entities.Should().NotBeEmpty();

        var customerSummary = dataset.EntitySummaries.FirstOrDefault(s => s.EntityName.Contains("Customer"));
        customerSummary.Should().NotBeNull();
        customerSummary!.CreateCount.Should().Be(1);
        customerSummary.ReadCount.Should().Be(1);
        customerSummary.UpdateCount.Should().Be(1);
        customerSummary.DeleteCount.Should().Be(1);
    }
}
