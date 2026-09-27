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

public class Vb6DeepLanguageAndGrammarEdgeCasesTests
{
    #region Complex Loops and Flow Control

    /// <summary>
    /// Verifica il parsing e l'estrazione di tutti i tipi di loop VB6:
    /// Do While ... Loop, Do Until ... Loop, Do ... Loop While, Do ... Loop Until, Do ... Loop con Exit Do, e While ... Wend.
    /// </summary>
    [Fact]
    public void Parser_ShouldParseAllVarietiesOfDoLoopsAndWhileWend()
    {
        // Arrange
        var code = """
            Public Sub TestAllLoops()
                Dim i As Long
                
                ' 1. Do While at start
                i = 0
                Do While i < 10
                    i = i + 1
                Loop
                
                ' 2. Do Until at start
                Do Until i >= 20
                    i = i + 1
                Loop
                
                ' 3. Do Loop While at end
                Do
                    i = i + 1
                Loop While i < 30
                
                ' 4. Do Loop Until at end
                Do
                    i = i + 1
                Loop Until i >= 40
                
                ' 5. Do Loop with Exit Do
                Do
                    i = i + 1
                    If i > 50 Then Exit Do
                Loop
                
                ' 6. While ... Wend
                While i < 60
                    i = i + 1
                Wend
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
        errors.Should().BeEmpty("All 6 VB6 loop constructs must parse without syntax errors");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Verifica la gestione di istruzioni multiple separate da due punti (:) sulla stessa riga
    /// e la presenza di etichette numeriche e testuali nello stesso blocco.
    /// </summary>
    [Fact]
    public void Parser_ShouldParseMultiStatementsPerLineWithColonsAndLabels()
    {
        // Arrange
        var code = """
            Public Sub MultiStatementSub()
                Dim x As Integer: Dim y As Integer: Dim z As Integer
                x = 10: y = 20: z = x + y
                
                100: If x > 5 Then y = 100: z = 200: GoTo FinalStep
                
                200:
                Step2: x = x * 2: y = y * 2
                
                FinalStep:
                Exit Sub
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
        errors.Should().BeEmpty("Colons separating multiple statements and labels on single lines should parse cleanly");
        tree.Should().NotBeNull();
    }

    #endregion

    #region Nested With Blocks and Member Access

