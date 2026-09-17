using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;

namespace Macondo.App.Views;

public partial class InfoWindow : Window
{
    public InfoWindow() : this(0)
    {
    }

    public InfoWindow(int initialTab)
    {
        InitializeComponent();

        ApplyTheme(ThemeService.Instance.CurrentDefinition);
        UpdateLocalizedTexts();
        SetSelectedNav(initialTab);

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            UpdateLocalizedTexts();
        };

        ThemeService.Instance.ThemeChanged += (_, theme) =>
        {
            ApplyTheme(theme);
        };

        AddHandler(KeyDownEvent, (sender, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
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

    private int _currentTabIndex = 0;

    private void OnNavOverviewClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(0);
    private void OnNavLayoutsClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(1);
    private void OnNavLegendClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(2);
    private void OnNavTreemapClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(3);
    private void OnNavFlowsClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(4);
    private void OnNavDsmHotspotsClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(5);
    private void OnNavBlastRadiusClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(6);
    private void OnNavMartinDeadCodeClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(7);
    private void OnNavSharedTemporalClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(8);
    private void OnNavFlowCityClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(9);
    private void OnNavAiExportClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(10);

    public void SetSelectedNav(int index)
    {
        _currentTabIndex = index;

        NavOverviewButton.Classes.Set("selected", index == 0);
        NavLayoutsButton.Classes.Set("selected", index == 1);
        NavLegendButton.Classes.Set("selected", index == 2);
        NavTreemapButton.Classes.Set("selected", index == 3);
        NavFlowsButton.Classes.Set("selected", index == 4);
        NavDsmHotspotsButton.Classes.Set("selected", index == 5);
        NavBlastRadiusButton.Classes.Set("selected", index == 6);
        NavMartinDeadCodeButton.Classes.Set("selected", index == 7);
        NavSharedTemporalButton.Classes.Set("selected", index == 8);
        NavFlowCityButton.Classes.Set("selected", index == 9);
        NavAiExportButton.Classes.Set("selected", index == 10);

        var isEn = LocalizationService.Instance.CurrentLanguage == AppLanguage.English;

        OverviewSection_IT.IsVisible = index == 0 && !isEn;
        OverviewSection_EN.IsVisible = index == 0 && isEn;

        LayoutsSection_IT.IsVisible = index == 1 && !isEn;
        LayoutsSection_EN.IsVisible = index == 1 && isEn;

        LegendSection_IT.IsVisible = index == 2 && !isEn;
        LegendSection_EN.IsVisible = index == 2 && isEn;

        TreemapSection_IT.IsVisible = index == 3 && !isEn;
        TreemapSection_EN.IsVisible = index == 3 && isEn;

        FlowsSection_IT.IsVisible = index == 4 && !isEn;
        FlowsSection_EN.IsVisible = index == 4 && isEn;

        DsmHotspotsSection_IT.IsVisible = index == 5 && !isEn;
        DsmHotspotsSection_EN.IsVisible = index == 5 && isEn;

        BlastRadiusSection_IT.IsVisible = index == 6 && !isEn;
        BlastRadiusSection_EN.IsVisible = index == 6 && isEn;

        MartinDeadCodeSection_IT.IsVisible = index == 7 && !isEn;
        MartinDeadCodeSection_EN.IsVisible = index == 7 && isEn;

        SharedTemporalSection_IT.IsVisible = index == 8 && !isEn;
        SharedTemporalSection_EN.IsVisible = index == 8 && isEn;

        FlowCitySection_IT.IsVisible = index == 9 && !isEn;
        FlowCitySection_EN.IsVisible = index == 9 && isEn;

        AiExportSection_IT.IsVisible = index == 10 && !isEn;
        AiExportSection_EN.IsVisible = index == 10 && isEn;
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        var isEn = loc.CurrentLanguage == AppLanguage.English;

        Title = isEn ? "Guide & Concepts — Macondo" : "Macondo — Guida & Concetti";
        WindowTitleText.Text = Title;

        NavOverviewText.Text = isEn ? "Overview & Architecture" : "Panoramica & Architettura";
        NavLayoutsText.Text = isEn ? "Graph Layouts" : "Disposizioni del Grafo";
        NavLegendText.Text = isEn ? "Node & Color Legend" : "Legenda Nodi & Colori";
        NavTreemapText.Text = isEn ? "Treemap (Code Map)" : "Treemap (Mappa ad Albero)";
        NavFlowsText.Text = isEn ? "Screen & Control Flow" : "Screen & Control Flow";
        NavDsmHotspotsText.Text = isEn ? "DSM & Hotspots" : "DSM & Hotspots";
        NavBlastRadiusText.Text = isEn ? "Blast Radius (Risk)" : "Blast Radius (Rischio)";
        NavMartinDeadCodeText.Text = isEn ? "Martin & Dead Code" : "Martin & Dead Code";
        NavSharedTemporalText.Text = isEn ? "Global State & Temporal" : "Stato Globale & Temporal";
        NavFlowCityText.Text = isEn ? "Chord Flows & CodeCity" : "Flussi Chord & CodeCity";
        NavAiExportText.Text = isEn ? "AI & LLM Export" : "AI & Esportazione LLM";

        SetSelectedNav(_currentTabIndex);
    }

    private void ApplyTheme(ThemeDefinition theme)
    {
        if (Color.TryParse(theme.BackgroundPrimary, out var bgPrimary))
            Background = new SolidColorBrush(bgPrimary);

        if (Color.TryParse(theme.BackgroundSecondary, out var bgSecondary))
        {
            var secondaryBrush = new SolidColorBrush(bgSecondary);
            TopTitleBorder.Background = secondaryBrush;
            SidebarBorder.Background = secondaryBrush;
        }

        if (Color.TryParse(theme.BorderDefault, out var borderCol))
        {
            var borderBrush = new SolidColorBrush(borderCol);
            TopTitleBorder.BorderBrush = borderBrush;
            SidebarBorder.BorderBrush = borderBrush;
        }
    }
}
