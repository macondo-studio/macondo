using FluentAssertions;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

/// <summary>
/// Regression test suite to validate and prevent Dead Code false positives
/// identified in the analysis of the 'LegacyApp' project (documented in 'false-positives-dead-code.md').
/// 
/// CONTEXT OF THE PROBLEMS ENCOUNTERED:
/// 1. UserControl members with project prefix in designer:
/// In the visual sections 'Begin DUE.cntrDestination cntrDestination', the type name contained the prefix
/// of the project ('TWO.'). The TypeResolver did not resolve the type of the control, leaving all methods orphaned
/// and properties invoked on the control (over 100 false positives).
/// 
/// 2. Events raised via 'RaiseEvent' statements:
/// In the same component (UserControl or Class), the 'RaiseEvent Cancel' statement did not raise an arc
/// CALLS to the Event symbol, making all custom events appear as dead code (dozens of false positives).
/// 
/// 3. Routines and properties used as receiver in chain calls:
/// In expressions like 'getFirstForm(parent).SelectGridRows' or 'frmAccountInvoices.ShowModalFormBy',
/// the intermediate routine or property that acted as the receiver was not recorded in the Call Graph, resulting
/// not invoked and therefore labeled Dead Code.
/// 
/// 4. Late binding polymorphic calls (IDispatch):
///    Variabili dichiarate 'As Form', 'As Control' o 'As Object' (es. 'refFrmElencoDoc_Attiva.EliminaRicevute'
/// or 'ActiveForm.Close') were confidently classified as Unknown and ignored by the Call Graph, marking
/// the public methods of the Forms invoked via dynamic polymorphism are unused.
/// 
/// 5. Calls to the default instance of the Form with ByVal syntax in parentheses:
/// VB6 constructs like 'Form.Sub(argument)' where the single argument is enclosed in parentheses to force
/// ByVal steps were not recognized if invoked on the implicit global instance of the Form.
/// </summary>
public class Vb6DeadCodeFalsePositivesRegressionTests
{
    // =========================================================================
    // 1. USERCONTROL MEMBERS WITH PROJECT PREFIX IN THE FORM DESIGNER
    // =========================================================================

    /// <summary>
    /// Verifies that a user control declared with project prefix (e.g. 'Begin DUE.cntrDestination cntrDestination')
    /// correctly resolves the underlying semantic type ('cntrDestination.ctl') and that the invocations
    /// methods and properties on the control generate CALLS arcs, avoiding false positives in the DeadCodeAnalyzer.
    /// </summary>
    [Fact]
    public void UserControl_WithProjectPrefixInDesigner_ShouldResolveMembers_AndNotBeMarkedAsDeadCode()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: Definition of the user control and the Form that hosts it
        // ---------------------------------------------------------------------
        var graph = new CodeGraph();

        // User control cntrDestination.ctl with 'Enabled' property and 'Clear' method
        var ctlCode = """
            VERSION 5.00
            Begin VB.UserControl cntrDestinazione
               ClientHeight = 3600
               ClientWidth = 4800
            End
            Attribute VB_Name = "cntrDestinazione"

            Public Property Let Enabled(ByVal v As Boolean)
            End Property

            Public Sub Clear()
            End Sub
            """;

        // Form frmGestioneDDT.frm which declares the control with project prefix 'Begin DUE.cntrDestinazione'
        var frmCode = """
            VERSION 5.00
            Begin VB.Form frmGestioneDDT
               Caption = "DDT"
               Begin DUE.cntrDestinazione cntrDestinazione
                  Height = 3000
               End
            End
            Attribute VB_Name = "frmGestioneDDT"

            Private Sub Form_Load()
                cntrDestinazione.Enabled = False
                cntrDestinazione.Clear
            End Sub
            """;

        // ---------------------------------------------------------------------
        // 2. ACT: Module extraction, semantic type resolution and Call Graph construction
        // ---------------------------------------------------------------------
        var resCtl = ModuleExtractor.ExtractModule(graph, "cntrDestinazione.ctl", ctlCode);
        var resFrm = ModuleExtractor.ExtractModule(graph, "frmGestioneDDT.frm", frmCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { resCtl, resFrm });

