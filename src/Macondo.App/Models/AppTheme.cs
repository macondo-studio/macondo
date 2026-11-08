namespace Macondo.App.Models;

/// <summary>
/// Temi grafici selezionabili dall'utente per l'applicazione.
/// </summary>
public enum AppTheme
{
    DarkModern,
    DarkStudio,
    OneDarkPro,
    DarkGreen,
    LightModern
}

/// <summary>
/// Definizione completa dei colori e dei pennelli di un tema grafico dell'applicazione.
/// </summary>
public sealed class ThemeDefinition
{
    public required AppTheme Theme { get; init; }
    public required bool IsDark { get; init; }

    public required string BackgroundPrimary { get; init; }
    public required string BackgroundSecondary { get; init; }
    public required string BackgroundSurface { get; init; }
    public required string BackgroundElevated { get; init; }
    public required string BorderDefault { get; init; }
    public required string BorderSubtle { get; init; }

    public required string TextPrimary { get; init; }
    public required string TextSecondary { get; init; }
    public required string TextMuted { get; init; }

    public required string AccentColor { get; init; }
    public required string AccentHover { get; init; }

    public required string GraphBackground { get; init; }
    public required string GraphEdgeCalls { get; init; }
    public required string GraphNodeFill { get; init; }
    public required string GraphNodeStroke { get; init; }

    public string[] PreviewSwatches => new[] { BackgroundPrimary, BackgroundSurface, AccentColor, TextPrimary };
}
