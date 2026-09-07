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
}