        var enabledProp = graph.Symbols.First(s => s.Kind == SymbolKind.Property && s.Name == "Enabled");
        var clearMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "Clear");

        // ---------------------------------------------------------------------
        // 3. ASSERT: Check CALLS arcs and absence from the Dead Code report
        // ---------------------------------------------------------------------
        // Verify that the 'cntrDestination.Enabled' and 'cntrDestination.Clear' calls generated CALLS arcs
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == enabledProp.Id,
            "cntrDestinazione.Enabled deve ricevere un arco Calls nonostante il tipo dichiarato nel designer sia qualificato come DUE.cntrDestinazione");
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == clearMethod.Id,
            "cntrDestinazione.Clear deve ricevere un arco Calls");

        // Running the Dead Code analysis: Members must not be orphaned
        var deadResult = DeadCodeAnalyzer.Analyze(graph);
        deadResult.Items.Should().NotContain(i => i.Id == enabledProp.Id,
            "cntrDestinazione.Enabled non deve essere considerato Dead Code");
        deadResult.Items.Should().NotContain(i => i.Id == clearMethod.Id,
            "cntrDestinazione.Clear non deve essere considerato Dead Code");
    }

    /// <summary>
    /// Check that a user control with an instance name different from the type name (e.g. 'cntrRigaDocumento' of type 'DUE.cntrArticles')
    /// correctly resolves members invoked both with direct syntax and with 'Call' keywords.
    /// </summary>
    [Fact]
    public void UserControl_DifferentControlNameThanType_ShouldResolveMembers()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: Control 'cntrArticles' hosted with instance name 'cntrRigaDocumento'
        // ---------------------------------------------------------------------
        var graph = new CodeGraph();

        var ctlCode = """
            VERSION 5.00
            Begin VB.UserControl cntrArticoli
               ClientHeight = 3600
               ClientWidth = 4800
            End
            Attribute VB_Name = "cntrArticoli"

            Public Property Let AllowNegative(ByVal v As Boolean)
            End Property

            Public Sub AmmettiQtaZero()
            End Sub
            """;

        var frmCode = """
            VERSION 5.00
            Begin VB.Form frmGestioneOrdini
               Caption = "Ordini"
               Begin DUE.cntrArticoli cntrRigaDocumento
                  Height = 3000
               End
            End
            Attribute VB_Name = "frmGestioneOrdini"

            Private Sub Form_Load()
                cntrRigaDocumento.AllowNegative = False
                Call cntrRigaDocumento.AmmettiQtaZero
            End Sub
            """;

        // ---------------------------------------------------------------------
        // 2. ACT: Extraction, type resolution and Call Graph generation
        // ---------------------------------------------------------------------
        var resCtl = ModuleExtractor.ExtractModule(graph, "cntrArticoli.ctl", ctlCode);
        var resFrm = ModuleExtractor.ExtractModule(graph, "frmGestioneOrdini.frm", frmCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { resCtl, resFrm });

        var allowNegProp = graph.Symbols.First(s => s.Kind == SymbolKind.Property && s.Name == "AllowNegative");
        var ammettiMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "AmmettiQtaZero");

        // ---------------------------------------------------------------------
        // 3. ASSERT: Verify member resolution and absence from the Dead Code
        // ---------------------------------------------------------------------
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == allowNegProp.Id,
            "L'assegnazione a cntrRigaDocumento.AllowNegative deve generare un arco Calls sulla property");
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == ammettiMethod.Id,
            "Call cntrRigaDocumento.AmmettiQtaZero deve generare un arco Calls sul metodo");

        var deadResult = DeadCodeAnalyzer.Analyze(graph);
        deadResult.Items.Should().NotContain(i => i.Id == allowNegProp.Id);
        deadResult.Items.Should().NotContain(i => i.Id == ammettiMethod.Id);
    }

    /// <summary>
    /// Check inter-form cascade access: 'Form.Control.Method' (e.g. 'frmCashPoint.ViewerCashPoint.LoadSkinOverride')
    /// invoked by a standard .bas module.
    /// </summary>
    [Fact]
    public void UserControl_CrossFormAccess_FormDotControlDotMethod_ShouldResolve()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: UserControl VisorePuntoCassa, Form ospitante e modulo chiamante
        // ---------------------------------------------------------------------
        var graph = new CodeGraph();

        var ctlCode = """
            VERSION 5.00
            Begin VB.UserControl VisorePuntoCassa
            End
            Attribute VB_Name = "VisorePuntoCassa"

            Public Sub CaricaSkinOverride(ByVal pathIni As String)
            End Sub
            """;

        var frmCode = """
            VERSION 5.00
            Begin VB.Form frmPuntoCassa
               Begin DUE.VisorePuntoCassa VisorePuntoCassa
               End
            End
            Attribute VB_Name = "frmPuntoCassa"
            """;

        var basCode = """
            Attribute VB_Name = "modPuntoCassa"

            Public Sub InitSkin()
                Call frmPuntoCassa.VisorePuntoCassa.CaricaSkinOverride("test.ini")
            End Sub
            """;

        // ---------------------------------------------------------------------
        // 2. ACT: Extraction of the 3 modules and overall semantic resolution
        // ---------------------------------------------------------------------
        var resCtl = ModuleExtractor.ExtractModule(graph, "VisorePuntoCassa.ctl", ctlCode);
        var resFrm = ModuleExtractor.ExtractModule(graph, "frmPuntoCassa.frm", frmCode);
        var resBas = ModuleExtractor.ExtractModule(graph, "modPuntoCassa.bas", basCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { resCtl, resFrm, resBas });

        var skinMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "CaricaSkinOverride");

        // ---------------------------------------------------------------------
        // 3. ASSERT: Verify that the three-level invocation creates the CALLS arc
        // ---------------------------------------------------------------------
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == skinMethod.Id,
            "La chiamata a catena frmPuntoCassa.VisorePuntoCassa.CaricaSkinOverride deve raggiungere il metodo del controllo");
    }

    // =========================================================================
    // 2. EVENTS RAISED VIA RaiseEvent IN THE SAME FORM
    // =========================================================================

    /// <summary>
    /// Verify that the 'RaiseEventEventName' statement formally links the calling routine
    /// to the event symbol with a CALLS relationship, ensuring that declared events do not occur
    /// falsely reported as Dead Code.
    /// </summary>
    [Fact]
    public void RaiseEvent_InSameComponent_ShouldCreateCallRelationship_AndNotBeMarkedAsDeadCode()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: UserControl with 'Deletion' and 'Selection' events raised internally
        // ---------------------------------------------------------------------
        var graph = new CodeGraph();

        var ctlCode = """
            VERSION 5.00
            Begin VB.UserControl cntrPagamenti
            End
            Attribute VB_Name = "cntrPagamenti"

            Public Event Cancellazione()
            Public Event Selezione()

            Private Sub cmdSvuota_Click()
                RaiseEvent Cancellazione
            End Sub

            Private Sub cmdFind_Click()
                RaiseEvent Selezione
            End Sub
            """;

        // ---------------------------------------------------------------------
        // 2. ACT: Module extraction and semantic graph construction
        // ---------------------------------------------------------------------
        var res = ModuleExtractor.ExtractModule(graph, "cntrPagamenti.ctl", ctlCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { res });

        var cancelEvent = graph.Symbols.First(s => s.Kind == SymbolKind.Event && s.Name == "Cancellazione");
        var selEvent = graph.Symbols.First(s => s.Kind == SymbolKind.Event && s.Name == "Selezione");
        var cmdSvuota = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "cmdSvuota_Click");

        // ---------------------------------------------------------------------
        // 3. ASSERT: Check CALLS relationship to the event and absence from Dead Code
        // ---------------------------------------------------------------------
        // CallGraphBuilder must create the Calls relation to the event starting from the method that executes RaiseEvent
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls &&
                                                 r.SourceId == cmdSvuota.Id &&
                                                 r.TargetId == cancelEvent.Id,
            "RaiseEvent deve generare un arco Calls da cmdSvuota_Click all'evento Cancellazione");

        // Dead Code analysis must recognize events as active and reachable
        var deadResult = DeadCodeAnalyzer.Analyze(graph);
        deadResult.Items.Should().NotContain(i => i.Id == cancelEvent.Id,
            "L'evento Cancellazione sollevato tramite RaiseEvent non deve essere marcato come codice morto");
        deadResult.Items.Should().NotContain(i => i.Id == selEvent.Id,
            "L'evento Selezione sollevato tramite RaiseEvent non deve essere marcato come codice morto");
    }

    // =========================================================================
    // 3. CHAIN CALLS WITH ROUTINE RECEIVER (Function/Property.Member)
    // =========================================================================

    /// <summary>
    /// Check that in a chain call where the receiver is a Function (e.g. 'getFirstForm(parent).SelectGridRows (True)'),
    /// the receiving Function ('getFirstForm') is registered as a call (CALLS arc) together with the final target method.
    /// </summary>
    [Fact]
    public void ChainedInvocation_WithFunctionReceiver_ShouldRecordCallToReceiverFunction()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: MDIForm with Function getFirstForm and cascading call into a command handler
        // ---------------------------------------------------------------------
        var graph = new CodeGraph();

        var formCode = """
            VERSION 5.00
            Begin VB.Form MDIForm
            End
            Attribute VB_Name = "MDIForm"

            Public Function getFirstForm(ByVal parentObj As Object) As MDIForm
                Set getFirstForm = Me
            End Function

            Public Sub SelezionaRigheGriglia(ByVal flag As Boolean)
            End Sub

            Private Sub cmdBarMDI_Execute()
                getFirstForm(reportControl_reference.Parent).SelezionaRigheGriglia (True)
            End Sub
            """;

        // ---------------------------------------------------------------------
        // 2. ACT: Semantic chain resolution
        // ---------------------------------------------------------------------
        var res = ModuleExtractor.ExtractModule(graph, "MDIForm.frm", formCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { res });

        var getFirstFormMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "getFirstForm");
        var selezionaMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "SelezionaRigheGriglia");
        var executeSub = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "cmdBarMDI_Execute");

        // ---------------------------------------------------------------------
        // 3. ASSERT: Both routines (receiver and end member) must receive CALLS arcs
        // ---------------------------------------------------------------------
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls &&
                                                 r.SourceId == executeSub.Id &&
                                                 r.TargetId == getFirstFormMethod.Id,
            "getFirstForm deve essere registrato come chiamato quando usato come ricevitore di una chiamata a membro");

        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls &&
                                                 r.SourceId == executeSub.Id &&
                                                 r.TargetId == selezionaMethod.Id,
            "SelezionaRigheGriglia deve essere registrato come chiamato");

        var deadResult = DeadCodeAnalyzer.Analyze(graph);
        deadResult.Items.Should().NotContain(i => i.Id == getFirstFormMethod.Id,
            "getFirstForm non deve essere considerato Dead Code");
    }

    /// <summary>
    /// Check that in a call where the receiver is a Get Property declared in a .bas module
    /// which returns the form itself (e.g. 'frmContabilizzaFatture.ShowModalFormBy 123'),
    /// both the receiving Property and the final method are recorded in the Call Graph.
    /// </summary>
    [Fact]
    public void ChainedInvocation_WithBasPropertyReceiver_ShouldRecordCallToBasProperty()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: .bas module with Property accessor and calling Form
        // ---------------------------------------------------------------------
        var graph = new CodeGraph();

        var basCode = """
            Attribute VB_Name = "modContabilita"

            Public Property Get frmContabilizzaFatture() As modContabilita
            End Property

            Public Sub ShowModalFormBy(ByVal mode As Long)
            End Sub
            """;

        var frmCode = """
            VERSION 5.00
            Begin VB.Form MDIForm
            End
            Attribute VB_Name = "MDIForm"

            Private Sub cmdBarMDI_Execute()
                frmContabilizzaFatture.ShowModalFormBy 123
            End Sub
            """;

        // ---------------------------------------------------------------------
        // 2. ACT: Extraction, resolution of .bas and .frm modules and graph construction
        // ---------------------------------------------------------------------
        var resBas = ModuleExtractor.ExtractModule(graph, "modContabilita.bas", basCode);
        var resFrm = ModuleExtractor.ExtractModule(graph, "MDIForm.frm", frmCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { resBas, resFrm });

        var propSym = graph.Symbols.First(s => s.Kind == SymbolKind.Property && s.Name == "frmContabilizzaFatture");
        var targetSub = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "ShowModalFormBy");

        // ---------------------------------------------------------------------
        // 3. ASSERT: Verify CALLS arcs towards property and target method
        // ---------------------------------------------------------------------
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == propSym.Id,
            "La Property frmContabilizzaFatture usata come ricevitore deve ricevere una chiamata");
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == targetSub.Id,
            "Il metodo ShowModalFormBy invocato sul tipo restituito dal modulo deve ricevere una chiamata");

        var deadResult = DeadCodeAnalyzer.Analyze(graph);
        deadResult.Items.Should().NotContain(i => i.Id == propSym.Id,
            "La Property frmContabilizzaFatture non deve essere considerata Dead Code");
    }

    // =========================================================================
    // 4. CHIAMATE POLIMORFICHE IN LATE BINDING (As Form / As Object / ActiveForm)
    // =========================================================================

    /// <summary>
    /// Verify that method calls on weakly typed 'As Form' variables
    /// (e.g. 'refFrmElencoDoc_Attiva.EliminaRicevute') are connected with Inferred confidence
    /// to compatible public routines present in the project Forms, preventing false positives of Dead Code.
    /// </summary>
    [Fact]
    public void LateBinding_OnAsFormVariable_ShouldCreateProbableCall_AndNotMarkRoutineAsDead()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: Secondary Form with public routine and MDIForm with 'As Form' variable
        // ---------------------------------------------------------------------
        var graph = new CodeGraph();

        // Form that defines DeleteReceipts
        var frmElencoCode = """
            VERSION 5.00
            Begin VB.Form frmElencoDocumenti
            End
            Attribute VB_Name = "frmElencoDocumenti"

            Public Sub EliminaRicevute()
            End Sub
            """;

        // MDIForm invoking DeleteReceipts on As Form variable
        var frmMdiCode = """
            VERSION 5.00
            Begin VB.Form MDIForm
            End
            Attribute VB_Name = "MDIForm"

            Private refFrmElencoDoc_Attiva As Form

            Private Sub cmdElimina_Click()
                Call refFrmElencoDoc_Attiva.EliminaRicevute
            End Sub
            """;

        // ---------------------------------------------------------------------
        // 2. ACT: Semantic resolution and Call Graph construction
        // ---------------------------------------------------------------------
        var resElenco = ModuleExtractor.ExtractModule(graph, "frmElencoDocumenti.frm", frmElencoCode);
        var resMdi = ModuleExtractor.ExtractModule(graph, "MDIForm.frm", frmMdiCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { resElenco, resMdi });

        var eliminaMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "EliminaRicevute");

        // ---------------------------------------------------------------------
        // 3. ASSERT: Verify CALLS Inferred arc and save from Dead Code
        // ---------------------------------------------------------------------
        // There must be a call arc with Inferred confidence towards DeleteReceipts
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == eliminaMethod.Id,
            "Le chiamate in late binding su 'As Form' devono collegare le routine pubbliche delle form con lo stesso nome");

        var deadResult = DeadCodeAnalyzer.Analyze(graph);
        deadResult.Items.Should().NotContain(i => i.Id == eliminaMethod.Id,
            "EliminaRicevute non deve essere considerato Dead Code quando invocato in late binding da una form attiva");
    }

    // =========================================================================
    // 5. DEFAULT FORM INSTANCE WITH SINGLE TOPIC IN BRACKETS
    // =========================================================================

    /// <summary>
    /// Check VB6 syntax with single parameter ByVal pass enclosed in parentheses
    /// on the default instance of the Form (e.g. 'frmRicercaDocumentiModal.setTipoDocumentoPreselezionate (dati_TipoDocumento)'),
    /// ensuring that the Sub is correctly resolved and not considered dead code.
    /// </summary>
    [Fact]
    public void DefaultFormInstance_WithSingleArgInParentheses_ShouldResolveAndNotBeDeadCode()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: Modal Form with Sub setter and Calling Form with ByVal syntax in brackets
        // ---------------------------------------------------------------------
        var graph = new CodeGraph();

        var frmModalCode = """
            VERSION 5.00
            Begin VB.Form frmRicercaDocumentiModal
            End
            Attribute VB_Name = "frmRicercaDocumentiModal"

            Public Sub setTipoDocumentoPreselezionato(ByVal tipoDoc As Long)
            End Sub
            """;

        var frmCallerCode = """
            VERSION 5.00
            Begin VB.Form frmFrameImportTerminalino
            End
            Attribute VB_Name = "frmFrameImportTerminalino"

            Private Sub Form_Load()
                Dim dati_TipoDocumento As Long
                dati_TipoDocumento = 1
                frmRicercaDocumentiModal.setTipoDocumentoPreselezionato (dati_TipoDocumento)
            End Sub
            """;

        // ---------------------------------------------------------------------
        // 2. ACT: Extraction, Resolution and Call Graph
        // ---------------------------------------------------------------------
        var resModal = ModuleExtractor.ExtractModule(graph, "frmRicercaDocumentiModal.frm", frmModalCode);
        var resCaller = ModuleExtractor.ExtractModule(graph, "frmFrameImportTerminalino.frm", frmCallerCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { resModal, resCaller });

        var targetMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "setTipoDocumentoPreselezionato");

        // ---------------------------------------------------------------------
        // 3. ASSERT: Verify CALLS arc and absence from the Dead Code
        // ---------------------------------------------------------------------
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == targetMethod.Id,
            "frmRicercaDocumentiModal.setTipoDocumentoPreselezionato (dati) deve risolvere la procedura sull'istanza predefinita del form");

        var deadResult = DeadCodeAnalyzer.Analyze(graph);
        deadResult.Items.Should().NotContain(i => i.Id == targetMethod.Id,
            "setTipoDocumentoPreselezionato non deve essere marcato come Dead Code");
    }
}
