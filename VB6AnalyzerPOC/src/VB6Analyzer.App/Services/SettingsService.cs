using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VB6Analyzer.App.Services;

/// <summary>
/// Servizio singleton responsabile del caricamento, salvataggio e gestione della persistenza
/// delle preferenze utente su file JSON (%APPDATA%\VB6Analyzer\settings.json).
/// </summary>
public sealed class SettingsService
{
    private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
    public static SettingsService Instance => _instance.Value;

    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lock = new();

    public AppSettings Settings { get; private set; }

    private SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "VB6Analyzer");
        _settingsFilePath = Path.Combine(dir, "settings.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        Settings = Load();
    }

    /// <summary>
    /// Carica le impostazioni dal disco. Se il file non esiste o è corrotto, restituisce le impostazioni predefinite.
    /// </summary>
    public AppSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                    if (loaded != null)
                    {
                        Settings = loaded;
                        return loaded;
                    }
                }
            }
            catch
            {
                // In caso di errore di lettura o JSON invalido, si ricade sui valori di default
            }

            Settings = new AppSettings();
            return Settings;
        }
    }

    /// <summary>
    /// Salva immediatamente lo stato corrente delle impostazioni su file JSON in modo sicuro.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(Settings, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch
            {
                // Tolleranza agli errori di scrittura (es. dischi protetti o permessi restrittivi temporanei)
            }
        }
    }

    /// <summary>
    /// Modifica le impostazioni tramite una lambda ed esegue automaticamente la persistenza su disco.
    /// </summary>
    public void Update(Action<AppSettings> updateAction)
    {
        lock (_lock)
        {
            updateAction(Settings);
            Save();
        }
    }
}
