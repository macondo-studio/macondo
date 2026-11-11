using Antlr4.Runtime;
using FluentAssertions;
using Macondo.Parser.Diagnostics;
using Macondo.Parser.Grammar;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6UncoveredEdgeCasesAndBugsHunterTests
{
    #region Control Arrays and Event Handlers

    /// <summary>
    /// Test parsing and extraction of UI controls grouped into Control Arrays (with Index properties)
    /// and correct generation of HANDLES_EVENT relationships for parameterized event handlers (Index As Integer).
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireHandlesEvent_ForControlArrayEventHandlersWithIndexParameter()
    {
        // Arrange
        var graph = new CodeGraph();
        var formCode = """
            VERSION 5.00
            Begin VB.Form OptionsForm
               Caption = "Options"
               Begin VB.OptionButton optChoice
                  Caption = "Choice A"
                  Index = 0
               End
               Begin VB.OptionButton optChoice
                  Caption = "Choice B"
                  Index = 1
               End
               Begin VB.CommandButton cmdAction
                  Caption = "Run"
                  Index = 0
               End
            End
            Attribute VB_Name = "OptionsForm"

            Private Sub optChoice_Click(Index As Integer)
                Call HandleOptionChanged(Index)
            End Sub

            Private Sub cmdAction_Click(Index As Integer)
                Call ExecuteAction(Index)
            End Sub

            Private Sub HandleOptionChanged(ByVal selected As Integer)
            End Sub

            Private Sub ExecuteAction(ByVal actionId As Integer)
            End Sub
            """;

        var res = ModuleExtractor.ExtractModule(graph, "OptionsForm.frm", formCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        // Act
        builder.BuildGraph(new[] { res });

        // Assert
        res.Success.Should().BeTrue();
        res.ParseErrors.Should().BeEmpty();

        var formSym = graph.Symbols.First(s => s.Kind == SymbolKind.Form && s.Name == "OptionsForm");
        var optClickSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "optChoice_Click");
        var cmdClickSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "cmdAction_Click");

        optClickSym.Should().NotBeNull();
        cmdClickSym.Should().NotBeNull();
        optClickSym!.Parameters.Should().HaveCount(1);
        optClickSym.Parameters[0].Name.Should().Be("Index");

        var eventRels = graph.Relationships.Where(r => r.Kind == RelationshipKind.HandlesEvent).ToList();
        eventRels.Should().Contain(r => r.SourceId == formSym.Id && r.TargetId == optClickSym!.Id);
        eventRels.Should().Contain(r => r.SourceId == formSym.Id && r.TargetId == cmdClickSym!.Id);
    }

    #endregion

    #region GoSub / Return and On...GoTo / On...GoSub Flow Controls

    /// <summary>
    /// Tests parsing of internal GoSub ... Return jump statements and On ... GoTo / On ... GoSub constructs.
    /// </summary>
    [Fact]
    public void Parser_ShouldParseGoSubReturnAndComputedOnGoToStatements()
    {
        // Arrange
        var code = """
            Public Sub LegacyFlowRoutine(ByVal selector As Integer)
                Dim total As Long
                total = 0

                On selector GoTo HandlerOne, HandlerTwo, HandlerThree
                GoSub LocalCalculation
                Exit Sub

            HandlerOne:
                total = 10
                Exit Sub

            HandlerTwo:
                total = 20
                Exit Sub

            HandlerThree:
                total = 30
                Exit Sub

            LocalCalculation:
                total = total + 5
                Return
            End Sub
            """;

        var errors = new List<string>();
        var lexer = new VisualBasic6Lexer(new AntlrInputStream(code));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new CollectingErrorListener(errors));

        var parser = new VisualBasic6Parser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        // Act
        var tree = parser.startRule();

        // Assert
        errors.Should().BeEmpty("GoSub, Return, and On ... GoTo statements should parse cleanly without errors");
        tree.Should().NotBeNull();
    }

    #endregion

    #region Line Continuations with Named Arguments and Complex Expressions

    /// <summary>
    /// Verify correct parsing and extraction of calls with line continuations (_)
    /// on named arguments (ParamName:=Value) and multiline expressions.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldHandleLineContinuationsWithNamedArguments()
    {
        // Arrange
        var code = """
            Public Sub ProcessBatch()
                Call ExecuteComplexOperation( _
                    PrimaryId:=1001, _
                    SecondaryId:=2002, _
                    Description:="Batch Job " & _
                                 "Initiated by user", _
                    IsForced:=True _
                )
            End Sub

            Public Sub ExecuteComplexOperation( _
                ByVal PrimaryId As Long, _
                ByVal SecondaryId As Long, _
                ByVal Description As String, _
                ByVal IsForced As Boolean _
            )
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("LineContMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var processBatch = graph.Symbols.FirstOrDefault(s => s.Name == "ProcessBatch");
        var execOp = graph.Symbols.FirstOrDefault(s => s.Name == "ExecuteComplexOperation");

        processBatch.Should().NotBeNull();
        execOp.Should().NotBeNull();
        execOp!.Parameters.Should().HaveCount(4);

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == processBatch!.Id && r.TargetId == execOp!.Id).ToList();
        calls.Should().NotBeEmpty("Call with named arguments across line continuations must resolve");
    }

    #endregion

    #region Static Variables and Static Sub/Function Declarations

    /// <summary>
    /// Tests parsing and extraction of routines and variables with Static modifier.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractStaticVariablesAndStaticProcedures()
    {
        // Arrange
        var code = """
            Public Static Sub RunAccumulator(ByVal delta As Long)
                Static total As Long
                Static lastTimestamp As Date
                Static isInitialized As Boolean

                If Not isInitialized Then
                    total = 0
                    isInitialized = True
                End If

                total = total + delta
            End Sub

            Public Static Function GetCounter() As Long
                Static callCount As Long
                callCount = callCount + 1
                GetCounter = callCount
            End Function
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("StaticMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var runAcc = graph.Symbols.FirstOrDefault(s => s.Name == "RunAccumulator");
        var getCounter = graph.Symbols.FirstOrDefault(s => s.Name == "GetCounter");

        runAcc.Should().NotBeNull();
        getCounter.Should().NotBeNull();
    }

    #endregion

    #region Multi-Interface Implements and Method Mapping

    /// <summary>
    /// Tests a class that implements multiple distinct interfaces (Implements IFoo, Implements IBar)
    /// and the correct mapping of implementation methods with the Interface_Method prefix.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldResolveMultiInterfaceImplementationsAndInheritedContracts()
    {
        // Arrange
        var graph = new CodeGraph();

        var iPersistable = """
            Public Sub Save(ByVal targetPath As String)
            End Sub

            Public Sub Load(ByVal sourcePath As String)
            End Sub
            """;

        var iValidatable = """
            Public Function Validate() As Boolean
            End Function
            """;

        var entityClass = """
            Implements IPersistable
            Implements IValidatable

            Private Sub IPersistable_Save(ByVal targetPath As String)
                ' Save logic
            End Sub

            Private Sub IPersistable_Load(ByVal sourcePath As String)
                ' Load logic
            End Sub

            Private Function IValidatable_Validate() As Boolean
                IValidatable_Validate = True
            End Function
            """;

        var res1 = ModuleExtractor.ExtractModule(graph, "IPersistable.cls", iPersistable);
        var res2 = ModuleExtractor.ExtractModule(graph, "IValidatable.cls", iValidatable);
        var res3 = ModuleExtractor.ExtractModule(graph, "OrderEntity.cls", entityClass);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        // Act
        builder.BuildGraph(new[] { res1, res2, res3 });

        // Assert
        var orderSym = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "OrderEntity");
        var iPersistSym = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "IPersistable");
        var iValidSym = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "IValidatable");

        var implementsRels = graph.Relationships.Where(r => r.Kind == RelationshipKind.Implements && r.SourceId == orderSym.Id).ToList();
        implementsRels.Should().Contain(r => r.TargetId == iPersistSym.Id);
        implementsRels.Should().Contain(r => r.TargetId == iValidSym.Id);
    }

    #endregion

    #region Mid Statement as L-Value and File I/O Operations

    /// <summary>
    /// Test parsing of Mid$ assignments to the left of equals (Mid Statement L-Value)
    /// and binary and sequential file I/O operations (Open, Put, Get, Close).
    /// </summary>
    [Fact]
    public void Parser_ShouldParseMidStatementLValueAndBinaryFileIO()
    {
        // Arrange
        var code = """
            Public Sub ProcessBinaryRecord()
                Dim buffer As String
                buffer = "XXXXXXXXXX"
                Mid(buffer, 1, 4) = "DATA"
                Mid$(buffer, 5, 2) = "99"

                Dim fileNum As Integer
                fileNum = FreeFile
                Open "C:\temp\data.bin" For Binary Access Read Write As #fileNum
                Put #fileNum, 1, buffer
                Get #fileNum, 1, buffer
                Close #fileNum
            End Sub
            """;

        var errors = new List<string>();
        var lexer = new VisualBasic6Lexer(new AntlrInputStream(code));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new CollectingErrorListener(errors));

        var parser = new VisualBasic6Parser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        // Act
        var tree = parser.startRule();

        // Assert
        errors.Should().BeEmpty("Mid$ L-Value statement and Binary File I/O (Open/Put/Get/Close) should parse cleanly");
        tree.Should().NotBeNull();
    }

    #endregion

    #region ReDim and ReDim Preserve with Multidimensional Bounds

    /// <summary>
    /// Test ReDim Preserve parsing and extraction with multiple dimensions and custom lower bounds.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldSupportReDimPreserveWithCustomBounds()
    {
        // Arrange
        var code = """
            Public Sub ResizeMatrix(ByVal rows As Long, ByVal cols As Long)
                Dim dynamicMatrix() As Double
                ReDim dynamicMatrix(1 To rows, 1 To 10)
                
                ' Redim preserve last dimension
                ReDim Preserve dynamicMatrix(1 To rows, 1 To cols)
                
                Dim simpleArr() As String
                ReDim simpleArr(0 To 100)
                Erase simpleArr
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("ReDimMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var resizeMethod = graph.Symbols.FirstOrDefault(s => s.Name == "ResizeMatrix");
        resizeMethod.Should().NotBeNull();
    }

    #endregion

    #region Default Type Directives (DefInt, DefLng, DefStr, DefBool, DefObj)

    /// <summary>
    /// Verify that historical directives of default type (DefInt, DefLng, DefStr, DefBool, DefObj)
    /// at the module level are analyzed without generating parsing errors.
    /// </summary>
    [Fact]
    public void Parser_ShouldParseModuleLevelDefTypeDirectives()
    {
        // Arrange
        var code = """
            DefInt I-N
            DefLng L
            DefStr S
            DefBool B
            DefObj O

            Public Sub LegacyTypedSub(count, label, flag)
                Dim result
                result = count * 2
            End Sub
            """;

        var errors = new List<string>();
        var lexer = new VisualBasic6Lexer(new AntlrInputStream(code));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new CollectingErrorListener(errors));

        var parser = new VisualBasic6Parser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        // Act
        var tree = parser.startRule();

        // Assert
        errors.Should().BeEmpty("DefInt / DefLng / DefStr / DefBool / DefObj directives should parse without syntax errors");
        tree.Should().NotBeNull();
    }

    #endregion
}
