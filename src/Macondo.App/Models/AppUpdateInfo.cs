using System;
using System.Collections.Generic;

namespace Macondo.App.Models;

/// <summary>
/// Details about an available software update.
/// Supports release notes and multilingual titles (e.g. IT, EN).
/// </summary>
public sealed class AppUpdateInfo
{
    /// <summary>Numeric version of remote update.</summary>
    public required Version Version { get; init; }

    /// <summary>Formatted version string (e.g. '1.1.0').</summary>
    public string VersionString => Version.ToString(3);

    /// <summary>Default release title or name.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Default release notes / changelog.</summary>
    public string ReleaseNotes { get; init; } = string.Empty;

    /// <summary>Titles localized by language code ('it', 'en').</summary>
    public IReadOnlyDictionary<string, string>? LocalizedTitles { get; init; }

    /// <summary>Release notes localized by language code ('it', 'en').</summary>
    public IReadOnlyDictionary<string, string>? LocalizedReleaseNotes { get; init; }

    /// <summary>URL of the download page or installation package.</summary>
    public string DownloadUrl { get; init; } = string.Empty;

    /// <summary>Date and time of publication of the release.</summary>
    public DateTime? PublishedAtUtc { get; init; }

    /// <summary>Indicates whether the update is critical/mandatory.</summary>
    public bool IsMandatory { get; init; }

    /// <summary>
    /// Returns the localized title for the requested language, with automatic fallback.
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
    /// Returns localized release notes for the requested language, with automatic fallback.
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
