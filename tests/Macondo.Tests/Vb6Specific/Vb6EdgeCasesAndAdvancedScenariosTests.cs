using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6EdgeCasesAndAdvancedScenariosTests
{
    /// <summary>
    /// Checks for multiple statements concatenated on the same line via colons (':')
    /// are analyzed correctly by extracting all invocations and assignments.
    /// </summary>
    [Fact]
    public void MultiStatementOnSingleLine_ShouldExtractAllCallsAndStatements()
    {
        // Arrange
        var code = """
            Public Sub ExecutePipeline()
                Dim a As Integer: Dim b As Integer: a = 10: b = 20: Call WorkerSub(a, b)
            End Sub

            Private Sub WorkerSub(ByVal x As Integer, ByVal y As Integer)
            End Sub
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeSingleModule("Pipeline.bas", code);

        // Assert
        var execSub = graph.Symbols.FirstOrDefault(s => s.Name == "ExecutePipeline");
        var workerSub = graph.Symbols.FirstOrDefault(s => s.Name == "WorkerSub");

        execSub.Should().NotBeNull();
        workerSub.Should().NotBeNull();

        var callRel = graph.Relationships.FirstOrDefault(r => r.SourceId == execSub!.Id && r.TargetId == workerSub!.Id);
        callRel.Should().NotBeNull();
        callRel!.Kind.Should().Be(RelationshipKind.Calls);
    }

    /// <summary>
    /// Verify correct handling of VB6 exception handling constructs:
    /// 'On Error GoTo ErrHandler', 'On Error Resume Next', 'On Error GoTo 0' and the error handler label.
    /// </summary>
    [Fact]
    public void ErrorHandling_OnErrorGoTo_ShouldParseAndExtractRoutines()
    {
        // Arrange
        var code = """
            Public Sub SafeOperation()
                On Error GoTo ErrHandler
                
                Call RiskyAction
                
                On Error Resume Next
                Call MinorAction
                On Error GoTo 0
                Exit Sub
                
            ErrHandler:
                Call LogError
            End Sub

            Private Sub RiskyAction()
            End Sub

            Private Sub MinorAction()
            End Sub

            Private Sub LogError()
            End Sub
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeSingleModule("ErrorHandler.bas", code);

        // Assert
        var safeSub = graph.Symbols.FirstOrDefault(s => s.Name == "SafeOperation");
        var riskySub = graph.Symbols.FirstOrDefault(s => s.Name == "RiskyAction");
        var logSub = graph.Symbols.FirstOrDefault(s => s.Name == "LogError");

        safeSub.Should().NotBeNull();
        riskySub.Should().NotBeNull();
        logSub.Should().NotBeNull();

        graph.Relationships.Should().Contain(r => r.SourceId == safeSub!.Id && r.TargetId == riskySub!.Id);
        graph.Relationships.Should().Contain(r => r.SourceId == safeSub!.Id && r.TargetId == logSub!.Id);
    }

    /// <summary>
    /// Test extraction of external Win32 API declarations via 'Declare Function ... Lib "..." Alias "..."'
    /// and the correct resolution of calls to them.
    /// </summary>
    [Fact]
    public void Win32ApiDeclare_ShouldExtractExternalFunctionDeclaration()
    {
        // Arrange
        var code = """
            Private Declare Function GetTickCount Lib "kernel32" () As Long

            Public Sub MeasurePerformance()
                Dim startTick As Long
                startTick = GetTickCount()
            End Sub
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeSingleModule("NativeTimer.bas", code);

        // Assert
        var declareSym = graph.Symbols.FirstOrDefault(s => s.Name == "GetTickCount");
        var measureSub = graph.Symbols.FirstOrDefault(s => s.Name == "MeasurePerformance");

        declareSym.Should().NotBeNull();
        declareSym!.Kind.Should().Be(SymbolKind.Method);
        measureSub.Should().NotBeNull();

        graph.Relationships.Should().Contain(r => r.SourceId == measureSub!.Id && r.TargetId == declareSym.Id && r.Kind == RelationshipKind.Calls);
    }

    /// <summary>
    /// Check the handling of the 'TypeOf ... Is ...' operator for dynamic type checking in If/Then blocks.
    /// </summary>
    [Fact]
    public void TypeOfIsPatternMatching_ShouldParseWithoutErrors()
    {
        // Arrange
        var code = """
            Public Sub CheckControl(ByVal ctrl As Object)
                If TypeOf ctrl Is Form Then
                    Call ProcessForm(ctrl)
                End If
            End Sub

            Private Sub ProcessForm(ByVal f As Object)
            End Sub
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeSingleModule("TypeCheck.bas", code);

        // Assert
        var checkSub = graph.Symbols.FirstOrDefault(s => s.Name == "CheckControl");
        var procSub = graph.Symbols.FirstOrDefault(s => s.Name == "ProcessForm");

        checkSub.Should().NotBeNull();
        procSub.Should().NotBeNull();
        graph.Relationships.Should().Contain(r => r.SourceId == checkSub!.Id && r.TargetId == procSub!.Id);
    }

    /// <summary>
    /// Verify that the dynamic array manipulation instructions 'ReDim' and 'ReDim Preserve'
    /// do not generate conflicts or anomalies in the resolution of the symbols.
    /// </summary>
    [Fact]
    public void ReDimPreserve_ShouldParseAndHandleArrays()
    {
        // Arrange
        var code = """
            Public Sub ManageDynamicBuffer()
                Dim buffer() As String
                ReDim buffer(10)
                buffer(0) = "Initial"
                ReDim Preserve buffer(20)
                buffer(15) = "Extended"
                Call FlushBuffer(buffer)
            End Sub

            Private Sub FlushBuffer(ByRef buf() As String)
            End Sub
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeSingleModule("ArrayMod.bas", code);

        // Assert
        var manageSub = graph.Symbols.FirstOrDefault(s => s.Name == "ManageDynamicBuffer");
        var flushSub = graph.Symbols.FirstOrDefault(s => s.Name == "FlushBuffer");

        manageSub.Should().NotBeNull();
        flushSub.Should().NotBeNull();
        graph.Relationships.Should().Contain(r => r.SourceId == manageSub!.Id && r.TargetId == flushSub!.Id);
    }
}
