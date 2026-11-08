using FluentAssertions;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

/// <summary>
/// Suite di test di regressione per validare e prevenire i falsi positivi di Dead Code
/// identificati nell'analisi del progetto 'LegacyApp' (documentati in 'falsi-positivi-dead-code.md').
/// 
/// CONTESTO DEI PROBLEMI RISCONTRATI:
/// 1. Membri di UserControl con prefisso di progetto nel designer:
///    Nelle sezioni visive 'Begin DUE.cntrDestinazione cntrDestinazione', il nome del tipo conteneva il prefisso
///    del progetto ('DUE.'). Il TypeResolver non risolveva il tipo del controllo, lasciando orfani tutti i metodi
///    e proprietà invocati sul controllo (oltre 100 falsi positivi).
/// 
/// 2. Eventi sollevati tramite istruzioni 'RaiseEvent':
///    Nello stesso componente (UserControl o Class), l'istruzione 'RaiseEvent Cancellazione' non generava un arco
///    CALLS verso il simbolo Event, facendo apparire tutti gli eventi personalizzati come codice morto (decine di falsi positivi).
/// 
/// 3. Routine e proprietà usate come ricevitore in chiamate a catena:
///    In espressioni come 'getFirstForm(parent).SelezionaRigheGriglia' o 'frmContabilizzaFatture.ShowModalFormBy',
///    la routine o proprietà intermedia che fungeva da ricevitore non veniva registrata nel Call Graph, risultando
///    non invocata e quindi etichettata come Dead Code.
/// 
/// 4. Chiamate polimorfiche in late binding (IDispatch):
///    Variabili dichiarate 'As Form', 'As Control' o 'As Object' (es. 'refFrmElencoDoc_Attiva.EliminaRicevute'
///    o 'ActiveForm.Chiudi') venivano classificate con confidenza Unknown e ignorate dal Call Graph, marcando
///    come inutilizzati i metodi pubblici delle Form invocati tramite polimorfismo dinamico.
/// 
/// 5. Chiamate all'istanza predefinita della Form con sintassi ByVal tra parentesi:
///    Costrutti VB6 come 'Form.Sub (argomento)' dove l'argomento singolo è racchiuso tra parentesi per forzare
///    il passaggio ByVal non venivano riconosciuti se invocati sull'istanza globale implicita della Form.
/// </summary>
public class Vb6DeadCodeFalsePositivesRegressionTests
{
    // =========================================================================
    // 1. MEMBRI USERCONTROL CON PREFISSO PROGETTO NEL DESIGNER DELLA FORM
    // =========================================================================

    /// <summary>
    /// Verifica che un controllo utente dichiarato con prefisso di progetto (es. 'Begin DUE.cntrDestinazione cntrDestinazione')
    /// risolva correttamente il tipo semantico sottostante ('cntrDestinazione.ctl') e che le invocazioni
    /// a metodi e proprietà sul controllo generino archi CALLS, evitando falsi positivi nel DeadCodeAnalyzer.
    /// </summary>
    [Fact]
    public void UserControl_WithProjectPrefixInDesigner_ShouldResolveMembers_AndNotBeMarkedAsDeadCode()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: Definizione del controllo utente e della Form che lo ospita
        // ---------------------------------------------------------------------
        var graph = new CodeGraph();

        // Controllo utente cntrDestinazione.ctl con proprietà 'Enabled' e metodo 'Clear'
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

