using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6ExtendedGrammarAndSyntaxEdgeCasesTests
{
    /// <summary>
    /// Verifica l'estrazione e il parsing di istruzioni multiple collocate sulla stessa riga e separate dai due punti (:).
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldParseMultiStatementLines()
    {
        var code = """
            Public Sub MultiStatementProc()
                Dim x As Integer: Dim y As Integer
                x = 10: y = 20
                If x > 5 Then x = 0: y = 0
            End Sub
            """;

        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("MultiStmt.bas", code);

        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var method = graph.Symbols.FirstOrDefault(s => s.Name == "MultiStatementProc");
        method.Should().NotBeNull();
        method!.Kind.Should().Be(SymbolKind.Method);
    }

    /// <summary>
    /// Verifica l'estrazione di routine e variabili che utilizzano i suffissi di tipo VB6 storici:
    /// % (Integer), & (Long), ! (Single), # (Double), $ (String), @ (Currency).
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldHandleTypeSuffixes()
    {
        var code = """
            Public Sub CalculateValues(ByVal amount@, ByVal name$)
                Dim count%
                Dim total&
                Dim ratio!
                Dim factor#
                count% = 10
                total& = 100000
            End Sub
            """;

        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("TypeSuffixes.bas", code);

        result.Success.Should().BeTrue();
        var method = graph.Symbols.FirstOrDefault(s => s.Name == "CalculateValues");
        method.Should().NotBeNull();
        method!.Parameters.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifica l'estrazione corretta di eventi generati da Control Arrays contenenti il parametro 'Index As Integer'.
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldExtractControlArrayEvents()
    {
        var code = """
            Private Sub cmdButton_Click(Index As Integer)
                Select Case Index
                    Case 0
                        Call ActionOne
                    Case 1
                        Call ActionTwo
                End Select
            End Sub

            Private Sub ActionOne()
            End Sub

            Private Sub ActionTwo()
            End Sub
            """;

        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("FormWithControlArray.frm", code);

        result.Success.Should().BeTrue();
        var clickEvent = graph.Symbols.FirstOrDefault(s => s.Name == "cmdButton_Click");
        clickEvent.Should().NotBeNull();
        clickEvent!.Parameters.Should().HaveCount(1);
        clickEvent.Parameters[0].Name.Should().Be("Index");
    }

    /// <summary>
    /// Verifica il supporto e l'estrazione di eventi e routine all'interno di UserControl (.ctl).
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldExtractUserControlEvents()
    {
        var code = """
            Private Sub UserControl_Initialize()
                Call SetupControl
            End Sub

            Private Sub UserControl_Terminate()
                Call CleanupControl
            End Sub

            Private Sub SetupControl()
            End Sub

            Private Sub CleanupControl()
            End Sub
            """;

        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("MyCustomWidget.ctl", code);

        result.Success.Should().BeTrue();
        var initSym = graph.Symbols.FirstOrDefault(s => s.Name == "UserControl_Initialize");
        var termSym = graph.Symbols.FirstOrDefault(s => s.Name == "UserControl_Terminate");

        initSym.Should().NotBeNull();
        termSym.Should().NotBeNull();
    }

    /// <summary>
    /// Verifica che blocchi con direttive di compilazione condizionale (#If ... #Else ... #End If)
    /// vengano analizzati senza errori di sintassi.
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldParseConditionalCompilationBlocks()
    {
        var code = """
            #Const DEBUG_MODE = 1

            Public Sub ProcessData()
                #If DEBUG_MODE Then
                    Call LogDebugInfo
                #Else
                    Call ExecuteProductionFlow
                #End If
            End Sub

            Private Sub LogDebugInfo()
            End Sub

            Private Sub ExecuteProductionFlow()
            End Sub
            """;

        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("ConditionalModule.bas", code);

        result.Success.Should().BeTrue();
        var proc = graph.Symbols.FirstOrDefault(s => s.Name == "ProcessData");
        proc.Should().NotBeNull();
    }
}
