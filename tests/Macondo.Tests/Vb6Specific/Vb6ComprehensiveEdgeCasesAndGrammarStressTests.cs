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

public class Vb6ComprehensiveEdgeCasesAndGrammarStressTests
{
    #region Conditional Compilation Directives (#If, #ElseIf, #Else, #End If, #Const)

    /// <summary>
    /// Verifica il parsing e l'estrazione di codice contenente direttive di compilazione condizionale
    /// (#Const, #If, #ElseIf, #Else, #End If) a livello di modulo e all'interno delle routine.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldHandleConditionalCompilationDirectives()
    {
        // Arrange
        var code = """
            #Const DEBUG_MODE = 1
            #Const WIN32_PLATFORM = True

            Public Sub InitializeEnvironment()
                #If DEBUG_MODE Then
                    Call LogDiagnosticInfo("Debug environment activated")
                #ElseIf WIN32_PLATFORM Then
                    Call SetupWin32Subsystems
                #Else
                    Call SetupDefaultSubsystems
                #End If
            End Sub

            Private Sub LogDiagnosticInfo(ByVal msg As String)
            End Sub

            Private Sub SetupWin32Subsystems()
            End Sub

            Private Sub SetupDefaultSubsystems()
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("CondCompMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var initSub = graph.Symbols.FirstOrDefault(s => s.Name == "InitializeEnvironment");
        initSub.Should().NotBeNull();

        var logSub = graph.Symbols.FirstOrDefault(s => s.Name == "LogDiagnosticInfo");
        var win32Sub = graph.Symbols.FirstOrDefault(s => s.Name == "SetupWin32Subsystems");

        logSub.Should().NotBeNull();
        win32Sub.Should().NotBeNull();

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == initSub!.Id).ToList();
        calls.Should().NotBeEmpty();
    }

    #endregion

    #region AddressOf Operator in Win32 Subclassing & Callbacks

    /// <summary>
    /// Verifica il corretto parsing e l'estrazione dell'operatore AddressOf utilizzato nei callback di sottoclasse e timer Windows API.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldHandleAddressOfOperatorInApiCalls()
    {
        // Arrange
        var code = """
            Private Declare Function SetTimer Lib "user32" (ByVal hWnd As Long, ByVal nIDEvent As Long, ByVal uElapse As Long, ByVal lpTimerFunc As Long) As Long
            Private Declare Function KillTimer Lib "user32" (ByVal hWnd As Long, ByVal nIDEvent As Long) As Long

            Public Sub StartAppTimer(ByVal hWnd As Long)
                Call SetTimer(hWnd, 100, 1000, AddressOf TimerProc)
            End Sub

            Public Sub StopAppTimer(ByVal hWnd As Long)
                Call KillTimer(hWnd, 100)
            End Sub

            Public Sub TimerProc(ByVal hWnd As Long, ByVal uMsg As Long, ByVal idEvent As Long, ByVal dwTime As Long)
                Call OnTimerTick
            End Sub

            Private Sub OnTimerTick()
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("TimerMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var startSub = graph.Symbols.FirstOrDefault(s => s.Name == "StartAppTimer");
        var timerProc = graph.Symbols.FirstOrDefault(s => s.Name == "TimerProc");
        var onTick = graph.Symbols.FirstOrDefault(s => s.Name == "OnTimerTick");

        startSub.Should().NotBeNull();
        timerProc.Should().NotBeNull();
        onTick.Should().NotBeNull();

        var timerTickCall = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == timerProc!.Id && r.TargetId == onTick!.Id);
        timerTickCall.Should().NotBeNull();
    }

    #endregion

    #region User-Defined Types (UDTs) with Fixed-Size Arrays and Fixed-Length Strings

