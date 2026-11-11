using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Macondo.App.Models;

namespace Macondo.App.Services;

/// <summary>
/// Singleton service responsible for loading, saving, and managing persistence
/// of user preferences in JSON file (%APPDATA%\Macondo\settings.json).
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
        var dir = Path.Combine(appData, "Macondo");
        _settingsFilePath = Path.Combine(dir, "settings.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        Settings = Load();
    }

    /// <summary>
    /// Load settings from disk. If the file does not exist or is corrupt, returns the default settings.
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
                // In the event of a reading error or invalid JSON, the default values are returned
            }

            Settings = new AppSettings();
            return Settings;
        }
    }

    /// <summary>
    /// Instantly save current state of settings to JSON file securely.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))

                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(Settings, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch
            {
                // Write error tolerance (e.g. protected disks or temporary restrictive permissions)
            }
        }
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>
    /// It changes settings via a lambda and automatically persists to disk.
    /// </summary>
    public void Update(Action<AppSettings> updateAction)
    {
        lock (_lock)
        {
            updateAction(Settings);
            Save();
        }
        SettingsChanged?.Invoke(this, Settings);
    }

    /// <summary>
    /// Reset all user preferences to factory defaults and save the file to disk.
    /// </summary>
    public AppSettings Reset()
    {
        lock (_lock)
        {
            Settings = new AppSettings();
            Save();
        }
        SettingsChanged?.Invoke(this, Settings);
        return Settings;
    }
}
