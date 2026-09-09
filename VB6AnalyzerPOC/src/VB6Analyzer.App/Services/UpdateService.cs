using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Macondo.App.Services;

/// <summary>
/// Dettagli relativi a un aggiornamento software disponibile.
/// </summary>
public sealed class AppUpdateInfo
{
    /// <summary>Versione numerica dell'aggiornamento remoto.</summary>
    public required Version Version { get; init; }

    /// <summary>Stringa formattata della versione (es. '1.1.0').</summary>
    public string VersionString => Version.ToString(3);

    /// <summary>Titolo o nome della release.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Note di rilascio / changelog con le novità della versione.</summary>
    public string ReleaseNotes { get; init; } = string.Empty;

    /// <summary>URL della pagina di download o del pacchetto di installazione.</summary>
    public string DownloadUrl { get; init; } = string.Empty;

    /// <summary>Data e ora di pubblicazione della release.</summary>
    public DateTime? PublishedAtUtc { get; init; }

    /// <summary>Indica se l'aggiornamento è critico/obbligatorio.</summary>
    public bool IsMandatory { get; init; }
}

/// <summary>
/// Servizio per il controllo automatico e manuale degli aggiornamenti dell'applicazione.
/// Esegue la verifica in background all'avvio senza bloccare l'interfaccia utente.
/// </summary>
public sealed class UpdateService
{
    private static readonly Lazy<UpdateService> _instance = new(() => new UpdateService());
    public static UpdateService Instance => _instance.Value;

    // Versione corrente dell'applicazione Macondo (VB6 Analyzer)
    public static readonly Version CurrentVersion = new Version(1, 0, 0);
    public static readonly string CurrentVersionString = "1.0.0";

    // =========================================================================================
    // TODO: CONFIGURAZIONE SERVER AGGIORNAMENTI
    // Inserire qui l'URL del server / endpoint da cui scaricare il manifesto di aggiornamento o la release.
    // Esempi di architetture supportate:
    //   1. Endpoint REST/JSON personalizzato (es. "https://update.example.com/vb6analyzer/version.json")
    //   2. GitHub Releases API (es. "https://api.github.com/repos/macondo-studio/Vb6Analyzer/releases/latest")
    // =========================================================================================
    public string? UpdateEndpointUrl { get; set; } = null; // TODO: Impostare l'URL quando il server sarà online

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

        // Se l'endpoint non è ancora configurato (TODO server), restituiamo null senza generare errori
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            // TODO: Quando il server sarà pronto, rimuovere o valorizzare UpdateEndpointUrl
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

            var manifest = await response.Content.ReadFromJsonAsync<UpdateManifestDto>(cancellationToken: ct).ConfigureAwait(false);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
            {
                return null;
            }

            if (Version.TryParse(manifest.Version.TrimStart('v', 'V'), out var remoteVersion))
            {
                if (remoteVersion > CurrentVersion)
                {
                    return new AppUpdateInfo
                    {
                        Version = remoteVersion,
                        Title = manifest.Title ?? $"Versione {remoteVersion}",
                        ReleaseNotes = manifest.ReleaseNotes ?? "Miglioramenti di stabilità e nuove funzionalità.",
                        DownloadUrl = manifest.DownloadUrl ?? string.Empty,
                        PublishedAtUtc = manifest.PublishedAt,
                        IsMandatory = manifest.IsMandatory
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

    /// <summary>
    /// Apre nel browser predefinito dell'utente la pagina o il link per scaricare l'aggiornamento.
    /// </summary>
    public void OpenDownloadPage(string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            // TODO: URL di fallback della pagina del progetto se il downloadUrl non è specificato
            downloadUrl = "https://github.com";
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

    /// <summary>
    /// DTO per la deserializzazione del manifesto JSON degli aggiornamenti.
    /// </summary>
    private sealed class UpdateManifestDto
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("releaseNotes")]
        public string? ReleaseNotes { get; set; }

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("publishedAt")]
        public DateTime? PublishedAt { get; set; }

        [JsonPropertyName("isMandatory")]
        public bool IsMandatory { get; set; }
    }
}