    /// <summary>
    /// Verifica il parsing di strutture dati utente (Type ... End Type) complesse con array a dimensione fissa,
    /// stringhe a lunghezza fissa (String * N) e tipi annidati.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractComplexUDTsWithFixedBuffers()
    {
        // Arrange
        var code = """
            Public Type HeaderRecord
                MagicNumber As Long
                VersionTag As String * 8
                Checksum As Long
            End Type

            Public Type DataPacket
                Header As HeaderRecord
                PayloadSize As Long
                DataBuffer(1 To 256) As Byte
                StatusFlags(0 To 7) As Boolean
            End Type

            Public Sub ProcessPacket(ByRef packet As DataPacket)
                packet.Header.Checksum = 12345
                packet.DataBuffer(1) = 255
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("PacketTypes.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var headerType = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Type && s.Name == "HeaderRecord");
        var packetType = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Type && s.Name == "DataPacket");
        var processSub = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "ProcessPacket");

        headerType.Should().NotBeNull();
        packetType.Should().NotBeNull();
        processSub.Should().NotBeNull();
        processSub!.Parameters.Should().HaveCount(1);
        processSub.Parameters[0].DeclaredTypeName.Should().Be("DataPacket");
    }

    #endregion

    #region Enums with Negative, Hex, and Explicit Values

    /// <summary>
    /// Verifica il parsing di Enum VB6 contenenti valori esadecimali (&amp;H...), valori negativi ed espressioni di bitmask.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractEnumsWithHexAndNegativeValues()
    {
        // Arrange
        var code = """
            Public Enum WindowStyles
                wsOverlapped = &H0&
                wsPopup = &H80000000
                wsChild = &H40000000
                wsMinimize = &H20000000
                wsVisible = &H10000000
                wsDisabled = &H8000000
                wsInvalid = -1
            End Enum

            Public Sub ApplyWindowStyle(ByVal hWnd As Long, ByVal style As WindowStyles)
                If style = wsInvalid Then Exit Sub
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("WinStyles.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var enumSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Enum && s.Name == "WindowStyles");
        enumSym.Should().NotBeNull();

        var members = graph.GetChildren(enumSym!.Id).ToList();
        members.Should().NotBeEmpty();
        members.Select(m => m.Name).Should().Contain(new[] { "wsOverlapped", "wsPopup", "wsInvalid" });
    }

    #endregion

    #region ParamArray and Optional Variant Arguments

    /// <summary>
    /// Verifica l'estrazione e il call-graph di routine con parametri ParamArray e parametri opzionali multipli.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractParamArrayAndOptionalParameters()
    {
        // Arrange
        var code = """
            Public Sub LogFormatted(ByVal level As Integer, ParamArray messages() As Variant)
                Dim i As Long
                For i = LBound(messages) To UBound(messages)
                    Call WriteToConsole(CStr(messages(i)))
                Next i
            End Sub

            Public Sub WriteToConsole(ByVal msg As String, Optional ByVal prefix As String = "[INFO]")
            End Sub

            Public Sub TestLogger()
                Call LogFormatted(1, "User logged in", "ID: 42", "IP: 127.0.0.1")
                Call WriteToConsole("Ready")
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("LoggerMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var logFormatted = graph.Symbols.FirstOrDefault(s => s.Name == "LogFormatted");
        var writeConsole = graph.Symbols.FirstOrDefault(s => s.Name == "WriteToConsole");
        var testLogger = graph.Symbols.FirstOrDefault(s => s.Name == "TestLogger");

        logFormatted.Should().NotBeNull();
        writeConsole.Should().NotBeNull();
        testLogger.Should().NotBeNull();

        writeConsole!.Parameters.Should().HaveCount(2);
        writeConsole.Parameters[1].IsOptional.Should().BeTrue();

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == testLogger!.Id).ToList();
        calls.Should().Contain(r => r.TargetId == logFormatted!.Id);
        calls.Should().Contain(r => r.TargetId == writeConsole.Id);
    }

    #endregion

    #region Multiple Variable Declarations with Dim As New and Mixed Types

    /// <summary>
    /// Verifica il parsing di dichiarazioni Dim composite su singola riga con auto-istanziazione (As New),
    /// array dinamici e tipi primitivi misti.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractMixedCompoundDeclarations()
    {
        // Arrange
        var code = """
            Public Sub ExecuteMixedDeclarations()
                Dim w1 As New Worker, w2 As Worker, items() As String, totalCount As Long, isValid As Boolean
                Set w2 = w1
                totalCount = 100
                isValid = True
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("CompoundDim.cls", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var method = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "ExecuteMixedDeclarations");
        method.Should().NotBeNull();
    }

    #endregion

    #region On Error GoTo -1 and Complex Error Recovery

    /// <summary>
    /// Verifica il parsing di costrutti avanzati di gestione eccezioni VB6 con 'On Error GoTo -1'
    /// per il reset dello stato di eccezione attivo.
    /// </summary>
    [Fact]
    public void Parser_ShouldParseOnErrorResetDirective()
    {
        // Arrange
        var code = """
            Public Sub ProcessWithNestedErrorHandling()
                On Error GoTo FirstLevelError
                
                Call RiskyPhase1
                Exit Sub

            FirstLevelError:
                ' Reset exception state
                On Error GoTo -1
                On Error GoTo SecondLevelError
                
                Call FallbackPhase2
                Exit Sub

            SecondLevelError:
                Call LogFatalError
            End Sub

            Private Sub RiskyPhase1() : End Sub
            Private Sub FallbackPhase2() : End Sub
            Private Sub LogFatalError() : End Sub
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
        errors.Should().BeEmpty("On Error GoTo -1 and multi-level error handlers should parse cleanly");
        tree.Should().NotBeNull();
    }

    #endregion
}
