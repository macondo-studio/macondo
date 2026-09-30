using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6ComprehensiveLanguageEdgeCasesTests
{
    #region Property Get / Property Let / Property Set Triplets

    /// <summary>
    /// Verifica l'estrazione e la risoluzione di proprietà VB6 complete provviste di triplette
    /// Property Get, Property Let (per tipi primitivi) e Property Set (per riferimenti a oggetti).
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractPropertyGetLetSetTriplets()
    {
        // Arrange
        var code = """
            Public Class Node
            End Class

            Private m_Node As Node
            Private m_Tag As String

            Public Property Get TargetNode() As Node
                Set TargetNode = m_Node
            End Property

            Public Property Set TargetNode(ByRef newObj As Node)
                Set m_Node = newObj
            End Property

            Public Property Get Tag() As String
                Tag = m_Tag
            End Property

            Public Property Let Tag(ByVal value As String)
                m_Tag = value
            End Property
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("PropertyClass.cls", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var properties = graph.Symbols.Where(s => s.Kind == SymbolKind.Property).ToList();
        properties.Should().NotBeEmpty();
        properties.Select(p => p.Name).Should().Contain(new[] { "TargetNode", "Tag" });
    }

    #endregion

    #region Module-Level Const Expressions and Hex Bitmasks

    /// <summary>
    /// Verifica il parsing di costanti a livello di modulo definite tramite espressioni aritmetiche e maschere esadecimali.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractModuleConstantsWithExpressions()
    {
        // Arrange
        var code = """
            Public Const MAX_BUFFER_SIZE As Long = 1024 * 64
            Public Const DEFAULT_TIMEOUT_MS As Long = 5000
            Private Const FLAG_ACTIVE As Long = &H1
            Private Const FLAG_SUSPENDED As Long = &H2
            Private Const MASK_ALL_FLAGS As Long = FLAG_ACTIVE Or FLAG_SUSPENDED

            Public Sub InitializeBuffer()
                Dim bufSize As Long
                bufSize = MAX_BUFFER_SIZE
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("ConstMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var initSub = graph.Symbols.FirstOrDefault(s => s.Name == "InitializeBuffer");
        initSub.Should().NotBeNull();
    }

    #endregion

    #region On Error Resume Next and On Error GoTo 0 Scopes

    /// <summary>
    /// Verifica il parsing di pattern di gestione errori locali con 'On Error Resume Next'
    /// seguito da controllo su 'Err.Number' e ripristino con 'On Error GoTo 0'.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldHandleInlineErrorHandlingAndResumeNext()
    {
        // Arrange
        var code = """
            Public Function TryReadFile(ByVal filePath As String) As Boolean
                On Error Resume Next
                
                Dim fNum As Integer
                fNum = FreeFile
                Open filePath For Input As #fNum
                Close #fNum

                If Err.Number <> 0 Then
                    Err.Clear
                    On Error GoTo 0
                    TryReadFile = False
                    Exit Function
                End If

                On Error GoTo 0
                TryReadFile = True
            End Function
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("FileOps.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var func = graph.Symbols.FirstOrDefault(s => s.Name == "TryReadFile");
        func.Should().NotBeNull();
        func!.ReturnType.Should().Be("Boolean");
    }

    #endregion

    #region Line Labels and Colon-Separated Multi-Statements

    /// <summary>
    /// Verifica il parsing di etichette alfanumeriche combinate con istruzioni multiple
    /// separate da due punti sulla medesima riga.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldSupportLabelsAndInlineMultiStatements()
    {
        // Arrange
        var code = """
            Public Sub ExecuteStateMachine(ByVal state As Long)
                Dim stepCount As Long: stepCount = 0

                If state = 1 Then GoTo StateAlpha
                If state = 2 Then GoTo StateBeta
                Exit Sub

            StateAlpha:
                stepCount = stepCount + 10: Call LogProgress("Alpha Completed"): Exit Sub

            StateBeta:
                stepCount = stepCount + 20: Call LogProgress("Beta Completed"): Exit Sub
            End Sub

            Private Sub LogProgress(ByVal msg As String)
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("StateMachine.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var execSub = graph.Symbols.FirstOrDefault(s => s.Name == "ExecuteStateMachine");
        var logSub = graph.Symbols.FirstOrDefault(s => s.Name == "LogProgress");

        execSub.Should().NotBeNull();
        logSub.Should().NotBeNull();

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == execSub!.Id).ToList();
        calls.Should().NotBeEmpty();
    }

    #endregion
}
