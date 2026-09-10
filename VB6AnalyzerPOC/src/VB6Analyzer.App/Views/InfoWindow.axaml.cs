using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Macondo.App.Services;

namespace Macondo.App.Views;

public partial class InfoWindow : Window
{
    public InfoWindow()
    {
        InitializeComponent();

        ApplyTheme(ThemeService.Instance.CurrentDefinition);
        UpdateLocalizedTexts();

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

    private void OnNavOverviewClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(0);
    private void OnNavLayoutsClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(1);
    private void OnNavTreemapClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(2);
    private void OnNavFlowsClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(3);
    private void OnNavDsmHotspotsClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(4);
    private void OnNavBlastRadiusClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(5);
    private void OnNavAiExportClicked(object? sender, RoutedEventArgs e) => SetSelectedNav(6);

    private void SetSelectedNav(int index)
    {
        NavOverviewButton.Classes.Set("selected", index == 0);
        NavLayoutsButton.Classes.Set("selected", index == 1);
        NavTreemapButton.Classes.Set("selected", index == 2);
        NavFlowsButton.Classes.Set("selected", index == 3);
        NavDsmHotspotsButton.Classes.Set("selected", index == 4);
        NavBlastRadiusButton.Classes.Set("selected", index == 5);
        NavAiExportButton.Classes.Set("selected", index == 6);

        OverviewSection.IsVisible = index == 0;
        LayoutsSection.IsVisible = index == 1;
        TreemapSection.IsVisible = index == 2;
        FlowsSection.IsVisible = index == 3;
        DsmHotspotsSection.IsVisible = index == 4;
        BlastRadiusSection.IsVisible = index == 5;
        AiExportSection.IsVisible = index == 6;
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        var isEn = loc.CurrentLanguage == AppLanguage.English;

        Title = isEn ? "Guide & Concepts — Macondo" : "Macondo — Guida & Concetti";
        WindowTitleText.Text = Title;

        NavOverviewText.Text = isEn ? "Overview & Architecture" : "Panoramica & Architettura";
        NavLayoutsText.Text = isEn ? "Graph Layouts" : "Disposizioni del Grafo";
        NavTreemapText.Text = isEn ? "Treemap (Code Map)" : "Treemap (Mappa ad Albero)";
        NavFlowsText.Text = isEn ? "Screen & Control Flow" : "Screen & Control Flow";
        NavDsmHotspotsText.Text = isEn ? "DSM & Hotspots" : "DSM & Hotspots";
        NavBlastRadiusText.Text = isEn ? "Blast Radius (Risk)" : "Blast Radius (Rischio)";
        NavAiExportText.Text = isEn ? "AI & LLM Export" : "AI & Esportazione LLM";
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
