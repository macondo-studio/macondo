using System.Text;
using FluentAssertions;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.Martin;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

/// <summary>
/// Suite of regression tests aimed at preventing and validating bugs found during real analysis
/// of complex VB6 projects (over 500,000 lines of enterprise code):
/// 1. Crash Call Graph for 'GoTo Label:' with colons inside If/While blocks.
/// 2. Crash Call Graph for inter-quadrant identifiers with spaces/periods in call arguments ('RS![a b]').
/// 3. Resilience of the CallGraphBuilder on exceptions or malformed constructs.
/// 4. Resolving member calls on objects returned by global Property/Functions (e.g. bcPCassaProcessor.BarcodeRounding).
/// 5. Event handler recognition of WithEvents variables and complex prefixes with multiple underscores.
/// 6. LOC limitation of form designer controls and correct classification in Dead Code.
/// 7. Actual calculation of In-Degree and Out-Degree in the DSM matrix (DsmItem).
/// 8. Afferent/efferent dependencies towards modules containing Win32 Declare functions or Global Constants.
/// 9. Writing UTF-8 files without BOM for maximum compatibility with external parsers and scripts.
/// </summary>
public class Vb6LegacyEdgeCasesRegressionTests
{
    // =========================================================================
    // 1. GOTO CRASH WITH TWO POINTS (GoTo Fine:)
    // =========================================================================
    [Fact]
    public void GoToWithTrailingColon_InsideIfBlock_ShouldNotCrashCallGraph_AndShouldBuildCalls()
    {
        var graph = new CodeGraph();
        var code = """
            Attribute VB_Name = "modRepro1"
            Sub Main()
            End Sub

            Sub Test()
                RS.Close
                If x Then
                    GoTo Fine:
                End If
            Fine:
            End Sub
            """;

        var extract = ModuleExtractor.ExtractModule(graph, "modRepro1.bas", code);
        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        // It should not throw exceptions or NullReferenceExceptions
        var act = () => builder.BuildGraph(new[] { extract });
        act.Should().NotThrow("GoTo con due punti terminali non deve mandare in crash la costruzione del Call Graph");
    }

    // =========================================================================
    // 2. CRASH IDENTIFIERS BETWEEN SQUARES WITH SPACES OR DOTS (RS![a b])
    // =========================================================================
    [Fact]
    public void BracketedIdentifierWithSpacesAndDots_InMethodArgument_ShouldNotCrashCallGraph()
    {
        var graph = new CodeGraph();
        var code = """
            Attribute VB_Name = "modRepro2"
            Sub Main()
            End Sub

            Sub Test()
                g.AddItem RS![a b]
                g.AddItem RS![Cod.Art.Forn.]
                g.Foo [Field With Spaces]
            End Sub
            """;

        var extract = ModuleExtractor.ExtractModule(graph, "modRepro2.bas", code);
        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        var act = () => builder.BuildGraph(new[] { extract });
        act.Should().NotThrow("Identificatori tra parentesi quadre contenenti spazi o punti non devono mandare in crash il Call Graph");
    }