        // Form frmGestioneDDT.frm che dichiara il controllo con prefisso di progetto 'Begin DUE.cntrDestinazione'
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
        // 2. ACT: Estrazione moduli, risoluzione semantica dei tipi e costruzione Call Graph
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
        // 3. ASSERT: Verifica archi CALLS e assenza dal report Dead Code
        // ---------------------------------------------------------------------
        // Verifica che le chiamate 'cntrDestinazione.Enabled' e 'cntrDestinazione.Clear' abbiano generato archi CALLS
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == enabledProp.Id,
            "cntrDestinazione.Enabled deve ricevere un arco Calls nonostante il tipo dichiarato nel designer sia qualificato come DUE.cntrDestinazione");
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == clearMethod.Id,
            "cntrDestinazione.Clear deve ricevere un arco Calls");

        // Esecuzione dell'analisi Dead Code: i membri non devono risultare orfani
        var deadResult = DeadCodeAnalyzer.Analyze(graph);
        deadResult.Items.Should().NotContain(i => i.Id == enabledProp.Id,
            "cntrDestinazione.Enabled non deve essere considerato Dead Code");
        deadResult.Items.Should().NotContain(i => i.Id == clearMethod.Id,
            "cntrDestinazione.Clear non deve essere considerato Dead Code");
    }

    /// <summary>
    /// Verifica che un controllo utente con nome istanza diverso dal nome del tipo (es. 'cntrRigaDocumento' di tipo 'DUE.cntrArticoli')
    /// risolva correttamente i membri invocati sia con sintassi diretta sia con keyword 'Call'.
    /// </summary>
    [Fact]
    public void UserControl_DifferentControlNameThanType_ShouldResolveMembers()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: Controllo 'cntrArticoli' ospitato con nome istanza 'cntrRigaDocumento'
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
        // 2. ACT: Estrazione, type resolution e generazione Call Graph
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
        // 3. ASSERT: Verifica risoluzione membri e assenza dal Dead Code
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
    /// Verifica l'accesso inter-modulo a catena: 'Form.Controllo.Metodo' (es. 'frmPuntoCassa.VisorePuntoCassa.CaricaSkinOverride')
    /// invocato da un modulo standard .bas.
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
        // 2. ACT: Estrazione dei 3 moduli e risoluzione semantica complessiva
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
        // 3. ASSERT: Verifica che l'invocazione a tre livelli crei l'arco CALLS
        // ---------------------------------------------------------------------
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == skinMethod.Id,
            "La chiamata a catena frmPuntoCassa.VisorePuntoCassa.CaricaSkinOverride deve raggiungere il metodo del controllo");
    }

    // =========================================================================
    // 2. EVENTI SOLLEVATI TRAMITE RaiseEvent NELLO STESSO MODULO
    // =========================================================================

    /// <summary>
    /// Verifica che l'istruzione 'RaiseEvent NomeEvento' colleghi formalmente la routine chiamante
    /// al simbolo dell'evento con una relazione CALLS, garantendo che gli eventi dichiarati non vengano
    /// falsamente segnalati come Dead Code.
    /// </summary>
    [Fact]
    public void RaiseEvent_InSameComponent_ShouldCreateCallRelationship_AndNotBeMarkedAsDeadCode()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: UserControl con eventi 'Cancellazione' e 'Selezione' sollevati internamente
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
        // 2. ACT: Estrazione modulo e costruzione del grafo semantico
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
        // 3. ASSERT: Verifica relazione CALLS verso l'evento e assenza da Dead Code
        // ---------------------------------------------------------------------
        // CallGraphBuilder deve creare la relazione Calls verso l'evento a partire dal metodo che esegue RaiseEvent
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls &&
                                                 r.SourceId == cmdSvuota.Id &&
                                                 r.TargetId == cancelEvent.Id,
            "RaiseEvent deve generare un arco Calls da cmdSvuota_Click all'evento Cancellazione");

        // L'analisi Dead Code deve riconoscere gli eventi come attivi e raggiungibili
        var deadResult = DeadCodeAnalyzer.Analyze(graph);
        deadResult.Items.Should().NotContain(i => i.Id == cancelEvent.Id,
            "L'evento Cancellazione sollevato tramite RaiseEvent non deve essere marcato come codice morto");
        deadResult.Items.Should().NotContain(i => i.Id == selEvent.Id,
            "L'evento Selezione sollevato tramite RaiseEvent non deve essere marcato come codice morto");
    }

    // =========================================================================
    // 3. CHIAMATE A CATENA CON RICEVITORE ROUTINE (Function/Property.Member)
    // =========================================================================

    /// <summary>
    /// Verifica che in una chiamata a catena in cui il ricevitore è una Function (es. 'getFirstForm(parent).SelezionaRigheGriglia (True)'),
    /// la Function ricevente ('getFirstForm') venga registrata come chiamata (arco CALLS) insieme al metodo target finale.
    /// </summary>
    [Fact]
    public void ChainedInvocation_WithFunctionReceiver_ShouldRecordCallToReceiverFunction()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: MDIForm con Function getFirstForm e chiamata a catena in un gestore comandi
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
        // 2. ACT: Risoluzione semantica della catena
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
        // 3. ASSERT: Entrambe le routine (ricevitore e membro finale) devono ricevere archi CALLS
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
    /// Verifica che in una chiamata in cui il ricevitore è una Property Get dichiarata in un modulo .bas
    /// che restituisce il modulo stesso (es. 'frmContabilizzaFatture.ShowModalFormBy 123'),
    /// sia la Property ricevente sia il metodo finale vengano registrati nel Call Graph.
    /// </summary>
    [Fact]
    public void ChainedInvocation_WithBasPropertyReceiver_ShouldRecordCallToBasProperty()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: Modulo .bas con Property accessor e Form chiamante
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
        // 2. ACT: Estrazione, risoluzione dei moduli .bas e .frm e costruzione grafo
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
        // 3. ASSERT: Verifica archi CALLS verso property e metodo target
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
    /// Verifica che le invocazioni a metodo su variabili debolmente tipizzate 'As Form'
    /// (es. 'refFrmElencoDoc_Attiva.EliminaRicevute') vengano collegate con confidenza Inferred
    /// alle routine pubbliche compatibili presenti nelle Form del progetto, prevenendo falsi positivi di Dead Code.
    /// </summary>
    [Fact]
    public void LateBinding_OnAsFormVariable_ShouldCreateProbableCall_AndNotMarkRoutineAsDead()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: Form secondaria con routine pubblica e MDIForm con variabile 'As Form'
        // ---------------------------------------------------------------------
        var graph = new CodeGraph();

        // Form che definisce EliminaRicevute
        var frmElencoCode = """
            VERSION 5.00
            Begin VB.Form frmElencoDocumenti
            End
            Attribute VB_Name = "frmElencoDocumenti"

            Public Sub EliminaRicevute()
            End Sub
            """;

        // MDIForm che invoca EliminaRicevute su variabile As Form
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
        // 2. ACT: Risoluzione semantica e costruzione Call Graph
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
        // 3. ASSERT: Verifica arco CALLS Inferred e salvataggio da Dead Code
        // ---------------------------------------------------------------------
        // Deve esistere un arco di chiamata con confidenza Inferred verso EliminaRicevute
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == eliminaMethod.Id,
            "Le chiamate in late binding su 'As Form' devono collegare le routine pubbliche delle form con lo stesso nome");

        var deadResult = DeadCodeAnalyzer.Analyze(graph);
        deadResult.Items.Should().NotContain(i => i.Id == eliminaMethod.Id,
            "EliminaRicevute non deve essere considerato Dead Code quando invocato in late binding da una form attiva");
    }

    // =========================================================================
    // 5. ISTANZA PREDEFINITA FORM CON ARGOMENTO SINGOLO TRA PARENTESI
    // =========================================================================

    /// <summary>
    /// Verifica la sintassi VB6 con passaggio ByVal a parametro singolo racchiuso tra parentesi
    /// sull'istanza predefinita della Form (es. 'frmRicercaDocumentiModal.setTipoDocumentoPreselezionato (dati_TipoDocumento)'),
    /// garantendo che la Sub sia correttamente risolta e non considerata codice morto.
    /// </summary>
    [Fact]
    public void DefaultFormInstance_WithSingleArgInParentheses_ShouldResolveAndNotBeDeadCode()
    {
        // ---------------------------------------------------------------------
        // 1. ARRANGE: Form modale con Sub setter e Form chiamante con sintassi ByVal tra parentesi
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
        // 2. ACT: Estrazione, risoluzione e Call Graph
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
        // 3. ASSERT: Verifica arco CALLS e assenza dal Dead Code
        // ---------------------------------------------------------------------
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.TargetId == targetMethod.Id,
            "frmRicercaDocumentiModal.setTipoDocumentoPreselezionato (dati) deve risolvere la procedura sull'istanza predefinita del form");

        var deadResult = DeadCodeAnalyzer.Analyze(graph);
        deadResult.Items.Should().NotContain(i => i.Id == targetMethod.Id,
            "setTipoDocumentoPreselezionato non deve essere marcato come Dead Code");
    }
}
