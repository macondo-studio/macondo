using Macondo.App.Models;

namespace Macondo.App.Services;

public sealed class LocalizationService
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    public event EventHandler<AppLanguage>? LanguageChanged;

    public AppLanguage CurrentLanguage { get; private set; } = DetectSystemLanguage();

    private readonly Dictionary<string, string> _itStrings = new()
    {
        // Menu File
        ["Menu_File"] = "_File",
        ["Menu_Open"] = "Apri",
        ["Menu_OpenProjectFiles"] = "Progetto / Gruppo...",
        ["Menu_OpenFolder"] = "Cartella...",
        ["Menu_Recent"] = "Recenti",
        ["Menu_NoRecentProjects"] = "Nessun elemento recente",
        ["Menu_ClearRecentProjects"] = "Cancella cronologia recenti",
        ["Menu_RefreshProject"] = "Ricarica Progetto Corrente",
        ["Menu_LoadFixtures"] = "Carica Fixtures Test",
        ["Menu_GenerateFake"] = "Grafo Demo (~500 nodi)",
        ["Menu_Settings"] = "Impostazioni...",
        ["Menu_Exit"] = "Esci",

        // Menu Visualizza
        ["Menu_View"] = "_Visualizza",
        ["Menu_ToggleExplorer"] = "Mostra/Nascondi Project Explorer",
        ["Menu_ToggleInspector"] = "Mostra/Nascondi Symbol Inspector",
        ["Menu_ResetView"] = "Reset Vista",
        ["Menu_ExpandAll"] = "Espandi Tutto",
        ["Menu_CollapseAll"] = "Comprimi Tutto",
        ["Menu_FilterDetailLevel"] = "Filtro Livelli Simboli",
        ["Menu_FilterProjects"] = "Filtra Progetti",
        ["Filter_AllMembers"] = "Completo (Tutti i Membri)",
        ["Filter_MethodsAndProperties"] = "Metodi & Proprietà",
        ["Filter_ContainersOnly"] = "Solo Contenitori (Moduli)",
        ["Filter_Custom"] = "Personalizzato...",
        ["Filter_ShowMethods"] = "Mostra Metodi (Sub / Function)",
        ["Filter_ShowProperties"] = "Mostra Proprietà",
        ["Filter_ShowFields"] = "Mostra Campi e Variabili",
        ["Filter_ShowConstants"] = "Mostra Costanti",
        ["Filter_ShowTypesEnums"] = "Mostra Tipi ed Enumerazioni",
        ["HUD_FilterLabel"] = "Dettaglio:",
        ["HUD_FilterButton"] = "Filtra Membri",
        ["HUD_FilterProjects"] = "Progetti",
        ["HUD_ProjectsFilterTitle"] = "Filtro Progetti",
        ["HUD_SelectAll"] = "Tutti",
        ["Tooltip_FilterMethods"] = "Mostra o nasconde le routine (Sub e Function)",
        ["Tooltip_FilterProperties"] = "Mostra o nasconde le proprietà (Property)",
        ["Tooltip_FilterFields"] = "Mostra o nasconde campi e variabili interne",
        ["Tooltip_FilterConstants"] = "Mostra o nasconde le costanti",
        ["Tooltip_FilterTypes"] = "Mostra o nasconde tipi UDT ed enumerazioni",
        ["Tooltip_FilterProjects"] = "Mostra o nasconde i singoli progetti nel grafo",
        ["Tooltip_LegendToggle"] = "Clicca per mostrare/nascondere questa categoria nel grafo",
        ["Menu_GraphLayout"] = "Disposizione Grafo",
        ["Layout_StandardRadial"] = "Radiale Standard",
        ["Layout_AffinityRadial"] = "Radiale per Affinità",
        ["Layout_ClusteredIslands"] = "Cluster a Isole (Sottosistemi)",
        ["Layout_ForceDirected"] = "Force-Directed (Fisica a Molle)",
        ["Menu_HoverSelection"] = "Selezione al Passaggio del Mouse (Hover)",
        ["HUD_LayoutLabel"] = "Layout:",
        ["Status_LayoutApplied"] = "Applicato layout '{0}' al grafo.",

        // Menu Analisi
        ["Menu_Analysis"] = "_Analisi",
        ["Menu_BlastRadius"] = "Calcola Blast Radius",
        ["Menu_ShowCallers"] = "Mostra Callers",
        ["Menu_ShowCallees"] = "Mostra Callees",
        ["Menu_ClearHighlight"] = "Pulisci Filtri",

        // Ricerca
        ["Search_Watermark"] = "Cerca simbolo nel grafo...",
        ["Search_NoMatches"] = "Nessun simbolo trovato per '{0}'",
        ["Search_SingleMatch"] = "Trovato 1 simbolo: [{0}] {1}",
        ["Search_MultipleMatches"] = "Trovati {0} simboli (clicca per selezionare):",

        // Explorer
        ["Explorer_Title"] = "Project Explorer",
        ["Explorer_WorkspaceFormat"] = "Workspace ({0} Progetti)",
        ["Tooltip_HideExplorer"] = "Nascondi Project Explorer (Ctrl+B)",
        ["Tooltip_ToggleExplorer"] = "Mostra/Nascondi Project Explorer (Ctrl+B)",
        ["Explorer_Group_Forms"] = "Forms",
        ["Explorer_Group_Modules"] = "Moduli",
        ["Explorer_Group_Classes"] = "Moduli di classe",
        ["Explorer_Group_UserControls"] = "Controlli utente",
        ["Explorer_Group_Types"] = "Tipi definiti dall'utente",
        ["Explorer_Group_Enums"] = "Enumerazioni",
        ["Explorer_Group_Other"] = "Altri file",
        ["Explorer_OpenOriginalFile"] = "Apri file originale",
        ["Explorer_OpenContainingFolder"] = "Mostra nella cartella",
        ["Explorer_CopyFilePath"] = "Copia percorso file",
        ["Explorer_FileNotFound"] = "File non trovato su disco: {0}",
        ["Explorer_FileOpened"] = "Aperto file: {0}",
        ["Explorer_FolderOpened"] = "Aperta cartella: {0}",
        ["Explorer_PathCopied"] = "Percorso copiato negli appunti: {0}",
        ["Inspector_Tooltip_OpenFile"] = "Apri file sorgente originale",
        ["Inspector_Tooltip_OpenFolder"] = "Mostra file nella cartella di sistema",

        // Inspector & Blast Radius
        ["Inspector_Title"] = "Symbol Inspector & Blast Radius",
        ["Tooltip_HideInspector"] = "Nascondi Symbol Inspector (Ctrl+I)",
        ["Tooltip_ToggleInspector"] = "Mostra/Nascondi Symbol Inspector (Ctrl+I)",

        // Tab Centrali
        ["Tab_GraphView"] = "Vista Grafo",
        ["Tab_DsmMatrix"] = "Matrice DSM",
        ["Tab_TreemapMetrics"] = "Treemap Metriche",
        ["Tab_RiskHotspots"] = "Hotspot di Rischio",
        ["Tab_ScreenFlow"] = "Flusso Schermate",
        ["Tab_ControlFlow"] = "Control Flow (CFG)",
        ["Tab_BlastRadiusGrid"] = "Griglia Blast Radius",

        // Treemap Metriche
        ["Treemap_Placeholder"] = "Nessuna Treemap calcolata. Carica un progetto per visualizzare la mappa delle metriche.",
        ["Treemap_BreadcrumbDrillUp"] = "⬅ Torna su | Vista: {0} ({1})",
        ["Treemap_BreadcrumbRoot"] = "Tutti i Moduli del Progetto (Grandezza = LOC / Dimensione | Colore = Complessità)",
        ["Treemap_MembersCount"] = "{0} membri",
        ["Treemap_HoverSub"] = "Complessità Ciclomatica: {0:F1} | Dimensione: {1:F0} LOC",

        // Hotspot di Rischio
        ["Hotspot_Placeholder"] = "Nessun dataset Hotspot calcolato. Carica un progetto per analizzare i punti di rischio.",
        ["Hotspot_DangerBadge"] = "🔥 ZONA HOTSPOT (Critico)",
        ["Hotspot_SafeBadge"] = "✓ ZONA STABILE",
        ["Hotspot_AxisY"] = "↑ COMPLESSITÀ CICLOMATICA",
        ["Hotspot_AxisX"] = "FREQUENZA MODIFICHE / FAN-IN (CHURN) →",
        ["Hotspot_HoverSub"] = "Rischio: {0:F0}% ({1}) | CC: {2:F1} | Churn: {3:F0} | Chiamanti: {4}",

        // Flusso Schermate (Screen Flow)
        ["ScreenFlow_Placeholder"] = "Nessun Form o Schermata UI individuata nel progetto corrente.",
        ["ScreenFlow_ControlsCount"] = "Controlli UI: {0}",
        ["ScreenFlow_EventHandlersCount"] = "Event Handlers: {0}",
        ["ScreenFlow_TransitionsCount"] = "Transizioni: {0} out / {1} in",
        ["ScreenFlow_StartupBadge"] = "STARTUP",

        // Matrice DSM
        ["Dsm_Placeholder"] = "Nessuna Matrice DSM calcolata. Carica un progetto per analizzare le dipendenze.",

        // Control Flow Graph (CFG)
        ["ControlFlow_Placeholder"] = "Nessun Control Flow Graph (CFG) selezionato. Seleziona una routine o metodo per visualizzare il flusso.",
        ["ControlFlow_Header"] = "CFG: {0} ({1}) | Complessità Ciclomatica (CC): {2} | Blocchi Base: {3}",

        // Floating HUD
        ["HUD_ResetView"] = "Reset Vista",
        ["HUD_ExpandAll"] = "Espandi Tutto",
        ["HUD_CollapseAll"] = "Comprimi Tutto",
        ["HUD_Refresh"] = "Ricarica",
        ["HUD_BlastRadius"] = "Blast Radius",
        ["HUD_ShowCallers"] = "Mostra Callers",
        ["HUD_ShowCallees"] = "Mostra Callees",
        ["HUD_ClearFilter"] = "Pulisci Filtri",

        // Griglia Toolbar
        ["Grid_FilterLabel"] = "Filtra:",
        ["Grid_FilterWatermark"] = "Nome routine, modulo o progetto...",
        ["Grid_RiskLabel"] = "Rischio:",
        ["Grid_AllRisks"] = "Tutti i Livelli",
        ["Grid_Recalculate"] = "Ricalcola Audit",
        ["Grid_StatsFormat"] = "{0} di {1} routine",
        ["Grid_Calculating"] = "Calcolo audit in corso per {0} routine...",

        // Griglia Colonne
        ["Col_Project"] = "Progetto",
        ["Col_Routine"] = "Routine / Simbolo",
        ["Col_Module"] = "Modulo / Container",
        ["Col_DirectCallers"] = "Chiamanti Diretti",
        ["Col_IndirectCallers"] = "Chiamanti Indiretti",
        ["Col_Depth"] = "Profondità",
        ["Col_Risk"] = "Rischio",
        ["Col_Forms"] = "Form Impattate",
        ["Col_TotalImpact"] = "Tot. Impatto",
        ["Col_Action"] = "Azione",
        ["Btn_ShowInGraph"] = "Mostra Grafo",

        // Tooltip Pulsanti e Azioni
        ["Tooltip_Settings"] = "Impostazioni (Ctrl+,)",
        ["Tooltip_Minimize"] = "Riduci a icona",
        ["Tooltip_Maximize"] = "Ingrandisci / Ripristina",
        ["Tooltip_Close"] = "Chiudi",
        ["Tooltip_ResetView"] = "Ripristina zoom 100% e centra la visuale",
        ["Tooltip_ExpandAll"] = "Espande tutti i moduli mostrando metodi e proprietà",
        ["Tooltip_CollapseAll"] = "Comprime tutti i moduli nascondendo i membri interni",
        ["Tooltip_RefreshProject"] = "Ricarica Progetto da disco e aggiorna cache (Ctrl+R)",
        ["Tooltip_BlastRadius"] = "Calcola ed evidenzia l'impatto di modifica (chiamanti indiretti, form e classi impattate)",
        ["Tooltip_ShowCallers"] = "Evidenzia chi chiama la routine selezionata (callers)",
        ["Tooltip_ShowCallees"] = "Evidenzia cosa chiama la routine selezionata (callees)",
        ["Tooltip_ClearHighlight"] = "Rimuove le evidenziazioni e i filtri attivi sul grafo",
        ["Tooltip_RecalculateAudit"] = "Ricalcola la matrice di rischio e impatto per tutte le routine",
        ["Tooltip_ShowInGraph"] = "Centra e seleziona questa routine nel canvas del grafo",

        // Inspector & Blast Radius
        ["Inspector_Title"] = "Ispettore Simboli & Raggio d'Impatto",
        ["Inspector_NoSelection"] = "Nessun simbolo selezionato.",
        ["Inspector_Hint1"] = "• Clicca su un nodo per visualizzare dettagli semantici, tipi, parametri e chiamate.",
        ["Inspector_Hint2"] = "• Clicca su 'Blast Radius' per calcolare il raggio di impatto e il rischio di regressione.",
        ["Inspector_Hint3"] = "• Doppio clic su una Classe, Form o Modulo per espandere/comprimere i membri.",
        ["Inspector_Sec_Symbol"] = "SIMBOLO",
        ["Inspector_Sec_Kind"] = "CATEGORIA",
        ["Inspector_Sec_Visibility"] = "VISIBILITÀ",
        ["Inspector_Sec_Type"] = "TIPO",
        ["Inspector_Sec_Params"] = "PARAMETRI FORMALI",
        ["Inspector_Sec_File"] = "FILE & RIGHE",
        ["Inspector_Sec_BlastRadius"] = "RAGGIO D'IMPATTO (BLAST RADIUS)",
        ["Inspector_DirectCallersCount"] = "Chiamanti Diretti:",
        ["Inspector_IndirectCallersCount"] = "Chiamanti Indiretti:",
        ["Inspector_MaxDepth"] = "Profondità Max (Depth):",
        ["Inspector_Sec_Forms"] = "FORM UI IMPATTATE",
        ["Inspector_Sec_Classes"] = "CLASSI & MODULI TOCCATI",
        ["Inspector_Sec_Paths"] = "PERCORSI DI ESECUZIONE (CALL CHAINS)",
        ["Inspector_Sec_WhoCallsMe"] = "CHIAMANTI DIRETTI (WHO CALLS ME?)",
        ["Inspector_Sec_WhatDoICall"] = "CHIAMATE EFFETTUATE (WHAT DO I CALL?)",
        ["Inspector_Sec_Instantiates"] = "CLASSI ISTANZIATE (NEW)",
        ["Inspector_AiContextDesc"] = "Estrae il subgrafo semantico di questo simbolo ottimizzato per prompt AI.",
        ["Inspector_Calculating"] = "CALCOLO...",
        ["Inspector_CalculatingCallers"] = "Calcolo chiamanti in corso...",
        ["Inspector_CalculatingCallees"] = "Calcolo chiamate in corso...",
        ["Inspector_NoCallers"] = "(nessun chiamante diretto)",
        ["Inspector_NoCallees"] = "(nessuna chiamata effettuata)",
        ["Inspector_NoItems"] = "(nessuno)",
        ["Risk_Low"] = "BASSO",
        ["Risk_Medium"] = "MEDIO",
        ["Risk_High"] = "ALTO",
        ["Risk_Critical"] = "CRITICO",
        ["Risk_Format"] = "RISCHIO: {0}",
        ["Status_SelectSymbolForBlast"] = "Seleziona prima un simbolo per calcolare il Blast Radius.",
        ["Status_BlastReportFormat"] = "Blast Radius per '{0}': {1} diretti, {2} indiretti, {3} form, Rischio: {4}",
        ["Status_BlastError"] = "Errore nel calcolo del Blast Radius: {0}",
        ["Status_SelectSymbolForCallers"] = "Seleziona prima una routine o un modulo per visualizzare i chiamanti (callers).",
        ["Status_SelectSymbolForCallees"] = "Seleziona prima una routine o un modulo per visualizzare le chiamate uscenti (callees).",
        ["Status_SelectedFromGrid"] = "Selezionato dalla griglia: [{0}] {1}",
        ["Status_NodeAnalysisError"] = "Errore nell'analisi del simbolo: {0}",
        ["Loading_BlastRadiusTitle"] = "Calcolo Blast Radius in corso...",
        ["Loading_BlastRadiusSub"] = "Analisi d'impatto e percorsi di chiamata per '{0}'",
        ["Loading_NodeAnalysisTitle"] = "Analisi del nodo in corso...",
        ["Loading_NodeAnalysisSub"] = "Esplorazione chiamanti e dipendenze per '{0}'",
        ["Loading_AnalyzeProjectTitle"] = "Analisi del progetto VB6...",
        ["Loading_AnalyzeProjectSub"] = "Scansione ed estrazione da '{0}'...",
        ["Loading_FixturesTitle"] = "Caricamento fixture VB6...",
        ["Loading_FixturesSub"] = "Analisi semantica dei moduli fixture in corso...",
        ["Loading_ParsingFile"] = "Analisi ed estrazione: {0}",
        ["Loading_ResolvingTypes"] = "Risoluzione globale dei tipi in corso...",
        ["Loading_BuildingCallGraph"] = "Costruzione Call Graph e relazioni...",
        ["Loading_GeneratingVisualGraph"] = "Generazione grafo visuale e layout...",
        ["Loading_FilesProgressFormat"] = "{0} / {1} file ({2}%)",
        ["Loading_NodesExtractedFormat"] = "{0} nodi rilevati",
        ["Loading_Cancel"] = "Annulla",
        ["Loading_Cancelling"] = "Annullamento in corso...",
        ["Status_AnalysisCancelled"] = "Analisi annullata dall'utente.",
        ["Notification_AnalysisCompleted_Title"] = "Analisi VB6 completata",
        ["Notification_AnalysisCompleted_Message"] = "L'analisi di '{0}' è terminata con successo ({1} nodi estratti).",
        ["Notification_FixturesCompleted_Message"] = "L'analisi delle fixture VB6 è terminata con successo ({0} nodi estratti).",

        // Finestra Impostazioni
        ["Settings_Title"] = "Impostazioni",
        ["Settings_Tab_Appearance"] = "Aspetto & Temi",
        ["Settings_Tab_Language"] = "Lingua",
        ["Settings_Tab_Behavior"] = "Comportamento",
        ["Settings_Tab_Shortcuts"] = "Scorciatoie da Tastiera",
        ["Settings_Appearance_Header"] = "Seleziona Tema Grafico",
        ["Settings_Appearance_Sub"] = "Scegli una palette visiva per l'interfaccia e il canvas del grafo.",
        ["Theme_DarkModern_Title"] = "Dark Modern (Predefinito)",
        ["Theme_DarkModern_Desc"] = "Stile grafite scuro minimale con accenti verde smeraldo.",
        ["Theme_DarkStudio_Title"] = "Dark Studio",
        ["Theme_DarkStudio_Desc"] = "Palette scura classica con accento blu.",
        ["Theme_OneDark_Title"] = "One Dark Pro",
        ["Theme_OneDark_Desc"] = "Ispirato al celebre tema Atom con toni blu notte e accento celeste.",
        ["Theme_DarkGreen_Title"] = "Forest Dark (Verde Scuro)",
        ["Theme_DarkGreen_Desc"] = "Atmosfera scura foresta con toni smeraldo e verde salvia.",
        ["Theme_LightModern_Title"] = "Light Modern (Chiaro)",
        ["Theme_LightModern_Desc"] = "Tema chiaro ad alto contrasto per ambienti diurni con accento cobalto.",
        ["Settings_Language_Header"] = "Seleziona Lingua Interfaccia",
        ["Settings_Language_Sub"] = "La lingua selezionata verrà applicata immediatamente a tutti i menu e comandi.",
        ["Lang_Italian_Title"] = "Italiano",
        ["Lang_Italian_Desc"] = "Interfaccia, menu e documentazione in lingua italiana.",
        ["Lang_English_Title"] = "English",
        ["Lang_English_Desc"] = "Interfaccia completa in inglese, menu e terminologia di analisi.",
        ["Settings_Behavior_Header"] = "Opzioni Interazione e Grafo",
        ["Settings_Behavior_Sub"] = "Personalizza il comportamento del puntatore del mouse e della vista grafo.",
        ["Settings_HoverSelection_Title"] = "Selezione al passaggio del mouse (Hover)",
        ["Settings_HoverSelection_Sub"] = "Seleziona e ispeziona automaticamente i nodi quando il cursore vi passa sopra.",
        ["Settings_Shortcuts_Header"] = "Catalogo Scorciatoie da Tastiera",
        ["Settings_Shortcuts_Sub"] = "Tutte le combinazioni di tasti per navigare rapidamente nel codice e nel grafo.",
        ["Settings_Shortcuts_Search"] = "Cerca scorciatoia...",
        ["Settings_Reset"] = "Ripristina Predefiniti",
        ["Settings_Close"] = "Chiudi",

        // Esportazione, Importazione & LLM
        ["Menu_ExportMarkdown"] = "Esporta Repo Map (Markdown)...",
        ["Menu_ExportJson"] = "Esporta Grafo (JSON)...",
        ["Menu_ImportJson"] = "Importa Grafo (JSON)...",
        ["Menu_ExportSlice"] = "Esporta Context Slice LLM (del simbolo)...",
        ["HUD_ExportMarkdown"] = "Esporta MD",
        ["HUD_ExportJson"] = "Esporta JSON",
        ["HUD_ImportJson"] = "Importa JSON",
        ["Inspector_Sec_AIContext"] = "AI CONTEXT / LLM SLICE",
        ["Inspector_CopyLlmSlice"] = "Copia Slice LLM",
        ["Inspector_SaveLlmSlice"] = "Salva Slice (.md)",
        ["Tooltip_ExportMarkdown"] = "Esporta l'intero grafo in formato Markdown compatto gerarchico per modelli AI",
        ["Tooltip_ExportJson"] = "Esporta l'intero grafo in formato JSON strutturato",
        ["Tooltip_ImportJson"] = "Importa e visualizza un grafo CodeGraph da file JSON",
        ["Tooltip_CopyLlmSlice"] = "Copia negli appunti il subgrafo semantico del simbolo selezionato",
        ["Tooltip_SaveLlmSlice"] = "Salva su file .md il context slice del simbolo selezionato",
        ["Status_ExportSuccess"] = "Grafo esportato con successo in '{0}'.",
        ["Status_ImportSuccess"] = "Grafo importato da '{0}': {1} simboli, {2} relazioni.",
        ["Status_CopiedLlmSlice"] = "Context Slice LLM per '{0}' copiato negli appunti!",
        ["Status_NoSymbolSelected"] = "Nessun simbolo selezionato per l'esportazione dello slice.",
        ["Status_NoSemanticGraph"] = "Nessun grafo semantico disponibile per l'esportazione.",
        ["Info_CliTitle"] = "4. Utilizzo da Riga di Comando (CLI & Headless)",
        ["Info_CliDescription"] = "Macondo può essere eseguito direttamente da terminale (PowerShell, Command Prompt o CI/CD) per analizzare progetti ed esportare report senza aprire l'interfaccia grafica:",
        ["Info_CliCommentMd"] = "# Esporta la Repo Map Markdown del progetto",
        ["Info_CliCommentJson"] = "# Esporta il grafo semantico completo in JSON",
        ["Info_CliCommentBoth"] = "# Esporta entrambi i formati contemporaneamente",

        // Git & SVN (VCS) Integration
        ["Git_BranchTooltip"] = "Branch Git corrente: {0}. Clicca per cambiare branch.",
        ["Git_SwitchBranchTitle"] = "Cambia Branch Git",
        ["Git_SearchBranchWatermark"] = "Cerca branch...",
        ["Git_NoBranches"] = "Nessun branch trovato",
        ["Git_SwitchingBranch"] = "Checkout del branch '{0}' in corso...",
        ["Git_SwitchSuccess"] = "Branch '{0}' attivato con successo.",
        ["Git_SwitchError"] = "Errore durante il cambio branch: {0}",
        ["Svn_BranchTooltip"] = "Branch/URL SVN corrente: {0}. Clicca per cambiare (svn switch).",
        ["Svn_SwitchBranchTitle"] = "Cambia Branch/Trunk SVN (svn switch)",
        ["Svn_SearchBranchWatermark"] = "Cerca branch o percorso SVN...",
        ["Svn_SwitchingBranch"] = "Switch a '{0}' in corso (svn switch)...",
        ["Svn_SwitchSuccess"] = "Switch completato con successo a '{0}'.",
        ["Svn_SwitchError"] = "Errore durante svn switch: {0}",

        // Aggiornamenti Software & Cache (Menu Aiuto)
        ["Menu_Help"] = "_Aiuto",
        ["Menu_HelpInfo"] = "Guida & Concetti...",
        ["Menu_CheckUpdates"] = "Verifica Aggiornamenti...",
        ["Menu_ResetSettings"] = "Ripristina Impostazioni Predefinite...",
        ["Menu_ClearCache"] = "Pulisci Cache Progetti...",
        ["Tooltip_ResetSettings"] = "Ripristina tutte le preferenze e impostazioni ai valori predefiniti",
        ["Tooltip_ClearCache"] = "Elimina tutti gli snapshot di cache persistenti da disco (%APPDATA%\\Macondo\\cache\\)",
        ["Update_AvailableTitle"] = "Nuovo Aggiornamento Disponibile!",
        ["Update_AvailableSubtitle"] = "Versione {0} disponibile (versione attuale: {1})",
        ["Update_NotesLabel"] = "Novità e Note di Rilascio:",
        ["Update_ButtonLater"] = "Scarica dopo",
        ["Update_ButtonNow"] = "Aggiorna ora",
        ["Update_Checking"] = "Controllo aggiornamenti in corso...",
        ["Update_NoUpdates"] = "L'applicazione è aggiornata all'ultima versione ({0}).",

        // Legenda Grafo
        ["Legend_Title"] = "Legenda Nodi & Colori",
        ["Legend_Forms"] = "Form (.frm)",
        ["Legend_Modules"] = "Modulo (.bas)",
        ["Legend_Classes"] = "Classe (.cls)",
        ["Legend_Controls"] = "Controllo (.ctl)",
        ["Legend_Methods"] = "Sub / Function",
        ["Legend_Properties"] = "Proprietà",
        ["Legend_Fields"] = "Campo / Var",
        ["Legend_Types"] = "Tipo / Enum",
        ["Legend_Calls"] = "Calls (Chiamata)",
        ["Legend_Instantiates"] = "Instantiates (New)",

        // Grafo Canvas
        ["Graph_Placeholder"] = "Nessun CodeGraph caricato. Apri un progetto o una cartella VB6 dal menu File.",

        // Barra di Stato & Statistiche
        ["Status_Ready"] = "Pronto.",
        ["Status_Selected"] = "Selezionato: [{0}] {1}",
        ["Status_StatsFormat"] = "Nodi: {0} | Relazioni: {1} (Chiamate: {2}, Istanze: {3})",
        ["Stats_ProjectsFormat"] = "{0} Progetti",
        ["Stats_ProjectsRatioFormat"] = "{0}/{1} Progetti",
        ["Stats_NodesFormat"] = "{0} Nodi",
        ["Stats_NodesRatioFormat"] = "{0}/{1} Nodi",
        ["Stats_RelationsFormat"] = "{0} Relazioni",
        ["Stats_RelationsCrossFormat"] = "{0} Relazioni ({1} Cross-Project)",
        ["Stats_CallsFormat"] = "{0} Chiamate",
        ["Stats_InstantiatesFormat"] = "{0} Istanze",
        ["Explorer_ProjectsVisible"] = "{0}/{1} Progetti visibili",
        ["Open_FolderTitle"] = "Seleziona una o più cartelle contenenti file/progetti VB6",
        ["Open_ProjectFilesTitle"] = "Seleziona file progetto VB6 (.vbp) o file gruppo (.vbg)",
        ["Open_FileTypeProjects"] = "Progetti e Gruppi VB6 (*.vbp, *.vbg)",
        ["Open_FileTypeAll"] = "Tutti i file (*.*)",
        ["Status_FixturesLoaded"] = "Suite di fixture VB6 analizzata con successo in memoria.",
        ["Status_FakeGraphGenerating"] = "Generazione del grafo sintetico (~500 nodi)...",
        ["Status_FakeGraphGenerated"] = "Grafo sintetico di benchmark (~500 nodi) generato con successo.",
        ["Status_AnalyzingSources"] = "Analisi di {0} sorgenti/progetti in corso...",
        ["Status_WorkspaceAnalyzed"] = "Workspace analizzato: {0} Progetti, {1} Simboli, {2} Relazioni Cross-Project.",
        ["Status_NodesDisplayed"] = "Nodi visualizzati: {0} di {1}",
        ["Status_CalleesFound"] = "Callees per '{0}': trovate {1} chiamate uscenti verso ({2}).",
        ["Status_CalleesNone"] = "Nessuna chiamata uscente effettuata da '{0}'.",
        ["Status_CallersFound"] = "Callers per '{0}': trovati {1} chiamanti diretti da ({2}).",
        ["Status_CallersNone"] = "Nessun chiamante diretto per '{0}'.",
        ["Status_DsmDependency"] = "DSM: Dipendenza [{0} collegamenti] tra container.",
        ["Status_CfgBlock"] = "CFG Blocco: [{0}] {1}",
        ["Status_ErrorLoading"] = "Errore durante il caricamento: {0}",
        ["Status_ErrorGenerating"] = "Errore durante la generazione: {0}",
        ["Status_ErrorAnalyzing"] = "Errore durante l'analisi: {0}",
        ["Status_ErrorOpenFile"] = "Errore nell'apertura del file: {0}",
        ["Status_ErrorOpenFolder"] = "Errore nell'apertura della cartella: {0}",
        ["Status_ErrorCopyPath"] = "Errore copia percorso: {0}",
        ["Status_ErrorGridCalc"] = "Errore durante il calcolo della griglia Blast Radius: {0}",
        ["Status_ErrorExportMarkdown"] = "Errore durante l'esportazione Markdown: {0}",
        ["Status_ErrorExportJson"] = "Errore durante l'esportazione JSON: {0}",
        ["Status_ErrorImportJson"] = "Errore durante l'importazione del JSON: {0}",
        ["Status_ErrorCopyClipboard"] = "Errore durante la copia negli appunti: {0}",
        ["Status_ErrorSaveSlice"] = "Errore durante il salvataggio dello slice: {0}",
        ["Status_ErrorCheckUpdates"] = "Errore controllo aggiornamenti: {0}",
        ["Status_LoadedFromCache"] = "Progetto '{0}' caricato dalla cache locale ({1}). Premi 'Ricarica' (Ctrl+R) per rianalizzare.",
        ["Status_CacheStale"] = "Progetto '{0}' caricato da cache. Modifiche rilevate su disco: premi 'Ricarica' (Ctrl+R) per aggiornare.",
        ["Status_Refreshed"] = "Progetto '{0}' ricaricato da disco ({1} file analizzati).",
        ["Status_CacheSaved"] = "Cache locale aggiornata per '{0}'.",
        ["Status_CacheCleared"] = "Cache locale dei progetti svuotata ({0} file eliminati).",
        ["Status_SettingsReset"] = "Impostazioni e preferenze ripristinate ai valori predefiniti.",
        ["Status_NoCachePending"] = "Nessuna cache per '{0}'. Clicca 'Analizza Ora' o premi F5 per analizzare i sorgenti.",
        ["Status_StartupEmptyReady"] = "Pronto. Seleziona un progetto o una cartella per iniziare.",

        // Schermata di Benvenuto (Welcome / Empty State View)
        ["Welcome_Title"] = "Macondo - VB6 Code Analyzer",
        ["Welcome_Subtitle"] = "Analisi semantica statica, grafo delle dipendenze e raggio d'impatto per progetti legacy Visual Basic 6.0",
        ["Welcome_QuickStart"] = "Inizia Subito",
        ["Welcome_OpenProject"] = "Apri Progetto o Gruppo...",
        ["Welcome_OpenProjectDesc"] = "Seleziona file .vbp o file gruppo .vbg",
        ["Welcome_OpenFolder"] = "Apri Cartella VB6...",
        ["Welcome_OpenFolderDesc"] = "Scansiona un'intera cartella di codice sorgente",
        ["Welcome_LoadFixtures"] = "Carica Fixture Demo",
        ["Welcome_LoadFixturesDesc"] = "Esplora un set completo di classi e form di esempio",
        ["Welcome_PendingTitle"] = "Ultimo Progetto Rilevato",
        ["Welcome_PendingNoCache"] = "Nessuna snapshot in cache presente per questo progetto. Avvia l'analisi per esplorare il codice.",
        ["Welcome_AnalyzeNow"] = "Analizza Ora",
        ["Welcome_RecentProjects"] = "Progetti Recenti",
        ["Welcome_NoRecent"] = "Nessun progetto recente disponibile.",
        ["Welcome_CacheReady"] = "In Cache",
        ["Welcome_CacheNeeded"] = "Analisi Richiesta",
        ["Welcome_CacheReadyTooltip"] = "Snapshot in cache disponibile: caricamento istantaneo (0ms)",
        ["Welcome_CacheNeededTooltip"] = "Nessuna cache presente: richiede scansione e analisi completa dei sorgenti",

        // Impostazioni Avvio
        ["Settings_StartupSection"] = "Comportamento all'avvio dell'applicazione",
        ["Settings_StartupDesc"] = "Scegli l'azione da eseguire automaticamente all'apertura del software.",
        ["Settings_StartupOnlyIfCached"] = "Solo se presente in cache (Consigliato)",
        ["Settings_StartupOnlyIfCachedDesc"] = "Ripristina istantaneamente l'ultimo progetto se presente in cache. Se assente, mostra la schermata iniziale senza bloccare l'interfaccia.",
        ["Settings_StartupAlways"] = "Analizza sempre automaticamente",
        ["Settings_StartupAlwaysDesc"] = "Scansiona e analizza sempre l'ultimo progetto aperto da sorgenti, anche in assenza di cache.",
        ["Settings_StartupEmpty"] = "Mostra sempre schermata iniziale",
        ["Settings_StartupEmptyDesc"] = "Avvia sempre l'applicazione su un'area di lavoro vuota pronta per una nuova selezione."
    };

    private readonly Dictionary<string, string> _enStrings = new()
    {
        // Menu File
        ["Menu_File"] = "_File",
        ["Menu_Open"] = "Open",
        ["Menu_OpenProjectFiles"] = "Project / Group...",
        ["Menu_OpenFolder"] = "Folder...",
        ["Menu_Recent"] = "Recent",
        ["Menu_NoRecentProjects"] = "No recent items",
        ["Menu_ClearRecentProjects"] = "Clear recent history",
        ["Menu_RefreshProject"] = "Reload Current Project",
        ["Menu_LoadFixtures"] = "Load Test Fixtures",
        ["Menu_GenerateFake"] = "Demo Graph (~500 nodes)",
        ["Menu_Settings"] = "Settings...",
        ["Menu_Exit"] = "Exit",

        // Menu View
        ["Menu_View"] = "_View",
        ["Menu_ToggleExplorer"] = "Toggle Project Explorer",
        ["Menu_ToggleInspector"] = "Toggle Symbol Inspector",
        ["Menu_ResetView"] = "Reset View",
        ["Menu_ExpandAll"] = "Expand All",
        ["Menu_CollapseAll"] = "Collapse All",
        ["Menu_FilterDetailLevel"] = "Symbol Level Filter",
        ["Menu_FilterProjects"] = "Filter Projects",
        ["Filter_AllMembers"] = "Complete (All Members)",
        ["Filter_MethodsAndProperties"] = "Methods & Properties",
        ["Filter_ContainersOnly"] = "Containers Only (Modules)",
        ["Filter_Custom"] = "Custom...",
        ["Filter_ShowMethods"] = "Show Methods (Sub / Function)",
        ["Filter_ShowProperties"] = "Show Properties",
        ["Filter_ShowFields"] = "Show Fields & Variables",
        ["Filter_ShowConstants"] = "Show Constants",
        ["Filter_ShowTypesEnums"] = "Show Types & Enums",
        ["HUD_FilterLabel"] = "Detail:",
        ["HUD_FilterButton"] = "Filter Members",
        ["HUD_FilterProjects"] = "Projects",
        ["HUD_ProjectsFilterTitle"] = "Projects Filter",
        ["HUD_SelectAll"] = "All",
        ["Tooltip_FilterMethods"] = "Show or hide routines (Sub and Function)",
        ["Tooltip_FilterProperties"] = "Show or hide properties (Property)",
        ["Tooltip_FilterFields"] = "Show or hide internal fields and variables",
        ["Tooltip_FilterConstants"] = "Show or hide constants",
        ["Tooltip_FilterTypes"] = "Show or hide UDT types and enums",
        ["Tooltip_FilterProjects"] = "Show or hide individual projects in the graph",
        ["Tooltip_LegendToggle"] = "Click to toggle this category in the graph",
        ["Menu_GraphLayout"] = "Graph Layout",
        ["Layout_StandardRadial"] = "Standard Radial",
        ["Layout_AffinityRadial"] = "Affinity Radial",
        ["Layout_ClusteredIslands"] = "Clustered Islands (Subsystems)",
        ["Layout_ForceDirected"] = "Force-Directed (Physics)",
        ["Menu_HoverSelection"] = "Hover Node Selection",
        ["HUD_LayoutLabel"] = "Layout:",
        ["Status_LayoutApplied"] = "Applied '{0}' layout to graph.",

        // Menu Analysis
        ["Menu_Analysis"] = "_Analysis",
        ["Menu_BlastRadius"] = "Calculate Blast Radius",
        ["Menu_ShowCallers"] = "Show Callers",
        ["Menu_ShowCallees"] = "Show Callees",
        ["Menu_ClearHighlight"] = "Clear Filters",

        // Search
        ["Search_Watermark"] = "Search symbol in graph...",
        ["Search_NoMatches"] = "No symbol found for '{0}'",
        ["Search_SingleMatch"] = "Found 1 symbol: [{0}] {1}",
        ["Search_MultipleMatches"] = "Found {0} symbols (click to select):",

        // Explorer
        ["Explorer_Title"] = "Project Explorer",
        ["Explorer_WorkspaceFormat"] = "Workspace ({0} Projects)",
        ["Tooltip_HideExplorer"] = "Hide Project Explorer (Ctrl+B)",
        ["Tooltip_ToggleExplorer"] = "Toggle Project Explorer (Ctrl+B)",
        ["Explorer_Group_Forms"] = "Forms",
        ["Explorer_Group_Modules"] = "Modules",
        ["Explorer_Group_Classes"] = "Class Modules",
        ["Explorer_Group_UserControls"] = "User Controls",
        ["Explorer_Group_Types"] = "User-Defined Types",
        ["Explorer_Group_Enums"] = "Enumerations",
        ["Explorer_Group_Other"] = "Other Files",
        ["Explorer_OpenOriginalFile"] = "Open Original File",
        ["Explorer_OpenContainingFolder"] = "Show in Folder",
        ["Explorer_CopyFilePath"] = "Copy File Path",
        ["Explorer_FileNotFound"] = "File not found on disk: {0}",
        ["Explorer_FileOpened"] = "Opened file: {0}",
        ["Explorer_FolderOpened"] = "Opened folder: {0}",
        ["Explorer_PathCopied"] = "Path copied to clipboard: {0}",
        ["Inspector_Tooltip_OpenFile"] = "Open original source file",
        ["Inspector_Tooltip_OpenFolder"] = "Show file in system folder",

        // Inspector & Blast Radius
        ["Inspector_Title"] = "Symbol Inspector & Blast Radius",
        ["Tooltip_HideInspector"] = "Hide Symbol Inspector (Ctrl+I)",
        ["Tooltip_ToggleInspector"] = "Toggle Symbol Inspector (Ctrl+I)",

        // Center Tabs
        ["Tab_GraphView"] = "Graph View",
        ["Tab_DsmMatrix"] = "DSM Matrix",
        ["Tab_TreemapMetrics"] = "Treemap Metrics",
        ["Tab_RiskHotspots"] = "Risk Hotspots",
        ["Tab_ScreenFlow"] = "Screen Flow",
        ["Tab_ControlFlow"] = "Control Flow (CFG)",
        ["Tab_BlastRadiusGrid"] = "Blast Radius Grid",

        // Treemap Metrics
        ["Treemap_Placeholder"] = "No Treemap calculated. Load a project to view the metrics map.",
        ["Treemap_BreadcrumbDrillUp"] = "⬅ Go up | View: {0} ({1})",
        ["Treemap_BreadcrumbRoot"] = "All Project Modules (Size = LOC / Volume | Color = Complexity)",
        ["Treemap_MembersCount"] = "{0} members",
        ["Treemap_HoverSub"] = "Cyclomatic Complexity: {0:F1} | Size: {1:F0} LOC",

        // Risk Hotspots
        ["Hotspot_Placeholder"] = "No Hotspot dataset calculated. Load a project to analyze risk areas.",
        ["Hotspot_DangerBadge"] = "🔥 HOTSPOT ZONE (Critical)",
        ["Hotspot_SafeBadge"] = "✓ STABLE ZONE",
        ["Hotspot_AxisY"] = "↑ CYCLOMATIC COMPLEXITY",
        ["Hotspot_AxisX"] = "CHANGE FREQUENCY / FAN-IN (CHURN) →",
        ["Hotspot_HoverSub"] = "Risk: {0:F0}% ({1}) | CC: {2:F1} | Churn: {3:F0} | Callers: {4}",

        // Screen Flow
        ["ScreenFlow_Placeholder"] = "No Form or UI Screen detected in the current project.",
        ["ScreenFlow_ControlsCount"] = "UI Controls: {0}",
        ["ScreenFlow_EventHandlersCount"] = "Event Handlers: {0}",
        ["ScreenFlow_TransitionsCount"] = "Transitions: {0} out / {1} in",
        ["ScreenFlow_StartupBadge"] = "STARTUP",

        // DSM Matrix
        ["Dsm_Placeholder"] = "No DSM Matrix calculated. Load a project to analyze dependencies.",

        // Control Flow Graph (CFG)
        ["ControlFlow_Placeholder"] = "No Control Flow Graph (CFG) selected. Select a routine or method to view flow.",
        ["ControlFlow_Header"] = "CFG: {0} ({1}) | Cyclomatic Complexity (CC): {2} | Basic Blocks: {3}",

        // Floating HUD
        ["HUD_ResetView"] = "Reset View",
        ["HUD_ExpandAll"] = "Expand All",
        ["HUD_CollapseAll"] = "Collapse All",
        ["HUD_Refresh"] = "Reload",
        ["HUD_BlastRadius"] = "Blast Radius",
        ["HUD_ShowCallers"] = "Show Callers",
        ["HUD_ShowCallees"] = "Show Callees",
        ["HUD_ClearFilter"] = "Clear Filters",

        // Grid Toolbar
        ["Grid_FilterLabel"] = "Filter:",
        ["Grid_FilterWatermark"] = "Routine, module or project name...",
        ["Grid_RiskLabel"] = "Risk:",
        ["Grid_AllRisks"] = "All Risk Levels",
        ["Grid_Recalculate"] = "Recalculate Audit",
        ["Grid_StatsFormat"] = "{0} of {1} routines",
        ["Grid_Calculating"] = "Calculating audit for {0} routines...",

        // Grid Columns
        ["Col_Project"] = "Project",
        ["Col_Routine"] = "Routine / Symbol",
        ["Col_Module"] = "Module / Container",
        ["Col_DirectCallers"] = "Direct Callers",
        ["Col_IndirectCallers"] = "Indirect Callers",
        ["Col_Depth"] = "Depth",
        ["Col_Risk"] = "Risk Level",
        ["Col_Forms"] = "Affected Forms",
        ["Col_TotalImpact"] = "Total Impact",
        ["Col_Action"] = "Action",
        ["Btn_ShowInGraph"] = "Show Graph",

        // Button and Action Tooltips
        ["Tooltip_Settings"] = "Settings (Ctrl+,)",
        ["Tooltip_Minimize"] = "Minimize",
        ["Tooltip_Maximize"] = "Maximize / Restore",
        ["Tooltip_Close"] = "Close",
        ["Tooltip_ResetView"] = "Reset zoom to 100% and center view",
        ["Tooltip_ExpandAll"] = "Expand all modules showing methods and properties",
        ["Tooltip_CollapseAll"] = "Collapse all modules hiding internal members",
        ["Tooltip_RefreshProject"] = "Reload project from disk and update cache (Ctrl+R)",
        ["Tooltip_BlastRadius"] = "Calculate and highlight modification impact (indirect callers, forms and impacted classes)",
        ["Tooltip_ShowCallers"] = "Highlight incoming callers for selected routine (callers)",
        ["Tooltip_ShowCallees"] = "Highlight outgoing calls for selected routine (callees)",
        ["Tooltip_ClearHighlight"] = "Clear all active highlights and filters on the graph",
        ["Tooltip_RecalculateAudit"] = "Recalculate risk and impact matrix for all routines",
        ["Tooltip_ShowInGraph"] = "Center and select this routine on the graph canvas",

        // Inspector & Blast Radius
        ["Inspector_Title"] = "Symbol Inspector & Blast Radius",
        ["Inspector_NoSelection"] = "No symbol selected.",
        ["Inspector_Hint1"] = "• Click on a node to view semantic details, types, parameters and calls.",
        ["Inspector_Hint2"] = "• Click 'Blast Radius' to compute dependency reach and regression risk.",
        ["Inspector_Hint3"] = "• Double-click a Class, Form or Module to expand/collapse members.",
        ["Inspector_Sec_Symbol"] = "SYMBOL",
        ["Inspector_Sec_Kind"] = "CATEGORY",
        ["Inspector_Sec_Visibility"] = "VISIBILITY",
        ["Inspector_Sec_Type"] = "TYPE",
        ["Inspector_Sec_Params"] = "FORMAL PARAMETERS",
        ["Inspector_Sec_File"] = "FILE & LINES",
        ["Inspector_Sec_BlastRadius"] = "BLAST RADIUS (IMPACT)",
        ["Inspector_DirectCallersCount"] = "Direct Callers:",
        ["Inspector_IndirectCallersCount"] = "Indirect Callers:",
        ["Inspector_MaxDepth"] = "Max Depth:",
        ["Inspector_Sec_Forms"] = "AFFECTED UI FORMS",
        ["Inspector_Sec_Classes"] = "TOUCHED CLASSES & MODULES",
        ["Inspector_Sec_Paths"] = "CALL CHAINS (EXECUTION PATHS)",
        ["Inspector_Sec_WhoCallsMe"] = "DIRECT CALLERS (WHO CALLS ME?)",
        ["Inspector_Sec_WhatDoICall"] = "OUTGOING CALLS (WHAT DO I CALL?)",
        ["Inspector_Sec_Instantiates"] = "INSTANTIATED CLASSES (NEW)",
        ["Inspector_AiContextDesc"] = "Extracts the semantic subgraph of this symbol optimized for AI prompts.",
        ["Inspector_Calculating"] = "CALCULATING...",
        ["Inspector_CalculatingCallers"] = "Calculating callers in progress...",
        ["Inspector_CalculatingCallees"] = "Calculating outgoing calls in progress...",
        ["Inspector_NoCallers"] = "(no direct callers)",
        ["Inspector_NoCallees"] = "(no outgoing calls)",
        ["Inspector_NoItems"] = "(none)",
        ["Risk_Low"] = "LOW",
        ["Risk_Medium"] = "MEDIUM",
        ["Risk_High"] = "HIGH",
        ["Risk_Critical"] = "CRITICAL",
        ["Risk_Format"] = "RISK: {0}",
        ["Status_SelectSymbolForBlast"] = "Select a symbol first to compute Blast Radius.",
        ["Status_BlastReportFormat"] = "Blast Radius for '{0}': {1} direct, {2} indirect, {3} forms, Risk: {4}",
        ["Status_BlastError"] = "Error computing Blast Radius: {0}",
        ["Status_SelectSymbolForCallers"] = "Select a routine or module first to show incoming callers.",
        ["Status_SelectSymbolForCallees"] = "Select a routine or module first to show outgoing calls.",
        ["Status_SelectedFromGrid"] = "Selected from grid: [{0}] {1}",
        ["Status_NodeAnalysisError"] = "Error analyzing symbol: {0}",
        ["Loading_BlastRadiusTitle"] = "Calculating Blast Radius in progress...",
        ["Loading_BlastRadiusSub"] = "Impact analysis and call paths for '{0}'",
        ["Loading_NodeAnalysisTitle"] = "Analyzing node in progress...",
        ["Loading_NodeAnalysisSub"] = "Exploring callers and dependencies for '{0}'",
        ["Loading_AnalyzeProjectTitle"] = "Analyzing VB6 Project...",
        ["Loading_AnalyzeProjectSub"] = "Scanning and extracting from '{0}'...",
        ["Loading_FixturesTitle"] = "Loading VB6 Fixtures...",
        ["Loading_FixturesSub"] = "Semantic analysis of fixture modules in progress...",
        ["Loading_ParsingFile"] = "Parsing & extracting: {0}",
        ["Loading_ResolvingTypes"] = "Resolving global types...",
        ["Loading_BuildingCallGraph"] = "Building Call Graph and relationships...",
        ["Loading_GeneratingVisualGraph"] = "Generating visual graph and layout...",
        ["Loading_FilesProgressFormat"] = "{0} / {1} files ({2}%)",
        ["Loading_NodesExtractedFormat"] = "{0} nodes detected",
        ["Loading_Cancel"] = "Cancel",
        ["Loading_Cancelling"] = "Cancelling...",
        ["Status_AnalysisCancelled"] = "Analysis cancelled by user.",
        ["Notification_AnalysisCompleted_Title"] = "VB6 Analysis Completed",
        ["Notification_AnalysisCompleted_Message"] = "Analysis of '{0}' completed successfully ({1} nodes detected).",
        ["Notification_FixturesCompleted_Message"] = "VB6 fixtures analysis completed successfully ({0} nodes detected).",

        // Settings Window
        ["Settings_Title"] = "Settings",
        ["Settings_Tab_Appearance"] = "Appearance & Themes",
        ["Settings_Tab_Language"] = "Language",
        ["Settings_Tab_Behavior"] = "Behavior",
        ["Settings_Tab_Shortcuts"] = "Keyboard Shortcuts",
        ["Settings_Appearance_Header"] = "Select Theme",
        ["Settings_Appearance_Sub"] = "Choose a visual palette for the interface and graph canvas.",
        ["Theme_DarkModern_Title"] = "Dark Modern (Default)",
        ["Theme_DarkModern_Desc"] = "Minimal dark graphite style with vibrant emerald green accents.",
        ["Theme_DarkStudio_Title"] = "Dark Studio",
        ["Theme_DarkStudio_Desc"] = "Classic dark palette inspired with blue accents.",
        ["Theme_OneDark_Title"] = "One Dark Pro",
        ["Theme_OneDark_Desc"] = "Inspired by the popular Atom theme with deep night blue and cyan accents.",
        ["Theme_DarkGreen_Title"] = "Forest Dark (Emerald Green)",
        ["Theme_DarkGreen_Desc"] = "Dark forest environment with emerald and sage green accents.",
        ["Theme_LightModern_Title"] = "Light Modern (Light)",
        ["Theme_LightModern_Desc"] = "Clean high-contrast light theme for bright environments with cobalt accents.",
        ["Settings_Language_Header"] = "Select Interface Language",
        ["Settings_Language_Sub"] = "The selected language will be applied immediately to all menus and commands.",
        ["Lang_Italian_Title"] = "Italian",
        ["Lang_Italian_Desc"] = "Full Italian interface, menus, and documentation.",
        ["Lang_English_Title"] = "English (Default)",
        ["Lang_English_Desc"] = "Full English interface, menus, and inspection terminology.",
        ["Settings_Behavior_Header"] = "Interaction & Graph Options",
        ["Settings_Behavior_Sub"] = "Customize mouse pointer behavior and graph canvas interaction.",
        ["Settings_HoverSelection_Title"] = "Hover Node Selection",
        ["Settings_HoverSelection_Sub"] = "Automatically select and inspect nodes when hovering with the mouse cursor.",
        ["Settings_Shortcuts_Header"] = "Keyboard Shortcuts Catalog",
        ["Settings_Shortcuts_Sub"] = "All key combinations for rapid navigation and analysis.",
        ["Settings_Shortcuts_Search"] = "Search shortcut...",
        ["Settings_Reset"] = "Restore Defaults",
        ["Settings_Close"] = "Close",

        // Export, Import & LLM
        ["Menu_ExportMarkdown"] = "Export Repo Map (Markdown)...",
        ["Menu_ExportJson"] = "Export Graph (JSON)...",
        ["Menu_ImportJson"] = "Import Graph (JSON)...",
        ["Menu_ExportSlice"] = "Export LLM Context Slice...",
        ["HUD_ExportMarkdown"] = "Export MD",
        ["HUD_ExportJson"] = "Export JSON",
        ["HUD_ImportJson"] = "Import JSON",
        ["Inspector_Sec_AIContext"] = "AI CONTEXT / LLM SLICE",
        ["Inspector_CopyLlmSlice"] = "Copy LLM Slice",
        ["Inspector_SaveLlmSlice"] = "Save Slice (.md)",
        ["Tooltip_ExportMarkdown"] = "Export entire graph as token-efficient hierarchical Markdown for AI models",
        ["Tooltip_ExportJson"] = "Export entire graph as structured JSON",
        ["Tooltip_ImportJson"] = "Import and visualize a CodeGraph from JSON file",
        ["Tooltip_CopyLlmSlice"] = "Copy semantic sub-graph of selected symbol to clipboard",
        ["Tooltip_SaveLlmSlice"] = "Save context slice of selected symbol as .md file",
        ["Status_ExportSuccess"] = "Graph successfully exported to '{0}'.",
        ["Status_ImportSuccess"] = "Graph imported from '{0}': {1} symbols, {2} relationships.",
        ["Status_CopiedLlmSlice"] = "LLM Context Slice for '{0}' copied to clipboard!",
        ["Status_NoSymbolSelected"] = "No symbol selected for slice export.",
        ["Status_NoSemanticGraph"] = "No semantic graph available for export.",
        ["Info_CliTitle"] = "4. Command Line Usage (CLI & Headless)",
        ["Info_CliDescription"] = "Macondo can be executed directly from terminal (PowerShell, Command Prompt, or CI/CD) to analyze projects and export reports without launching the GUI:",
        ["Info_CliCommentMd"] = "# Export project Markdown Repo Map",
        ["Info_CliCommentJson"] = "# Export complete semantic graph to JSON",
        ["Info_CliCommentBoth"] = "# Export both formats simultaneously",

        // Git & SVN (VCS) Integration
        ["Git_BranchTooltip"] = "Current Git branch: {0}. Click to switch branch.",
        ["Git_SwitchBranchTitle"] = "Switch Git Branch",
        ["Git_SearchBranchWatermark"] = "Search branches...",
        ["Git_NoBranches"] = "No branches found",
        ["Git_SwitchingBranch"] = "Checking out branch '{0}'...",
        ["Git_SwitchSuccess"] = "Successfully checked out branch '{0}'.",
        ["Git_SwitchError"] = "Error switching branch: {0}",
        ["Svn_BranchTooltip"] = "Current SVN branch/URL: {0}. Click to switch (svn switch).",
        ["Svn_SwitchBranchTitle"] = "Switch SVN Branch/Trunk (svn switch)",
        ["Svn_SearchBranchWatermark"] = "Search SVN branch or path...",
        ["Svn_SwitchingBranch"] = "Switching to '{0}' in progress (svn switch)...",
        ["Svn_SwitchSuccess"] = "Successfully switched to '{0}'.",
        ["Svn_SwitchError"] = "Error during svn switch: {0}",

        // Software Updates & Cache (Help Menu)
        ["Menu_Help"] = "_Help",
        ["Menu_HelpInfo"] = "Guide & Concepts...",
        ["Menu_CheckUpdates"] = "Check for Updates...",
        ["Menu_ResetSettings"] = "Restore Default Settings...",
        ["Menu_ClearCache"] = "Clear Project Cache...",
        ["Tooltip_ResetSettings"] = "Restore all preferences and settings to factory default values",
        ["Tooltip_ClearCache"] = "Deletes all persistent project cache snapshots from disk (%APPDATA%\\Macondo\\cache\\)",
        ["Update_AvailableTitle"] = "New Update Available!",
        ["Update_AvailableSubtitle"] = "Version {0} is available (current version: {1})",
        ["Update_NotesLabel"] = "Release Notes & Changelog:",
        ["Update_ButtonLater"] = "Download later",
        ["Update_ButtonNow"] = "Update now",
        ["Update_Checking"] = "Checking for updates...",
        ["Update_NoUpdates"] = "You are using the latest version ({0}).",

        // Graph Legend
        ["Legend_Title"] = "Node & Color Legend",
        ["Legend_Forms"] = "Form (.frm)",
        ["Legend_Modules"] = "Module (.bas)",
        ["Legend_Classes"] = "Class (.cls)",
        ["Legend_Controls"] = "User Control (.ctl)",
        ["Legend_Methods"] = "Sub / Function",
        ["Legend_Properties"] = "Property",
        ["Legend_Fields"] = "Field / Var",
        ["Legend_Types"] = "Type / Enum",
        ["Legend_Calls"] = "Calls",
        ["Legend_Instantiates"] = "Instantiates (New)",

        // Graph Canvas
        ["Graph_Placeholder"] = "No CodeGraph loaded. Open a VB6 project or folder from the File menu.",

        // Status Bar & Stats
        ["Status_Ready"] = "Ready.",
        ["Status_Selected"] = "Selected: [{0}] {1}",
        ["Status_StatsFormat"] = "Nodes: {0} | Relations: {1} (Calls: {2}, Instantiates: {3})",
        ["Stats_ProjectsFormat"] = "{0} Projects",
        ["Stats_ProjectsRatioFormat"] = "{0}/{1} Projects",
        ["Stats_NodesFormat"] = "{0} Nodes",
        ["Stats_NodesRatioFormat"] = "{0}/{1} Nodes",
        ["Stats_RelationsFormat"] = "{0} Relations",
        ["Stats_RelationsCrossFormat"] = "{0} Relations ({1} Cross-Project)",
        ["Stats_CallsFormat"] = "{0} Calls",
        ["Stats_InstantiatesFormat"] = "{0} Instantiates",
        ["Explorer_ProjectsVisible"] = "{0}/{1} Projects visible",
        ["Open_FolderTitle"] = "Select one or more folders containing VB6 files/projects",
        ["Open_ProjectFilesTitle"] = "Select VB6 project files (.vbp) or group files (.vbg)",
        ["Open_FileTypeProjects"] = "VB6 Projects and Groups (*.vbp, *.vbg)",
        ["Open_FileTypeAll"] = "All files (*.*)",
        ["Status_FixturesLoaded"] = "VB6 fixtures suite successfully analyzed in memory.",
        ["Status_FakeGraphGenerating"] = "Generating synthetic demo graph (~500 nodes)...",
        ["Status_FakeGraphGenerated"] = "Synthetic benchmark demo graph (~500 nodes) successfully generated.",
        ["Status_AnalyzingSources"] = "Analyzing {0} sources/projects in progress...",
        ["Status_WorkspaceAnalyzed"] = "Workspace analyzed: {0} Projects, {1} Symbols, {2} Cross-Project Relationships.",
        ["Status_NodesDisplayed"] = "Nodes displayed: {0} of {1}",
        ["Status_CalleesFound"] = "Callees for '{0}': found {1} outgoing calls to ({2}).",
        ["Status_CalleesNone"] = "No outgoing calls made by '{0}'.",
        ["Status_CallersFound"] = "Callers for '{0}': found {1} direct callers from ({2}).",
        ["Status_CallersNone"] = "No direct callers for '{0}'.",
        ["Status_DsmDependency"] = "DSM: Dependency [{0} links] between containers.",
        ["Status_CfgBlock"] = "CFG Block: [{0}] {1}",
        ["Status_ErrorLoading"] = "Error during loading: {0}",
        ["Status_ErrorGenerating"] = "Error during generation: {0}",
        ["Status_ErrorAnalyzing"] = "Error during analysis: {0}",
        ["Status_ErrorOpenFile"] = "Error opening file: {0}",
        ["Status_ErrorOpenFolder"] = "Error opening folder: {0}",
        ["Status_ErrorCopyPath"] = "Error copying path: {0}",
        ["Status_ErrorGridCalc"] = "Error calculating Blast Radius grid: {0}",
        ["Status_ErrorExportMarkdown"] = "Error during Markdown export: {0}",
        ["Status_ErrorExportJson"] = "Error during JSON export: {0}",
        ["Status_ErrorImportJson"] = "Error importing JSON: {0}",
        ["Status_ErrorCopyClipboard"] = "Error copying to clipboard: {0}",
        ["Status_ErrorSaveSlice"] = "Error saving slice: {0}",
        ["Status_ErrorCheckUpdates"] = "Error checking for updates: {0}",
        ["Status_LoadedFromCache"] = "Project '{0}' loaded from local cache ({1}). Press 'Reload' (Ctrl+R) to reanalyze.",
        ["Status_CacheStale"] = "Project '{0}' loaded from cache. Modified files detected on disk: press 'Reload' (Ctrl+R) to update.",
        ["Status_Refreshed"] = "Project '{0}' reloaded from disk ({1} files analyzed).",
        ["Status_CacheSaved"] = "Local cache updated for '{0}'.",
        ["Status_CacheCleared"] = "Local project cache cleared ({0} files deleted).",
        ["Status_SettingsReset"] = "Settings and preferences restored to default values.",
        ["Status_NoCachePending"] = "No cache for '{0}'. Click 'Analyze Now' or press F5 to analyze sources.",
        ["Status_StartupEmptyReady"] = "Ready. Select a project or folder to start.",

        // Welcome / Empty State View
        ["Welcome_Title"] = "Macondo - VB6 Code Analyzer",
        ["Welcome_Subtitle"] = "Static semantic analysis, dependency graph, and blast radius impact for legacy Visual Basic 6.0 projects",
        ["Welcome_QuickStart"] = "Quick Start",
        ["Welcome_OpenProject"] = "Open Project or Group...",
        ["Welcome_OpenProjectDesc"] = "Select .vbp project or .vbg group files",
        ["Welcome_OpenFolder"] = "Open VB6 Folder...",
        ["Welcome_OpenFolderDesc"] = "Scan an entire folder of source code",
        ["Welcome_LoadFixtures"] = "Load Demo Fixtures",
        ["Welcome_LoadFixturesDesc"] = "Explore a complete suite of sample forms and classes",
        ["Welcome_PendingTitle"] = "Last Project Detected",
        ["Welcome_PendingNoCache"] = "No cache snapshot found for this project. Start analysis to explore code.",
        ["Welcome_AnalyzeNow"] = "⚡ Analyze Now",
        ["Welcome_RecentProjects"] = "Recent Projects",
        ["Welcome_NoRecent"] = "No recent projects available.",
        ["Welcome_CacheReady"] = "Cached",
        ["Welcome_CacheNeeded"] = "Analysis Required",
        ["Welcome_CacheReadyTooltip"] = "Cache snapshot available: instant loading (0ms)",
        ["Welcome_CacheNeededTooltip"] = "No cache found: requires full source scan and analysis",

        // Startup Settings
        ["Settings_StartupSection"] = "Application Startup Behavior",
        ["Settings_StartupDesc"] = "Choose the action to perform automatically when the software starts.",
        ["Settings_StartupOnlyIfCached"] = "Only if cached (Recommended)",
        ["Settings_StartupOnlyIfCachedDesc"] = "Instantly restore the last project if a cache snapshot exists. If absent, show the welcome screen without freezing the UI.",
        ["Settings_StartupAlways"] = "Always analyze automatically",
        ["Settings_StartupAlwaysDesc"] = "Always scan and analyze the last opened project from disk sources, even if cache is missing.",
        ["Settings_StartupEmpty"] = "Always show welcome screen",
        ["Settings_StartupEmptyDesc"] = "Always start the application with an empty workspace ready for a new project."
    };

    private LocalizationService() { }

    public void SetLanguage(AppLanguage language)
    {
        if (CurrentLanguage == language) return;
        CurrentLanguage = language;
        LanguageChanged?.Invoke(this, language);
    }

    public string Get(string key, params object[] args)
    {
        var dict = CurrentLanguage == AppLanguage.English ? _enStrings : _itStrings;
        if (!dict.TryGetValue(key, out var template))
        {
            if (!_itStrings.TryGetValue(key, out template))
                return key;
        }

        return args.Length > 0 ? string.Format(template, args) : template;
    }

    public string GetRiskLevel(Macondo.Semantic.Model.ImpactRiskLevel risk) => risk switch
    {
        Macondo.Semantic.Model.ImpactRiskLevel.Low => Get("Risk_Low"),
        Macondo.Semantic.Model.ImpactRiskLevel.Medium => Get("Risk_Medium"),
        Macondo.Semantic.Model.ImpactRiskLevel.High => Get("Risk_High"),
        Macondo.Semantic.Model.ImpactRiskLevel.Critical => Get("Risk_Critical"),
        _ => risk.ToString().ToUpperInvariant()
    };

    public string this[string key] => Get(key);

    /// <summary>
    /// Rileva automaticamente la lingua da impostare in base alla lingua e impostazioni regionali di sistema.
    /// Se la lingua di sistema o interfaccia utente è l'italiano ("it"), restituisce AppLanguage.Italian.
    /// Per qualsiasi altra lingua, restituisce AppLanguage.English come standard internazionale predefinito.
    /// </summary>
    public static AppLanguage DetectSystemLanguage()
    {
        try
        {
            if (IsItalianCulture(System.Globalization.CultureInfo.CurrentUICulture))
                return AppLanguage.Italian;

            if (IsItalianCulture(System.Globalization.CultureInfo.InstalledUICulture))
                return AppLanguage.Italian;

            if (IsItalianCulture(System.Globalization.CultureInfo.CurrentCulture))
                return AppLanguage.Italian;
        }
        catch
        {
            // Tolleranza ad eccezioni su ambienti o permessi restrittivi
        }

        return AppLanguage.English;
    }

    /// <summary>
    /// Determina se una specifica cultura appartiene alla lingua italiana.
    /// </summary>
    public static bool IsItalianCulture(System.Globalization.CultureInfo? culture)
    {
        if (culture == null) return false;
        return string.Equals(culture.TwoLetterISOLanguageName, "it", StringComparison.OrdinalIgnoreCase) ||
               culture.Name.StartsWith("it", StringComparison.OrdinalIgnoreCase);
    }
}