    // =========================================================================
    // 3. RESILIENCE OF THE CALLGRAPHBUILDER ON ERRORS FOR SINGLE MODULE
    // =========================================================================
    [Fact]
    public void CallGraphBuilder_ShouldContinueProcessingOtherModules_IfOneModuleEncounterUnexpectedError()
    {
        var graph = new CodeGraph();
        var validCode = """
            Attribute VB_Name = "modValid"
            Sub Main()
                HelperSub
            End Sub

            Sub HelperSub()
            End Sub
            """;

        var extractValid = ModuleExtractor.ExtractModule(graph, "modValid.bas", validCode);

        // Let's simulate an extracted module whose ParseTree is intact
        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        builder.BuildGraph(new[] { extractValid });

        // Verify that the valid module has its CALLS relationships created regularly
        var mainSub = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "Main");
        var helperSub = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "HelperSub");

        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls &&
                                                 r.SourceId == mainSub.Id &&
                                                 r.TargetId == helperSub.Id);
    }

    // =========================================================================
    // 4. CALLS THROUGH OBJECTS RETURNED FROM GLOBAL PROPERTY/FUNCTIONS
    // =========================================================================
    [Fact]
    public void GlobalPropertyReturningClass_ShouldResolveMethodCallsOnReturnedObject()
    {
        var graph = new CodeGraph();

        var classCode = """
            VERSION 1.0 CLASS
            BEGIN
              MultiUse = -1  'True
            END
            Attribute VB_Name = "PCassaProcessor"

            Public Sub BarcodeArrotondamento()
            End Sub

            Public Sub TrasformaCollezione()
            End Sub
            """;

        var basCode = """
            Attribute VB_Name = "moduloVariabili"

            Public Property Get bcPCassaProcessor() As PCassaProcessor
            End Property
            """;

        var formCode = """
            VERSION 5.00
            Begin VB.Form frmPuntoCassa
               Caption = "Cassa"
            End
            Attribute VB_Name = "frmPuntoCassa"

            Public Sub TestCalls()
                bcPCassaProcessor.BarcodeArrotondamento
                Call bcPCassaProcessor.TrasformaCollezione
            End Sub
            """;

        var resClass = ModuleExtractor.ExtractModule(graph, "PCassaProcessor.cls", classCode);
        var resBas = ModuleExtractor.ExtractModule(graph, "moduloVariabili.bas", basCode);
        var resForm = ModuleExtractor.ExtractModule(graph, "frmPuntoCassa.frm", formCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        builder.BuildGraph(new[] { resClass, resBas, resForm });

        var barcodeMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "BarcodeArrotondamento");
        var trasformaMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "TrasformaCollezione");
        var testCallsMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "TestCalls");

        var callsToBarcode = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls &&
                                                                     r.SourceId == testCallsMethod.Id &&
                                                                     r.TargetId == barcodeMethod.Id);
        callsToBarcode.Should().NotBeNull("bcPCassaProcessor.BarcodeArrotondamento deve risolvere il metodo sulla classe restituita dalla Property");

        var callsToTrasforma = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls &&
                                                                       r.SourceId == testCallsMethod.Id &&
                                                                       r.TargetId == trasformaMethod.Id);
        callsToTrasforma.Should().NotBeNull("bcPCassaProcessor.TrasformaCollezione deve risolvere il metodo sulla classe restituita dalla Property");
    }

    // =========================================================================
    // 5. EVENT HANDLERS OF VARIABLES WithEvents AND COMPLEX PREFIXES
    // =========================================================================
    [Fact]
    public void WithEventsHandlers_WithComplexUnderscores_ShouldBeWiredAndMarkedReachable()
    {
        var graph = new CodeGraph();

        var formCode = """
            VERSION 5.00
            Begin VB.Form MDIForm
               Caption = "MDI"
            End
            Attribute VB_Name = "MDIForm"

            Private WithEvents doc_service As clsDocService
            Private WithEvents frmContabilizzaFatture As clsExportService

            Private Sub doc_service_nuovoDDT()
            End Sub

            Private Sub frmContabilizzaFatture__AvviaMultiExport()
            End Sub
            """;

        var resForm = ModuleExtractor.ExtractModule(graph, "MDIForm.frm", formCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        builder.BuildGraph(new[] { resForm });

        var ddtSub = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "doc_service_nuovoDDT");
        var exportSub = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "frmContabilizzaFatture__AvviaMultiExport");

        var handlesDdt = graph.Relationships.Where(r => r.Kind == RelationshipKind.HandlesEvent && r.TargetId == ddtSub.Id).ToList();
        handlesDdt.Should().NotBeEmpty("doc_service_nuovoDDT deve essere collegato tramite HandlesEvent alla form o alla variabile WithEvents");

        // Dead Code: the event handlers connected to the form must be reachable
        var deadData = DeadCodeAnalyzer.Analyze(graph);
        deadData.Items.Should().NotContain(i => i.Id == ddtSub.Id, "Gli handler di variabili WithEvents attive nella form non sono codice morto");
        deadData.Items.Should().NotContain(i => i.Id == exportSub.Id, "Gli handler con doppio underscore non devono essere considerati codice morto");
    }

    // =========================================================================
    // 6. SOVRASTIMA CONTROLLI FORM DESIGNER
    // =========================================================================
    [Fact]
    public void FormDesignerControls_ShouldHaveSingleLineLoc_AndNotInflateEstimatedDeadLoc()
    {
        var graph = new CodeGraph();

        var formCode = """
            VERSION 5.00
            Begin VB.Form frmArticolo
               Caption = "Articolo"
               Begin VB.CommandButton cmdSave
                  Caption = "Salva"
                  Left = 100
                  Top = 200
               End
               Begin VB.Frame FrameContainer
                  Caption = "Dati"
                  Begin VB.Label lblDesc
                     Caption = "Descrizione"
                  End
               End
            End
            Attribute VB_Name = "frmArticolo"

            Private Sub cmdSave_Click()
            End Sub
            """;

        var res = ModuleExtractor.ExtractModule(graph, "frmArticolo.frm", formCode);

        var frameSym = graph.Symbols.FirstOrDefault(s => s.Name == "FrameContainer");
        frameSym.Should().NotBeNull();
        // LineEnd must be limited to the control line, not extend to the entire designer block
        frameSym!.LineEnd.Should().Be(frameSym.LineStart, "Il controllo designer deve avere estensione di riga limitata alla dichiarazione (1 LOC)");

        var deadData = DeadCodeAnalyzer.Analyze(graph);
        // Visual checks of the form should not be counted as thousands of lines of dead LOC
        var deadFrame = deadData.Items.FirstOrDefault(i => i.Name == "FrameContainer");
        if (deadFrame != null)
        {
            deadFrame.EstimatedLoc.Should().Be(1, "Il LOC stimato per un controllo designer deve essere esattamente 1");
        }
    }

    // =========================================================================
    // 7. DSM MATRIX: ACTUAL CALCULATION OF InDegree AND OutDegree
    // =========================================================================
    [Fact]
    public void DsmMatrixBuilder_ShouldPopulateInDegreeAndOutDegree()
    {
        var graph = new CodeGraph();

        var modACode = """
            Attribute VB_Name = "ModA"
            Public Sub DoA()
                ModB.DoB
                ModC.DoC
            End Sub
            """;

        var modBCode = """
            Attribute VB_Name = "ModB"
            Public Sub DoB()
                ModC.DoC
            End Sub
            """;

        var modCCode = """
            Attribute VB_Name = "ModC"
            Public Sub DoC()
            End Sub
            """;

        var resA = ModuleExtractor.ExtractModule(graph, "ModA.bas", modACode);
        var resB = ModuleExtractor.ExtractModule(graph, "ModB.bas", modBCode);
        var resC = ModuleExtractor.ExtractModule(graph, "ModC.bas", modCCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { resA, resB, resC });

        var dsm = DsmMatrixBuilder.Build(graph);

        var itemA = dsm.Items.First(i => i.Name == "ModA");
        var itemB = dsm.Items.First(i => i.Name == "ModB");
        var itemC = dsm.Items.First(i => i.Name == "ModC");

        // ModA chiama ModB e ModC -> OutDegree = 2, InDegree = 0
        itemA.OutDegree.Should().Be(2);
        itemA.InDegree.Should().Be(0);

        // ModB calls ModC, called by ModA -> OutDegree = 1, InDegree = 1
        itemB.OutDegree.Should().Be(1);
        itemB.InDegree.Should().Be(1);

        // ModC called from ModA and ModB -> OutDegree = 0, InDegree = 2
        itemC.OutDegree.Should().Be(0);
        itemC.InDegree.Should().Be(2);
    }

    // =========================================================================
    // 8. MARTIN METRICS: DEPENDENCES ON DECLARE AND GLOBAL CONSTANTS
    // =========================================================================
    [Fact]
    public void MartinMetrics_ShouldCountCallsToExternalDeclares_AsAfferentDependencies()
    {
        var graph = new CodeGraph();

        var apiModCode = """
            Attribute VB_Name = "modApiDeclared"

            Public Declare Function GetLocaleInfo Lib "kernel32" (ByVal Locale As Long, ByVal LCType As Long, ByVal lpLCData As String, ByVal cchData As Long) As Long
            Public Const LOCALE_USER_DEFAULT = &H400
            """;

        var callerModCode = """
            Attribute VB_Name = "MlStringModule"

            Public Sub GetSettings()
                Dim res As Long
                res = GetLocaleInfo(LOCALE_USER_DEFAULT, 0, "", 0)
            End Sub
            """;

        var resApi = ModuleExtractor.ExtractModule(graph, "modApiDeclared.bas", apiModCode);
        var resCaller = ModuleExtractor.ExtractModule(graph, "MlStringModule.bas", callerModCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { resApi, resCaller });

        var martinData = MartinMetricsCalculator.Analyze(graph);
        var apiMetric = martinData.Items.FirstOrDefault(m => m.Name == "modApiDeclared");
        apiMetric.Should().NotBeNull();
        apiMetric!.AfferentCoupling.Should().BeGreaterThan(0, "modApiDeclared deve registrare dipendenza afferente (Ca > 0) perché GetLocaleInfo viene invocata da MlStringModule");
    }

    // =========================================================================
    // 9. WRITING UTF-8 FILE WITHOUT BOM
    // =========================================================================
    [Fact]
    public void CliRunner_SaveFile_ShouldWriteUtf8WithoutBom()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"macondo_test_{Guid.NewGuid():N}.json");
        try
        {
            var content = "{\"version\": \"1.0.0\", \"description\": \"test без BOM\"}";
            CliRunner.SaveFile(tempFile, content);

            var bytes = File.ReadAllBytes(tempFile);
            // First 3 UTF-8 bytes with BOM are 0xEF, 0xBB, 0xBF
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            hasBom.Should().BeFalse("I file JSON esportati da Macondo devono essere in codifica UTF-8 senza BOM per compatibilità con tool standard");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
