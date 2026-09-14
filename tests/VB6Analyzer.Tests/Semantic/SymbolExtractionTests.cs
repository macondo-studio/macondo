using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Semantic;

public class SymbolExtractionTests
{
    [Fact]
    public void ModuleExtractor_ShouldExtractSubAndFunctionSymbols()
    {
        // Arrange
        var code = """
            Public Sub DoWork(ByVal id As Long, Optional ByRef name As String = "test")
                Dim localVal As Integer
            End Sub

            Private Function Calculate(ByVal a As Double, ByVal b As Double) As Double
                Calculate = a + b
            End Function
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("TestModule.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var moduleSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Module && s.Name == "TestModule");
        moduleSym.Should().NotBeNull();

        var doWorkSym = graph.Symbols.FirstOrDefault(s => s.Name == "DoWork");
        doWorkSym.Should().NotBeNull();
        doWorkSym!.Kind.Should().Be(SymbolKind.Method);
        doWorkSym.Accessibility.Should().Be(Accessibility.Public);
        doWorkSym.Parameters.Should().HaveCount(2);
        doWorkSym.Parameters[0].Name.Should().Be("id");
        doWorkSym.Parameters[0].DeclaredTypeName.Should().Be("Long");
        doWorkSym.Parameters[0].IsByVal.Should().BeTrue();
        doWorkSym.Parameters[1].Name.Should().Be("name");
        doWorkSym.Parameters[1].IsOptional.Should().BeTrue();

        var calcSym = graph.Symbols.FirstOrDefault(s => s.Name == "Calculate");
        calcSym.Should().NotBeNull();
        calcSym!.Kind.Should().Be(SymbolKind.Method);
        calcSym.Accessibility.Should().Be(Accessibility.Private);
        calcSym.ReturnType.Should().Be("Double");
    }

    [Fact]
    public void ModuleExtractor_ShouldExtractProperties_GetLetSet()
    {
        // Arrange
        var code = """
            Private m_Name As String

            Public Property Get Name() As String
                Name = m_Name
            End Property

            Public Property Let Name(ByVal value As String)
                m_Name = value
            End Property
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("Customer.cls", code);

        // Assert
        result.Success.Should().BeTrue();

        var propGet = graph.Symbols.FirstOrDefault(s => s.Name == "Name" && s.ReturnType == "String");
        propGet.Should().NotBeNull();
        propGet!.Kind.Should().Be(SymbolKind.Property);

        var fieldSym = graph.Symbols.FirstOrDefault(s => s.Name == "m_Name");
        fieldSym.Should().NotBeNull();
        fieldSym!.Kind.Should().Be(SymbolKind.Field);
    }

    [Fact]
    public void ModuleExtractor_ShouldExtractConstantsAndEnums()
    {
        // Arrange
        var code = """
            Public Const MAX_RETRIES As Integer = 5
            Private Const DEFAULT_SERVER = "127.0.0.1"

            Public Enum OrderStatus
                Pending = 0
                Approved = 1
                Cancelled = 2
            End Enum
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("Config.bas", code);

        // Assert
        result.Success.Should().BeTrue();

        var constSym = graph.Symbols.FirstOrDefault(s => s.Name == "MAX_RETRIES");
        constSym.Should().NotBeNull();
        constSym!.Kind.Should().Be(SymbolKind.Constant);
        constSym.Accessibility.Should().Be(Accessibility.Public);

        var enumSym = graph.Symbols.FirstOrDefault(s => s.Name == "OrderStatus");
        enumSym.Should().NotBeNull();
        enumSym!.Kind.Should().Be(SymbolKind.Enum);
    }

    [Fact]
    public void ModuleExtractor_ShouldExtractContainsRelationships()
    {
        // Arrange
        var code = """
            Public Sub MethodA()
            End Sub
            Public Sub MethodB()
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("MyModule.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        var moduleSym = graph.Symbols.First(s => s.Kind == SymbolKind.Module);
        var containsRels = graph.Relationships.Where(r => r.Kind == RelationshipKind.Contains && r.SourceId == moduleSym.Id).ToList();

        containsRels.Should().HaveCount(2);
    }
}
