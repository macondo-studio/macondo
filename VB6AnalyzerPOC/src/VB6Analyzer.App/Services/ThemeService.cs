using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Macondo.App.Services;

public enum AppTheme
{
    DarkModern,
    VisualStudioDark,
    OneDarkPro,
    LightModern
}

public sealed class ThemeDefinition
{
    public required AppTheme Theme { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
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

public sealed class ThemeService
{
    private static readonly Lazy<ThemeService> _instance = new(() => new ThemeService());
    public static ThemeService Instance => _instance.Value;

    public event EventHandler<ThemeDefinition>? ThemeChanged;

    public IReadOnlyDictionary<AppTheme, ThemeDefinition> Themes { get; }

    public AppTheme CurrentTheme { get; private set; } = AppTheme.DarkModern;
    public ThemeDefinition CurrentDefinition => Themes[CurrentTheme];

    private ThemeService()
    {
        Themes = new Dictionary<AppTheme, ThemeDefinition>
        {
            [AppTheme.DarkModern] = new ThemeDefinition
            {
                Theme = AppTheme.DarkModern,
                DisplayName = "Dark Modern (Default)",
                Description = "Stile grafite scuro minimale con accenti arancione caldo.",
                IsDark = true,
                BackgroundPrimary = "#181820",
                BackgroundSecondary = "#14141D",
                BackgroundSurface = "#1E1E28",
                BackgroundElevated = "#22222E",
                BorderDefault = "#2D2D3E",
                BorderSubtle = "#22222E",
                TextPrimary = "#ECECF0",
                TextSecondary = "#A0A0B8",
                TextMuted = "#707084",
                AccentColor = "#FF6633",
                AccentHover = "#FF8555",
                GraphBackground = "#181822",
                GraphEdgeCalls = "#4A4A62",
                GraphNodeFill = "#222230",
                GraphNodeStroke = "#3D3D55"
            },
            [AppTheme.VisualStudioDark] = new ThemeDefinition
            {
                Theme = AppTheme.VisualStudioDark,
                DisplayName = "Visual Studio Dark",
                Description = "La classica palette scura di Visual Studio con accento blu Microsoft.",
                IsDark = true,
                BackgroundPrimary = "#1E1E1E",
                BackgroundSecondary = "#252526",
                BackgroundSurface = "#2D2D30",
                BackgroundElevated = "#333337",
                BorderDefault = "#3F3F46",
                BorderSubtle = "#2A2A2E",
                TextPrimary = "#F1F1F1",
                TextSecondary = "#CCCCCC",
                TextMuted = "#999999",
                AccentColor = "#007ACC",
                AccentHover = "#1C97EA",
                GraphBackground = "#1B1B1C",
                GraphEdgeCalls = "#4D4D58",
                GraphNodeFill = "#252526",
                GraphNodeStroke = "#3E3E42"
            },
            [AppTheme.OneDarkPro] = new ThemeDefinition
            {
                Theme = AppTheme.OneDarkPro,
                DisplayName = "One Dark Pro",
                Description = "Ispirato al celebre tema scuro Atom/VS Code con sfumature blu notte e ciano.",
                IsDark = true,
                BackgroundPrimary = "#21252B",
                BackgroundSecondary = "#1E2227",
                BackgroundSurface = "#282C34",
                BackgroundElevated = "#2C313A",
                BorderDefault = "#3E4451",
                BorderSubtle = "#2C313C",
                TextPrimary = "#ABB2BF",
                TextSecondary = "#828997",
                TextMuted = "#5C6370",
                AccentColor = "#61AFEF",
                AccentHover = "#74B9F5",
                GraphBackground = "#1E2227",
                GraphEdgeCalls = "#3E4451",
                GraphNodeFill = "#282C34",
                GraphNodeStroke = "#4B5263"
            },
            [AppTheme.LightModern] = new ThemeDefinition
            {
                Theme = AppTheme.LightModern,
                DisplayName = "Light Modern",
                Description = "Tema chiaro ad alto contrasto per ambienti luminosi con accento cobalto.",
                IsDark = false,
                BackgroundPrimary = "#F3F3F7",
                BackgroundSecondary = "#E8E8EE",
                BackgroundSurface = "#FFFFFF",
                BackgroundElevated = "#EAEAF2",
                BorderDefault = "#D0D0DC",
                BorderSubtle = "#E2E2EC",
                TextPrimary = "#1A1A24",
                TextSecondary = "#4A4A5E",
                TextMuted = "#707084",
                AccentColor = "#0066B8",
                AccentHover = "#1878CA",
                GraphBackground = "#F7F7FA",
                GraphEdgeCalls = "#9E9EB0",
                GraphNodeFill = "#FFFFFF",
                GraphNodeStroke = "#B0B0C4"
            }
        };
    }

    public void ApplyTheme(AppTheme theme)
    {
        if (!Themes.TryGetValue(theme, out var def)) return;
        CurrentTheme = theme;

        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = def.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

            SetAppBrush("ThemeBackgroundPrimaryBrush", def.BackgroundPrimary);
            SetAppBrush("ThemeBackgroundSecondaryBrush", def.BackgroundSecondary);
            SetAppBrush("ThemeBackgroundSurfaceBrush", def.BackgroundSurface);
            SetAppBrush("ThemeBackgroundElevatedBrush", def.BackgroundElevated);
            SetAppBrush("ThemeBorderDefaultBrush", def.BorderDefault);
            SetAppBrush("ThemeBorderSubtleBrush", def.BorderSubtle);
            SetAppBrush("ThemeTextPrimaryBrush", def.TextPrimary);
            SetAppBrush("ThemeTextSecondaryBrush", def.TextSecondary);
            SetAppBrush("ThemeTextMutedBrush", def.TextMuted);
            SetAppBrush("ThemeAccentBrush", def.AccentColor);
            SetAppBrush("ThemeAccentHoverBrush", def.AccentHover);
        }

        ThemeChanged?.Invoke(this, def);
    }

    private static void SetAppBrush(string key, string colorHex)
    {
        if (Color.TryParse(colorHex, out var color))
        {
            Application.Current!.Resources[key] = new SolidColorBrush(color);
        }
    }
}
