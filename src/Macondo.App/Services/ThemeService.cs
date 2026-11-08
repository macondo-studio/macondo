using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Macondo.App.Models;

namespace Macondo.App.Services;

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
                AccentColor = "#10B981",
                AccentHover = "#34D399",
                GraphBackground = "#181822",
                GraphEdgeCalls = "#4A4A62",
                GraphNodeFill = "#222230",
                GraphNodeStroke = "#3D3D55"
            },
            [AppTheme.DarkStudio] = new ThemeDefinition
            {
                Theme = AppTheme.DarkStudio,
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
            [AppTheme.DarkGreen] = new ThemeDefinition
            {
                Theme = AppTheme.DarkGreen,
                IsDark = true,
                BackgroundPrimary = "#101814",
                BackgroundSecondary = "#0C130F",
                BackgroundSurface = "#16231D",
                BackgroundElevated = "#1D2E26",
                BorderDefault = "#2A4236",
                BorderSubtle = "#1C2E25",
                TextPrimary = "#E4EFE8",
                TextSecondary = "#98B5A4",
                TextMuted = "#638070",
                AccentColor = "#10B981",
                AccentHover = "#34D399",
                GraphBackground = "#0E1511",
                GraphEdgeCalls = "#2E4E3E",
                GraphNodeFill = "#16261E",
                GraphNodeStroke = "#2E5640"
            },
            [AppTheme.LightModern] = new ThemeDefinition
            {
                Theme = AppTheme.LightModern,
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

            // Avalonia Fluent Theme Override Keys for Menus, Dropdowns, Flyouts & ContextMenus
            SetAppBrush("MenuFlyoutPresenterBackground", def.BackgroundSurface);
            SetAppBrush("MenuFlyoutPresenterBorderBrush", def.BorderDefault);
            SetAppBrush("FlyoutPresenterBackground", def.BackgroundSurface);
            SetAppBrush("FlyoutPresenterBorderBrush", def.BorderDefault);

            SetAppBrush("MenuItemBackground", "Transparent");
            SetAppBrush("MenuItemForeground", def.TextPrimary);
            SetAppBrush("MenuItemBackgroundPointerOver", def.BackgroundElevated);
            SetAppBrush("MenuItemForegroundPointerOver", def.TextPrimary);
            SetAppBrush("MenuItemBackgroundPressed", def.BackgroundElevated);
            SetAppBrush("MenuItemForegroundPressed", def.TextPrimary);
            SetAppBrush("MenuItemBackgroundSelected", def.BackgroundElevated);
            SetAppBrush("MenuItemForegroundSelected", def.TextPrimary);
            SetAppBrush("MenuItemBackgroundDisabled", "Transparent");
            SetAppBrush("MenuItemForegroundDisabled", def.TextMuted);
            SetAppBrush("MenuItemChevronForeground", def.TextSecondary);
            SetAppBrush("MenuItemInputGestureTextForeground", def.TextMuted);

            SetAppBrush("MenuFlyoutItemBackground", "Transparent");
            SetAppBrush("MenuFlyoutItemForeground", def.TextPrimary);
            SetAppBrush("MenuFlyoutItemBackgroundPointerOver", def.BackgroundElevated);
            SetAppBrush("MenuFlyoutItemForegroundPointerOver", def.TextPrimary);
            SetAppBrush("MenuFlyoutItemBackgroundPressed", def.BackgroundElevated);
            SetAppBrush("MenuFlyoutItemForegroundPressed", def.TextPrimary);
            SetAppBrush("MenuFlyoutItemBackgroundDisabled", "Transparent");
            SetAppBrush("MenuFlyoutItemForegroundDisabled", def.TextMuted);
            SetAppBrush("MenuFlyoutSeparatorBackground", def.BorderSubtle);
            SetAppBrush("MenuFlyoutSeparatorForeground", def.BorderSubtle);

            SetAppBrush("MenuBackground", "Transparent");
            SetAppBrush("MenuForeground", def.TextPrimary);

            // Avalonia Fluent Theme Override Keys for ComboBox & ComboBox Dropdown
            SetAppBrush("ComboBoxDropDownBackground", def.BackgroundSurface);
            SetAppBrush("ComboBoxDropDownBorderBrush", def.BorderDefault);
            SetAppBrush("ComboBoxBackground", def.BackgroundSurface);
            SetAppBrush("ComboBoxForeground", def.TextPrimary);
            SetAppBrush("ComboBoxBackgroundPointerOver", def.BackgroundSurface);
            SetAppBrush("ComboBoxForegroundPointerOver", def.TextPrimary);
            SetAppBrush("ComboBoxBackgroundPressed", def.BackgroundSurface);
            SetAppBrush("ComboBoxForegroundPressed", def.TextPrimary);
            SetAppBrush("ComboBoxBackgroundDisabled", def.BackgroundSecondary);
            SetAppBrush("ComboBoxForegroundDisabled", def.TextMuted);
            SetAppBrush("ComboBoxBorderBrush", def.BorderSubtle);
            SetAppBrush("ComboBoxBorderBrushPointerOver", def.BorderDefault);
            SetAppBrush("ComboBoxBorderBrushPressed", def.BorderDefault);
            SetAppBrush("ComboBoxDropDownGlyphForeground", def.TextSecondary);

            SetAppBrush("ComboBoxItemBackground", "Transparent");
            SetAppBrush("ComboBoxItemForeground", def.TextPrimary);
            SetAppBrush("ComboBoxItemBackgroundPointerOver", def.BackgroundElevated);
            SetAppBrush("ComboBoxItemForegroundPointerOver", def.TextPrimary);
            SetAppBrush("ComboBoxItemBackgroundSelected", def.BackgroundElevated);
            SetAppBrush("ComboBoxItemForegroundSelected", def.TextPrimary);
            SetAppBrush("ComboBoxItemBackgroundSelectedPointerOver", def.BackgroundElevated);
            SetAppBrush("ComboBoxItemForegroundSelectedPointerOver", def.TextPrimary);
            SetAppBrush("ComboBoxItemBackgroundPressed", def.BackgroundElevated);
            SetAppBrush("ComboBoxItemForegroundPressed", def.TextPrimary);
            SetAppBrush("ComboBoxItemBackgroundDisabled", "Transparent");
            SetAppBrush("ComboBoxItemForegroundDisabled", def.TextMuted);
        }

        ThemeChanged?.Invoke(this, def);
    }

    private static void SetAppBrush(string key, string colorHex)
    {
        if (string.Equals(colorHex, "Transparent", StringComparison.OrdinalIgnoreCase))
        {
            Application.Current!.Resources[key] = Brushes.Transparent;
            return;
        }

        if (Color.TryParse(colorHex, out var color))

            Application.Current!.Resources[key] = new SolidColorBrush(color);
    }
}
