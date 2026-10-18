using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.VisualBasicNetSpecific;

public class VisualBasicNetAdvancedLanguageEdgeCasesTests
{
    /// <summary>
    /// Verifica che VB.NET gestisca correttamente la totale insensibilità alle maiuscole/minuscole (case-insensitivity),
    /// risolvendo chiamate e tipi anche con discrepanze di casing.
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldHandleCaseInsensitivity_InCallsAndTypeResolution()
    {
        // Arrange
        var code = """
            Public Class AccountManager
                Public Sub PROCESSDEPOSIT(amount As Decimal)
                End Sub

                Public Sub Execute()
                    processdeposit(100D)
                    Me.ProcessDeposit(200D)
                End Sub
            End Class
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("AccountManager.vb", code);

        // Assert
        result.Success.Should().BeTrue();

        var targetMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name.Equals("PROCESSDEPOSIT", StringComparison.OrdinalIgnoreCase));
        var callerMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name.Equals("Execute", StringComparison.OrdinalIgnoreCase));

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == callerMethod.Id && r.TargetId == targetMethod.Id).ToList();
        calls.Should().NotBeEmpty("VB.NET case-insensitivity must resolve calls regardless of identifier casing");
    }

    /// <summary>
    /// Verifica il corretto parsing ed estrazione sia in presenza della prosecuzione di riga esplicita con underscore ('_')
    /// sia con le continuazioni di riga implicite moderne di VB.NET (dopo operatori, virgole, ecc.).
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldHandleLineContinuations_UnderscoreAndImplicit()
    {
        // Arrange
        var code = """
            Public Class Formatter
                ' Continuazione esplicita con underscore
                Public Sub MultiLineMethod(ByVal paramA As String, _
                                           ByVal paramB As Integer, _
                                           ByVal paramC As Boolean)
                    ' Continuazione implicita dopo operatore
                    Dim total = 10 +
                                20 +
                                30
                End Sub
            End Class
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("Formatter.vb", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var method = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "MultiLineMethod");
        method.Should().NotBeNull();
        method!.Parameters.Should().HaveCount(3);
    }

    /// <summary>
    /// Verifica l'estrazione di classi e metodi generici ('(Of T)', '(Of T As Class)').
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldExtractGenericClassesAndMethods()
    {
        // Arrange
        var code = """
            Public Class Repository(Of T As Class)
                Public Sub Save(item As T)
                End Sub

                Public Function Transform(Of TResult)(input As T) As TResult
                    Return Nothing
                End Function
            End Class
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("Repository.vb", code);

        // Assert
        result.Success.Should().BeTrue();

        var repoClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Repository");
        repoClass.Should().NotBeNull();

        var saveMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "Save");
        saveMethod.Should().NotBeNull();

        var transformMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "Transform");
        transformMethod.Should().NotBeNull();
    }

    /// <summary>
    /// Verifica l'estrazione di gestori di eventi con la clausola 'Handles' (es. 'Handles Button1.Click').
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldExtractHandlesClauseForEventHandlers()
    {
        // Arrange
        var code = """
            Public Class MainForm
                Private WithEvents btnSubmit As Object

                Private Sub btnSubmit_Click(ByVal sender As Object, ByVal e As System.EventArgs) Handles btnSubmit.Click
                End Sub
            End Class
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("MainForm.vb", code);

        // Assert
        result.Success.Should().BeTrue();

        var clickMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "btnSubmit_Click");
        clickMethod.Should().NotBeNull();
        clickMethod!.Parameters.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifica l'estrazione degli operatori sovraccaricati ('Public Shared Operator + (...)').
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldExtractOperatorOverloading()
    {
        // Arrange
        var code = """
            Public Structure Vector2D
                Public X As Double
                Public Y As Double

                Public Shared Operator +(ByVal v1 As Vector2D, ByVal v2 As Vector2D) As Vector2D
                    Dim res As Vector2D
                    res.X = v1.X + v2.X
                    res.Y = v1.Y + v2.Y
                    Return res
                End Operator
            End Structure
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("Vector2D.vb", code);

        // Assert
        result.Success.Should().BeTrue();

        var opSymbol = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name.Contains("+"));
        opSymbol.Should().NotBeNull();
        opSymbol!.Parameters.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifica l'implementazione multipla di interfacce ('Implements IDisposable, ICloneable').
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldHandleMultipleInterfaceImplementations()
    {
        // Arrange
        var code = """
            Public Interface IDisposable
                Sub Dispose()
            End Interface

            Public Interface ICloneable
                Function Clone() As Object
            End Interface

            Public Class ResourceHolder
                Implements IDisposable, ICloneable

                Public Sub Dispose() Implements IDisposable.Dispose
                End Sub

                Public Function Clone() As Object Implements ICloneable.Clone
                    Return Me
                End Function
            End Class
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("ResourceHolder.vb", code);

        // Assert
        result.Success.Should().BeTrue();

        var holderClass = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "ResourceHolder");
        var dispIface = graph.Symbols.First(s => s.Kind == SymbolKind.Interface && s.Name == "IDisposable");
        var cloneIface = graph.Symbols.First(s => s.Kind == SymbolKind.Interface && s.Name == "ICloneable");

        var dispImpl = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Implements && r.SourceId == holderClass.Id && r.TargetId == dispIface.Id);
        dispImpl.Should().NotBeNull("ResourceHolder must implement IDisposable");

        var cloneImpl = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Implements && r.SourceId == holderClass.Id && r.TargetId == cloneIface.Id);
        cloneImpl.Should().NotBeNull("ResourceHolder must implement ICloneable");
    }
}
