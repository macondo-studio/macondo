namespace Macondo.App.Services;

/// <summary>
/// Modello serializzabile per la memorizzazione delle impostazioni e preferenze utente.
/// Salvato in %APPDATA%\Macondo\settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Tema grafico selezionato dall'utente.</summary>
    public AppTheme Theme { get; set; } = AppTheme.DarkModern;

    /// <summary>Lingua dell'interfaccia utente (Italiano o English).</summary>
    public AppLanguage Language { get; set; } = AppLanguage.Italian;

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
