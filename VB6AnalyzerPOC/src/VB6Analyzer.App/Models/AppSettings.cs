namespace Macondo.App.Models;

/// <summary>
/// Modello serializzabile per la memorizzazione delle impostazioni e preferenze utente.
/// Salvato in %APPDATA%\Macondo\settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Tema grafico selezionato dall'utente.</summary>
    public AppTheme Theme { get; set; } = AppTheme.DarkModern;

    /// <summary>Lingua dell'interfaccia utente (Italiano o English, rilevata automaticamente dalla lingua di sistema se non personalizzata).</summary>
    public AppLanguage Language { get; set; } = Services.LocalizationService.DetectSystemLanguage();

    /// <summary>Comportamento di caricamento e analisi del progetto all'avvio dell'applicazione.</summary>
    public StartupLoadMode StartupMode { get; set; } = StartupLoadMode.OnlyIfCached;

    /// <summary>Percorso dell'ultima cartella di progetto VB6 aperta con successo.</summary>
    public string? LastOpenedFolder { get; set; }

    /// <summary>Indice del filtro di rischio predefinito per la griglia Blast Radius (0: Tutti, 1: Critical, 2: High, 3: Medium, 4: Low).</summary>
    public int DefaultRiskFilterIndex { get; set; } = 0;

    /// <summary>Modalità di layout preferita per il canvas del grafo.</summary>
    public Layout.GraphLayoutMode PreferredLayout { get; set; } = Layout.GraphLayoutMode.StandardRadial;

    /// <summary>Larghezza della finestra memorizzata alla chiusura.</summary>
    public double WindowWidth { get; set; } = 1400;

    /// <summary>Altezza della finestra memorizzata alla chiusura.</summary>
    public double WindowHeight { get; set; } = 900;

    /// <summary>Coordinata X della finestra su schermo.</summary>
    public int? WindowX { get; set; }

    /// <summary>Coordinata Y della finestra su schermo.</summary>
    public int? WindowY { get; set; }

    /// <summary>Indica se la finestra era massimizzata all'ultima chiusura.</summary>
    public bool IsMaximized { get; set; } = false;

    /// <summary>Larghezza del pannello Project Explorer (pannello sinistro).</summary>
    public double ExplorerWidth { get; set; } = 280;

    /// <summary>Larghezza del pannello Symbol Inspector (pannello destro).</summary>
    public double InspectorWidth { get; set; } = 360;

    /// <summary>Indica se il pannello Project Explorer (sinistro) è visibile.</summary>
    public bool IsExplorerVisible { get; set; } = true;

    /// <summary>Indica se il pannello Symbol Inspector (destro) è visibile.</summary>
    public bool IsInspectorVisible { get; set; } = true;

    /// <summary>Indica se mostrare i metodi/procedure (Sub, Function) nel grafo.</summary>
    public bool ShowMethods { get; set; } = true;

    /// <summary>Indica se mostrare le proprietà (Property Get/Let/Set) nel grafo.</summary>
    public bool ShowProperties { get; set; } = true;

    /// <summary>Indica se mostrare le variabili e campi nel grafo.</summary>
    public bool ShowFields { get; set; } = true;

    /// <summary>Indica se mostrare le costanti nel grafo.</summary>
    public bool ShowConstants { get; set; } = true;

    /// <summary>Indica se mostrare i tipi definiti dall'utente ed enumerazioni nel grafo.</summary>
    public bool ShowTypesAndEnums { get; set; } = true;

    /// <summary>Indica se abilitare la selezione/ispezione automatica dei nodi al passaggio del cursore del mouse (Hover).</summary>
    public bool EnableHoverSelection { get; set; } = true;

    /// <summary>Indica se mostrare i nomi testuali accanto alle icone nelle schede di visualizzazione centrali (Vista Grafo, DSM, Treemap...).</summary>
    public bool ShowTabLabels { get; set; } = true;

    /// <summary>Indica se la scheda Vista Grafo è visibile.</summary>
    public bool ShowTabGraph { get; set; } = true;

    /// <summary>Indica se la scheda Matrice DSM è visibile.</summary>
    public bool ShowTabDsm { get; set; } = true;

    /// <summary>Indica se la scheda Treemap Metriche è visibile.</summary>
    public bool ShowTabTreemap { get; set; } = true;

    /// <summary>Indica se la scheda Hotspot di Rischio è visibile.</summary>
    public bool ShowTabHotspot { get; set; } = true;

    /// <summary>Indica se la scheda Flusso Schermate è visibile.</summary>
    public bool ShowTabScreenFlow { get; set; } = true;

    /// <summary>Indica se la scheda Control Flow (CFG) è visibile.</summary>
    public bool ShowTabControlFlow { get; set; } = true;

    /// <summary>Indica se la scheda Metriche Martin è visibile.</summary>
    public bool ShowTabMartin { get; set; } = true;

    /// <summary>Indica se la scheda Dead Code &amp; Orfani è visibile.</summary>
    public bool ShowTabDeadCode { get; set; } = true;

    /// <summary>Indica se la scheda Stato Condiviso è visibile.</summary>
    public bool ShowTabSharedState { get; set; } = true;

    /// <summary>Indica se la scheda Temporal Coupling è visibile.</summary>
    public bool ShowTabTemporalCoupling { get; set; } = true;

    /// <summary>Indica se la scheda Flussi Architetturali è visibile.</summary>
    public bool ShowTabArchitecturalFlow { get; set; } = true;

    /// <summary>Indica se la scheda CodeCity 3D è visibile.</summary>
    public bool ShowTabCodeCity { get; set; } = true;

    /// <summary>Indica se la scheda Griglia Blast Radius è visibile.</summary>
    public bool ShowTabBlastRadiusGrid { get; set; } = true;

    /// <summary>Indica se la scheda Coesione &amp; LCOM è visibile.</summary>
    public bool ShowTabCohesion { get; set; } = true;

    /// <summary>Indica se la scheda Indice di Manutenibilità è visibile.</summary>
    public bool ShowTabMaintainability { get; set; } = true;

    /// <summary>Indica se la scheda Regole Architetturali è visibile.</summary>
    public bool ShowTabArchitecturalRules { get; set; } = true;

    /// <summary>Indica se la scheda Duplicazioni (CPD) è visibile.</summary>
    public bool ShowTabCodeClone { get; set; } = true;

    /// <summary>Indica se la scheda Social &amp; Bus Factor è visibile.</summary>
    public bool ShowTabSocialCode { get; set; } = true;

    /// <summary>Indica se la scheda Data Flow &amp; Taint è visibile.</summary>
    public bool ShowTabDataFlow { get; set; } = true;

    /// <summary>Indica se la scheda API Diff &amp; SemVer è visibile.</summary>
    public bool ShowTabApiDiff { get; set; } = true;

    /// <summary>Indica se la scheda Matrice CRUD è visibile.</summary>
    public bool ShowTabCrudMatrix { get; set; } = true;

    /// <summary>Elenco degli ultimi progetti o cartelle analizzati (massimo 10 elementi).</summary>
    public List<RecentProjectItem> RecentProjects { get; set; } = new();
}

/// <summary>
/// Informazioni su un progetto o cartella precedentemente analizzato memorizzato nella cronologia recenti.
/// </summary>
public sealed class RecentProjectItem
{
    public List<string> Paths { get; set; } = new();
    public string DisplayName { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public DateTime LastOpenedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Modalità di gestione del caricamento e dell'analisi all'avvio dell'applicazione.
/// </summary>
public enum StartupLoadMode
{
    /// <summary>Ripristina automaticamente il progetto solo se è già presente uno snapshot in cache. Altrimenti mostra la schermata iniziale.</summary>
    OnlyIfCached = 0,

    /// <summary>Esegue sempre l'analisi automatica da disco dell'ultimo progetto aperto anche in assenza di cache.</summary>
    AlwaysAnalyze = 1,

    /// <summary>Non carica automaticamente alcun progetto all'avvio e mostra sempre la schermata iniziale (workspace vuoto).</summary>
    EmptyWorkspace = 2
}
