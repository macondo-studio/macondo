using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;
using Macondo.App.Models;
using Macondo.App.Services;

namespace Macondo.App.Views;

public partial class SettingsWindow : Window
{
    private readonly List<ShortcutItem> _allShortcuts;

    public SettingsWindow() : this(0)
    {
    }

    public SettingsWindow(int initialTab)
    {
        InitializeComponent();

        _allShortcuts = KeyboardShortcutsCatalog.GetShortcuts(LocalizationService.Instance.CurrentLanguage).ToList();
        ShortcutsList.ItemsSource = _allShortcuts;

        ApplyTheme(ThemeService.Instance.CurrentDefinition);
        UpdateThemeSelection(ThemeService.Instance.CurrentTheme);
        UpdateLanguageSelection(LocalizationService.Instance.CurrentLanguage);
        UpdateBehaviorSelection(SettingsService.Instance.Settings.EnableHoverSelection);
        UpdateTabLabelsSelection(SettingsService.Instance.Settings.ShowTabLabels);
        UpdateStartupSelection(SettingsService.Instance.Settings.StartupMode);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
        UpdateLocalizedTexts();
        SetSelectedNav(initialTab);

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

    private void OnNavAnalyticsTabsClicked(object? sender, RoutedEventArgs e)
    {
        SetSelectedNav(3);
    }

    private void OnNavShortcutsClicked(object? sender, RoutedEventArgs e)
    {
        SetSelectedNav(4);
    }

    public void SetSelectedNav(int index)
    {
        NavThemeButton.Classes.Set("selected", index == 0);
        NavLanguageButton.Classes.Set("selected", index == 1);
        NavBehaviorButton.Classes.Set("selected", index == 2);
        NavAnalyticsTabsButton.Classes.Set("selected", index == 3);
        NavShortcutsButton.Classes.Set("selected", index == 4);

        ThemeSection.IsVisible = index == 0;
        LanguageSection.IsVisible = index == 1;
        BehaviorSection.IsVisible = index == 2;
        AnalyticsTabsSection.IsVisible = index == 3;
        ShortcutsSection.IsVisible = index == 4;
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
        ThemeService.Instance.ApplyTheme(AppTheme.DarkStudio);
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
        CheckVSDark.IsVisible = theme == AppTheme.DarkStudio;
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
            CardShowTabLabels.BorderBrush = borderBrush;
            CardStartupOnlyIfCached.BorderBrush = borderBrush;
            CardStartupAlways.BorderBrush = borderBrush;
            CardStartupEmpty.BorderBrush = borderBrush;

            CardTabGraph.BorderBrush = borderBrush;
            CardTabDsm.BorderBrush = borderBrush;
            CardTabTreemap.BorderBrush = borderBrush;
            CardTabHotspot.BorderBrush = borderBrush;
            CardTabScreenFlow.BorderBrush = borderBrush;
            CardTabControlFlow.BorderBrush = borderBrush;
            CardTabMartin.BorderBrush = borderBrush;
            CardTabDeadCode.BorderBrush = borderBrush;
            CardTabSharedState.BorderBrush = borderBrush;
            CardTabTemporalCoupling.BorderBrush = borderBrush;
            CardTabArchitecturalFlow.BorderBrush = borderBrush;
            CardTabCodeCity.BorderBrush = borderBrush;
            CardTabBlastRadiusGrid.BorderBrush = borderBrush;
            CardTabCohesion.BorderBrush = borderBrush;
            CardTabMaintainability.BorderBrush = borderBrush;
            CardTabArchitecturalRules.BorderBrush = borderBrush;
            CardTabCodeClone.BorderBrush = borderBrush;
            CardTabSocialCode.BorderBrush = borderBrush;
            CardTabDataFlow.BorderBrush = borderBrush;
            CardTabApiDiff.BorderBrush = borderBrush;
            CardTabCrudMatrix.BorderBrush = borderBrush;

            SelectAllTabsButton.BorderBrush = borderBrush;
            DeselectAllTabsButton.BorderBrush = borderBrush;
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
            CardShowTabLabels.Background = surfBrush;
            CardStartupOnlyIfCached.Background = surfBrush;
            CardStartupAlways.Background = surfBrush;
            CardStartupEmpty.Background = surfBrush;

            CardTabGraph.Background = surfBrush;
            CardTabDsm.Background = surfBrush;
            CardTabTreemap.Background = surfBrush;
            CardTabHotspot.Background = surfBrush;
            CardTabScreenFlow.Background = surfBrush;
            CardTabControlFlow.Background = surfBrush;
            CardTabMartin.Background = surfBrush;
            CardTabDeadCode.Background = surfBrush;
            CardTabSharedState.Background = surfBrush;
            CardTabTemporalCoupling.Background = surfBrush;
            CardTabArchitecturalFlow.Background = surfBrush;
            CardTabCodeCity.Background = surfBrush;
            CardTabBlastRadiusGrid.Background = surfBrush;
            CardTabCohesion.Background = surfBrush;
            CardTabMaintainability.Background = surfBrush;
            CardTabArchitecturalRules.Background = surfBrush;
            CardTabCodeClone.Background = surfBrush;
            CardTabSocialCode.Background = surfBrush;
            CardTabDataFlow.Background = surfBrush;
            CardTabApiDiff.Background = surfBrush;
            CardTabCrudMatrix.Background = surfBrush;

            SelectAllTabsButton.Background = surfBrush;
            DeselectAllTabsButton.Background = surfBrush;
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

    private void OnToggleTabLabelsSetting(object? sender, PointerPressedEventArgs e)
    {
        var current = SettingsService.Instance.Settings.ShowTabLabels;
        var updated = !current;
        SettingsService.Instance.Update(s => s.ShowTabLabels = updated);
        UpdateTabLabelsSelection(updated);
    }

    private void UpdateTabLabelsSelection(bool enabled)
    {
        CheckShowTabLabels.IsVisible = enabled;
    }

    private void OnSelectStartupOnlyIfCached(object? sender, PointerPressedEventArgs e)
    {
        SettingsService.Instance.Update(s => s.StartupMode = StartupLoadMode.OnlyIfCached);
        UpdateStartupSelection(StartupLoadMode.OnlyIfCached);
    }

    private void OnSelectStartupAlways(object? sender, PointerPressedEventArgs e)
    {
        SettingsService.Instance.Update(s => s.StartupMode = StartupLoadMode.AlwaysAnalyze);
        UpdateStartupSelection(StartupLoadMode.AlwaysAnalyze);
    }

    private void OnSelectStartupEmpty(object? sender, PointerPressedEventArgs e)
    {
        SettingsService.Instance.Update(s => s.StartupMode = StartupLoadMode.EmptyWorkspace);
        UpdateStartupSelection(StartupLoadMode.EmptyWorkspace);
    }

    private void UpdateStartupSelection(StartupLoadMode mode)
    {
        CheckStartupOnlyIfCached.IsVisible = mode == StartupLoadMode.OnlyIfCached;
        CheckStartupAlways.IsVisible = mode == StartupLoadMode.AlwaysAnalyze;
        CheckStartupEmpty.IsVisible = mode == StartupLoadMode.EmptyWorkspace;
    }

    // =========================================================================
    // Visibilità Schede Analitiche (Tab Visibility)
    // =========================================================================

    private void OnToggleTabGraph(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabGraph;
        SettingsService.Instance.Update(s => s.ShowTabGraph = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabDsm(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabDsm;
        SettingsService.Instance.Update(s => s.ShowTabDsm = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabTreemap(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabTreemap;
        SettingsService.Instance.Update(s => s.ShowTabTreemap = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabHotspot(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabHotspot;
        SettingsService.Instance.Update(s => s.ShowTabHotspot = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabScreenFlow(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabScreenFlow;
        SettingsService.Instance.Update(s => s.ShowTabScreenFlow = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabControlFlow(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabControlFlow;
        SettingsService.Instance.Update(s => s.ShowTabControlFlow = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabMartin(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabMartin;
        SettingsService.Instance.Update(s => s.ShowTabMartin = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabDeadCode(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabDeadCode;
        SettingsService.Instance.Update(s => s.ShowTabDeadCode = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabSharedState(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabSharedState;
        SettingsService.Instance.Update(s => s.ShowTabSharedState = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabTemporalCoupling(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabTemporalCoupling;
        SettingsService.Instance.Update(s => s.ShowTabTemporalCoupling = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabArchitecturalFlow(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabArchitecturalFlow;
        SettingsService.Instance.Update(s => s.ShowTabArchitecturalFlow = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabCodeCity(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabCodeCity;
        SettingsService.Instance.Update(s => s.ShowTabCodeCity = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabBlastRadiusGrid(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabBlastRadiusGrid;
        SettingsService.Instance.Update(s => s.ShowTabBlastRadiusGrid = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabCohesion(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabCohesion;
        SettingsService.Instance.Update(s => s.ShowTabCohesion = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabMaintainability(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabMaintainability;
        SettingsService.Instance.Update(s => s.ShowTabMaintainability = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabArchitecturalRules(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabArchitecturalRules;
        SettingsService.Instance.Update(s => s.ShowTabArchitecturalRules = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabCodeClone(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabCodeClone;
        SettingsService.Instance.Update(s => s.ShowTabCodeClone = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabSocialCode(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabSocialCode;
        SettingsService.Instance.Update(s => s.ShowTabSocialCode = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabDataFlow(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabDataFlow;
        SettingsService.Instance.Update(s => s.ShowTabDataFlow = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabApiDiff(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabApiDiff;
        SettingsService.Instance.Update(s => s.ShowTabApiDiff = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleTabCrudMatrix(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.ShowTabCrudMatrix;
        SettingsService.Instance.Update(s => s.ShowTabCrudMatrix = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnToggleBackgroundAnalytics(object? sender, PointerPressedEventArgs e)
    {
        var updated = !SettingsService.Instance.Settings.EnableBackgroundAnalytics;
        SettingsService.Instance.Update(s => s.EnableBackgroundAnalytics = updated);
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnSelectAllTabsClicked(object? sender, RoutedEventArgs e)
    {
        SettingsService.Instance.Update(s =>
        {
            s.ShowTabGraph = true;
            s.ShowTabDsm = true;
            s.ShowTabTreemap = true;
            s.ShowTabHotspot = true;
            s.ShowTabScreenFlow = true;
            s.ShowTabControlFlow = true;
            s.ShowTabMartin = true;
            s.ShowTabDeadCode = true;
            s.ShowTabSharedState = true;
            s.ShowTabTemporalCoupling = true;
            s.ShowTabArchitecturalFlow = true;
            s.ShowTabCodeCity = true;
            s.ShowTabBlastRadiusGrid = true;
            s.ShowTabCohesion = true;
            s.ShowTabMaintainability = true;
            s.ShowTabArchitecturalRules = true;
            s.ShowTabCodeClone = true;
            s.ShowTabSocialCode = true;
            s.ShowTabDataFlow = true;
            s.ShowTabApiDiff = true;
            s.ShowTabCrudMatrix = true;
        });
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void OnDeselectAllTabsClicked(object? sender, RoutedEventArgs e)
    {
        SettingsService.Instance.Update(s =>
        {
            s.ShowTabGraph = false;
            s.ShowTabDsm = false;
            s.ShowTabTreemap = false;
            s.ShowTabHotspot = false;
            s.ShowTabScreenFlow = false;
            s.ShowTabControlFlow = false;
            s.ShowTabMartin = false;
            s.ShowTabDeadCode = false;
            s.ShowTabSharedState = false;
            s.ShowTabTemporalCoupling = false;
            s.ShowTabArchitecturalFlow = false;
            s.ShowTabCodeCity = false;
            s.ShowTabBlastRadiusGrid = false;
            s.ShowTabCohesion = false;
            s.ShowTabMaintainability = false;
            s.ShowTabArchitecturalRules = false;
            s.ShowTabCodeClone = false;
            s.ShowTabSocialCode = false;
            s.ShowTabDataFlow = false;
            s.ShowTabApiDiff = false;
            s.ShowTabCrudMatrix = false;
        });
        UpdateAnalyticsTabsSelection(SettingsService.Instance.Settings);
    }

    private void UpdateAnalyticsTabsSelection(AppSettings s)
    {
        CheckBackgroundAnalytics.IsVisible = s.EnableBackgroundAnalytics;
        CheckTabGraph.IsVisible = s.ShowTabGraph;
        CheckTabDsm.IsVisible = s.ShowTabDsm;
        CheckTabTreemap.IsVisible = s.ShowTabTreemap;
        CheckTabHotspot.IsVisible = s.ShowTabHotspot;
        CheckTabScreenFlow.IsVisible = s.ShowTabScreenFlow;
        CheckTabControlFlow.IsVisible = s.ShowTabControlFlow;
        CheckTabMartin.IsVisible = s.ShowTabMartin;
        CheckTabDeadCode.IsVisible = s.ShowTabDeadCode;
        CheckTabSharedState.IsVisible = s.ShowTabSharedState;
        CheckTabTemporalCoupling.IsVisible = s.ShowTabTemporalCoupling;
        CheckTabArchitecturalFlow.IsVisible = s.ShowTabArchitecturalFlow;
        CheckTabCodeCity.IsVisible = s.ShowTabCodeCity;
        CheckTabBlastRadiusGrid.IsVisible = s.ShowTabBlastRadiusGrid;
        CheckTabCohesion.IsVisible = s.ShowTabCohesion;
        CheckTabMaintainability.IsVisible = s.ShowTabMaintainability;
        CheckTabArchitecturalRules.IsVisible = s.ShowTabArchitecturalRules;
        CheckTabCodeClone.IsVisible = s.ShowTabCodeClone;
        CheckTabSocialCode.IsVisible = s.ShowTabSocialCode;
        CheckTabDataFlow.IsVisible = s.ShowTabDataFlow;
        CheckTabApiDiff.IsVisible = s.ShowTabApiDiff;
        CheckTabCrudMatrix.IsVisible = s.ShowTabCrudMatrix;
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
        UpdateTabLabelsSelection(defaultSettings.ShowTabLabels);
        UpdateStartupSelection(defaultSettings.StartupMode);
        UpdateAnalyticsTabsSelection(defaultSettings);
        UpdateLocalizedTexts();
    }

    private void UpdateLocalizedTexts()
    {
        var localizationService = LocalizationService.Instance;

        Title = localizationService["Settings_Title"];
        WindowTitleText.Text = localizationService["Settings_Title"];
        NavThemeText.Text = localizationService["Settings_Tab_Appearance"];
        NavLanguageText.Text = localizationService["Settings_Tab_Language"];
        NavBehaviorText.Text = localizationService["Settings_Tab_Behavior"];
        NavAnalyticsTabsText.Text = localizationService["Settings_Tab_AnalyticsTabs"];
        NavShortcutsText.Text = localizationService["Settings_Tab_Shortcuts"];

        ResetSettingsButtonText.Text = localizationService["Settings_Reset"];
        ToolTip.SetTip(ResetSettingsButton, localizationService["Tooltip_ResetSettings"]);

        ToolTip.SetTip(CloseButton, localizationService["Tooltip_Close"]);
        ToolTip.SetTip(NavThemeButton, localizationService["Settings_Tab_Appearance"]);
        ToolTip.SetTip(NavLanguageButton, localizationService["Settings_Tab_Language"]);
        ToolTip.SetTip(NavBehaviorButton, localizationService["Settings_Tab_Behavior"]);
        ToolTip.SetTip(NavAnalyticsTabsButton, localizationService["Settings_Tab_AnalyticsTabs"]);
        ToolTip.SetTip(NavShortcutsButton, localizationService["Settings_Tab_Shortcuts"]);

        ThemeHeaderTitle.Text = localizationService["Settings_Appearance_Header"];
        ThemeHeaderSub.Text = localizationService["Settings_Appearance_Sub"];

        ThemeDarkModernTitle.Text = localizationService["Theme_DarkModern_Title"];
        ThemeDarkModernDesc.Text = localizationService["Theme_DarkModern_Desc"];
        ThemeVSDarkTitle.Text = localizationService["Theme_DarkStudio_Title"];
        ThemeVSDarkDesc.Text = localizationService["Theme_DarkStudio_Desc"];
        ThemeOneDarkTitle.Text = localizationService["Theme_OneDark_Title"];
        ThemeOneDarkDesc.Text = localizationService["Theme_OneDark_Desc"];
        ThemeDarkGreenTitle.Text = localizationService["Theme_DarkGreen_Title"];
        ThemeDarkGreenDesc.Text = localizationService["Theme_DarkGreen_Desc"];
        ThemeLightModernTitle.Text = localizationService["Theme_LightModern_Title"];
        ThemeLightModernDesc.Text = localizationService["Theme_LightModern_Desc"];

        LangHeaderTitle.Text = localizationService["Settings_Language_Header"];
        LangHeaderSub.Text = localizationService["Settings_Language_Sub"];
        LangItalianTitle.Text = localizationService["Lang_Italian_Title"];
        LangItalianDesc.Text = localizationService["Lang_Italian_Desc"];
        LangEnglishTitle.Text = localizationService["Lang_English_Title"];
        LangEnglishDesc.Text = localizationService["Lang_English_Desc"];

        BehaviorHeaderTitle.Text = localizationService["Settings_Behavior_Header"];
        BehaviorHeaderSub.Text = localizationService["Settings_Behavior_Sub"];
        HoverSelectionTitleText.Text = localizationService["Settings_HoverSelection_Title"];
        HoverSelectionSubText.Text = localizationService["Settings_HoverSelection_Sub"];
        TabLabelsTitleText.Text = localizationService["Settings_TabLabels_Title"];
        TabLabelsSubText.Text = localizationService["Settings_TabLabels_Sub"];

        StartupHeaderTitle.Text = localizationService["Settings_StartupSection"];
        StartupHeaderSub.Text = localizationService["Settings_StartupDesc"];
        StartupOnlyIfCachedTitle.Text = localizationService["Settings_StartupOnlyIfCached"];
        StartupOnlyIfCachedDesc.Text = localizationService["Settings_StartupOnlyIfCachedDesc"];
        StartupAlwaysTitle.Text = localizationService["Settings_StartupAlways"];
        StartupAlwaysDesc.Text = localizationService["Settings_StartupAlwaysDesc"];
        StartupEmptyTitle.Text = localizationService["Settings_StartupEmpty"];
        StartupEmptyDesc.Text = localizationService["Settings_StartupEmptyDesc"];

        AnalyticsTabsHeaderTitle.Text = localizationService["Settings_AnalyticsTabs_Header"];
        AnalyticsTabsHeaderSub.Text = localizationService["Settings_AnalyticsTabs_Sub"];
        BackgroundAnalyticsTitle.Text = localizationService["Settings_BackgroundAnalyticsTitle"];
        BackgroundAnalyticsDesc.Text = localizationService["Settings_BackgroundAnalyticsDesc"];
        SelectAllTabsButtonText.Text = localizationService["Settings_SelectAll"];
        DeselectAllTabsButtonText.Text = localizationService["Settings_DeselectAll"];

        TabGraphTitle.Text = localizationService["Tab_GraphView"];
        TabGraphDesc.Text = localizationService["Settings_Tab_Graph_Desc"];
        TabDsmTitle.Text = localizationService["Tab_DsmMatrix"];
        TabDsmDesc.Text = localizationService["Settings_Tab_Dsm_Desc"];
        TabTreemapTitle.Text = localizationService["Tab_TreemapMetrics"];
        TabTreemapDesc.Text = localizationService["Settings_Tab_Treemap_Desc"];
        TabHotspotTitle.Text = localizationService["Tab_RiskHotspots"];
        TabHotspotDesc.Text = localizationService["Settings_Tab_Hotspot_Desc"];
        TabScreenFlowTitle.Text = localizationService["Tab_ScreenFlow"];
        TabScreenFlowDesc.Text = localizationService["Settings_Tab_ScreenFlow_Desc"];
        TabControlFlowTitle.Text = localizationService["Tab_ControlFlow"];
        TabControlFlowDesc.Text = localizationService["Settings_Tab_ControlFlow_Desc"];
        TabMartinTitle.Text = localizationService["Tab_MartinMetrics"];
        TabMartinDesc.Text = localizationService["Settings_Tab_Martin_Desc"];
        TabDeadCodeTitle.Text = localizationService["Tab_DeadCode"];
        TabDeadCodeDesc.Text = localizationService["Settings_Tab_DeadCode_Desc"];
        TabSharedStateTitle.Text = localizationService["Tab_SharedState"];
        TabSharedStateDesc.Text = localizationService["Settings_Tab_SharedState_Desc"];
        TabTemporalCouplingTitle.Text = localizationService["Tab_TemporalCoupling"];
        TabTemporalCouplingDesc.Text = localizationService["Settings_Tab_TemporalCoupling_Desc"];
        TabArchitecturalFlowTitle.Text = localizationService["Tab_ArchitecturalFlow"];
        TabArchitecturalFlowDesc.Text = localizationService["Settings_Tab_ArchitecturalFlow_Desc"];
        TabCodeCityTitle.Text = localizationService["Tab_CodeCity"];
        TabCodeCityDesc.Text = localizationService["Settings_Tab_CodeCity_Desc"];
        TabBlastRadiusGridTitle.Text = localizationService["Tab_BlastRadiusGrid"];
        TabBlastRadiusGridDesc.Text = localizationService["Settings_Tab_BlastRadiusGrid_Desc"];
        TabCohesionTitle.Text = localizationService["Tab_Cohesion"];
        TabCohesionDesc.Text = localizationService["Settings_Tab_Cohesion_Desc"];
        TabMaintainabilityTitle.Text = localizationService["Tab_Maintainability"];
        TabMaintainabilityDesc.Text = localizationService["Settings_Tab_Maintainability_Desc"];
        TabArchitecturalRulesTitle.Text = localizationService["Tab_ArchitecturalRules"];
        TabArchitecturalRulesDesc.Text = localizationService["Settings_Tab_ArchitecturalRules_Desc"];
        TabCodeCloneTitle.Text = localizationService["Tab_CodeClone"];
        TabCodeCloneDesc.Text = localizationService["Settings_Tab_CodeClone_Desc"];
        TabSocialCodeTitle.Text = localizationService["Tab_SocialCode"];
        TabSocialCodeDesc.Text = localizationService["Settings_Tab_SocialCode_Desc"];
        TabDataFlowTitle.Text = localizationService["Tab_DataFlow"];
        TabDataFlowDesc.Text = localizationService["Settings_Tab_DataFlow_Desc"];
        TabApiDiffTitle.Text = localizationService["Tab_ApiDiff"];
        TabApiDiffDesc.Text = localizationService["Settings_Tab_ApiDiff_Desc"];
        TabCrudMatrixTitle.Text = localizationService["Tab_CrudMatrix"];
        TabCrudMatrixDesc.Text = localizationService["Settings_Tab_CrudMatrix_Desc"];

        ShortcutsHeaderTitle.Text = localizationService["Settings_Shortcuts_Header"];
        ShortcutsHeaderSub.Text = localizationService["Settings_Shortcuts_Sub"];
        ShortcutSearchBox.Watermark = localizationService["Settings_Shortcuts_Search"];
    }
}