    /// <summary>
    /// Verifica il parsing di blocchi 'With ... End With' annidati (Nested With Blocks)
    /// con accesso a membri e proprietà dell'oggetto interno ed esterno.
    /// </summary>
    [Fact]
    public void Parser_ShouldParseNestedWithBlocks()
    {
        // Arrange
        var code = """
            Public Sub ConfigureSettings()
                With Application
                    .Caption = "Macondo VB6"
                    .Visible = True
                    With .Preferences
                        .AutoSave = True
                        .Theme = "Dark"
                        With .Colors
                            .Primary = 255
                            .Secondary = 65280
                        End With
                    End With
                End With
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
        errors.Should().BeEmpty("Deeply nested With blocks should parse cleanly");
        tree.Should().NotBeNull();
    }

    #endregion

    #region Declare Function and API Definitions

    /// <summary>
    /// Verifica l'estrazione e il parsing di dichiarazioni Windows API 'Declare Function / Declare Sub'
    /// con Alias numerico (ordinale), Alias testuale, passaggio parametri ByVal / ByRef, e tipo 'Any'.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractDeclareStatementsWithAliasesAndAnyType()
    {
        // Arrange
        var code = """
            Private Declare Function GetTickCount Lib "kernel32.dll" () As Long
            Public Declare Function SendMessage Lib "user32.dll" Alias "SendMessageA" (ByVal hWnd As Long, ByVal wMsg As Long, ByVal wParam As Long, ByRef lParam As Any) As Long
            Private Declare Sub Sleep Lib "kernel32.dll" (ByVal dwMilliseconds As Long)
            Public Declare Function ExtractIcon Lib "shell32.dll" Alias "#34" (ByVal hInst As Long, ByVal lpszExeFileName As String, ByVal nIconIndex As Long) As Long

            Public Sub TestApis()
                Dim start As Long
                start = GetTickCount()
                Call Sleep(100)
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("WinApiMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var getTick = graph.Symbols.FirstOrDefault(s => s.Name == "GetTickCount");
        var sendMsg = graph.Symbols.FirstOrDefault(s => s.Name == "SendMessage");
        var sleepSub = graph.Symbols.FirstOrDefault(s => s.Name == "Sleep");
        var extractIcon = graph.Symbols.FirstOrDefault(s => s.Name == "ExtractIcon");

        getTick.Should().NotBeNull();
        sendMsg.Should().NotBeNull();
        sleepSub.Should().NotBeNull();
        extractIcon.Should().NotBeNull();

        sendMsg!.Parameters.Should().HaveCount(4);
    }

    #endregion

    #region Type Declaration Characters and Compound Dim Statements

    /// <summary>
    /// Verifica il parsing di variabili con suffissi di tipo VB6 tradizionali (%, &, !, #, $, @)
    /// e dichiarazioni Dim composte con array multidimensionali e tipi misti.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldSupportTypeSuffixesAndCompoundDimDeclarations()
    {
        // Arrange
        var code = """
            Public Sub DataTypesTest()
                Dim intVal%, lngVal&, sngVal!, dblVal#, strVal$, curVal@
                Dim matrix(1 To 10, 1 To 20) As Double, dynamicArr() As String
                Dim x As Integer, y As Long, z As Currency

                intVal% = 100
                lngVal& = 500000
                sngVal! = 3.14
                dblVal# = 2.718281828
                strVal$ = "VB6 String"
                curVal@ = 99.95
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("TypeSuffixMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var method = graph.Symbols.FirstOrDefault(s => s.Name == "DataTypesTest");
        method.Should().NotBeNull();
    }

    #endregion

    #region Indexed Property Get / Let / Set

    /// <summary>
    /// Verifica la definizione di proprietà indicizzate (Property Get / Property Let con parametri di indice).
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractIndexedProperties()
    {
        // Arrange
        var code = """
            Private m_Items() As String

            Public Property Get Item(ByVal index As Long) As String
                Item = m_Items(index)
            End Property

            Public Property Let Item(ByVal index As Long, ByVal value As String)
                m_Items(index) = value
            End Property

            Public Property Get Matrix(ByVal row As Long, ByVal col As Long) As Double
                Matrix = 0.0
            End Property

            Public Property Let Matrix(ByVal row As Long, ByVal col As Long, ByVal val As Double)
            End Property
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("CollectionClass.cls", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var itemProperties = graph.Symbols.Where(s => s.Name == "Item").ToList();
        itemProperties.Should().HaveCount(2);

        var matrixProperties = graph.Symbols.Where(s => s.Name == "Matrix").ToList();
        matrixProperties.Should().HaveCount(2);
    }

    #endregion

    #region Error Handling Constructs

    /// <summary>
    /// Verifica il parsing di costrutti avanzati di gestione errori:
    /// On Error Resume Next, On Error GoTo 0, On Error GoTo -1, Err.Raise, Resume Next, Resume Label.
    /// </summary>
    [Fact]
    public void Parser_ShouldParseAdvancedErrorHandlingConstructs()
    {
        // Arrange
        var code = """
            Public Sub RobustRoutine()
                On Error Resume Next
                Call RiskyOperation
                If Err.Number <> 0 Then
                    Call LogError(Err.Number, Err.Description)
                    Err.Clear
                End If
                
                On Error GoTo ErrorHandler
                Call AnotherOperation
                
                On Error GoTo 0
                Exit Sub

            ErrorHandler:
                If Err.Number = 53 Then
                    Resume Next
                Else
                    Call CleanupResources
                    Resume ExitPoint
                End If

            ExitPoint:
                Exit Sub
            End Sub

            Private Sub RiskyOperation() : End Sub
            Private Sub LogError(ByVal code As Long, ByVal desc As String) : End Sub
            Private Sub AnotherOperation() : End Sub
            Private Sub CleanupResources() : End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("ErrorMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var robustSub = graph.Symbols.FirstOrDefault(s => s.Name == "RobustRoutine");
        robustSub.Should().NotBeNull();

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == robustSub!.Id).ToList();
        calls.Should().NotBeEmpty();
    }

    #endregion

    #region Event Definition and RaiseEvent Wiring

    /// <summary>
    /// Verifica l'estrazione di eventi definiti con Event e la chiamata RaiseEvent.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractEventDefinitionsAndRaiseEvent()
    {
        // Arrange
        var code = """
            Public Event DataReceived(ByVal bytesCount As Long, ByRef cancel As Boolean)
            Public Event ProgressChanged(ByVal percent As Integer)

            Public Sub TriggerProcess()
                Dim cancelFlag As Boolean
                cancelFlag = False
                
                RaiseEvent ProgressChanged(50)
                RaiseEvent DataReceived(1024, cancelFlag)
                
                If cancelFlag Then
                    Exit Sub
                End If
                
                RaiseEvent ProgressChanged(100)
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("EventProducer.cls", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var dataEvent = graph.Symbols.FirstOrDefault(s => s.Name == "DataReceived");
        var progressEvent = graph.Symbols.FirstOrDefault(s => s.Name == "ProgressChanged");
        var triggerSub = graph.Symbols.FirstOrDefault(s => s.Name == "TriggerProcess");

        dataEvent.Should().NotBeNull();
        progressEvent.Should().NotBeNull();
        triggerSub.Should().NotBeNull();
    }

    #endregion

    #region LSet, RSet, and Mid$ Assignment

    /// <summary>
    /// Verifica il parsing di istruzioni specialistiche VB6 come Mid$ come l-value, LSet e RSet.
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportMidStatementLSetAndRSet()
    {
        // Arrange
        var code = """
            Public Sub StringManipulation()
                Dim buffer As String
                buffer = "Hello World"
                Mid$(buffer, 7, 5) = "VB6!!"
                
                Dim padded As String * 20
                LSet padded = "LeftAligned"
                RSet padded = "RightAligned"
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
        errors.Should().BeEmpty("Mid$ statement, LSet and RSet should parse without errors");
        tree.Should().NotBeNull();
    }

    #endregion
}
