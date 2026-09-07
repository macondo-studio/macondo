namespace VB6Analyzer.App.Services;

public enum AppLanguage
{
    Italian,
    English
}

public sealed class LocalizationService
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    public event EventHandler<AppLanguage>? LanguageChanged;

    public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.Italian;

    private readonly Dictionary<string, string> _itStrings = new()
    {
        // Menu File
        ["Menu_File"] = "_File",
        ["Menu_OpenFolder"] = "Apri Cartella VB6...",
        ["Menu_RefreshProject"] = "Ricarica Progetto Corrente",
        ["Menu_LoadFixtures"] = "Carica Fixtures Test",
        ["Menu_GenerateFake"] = "Grafo Demo (~500 nodi)",
        ["Menu_Settings"] = "Impostazioni...",
        ["Menu_Exit"] = "Esci",

        // Menu Visualizza
        ["Menu_View"] = "_Visualizza",
        ["Menu_ResetView"] = "Reset Vista",
        ["Menu_ExpandAll"] = "Espandi Tutto",
        ["Menu_CollapseAll"] = "Comprimi Tutto",

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

        // Tab Centrali
        ["Tab_GraphView"] = "Vista Grafo",
        ["Tab_BlastRadiusGrid"] = "Griglia Blast Radius",

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
        ["Grid_FilterWatermark"] = "Nome routine o modulo...",
        ["Grid_RiskLabel"] = "Rischio:",
        ["Grid_AllRisks"] = "Tutti i Livelli",
        ["Grid_Recalculate"] = "Ricalcola Audit",
        ["Grid_StatsFormat"] = "{0} di {1} routine",
        ["Grid_Calculating"] = "Calcolo audit in corso per {0} routine...",

        // Griglia Colonne
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

        // Inspector
        ["Inspector_Title"] = "Symbol Inspector & Blast Radius",
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
        ["Inspector_Sec_BlastRadius"] = "BLAST RADIUS (IMPACT)",
        ["Inspector_DirectCallersCount"] = "Chiamanti Diretti:",
        ["Inspector_IndirectCallersCount"] = "Chiamanti Indiretti:",
        ["Inspector_MaxDepth"] = "Profondità Max (Depth):",
        ["Inspector_Sec_Forms"] = "FORM UI IMPATTATE",
        ["Inspector_Sec_Classes"] = "CLASSI & MODULI TOCCATI",
        ["Inspector_Sec_Paths"] = "PERCORSI DI ESECUZIONE (CALL CHAINS)",
        ["Inspector_Sec_WhoCallsMe"] = "CHIAMANTI DIRETTI (WHO CALLS ME?)",
        ["Inspector_Sec_WhatDoICall"] = "CHIAMATE EFFETTUATE (WHAT DO I CALL?)",
        ["Inspector_Sec_Instantiates"] = "CLASSI ISTANZIATE (NEW)",

        // Finestra Impostazioni
        ["Settings_Title"] = "Impostazioni",
        ["Settings_Tab_Appearance"] = "Aspetto & Temi",
        ["Settings_Tab_Language"] = "Lingua",
        ["Settings_Tab_Shortcuts"] = "Scorciatoie da Tastiera",
        ["Settings_Appearance_Header"] = "Seleziona Tema Grafico",
        ["Settings_Appearance_Sub"] = "Scegli una palette visiva per l'interfaccia e il canvas del grafo.",
        ["Settings_Language_Header"] = "Seleziona Lingua Interfaccia",
        ["Settings_Language_Sub"] = "La lingua selezionata verrà applicata immediatamente a tutti i menu e comandi.",
        ["Settings_Shortcuts_Header"] = "Catalogo Scorciatoie da Tastiera",
        ["Settings_Shortcuts_Sub"] = "Tutte le combinazioni di tasti per navigare rapidamente nel codice e nel grafo.",
        ["Settings_Shortcuts_Search"] = "Cerca scorciatoia...",
        ["Settings_Close"] = "Chiudi",

        // Barra di Stato
        ["Status_Ready"] = "Pronto.",
        ["Status_Selected"] = "Selezionato: [{0}] {1}",
        ["Status_StatsFormat"] = "Nodi: {0} | Relazioni: {1} (Calls: {2}, Instantiates: {3})",
        ["Status_LoadedFromCache"] = "Progetto '{0}' caricato dalla cache locale ({1}). Premi 'Ricarica' (Ctrl+R) per rianalizzare.",
        ["Status_CacheStale"] = "Progetto '{0}' caricato da cache. Modifiche rilevate su disco: premi 'Ricarica' (Ctrl+R) per aggiornare.",
        ["Status_Refreshed"] = "Progetto '{0}' ricaricato da disco ({1} file analizzati).",
        ["Status_CacheSaved"] = "Cache locale aggiornata per '{0}'."
    };

    private readonly Dictionary<string, string> _enStrings = new()
    {
        // Menu File
        ["Menu_File"] = "_File",
        ["Menu_OpenFolder"] = "Open VB6 Folder...",
        ["Menu_RefreshProject"] = "Reload Current Project",
        ["Menu_LoadFixtures"] = "Load Test Fixtures",
        ["Menu_GenerateFake"] = "Demo Graph (~500 nodes)",
        ["Menu_Settings"] = "Settings...",
        ["Menu_Exit"] = "Exit",

        // Menu View
        ["Menu_View"] = "_View",
        ["Menu_ResetView"] = "Reset View",
        ["Menu_ExpandAll"] = "Expand All",
        ["Menu_CollapseAll"] = "Collapse All",

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

        // Center Tabs
        ["Tab_GraphView"] = "Graph View",
        ["Tab_BlastRadiusGrid"] = "Blast Radius Grid",

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
        ["Grid_FilterWatermark"] = "Routine or module name...",
        ["Grid_RiskLabel"] = "Risk:",
        ["Grid_AllRisks"] = "All Risk Levels",
        ["Grid_Recalculate"] = "Recalculate Audit",
        ["Grid_StatsFormat"] = "{0} of {1} routines",
        ["Grid_Calculating"] = "Calculating audit for {0} routines...",

        // Grid Columns
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

        // Inspector
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

        // Settings Window
        ["Settings_Title"] = "Settings",
        ["Settings_Tab_Appearance"] = "Appearance & Themes",
        ["Settings_Tab_Language"] = "Language",
        ["Settings_Tab_Shortcuts"] = "Keyboard Shortcuts",
        ["Settings_Appearance_Header"] = "Select Theme",
        ["Settings_Appearance_Sub"] = "Choose a visual palette for the interface and graph canvas.",
        ["Settings_Language_Header"] = "Select Interface Language",
        ["Settings_Language_Sub"] = "The selected language will be applied immediately to all menus and commands.",
        ["Settings_Shortcuts_Header"] = "Keyboard Shortcuts Catalog",
        ["Settings_Shortcuts_Sub"] = "All key combinations for rapid navigation and analysis.",
        ["Settings_Shortcuts_Search"] = "Search shortcut...",
        ["Settings_Close"] = "Close",

        // Status Bar
        ["Status_Ready"] = "Ready.",
        ["Status_Selected"] = "Selected: [{0}] {1}",
        ["Status_StatsFormat"] = "Nodes: {0} | Relations: {1} (Calls: {2}, Instantiates: {3})",
        ["Status_LoadedFromCache"] = "Project '{0}' loaded from local cache ({1}). Press 'Reload' (Ctrl+R) to reanalyze.",
        ["Status_CacheStale"] = "Project '{0}' loaded from cache. Modified files detected on disk: press 'Reload' (Ctrl+R) to update.",
        ["Status_Refreshed"] = "Project '{0}' reloaded from disk ({1} files analyzed).",
        ["Status_CacheSaved"] = "Local cache updated for '{0}'."
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

    public string this[string key] => Get(key);
}
