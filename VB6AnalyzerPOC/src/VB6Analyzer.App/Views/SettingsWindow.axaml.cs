using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Linq;
using VB6Analyzer.App.Services;

namespace VB6Analyzer.App.Views;

public partial class SettingsWindow : Window
{
    private readonly List<ShortcutItem> _allShortcuts;

    public SettingsWindow()
    {
        InitializeComponent();

        _allShortcuts = KeyboardShortcutsCatalog.GetShortcuts(LocalizationService.Instance.CurrentLanguage).ToList();
        ShortcutsList.ItemsSource = _allShortcuts;

        UpdateThemeSelection(ThemeService.Instance.CurrentTheme);
        UpdateLanguageSelection(LocalizationService.Instance.CurrentLanguage);
        UpdateLocalizedTexts();

        LocalizationService.Instance.LanguageChanged += (_, lang) =>
        {
            UpdateLanguageSelection(lang);
            UpdateLocalizedTexts();
            RefreshShortcutsList();
        };

        ThemeService.Instance.ThemeChanged += (_, theme) =>
        {
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

    private void OnNavShortcutsClicked(object? sender, RoutedEventArgs e)
    {
        SetSelectedNav(2);
    }

    private void SetSelectedNav(int index)
    {
        NavThemeButton.Classes.Set("selected", index == 0);
        NavLanguageButton.Classes.Set("selected", index == 1);
        NavShortcutsButton.Classes.Set("selected", index == 2);

        ThemeSection.IsVisible = index == 0;
        LanguageSection.IsVisible = index == 1;
        ShortcutsSection.IsVisible = index == 2;
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

    private void OnSelectLightModern(object? sender, PointerPressedEventArgs e)
    {
        ThemeService.Instance.ApplyTheme(AppTheme.LightModern);
    }

    private void UpdateThemeSelection(AppTheme theme)
    {
        CheckDarkModern.IsVisible = theme == AppTheme.DarkModern;
        CheckVSDark.IsVisible = theme == AppTheme.VisualStudioDark;
        CheckOneDark.IsVisible = theme == AppTheme.OneDarkPro;
        CheckLightModern.IsVisible = theme == AppTheme.LightModern;
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

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;

        WindowTitleText.Text = loc["Settings_Title"];
        NavThemeText.Text = loc["Settings_Tab_Appearance"];
        NavLanguageText.Text = loc["Settings_Tab_Language"];
        NavShortcutsText.Text = loc["Settings_Tab_Shortcuts"];

        ToolTip.SetTip(CloseButton, loc["Tooltip_Close"]);
        ToolTip.SetTip(NavThemeButton, loc["Settings_Tab_Appearance"]);
        ToolTip.SetTip(NavLanguageButton, loc["Settings_Tab_Language"]);
        ToolTip.SetTip(NavShortcutsButton, loc["Settings_Tab_Shortcuts"]);

        ThemeHeaderTitle.Text = loc["Settings_Appearance_Header"];
        ThemeHeaderSub.Text = loc["Settings_Appearance_Sub"];

        LangHeaderTitle.Text = loc["Settings_Language_Header"];
        LangHeaderSub.Text = loc["Settings_Language_Sub"];

        ShortcutsHeaderTitle.Text = loc["Settings_Shortcuts_Header"];
        ShortcutsHeaderSub.Text = loc["Settings_Shortcuts_Sub"];
        ShortcutSearchBox.Watermark = loc["Settings_Shortcuts_Search"];
    }
}
