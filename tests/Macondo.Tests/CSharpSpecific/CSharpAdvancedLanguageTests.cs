using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpAdvancedLanguageTests
{
    /// <summary>
    /// Verify that CSharpExtractor extracts generic classes with type constraints ('where TEntity : class')
    /// and generic methods, preserving the names of type parameters and declared types.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractGenericClassesAndMethods()
    {
        // Arrange
        var code = """
            namespace MyApp.Generics
            {
                public class Repository<TEntity, TKey> where TEntity : class
                {
                    public TEntity FindById(TKey id) => default!;
                    public void Save<TOperation>(TEntity entity, TOperation op) { }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("GenericRepo.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var repoClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name.StartsWith("Repository"));
        repoClass.Should().NotBeNull();
        repoClass!.QualifiedName.Should().Be("MyApp.Generics.Repository");

        var findMethod = graph.Symbols.FirstOrDefault(s => s.Name == "FindById");
        findMethod.Should().NotBeNull();
        findMethod!.ReturnType.Should().Be("TEntity");
        findMethod.Parameters.Should().ContainSingle(p => p.Name == "id" && p.DeclaredTypeName == "TKey");

        var saveMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Save");
        saveMethod.Should().NotBeNull();
        saveMethod!.Parameters.Should().HaveCount(2);
        saveMethod.Parameters[0].DeclaredTypeName.Should().Be("TEntity");
        saveMethod.Parameters[1].DeclaredTypeName.Should().Be("TOperation");
    }

    /// <summary>
    /// Check for correct recognition and extraction of asynchronous methods ('async Task<T>', 'async ValueTask')
    /// and the related parameters with cancellation token and default value.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractAsyncMethods_WithTaskReturnTypes()
    {
        // Arrange
        var code = """
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp.Async
            {
                public class RemoteService
                {
                    public async Task<string> FetchDataAsync(string endpoint, CancellationToken ct = default)
                    {
                        await Task.Delay(100, ct);
                        return "payload";
                    }

                    public async ValueTask ExecuteAsync()
                    {
                        await Task.CompletedTask;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("RemoteService.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var fetchMethod = graph.Symbols.FirstOrDefault(s => s.Name == "FetchDataAsync");
        fetchMethod.Should().NotBeNull();
        fetchMethod!.ReturnType.Should().Be("Task<string>");
        fetchMethod.Parameters.Should().HaveCount(2);
        fetchMethod.Parameters[0].DeclaredTypeName.Should().Be("string");
        fetchMethod.Parameters[1].IsOptional.Should().BeTrue();

        var execMethod = graph.Symbols.FirstOrDefault(s => s.Name == "ExecuteAsync");
        execMethod.Should().NotBeNull();
        execMethod!.ReturnType.Should().Be("ValueTask");
    }

    /// <summary>
    /// Tests extraction of nested types within container classes (nested classes and nested enums)
    /// and the creation of CONTAINS semantic relations towards the parent class.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractNestedTypes_AndWireContainmentRelationships()
    {
        // Arrange
        var code = """
            namespace MyApp.Structures
            {
                public class OuterContainer
                {
                    public class NestedWorker
                    {
                        public void DoNestedWork() { }
                    }

                    public enum InnerStatus
                    {
                        Ok,
                        Failed
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Containers.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var outerClass = graph.Symbols.First(s => s.Name == "OuterContainer");
        var nestedWorker = graph.Symbols.First(s => s.Name == "NestedWorker");
        var innerStatus = graph.Symbols.First(s => s.Name == "InnerStatus");

        nestedWorker.QualifiedName.Should().Be("MyApp.Structures.OuterContainer.NestedWorker");
        innerStatus.QualifiedName.Should().Be("MyApp.Structures.OuterContainer.InnerStatus");

        var outerChildren = graph.GetChildren(outerClass.Id).ToList();
        outerChildren.Should().Contain(s => s.Id == nestedWorker.Id);
        outerChildren.Should().Contain(s => s.Id == innerStatus.Id);
    }

    /// <summary>
    /// Tests extraction of multiple comma-separated variable/field declarations on the same line
    /// (es. 'int _rows = 10, _columns = 20, _depth;').
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractMultipleFieldDeclarations_OnSingleLine()
    {
        // Arrange
        var code = """
            namespace MyApp.Fields
            {
                public class MatrixConfig
                {
                    private int _rows = 10, _columns = 20, _depth;
                    public static readonly double Pi = 3.14, E = 2.71;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("MatrixConfig.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var rowsField = graph.Symbols.FirstOrDefault(s => s.Name == "_rows");
        var colsField = graph.Symbols.FirstOrDefault(s => s.Name == "_columns");
        var depthField = graph.Symbols.FirstOrDefault(s => s.Name == "_depth");

        rowsField.Should().NotBeNull();
        rowsField!.DeclaredTypeName.Should().Be("int");
        colsField.Should().NotBeNull();
        colsField!.DeclaredTypeName.Should().Be("int");
        depthField.Should().NotBeNull();
        depthField!.DeclaredTypeName.Should().Be("int");

        var piField = graph.Symbols.FirstOrDefault(s => s.Name == "Pi");
        piField.Should().NotBeNull();
        piField!.IsStatic.Should().BeTrue();
        piField.DeclaredTypeName.Should().Be("double");
    }

    /// <summary>
    /// Verifies the extraction and representation of nullable types (e.g. 'string?', 'int?') and nominative tuples.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractNullableAndTupleReturnTypes()
    {
        // Arrange
        var code = """
            namespace MyApp.Types
            {
                public class QueryEngine
                {
                    public string? FindName(int? id) => null;
                    public (int Count, string Label) GetStats() => (0, "Empty");
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("QueryEngine.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var findMethod = graph.Symbols.First(s => s.Name == "FindName");
        findMethod.ReturnType.Should().Be("string?");
        findMethod.Parameters[0].DeclaredTypeName.Should().Be("int?");

        var getStatsMethod = graph.Symbols.First(s => s.Name == "GetStats");
        getStatsMethod.ReturnType.Should().Be("(int Count, string Label)");
    }

    /// <summary>
    /// Check C# 12+ syntactic novelties as records with positional primary constructors,
    /// 'required' property and 'init' setter.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractPrimaryConstructors_AndInitProperties()
    {
        // Arrange
        var code = """
            namespace MyApp.Modern
            {
                public record PersonRecord(string FirstName, string LastName, int Age);

                public class ModernOptions
                {
                    public required string ApiKey { get; init; }
                    public int TimeoutSeconds { get; init; } = 30;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Modern.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var personRecord = graph.Symbols.First(s => s.Kind == SymbolKind.Record && s.Name == "PersonRecord");
        personRecord.Should().NotBeNull();

        var apiKeyProp = graph.Symbols.First(s => s.Name == "ApiKey");
        apiKeyProp.DeclaredTypeName.Should().Be("string");
        apiKeyProp.Accessibility.Should().Be(Accessibility.Public);

        var timeoutProp = graph.Symbols.First(s => s.Name == "TimeoutSeconds");
        timeoutProp.DeclaredTypeName.Should().Be("int");
    }
}
