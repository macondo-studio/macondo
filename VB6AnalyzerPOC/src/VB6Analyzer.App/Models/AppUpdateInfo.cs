using System;
using System.Collections.Generic;

namespace Macondo.App.Models;

/// <summary>
/// Dettagli relativi a un aggiornamento software disponibile.
/// Supporta note di rilascio e titoli multilingua (es. IT, EN).
/// </summary>
public sealed class AppUpdateInfo
{
    /// <summary>Versione numerica dell'aggiornamento remoto.</summary>
    public required Version Version { get; init; }

    /// <summary>Stringa formattata della versione (es. '1.1.0').</summary>
    public string VersionString => Version.ToString(3);

    /// <summary>Titolo o nome predefinito della release.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Note di rilascio predefinite / changelog.</summary>
    public string ReleaseNotes { get; init; } = string.Empty;

    /// <summary>Titoli localizzati per codice lingua ('it', 'en').</summary>
    public IReadOnlyDictionary<string, string>? LocalizedTitles { get; init; }

    /// <summary>Note di rilascio localizzate per codice lingua ('it', 'en').</summary>
    public IReadOnlyDictionary<string, string>? LocalizedReleaseNotes { get; init; }

    /// <summary>URL della pagina di download o del pacchetto di installazione.</summary>
    public string DownloadUrl { get; init; } = string.Empty;

    /// <summary>Data e ora di pubblicazione della release.</summary>
    public DateTime? PublishedAtUtc { get; init; }

    /// <summary>Indica se l'aggiornamento è critico/obbligatorio.</summary>
    public bool IsMandatory { get; init; }

    /// <summary>
    /// Restituisce il titolo localizzato per la lingua richiesta, con fallback automatico.
    /// </summary>
    public string GetTitle(string langCode = "it")
    {
        if (LocalizedTitles != null)
        {
            if (LocalizedTitles.TryGetValue(langCode, out var localized) && !string.IsNullOrWhiteSpace(localized))
                return localized;
            if (LocalizedTitles.TryGetValue("it", out var it) && !string.IsNullOrWhiteSpace(it))
                return it;
            if (LocalizedTitles.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en))
                return en;
        }

        return !string.IsNullOrWhiteSpace(Title) ? Title : $"Macondo v{VersionString}";
    }

    /// <summary>
    /// Restituisce le note di rilascio localizzate per la lingua richiesta, con fallback automatico.
    /// </summary>
    public string GetReleaseNotes(string langCode = "it")
    {
        if (LocalizedReleaseNotes != null)
        {
            if (LocalizedReleaseNotes.TryGetValue(langCode, out var localized) && !string.IsNullOrWhiteSpace(localized))
                return localized;
            if (LocalizedReleaseNotes.TryGetValue("it", out var it) && !string.IsNullOrWhiteSpace(it))
                return it;
            if (LocalizedReleaseNotes.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en))
                return en;
        }

        return !string.IsNullOrWhiteSpace(ReleaseNotes)
            ? ReleaseNotes
            : (langCode == "it"
                ? "Miglioramenti generali delle prestazioni e correzioni di bug."
                : "General performance improvements and bug fixes.");
    }
}
