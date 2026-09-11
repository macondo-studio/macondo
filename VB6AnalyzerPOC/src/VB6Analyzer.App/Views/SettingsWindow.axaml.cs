using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;
using Macondo.App.Services;

namespace Macondo.App.Views;

public partial class SettingsWindow : Window
{
    private readonly List<ShortcutItem> _allShortcuts;

    public SettingsWindow()
    {
        InitializeComponent();

        _allShortcuts = KeyboardShortcutsCatalog.GetShortcuts(LocalizationService.Instance.CurrentLanguage).ToList();
        ShortcutsList.ItemsSource = _allShortcuts;

        ApplyTheme(ThemeService.Instance.CurrentDefinition);
        UpdateThemeSelection(ThemeService.Instance.CurrentTheme);
        UpdateLanguageSelection(LocalizationService.Instance.CurrentLanguage);
        UpdateBehaviorSelection(SettingsService.Instance.Settings.EnableHoverSelection);
        UpdateLocalizedTexts();

        LocalizationService.Instance.LanguageChanged += (_, lang) =>
        {
            UpdateLanguageSelection(lang);
            UpdateLocalizedTexts();
            RefreshShortcutsList();
        };

        ThemeService.Instance.ThemeChanged += (_, theme) =>
        {
            ApplyTheme(theme);
            UpdateThemeSelection(theme.Theme);
        };
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // =========================================================================
    // Navigazione Sidebar
    // =========================================================================

    private void OnNavThemeClicked(object? sender, RoutedEventArgs e)
    {
        SetSelectedNav(0);
    }

    private void OnNavLanguageClicked(object? sender, RoutedEventArgs e)
    {
        SetSelectedNav(1);
    }

    private void OnNavBehaviorClicked(object? sender, RoutedEventArgs e)
    {
        SetSelectedNav(2);
    }

    private void OnNavShortcutsClicked(object? sender, RoutedEventArgs e)
    {
        SetSelectedNav(3);
    }

    private void SetSelectedNav(int index)
    {
        NavThemeButton.Classes.Set("selected", index == 0);
        NavLanguageButton.Classes.Set("selected", index == 1);
        NavBehaviorButton.Classes.Set("selected", index == 2);
        NavShortcutsButton.Classes.Set("selected", index == 3);

        ThemeSection.IsVisible = index == 0;
        LanguageSection.IsVisible = index == 1;
        BehaviorSection.IsVisible = index == 2;
        ShortcutsSection.IsVisible = index == 3;
    }

    // =========================================================================
    // Selezione Temi
    // =========================================================================

    private void OnSelectDarkModern(object? sender, PointerPressedEventArgs e)
    {
        ThemeService.Instance.ApplyTheme(AppTheme.DarkModern);
    }

    private void OnSelectVSDark(object? sender, PointerPressedEventArgs e)
    {
        ThemeService.Instance.ApplyTheme(AppTheme.VisualStudioDark);
    }

    private void OnSelectOneDark(object? sender, PointerPressedEventArgs e)
    {
        ThemeService.Instance.ApplyTheme(AppTheme.OneDarkPro);
    }

    private void OnSelectDarkGreen(object? sender, PointerPressedEventArgs e)
    {
        ThemeService.Instance.ApplyTheme(AppTheme.DarkGreen);
    }

    private void OnSelectLightModern(object? sender, PointerPressedEventArgs e)
    {
        ThemeService.Instance.ApplyTheme(AppTheme.LightModern);
    }

    private void UpdateThemeSelection(AppTheme theme)
    {
        CheckDarkModern.IsVisible = theme == AppTheme.DarkModern;
        CheckVSDark.IsVisible = theme == AppTheme.VisualStudioDark;
        CheckOneDark.IsVisible = theme == AppTheme.OneDarkPro;
        CheckDarkGreen.IsVisible = theme == AppTheme.DarkGreen;
        CheckLightModern.IsVisible = theme == AppTheme.LightModern;
    }

    private void ApplyTheme(ThemeDefinition theme)
    {
        if (Color.TryParse(theme.BackgroundPrimary, out var bgPrimary))
            Background = new SolidColorBrush(bgPrimary);

        SolidColorBrush? secBrush = Color.TryParse(theme.BackgroundSecondary, out var bgSec) ? new SolidColorBrush(bgSec) : null;
        SolidColorBrush? surfBrush = Color.TryParse(theme.BackgroundSurface, out var bgSurf) ? new SolidColorBrush(bgSurf) : null;
        SolidColorBrush? borderBrush = Color.TryParse(theme.BorderDefault, out var bDef) ? new SolidColorBrush(bDef) : null;
        SolidColorBrush? primaryBrush = Color.TryParse(theme.BackgroundPrimary, out var bgPrim) ? new SolidColorBrush(bgPrim) : null;

        if (secBrush != null)
        {
            TopTitleBorder.Background = secBrush;
            SidebarBorder.Background = secBrush;
        }

        if (primaryBrush != null)
        {
            ContentBorder.Background = primaryBrush;
        }

        if (borderBrush != null)
        {
            TopTitleBorder.BorderBrush = borderBrush;
            SidebarBorder.BorderBrush = borderBrush;

            CardDarkModern.BorderBrush = borderBrush;
            CardVSDark.BorderBrush = borderBrush;
            CardOneDark.BorderBrush = borderBrush;
            CardDarkGreen.BorderBrush = borderBrush;
            CardLightModern.BorderBrush = borderBrush;

            CardLangItalian.BorderBrush = borderBrush;
            CardLangEnglish.BorderBrush = borderBrush;

            CardHoverSelection.BorderBrush = borderBrush;
        }

        if (surfBrush != null)
        {
            CardDarkModern.Background = surfBrush;
            CardVSDark.Background = surfBrush;
            CardOneDark.Background = surfBrush;
            CardDarkGreen.Background = surfBrush;
            CardLightModern.Background = surfBrush;

            CardLangItalian.Background = surfBrush;
            CardLangEnglish.Background = surfBrush;

            CardHoverSelection.Background = surfBrush;
        }
    }

    // =========================================================================
    // Selezione Lingua
    // =========================================================================

    private void OnSelectItalian(object? sender, PointerPressedEventArgs e)
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.Italian);
    }

    private void OnSelectEnglish(object? sender, PointerPressedEventArgs e)
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.English);
    }

    private void UpdateLanguageSelection(AppLanguage language)
    {
        CheckLangItalian.IsVisible = language == AppLanguage.Italian;
        CheckLangEnglish.IsVisible = language == AppLanguage.English;
    }

    // =========================================================================
    // Opzioni Comportamento & Grafo
    // =========================================================================

    private void OnToggleHoverSetting(object? sender, PointerPressedEventArgs e)
    {
        var current = SettingsService.Instance.Settings.EnableHoverSelection;
        var updated = !current;
        SettingsService.Instance.Update(s => s.EnableHoverSelection = updated);
        UpdateBehaviorSelection(updated);
    }

    private void UpdateBehaviorSelection(bool enabled)
    {
        CheckHoverSelection.IsVisible = enabled;
    }

    // =========================================================================
    // Filtro Scorciatoie
    // =========================================================================

    private void RefreshShortcutsList()
    {
        _allShortcuts.Clear();
        _allShortcuts.AddRange(KeyboardShortcutsCatalog.GetShortcuts(LocalizationService.Instance.CurrentLanguage));
        ApplyShortcutsFilter();
    }

    private void OnShortcutSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyShortcutsFilter();
    }

    private void ApplyShortcutsFilter()
    {
        var filter = ShortcutSearchBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(filter))
        {
            ShortcutsList.ItemsSource = _allShortcuts;
        }
        else
        {
            ShortcutsList.ItemsSource = _allShortcuts
                .Where(s => s.ActionName.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
                            s.KeyChord.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
                            s.Category.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
                            s.Description.Contains(filter, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    // =========================================================================
    // Ripristino Impostazioni Predefinite
    // =========================================================================

    private void OnResetSettingsClicked(object? sender, RoutedEventArgs e)
    {
        var defaultSettings = SettingsService.Instance.Reset();
        ThemeService.Instance.ApplyTheme(defaultSettings.Theme);
        LocalizationService.Instance.SetLanguage(defaultSettings.Language);
        UpdateThemeSelection(defaultSettings.Theme);
        UpdateLanguageSelection(defaultSettings.Language);
        UpdateBehaviorSelection(defaultSettings.EnableHoverSelection);
        UpdateLocalizedTexts();
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;

        WindowTitleText.Text = loc["Settings_Title"];
        NavThemeText.Text = loc["Settings_Tab_Appearance"];
        NavLanguageText.Text = loc["Settings_Tab_Language"];
        NavBehaviorText.Text = loc["Settings_Tab_Behavior"];
        NavShortcutsText.Text = loc["Settings_Tab_Shortcuts"];

        ResetSettingsButtonText.Text = loc["Settings_Reset"];
        ToolTip.SetTip(ResetSettingsButton, loc["Tooltip_ResetSettings"]);

        ToolTip.SetTip(CloseButton, loc["Tooltip_Close"]);
        ToolTip.SetTip(NavThemeButton, loc["Settings_Tab_Appearance"]);
        ToolTip.SetTip(NavLanguageButton, loc["Settings_Tab_Language"]);
        ToolTip.SetTip(NavBehaviorButton, loc["Settings_Tab_Behavior"]);
        ToolTip.SetTip(NavShortcutsButton, loc["Settings_Tab_Shortcuts"]);

        ThemeHeaderTitle.Text = loc["Settings_Appearance_Header"];
        ThemeHeaderSub.Text = loc["Settings_Appearance_Sub"];

        LangHeaderTitle.Text = loc["Settings_Language_Header"];
        LangHeaderSub.Text = loc["Settings_Language_Sub"];

        BehaviorHeaderTitle.Text = loc["Settings_Behavior_Header"];
        BehaviorHeaderSub.Text = loc["Settings_Behavior_Sub"];
        HoverSelectionTitleText.Text = loc["Settings_HoverSelection_Title"];
        HoverSelectionSubText.Text = loc["Settings_HoverSelection_Sub"];

        ShortcutsHeaderTitle.Text = loc["Settings_Shortcuts_Header"];
        ShortcutsHeaderSub.Text = loc["Settings_Shortcuts_Sub"];
        ShortcutSearchBox.Watermark = loc["Settings_Shortcuts_Search"];
    }
}
