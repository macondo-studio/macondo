using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Macondo.App.Models;

namespace Macondo.App.Services;

/// <summary>
/// Servizio per il controllo automatico e manuale degli aggiornamenti dell'applicazione.
/// Esegue la verifica in background all'avvio senza bloccare l'interfaccia utente.
/// </summary>
public sealed class UpdateService
{
    private static readonly Lazy<UpdateService> _instance = new(() => new UpdateService());
    public static UpdateService Instance => _instance.Value;

    // Versione corrente dell'applicazione Macondo (VB6 Analyzer)
    public static readonly Version CurrentVersion = new Version(1, 4, 0);
    public static readonly string CurrentVersionString = "1.4.0";

    // =========================================================================================
    // Endpoint ufficiale per il download del manifesto di aggiornamento (version.json)
    public string? UpdateEndpointUrl { get; set; } = "https://macondo-studio.github.io/macondo/version.json";

    private readonly HttpClient _httpClient;

    private UpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"VB6Analyzer-Macondo/{CurrentVersionString}");
    }

    /// <summary>
    /// Verifica in modo asincrono se è disponibile una versione più recente dell'applicazione.
    /// Se non è ancora configurato un server o se la versione locale è già aggiornata, restituisce null.
    /// </summary>
    public async Task<AppUpdateInfo?> CheckForUpdatesAsync(bool forceCheck = false, CancellationToken ct = default)
    {
        var endpoint = UpdateEndpointUrl;

        // Se l'endpoint non è ancora configurato, restituiamo null senza generare errori
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        try
        {
            // Esecuzione richiesta HTTP al server degli aggiornamenti
            using var response = await _httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            if (!root.TryGetProperty("version", out var verProp) || verProp.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var versionStr = verProp.GetString();
            if (string.IsNullOrWhiteSpace(versionStr))
            {
                return null;
            }

            if (Version.TryParse(versionStr.TrimStart('v', 'V'), out var remoteVersion))
            {
                if (remoteVersion > CurrentVersion)
                {
                    var (defaultTitle, localizedTitles) = ParseLocalizedField(root, "title");
                    var (defaultNotes, localizedNotes) = ParseLocalizedField(root, "releaseNotes");

                    string downloadUrl = string.Empty;
                    if (root.TryGetProperty("downloadUrl", out var dlProp) && dlProp.ValueKind == JsonValueKind.String)
                    {
                        downloadUrl = dlProp.GetString() ?? string.Empty;
                    }

                    DateTime? publishedAt = null;
                    if (root.TryGetProperty("publishedAt", out var pubProp) && pubProp.ValueKind == JsonValueKind.String && DateTime.TryParse(pubProp.GetString(), out var dt))
                    {
                        publishedAt = dt;
                    }

                    bool isMandatory = false;
                    if (root.TryGetProperty("isMandatory", out var mandProp))
                    {
                        isMandatory = mandProp.ValueKind == JsonValueKind.True;
                    }

                    return new AppUpdateInfo
                    {
                        Version = remoteVersion,
                        Title = defaultTitle,
                        ReleaseNotes = defaultNotes,
                        LocalizedTitles = localizedTitles,
                        LocalizedReleaseNotes = localizedNotes,
                        DownloadUrl = downloadUrl,
                        PublishedAtUtc = publishedAt,
                        IsMandatory = isMandatory
                    };
                }
            }
        }
        catch
        {
            // Silenzioso in background per non disturbare l'utente se non c'è connessione
        }

        return null;
    }

    private static (string DefaultValue, Dictionary<string, string>? Localized) ParseLocalizedField(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var prop))
        {
            return (string.Empty, null);
        }

        if (prop.ValueKind == JsonValueKind.String)
        {
            var val = prop.GetString() ?? string.Empty;
            return (val, null);
        }

        if (prop.ValueKind == JsonValueKind.Object)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string fallback = string.Empty;

            foreach (var item in prop.EnumerateObject())
            {
                if (item.Value.ValueKind == JsonValueKind.String)
                {
                    var text = item.Value.GetString() ?? string.Empty;
                    dict[item.Name] = text;
                    if (string.IsNullOrEmpty(fallback) || item.Name.Equals("it", StringComparison.OrdinalIgnoreCase))
                    {
                        fallback = text;
                    }
                }
            }

            return (fallback, dict);
        }

        return (string.Empty, null);
    }

    /// <summary>
    /// Apre nel browser predefinito dell'utente la pagina o il link per scaricare l'aggiornamento.
    /// </summary>
    public void OpenDownloadPage(string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            downloadUrl = "https://github.com/macondo-studio/macondo";
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = downloadUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] Errore durante l'apertura del browser: {ex.Message}");
        }
    }
}
