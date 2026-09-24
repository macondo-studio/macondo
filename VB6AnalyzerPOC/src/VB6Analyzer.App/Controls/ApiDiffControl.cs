using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

public sealed class ApiDiffGridRow
{
    public required ApiDifferenceItem Item { get; init; }

    public string QualifiedName => Item.SymbolQualifiedName;
    public string KindText => Item.SymbolKind.ToString();
    public string ChangeKindText => Item.ChangeKind.ToString();
    public string ImpactText => Item.Impact switch
    {
        ApiChangeImpact.MajorBreaking => "Major (Breaking)",
        ApiChangeImpact.Minor => "Minor (Addition)",
        _ => "Patch (Internal)"
    };
    public string Description => Item.Description;

    public string ImpactBadgeBg => Item.Impact switch
    {
        ApiChangeImpact.MajorBreaking => "#EF4444",
        ApiChangeImpact.Minor => "#10B981",
        _ => "#3B82F6"
    };
}

public sealed class ApiDiffControl : UserControl
{
    public static readonly StyledProperty<ApiDiffReport?> ReportProperty =
        AvaloniaProperty.Register<ApiDiffControl, ApiDiffReport?>(nameof(Report));

    public ApiDiffReport? Report
    {
        get => GetValue(ReportProperty);
        set => SetValue(ReportProperty, value);
    }

    private readonly ObservableCollection<ApiDiffGridRow> _rows = new();
    private List<ApiDifferenceItem> _allDifferences = new();

    private readonly TextBlock _kpiRecommendedBumpValue;
    private readonly TextBlock _kpiBreakingChangesValue;
    private readonly TextBlock _kpiAdditionsValue;
    private readonly TextBlock _kpiTotalChangesValue;

    private readonly TextBox _searchBox;
    private readonly DataGrid _grid;
    private readonly TextBlock _statsText;

    public ApiDiffControl()
    {
        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(16)
        };

        // 1. KPI Cards Header
        var kpiPanel = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };

        var card1 = CreateKpiCard("Rilascio Consigliato (SemVer)", out _kpiRecommendedBumpValue, "#8B5CF6");
        var card2 = CreateKpiCard("Breaking Changes (Major)", out _kpiBreakingChangesValue, "#EF4444");
        var card3 = CreateKpiCard("Nuove API (Minor)", out _kpiAdditionsValue, "#10B981");
        var card4 = CreateKpiCard("Modifiche Totali API", out _kpiTotalChangesValue, "#3B82F6");

        kpiPanel.Children.Add(card1);
        kpiPanel.Children.Add(card2);
        kpiPanel.Children.Add(card3);
        kpiPanel.Children.Add(card4);
        Grid.SetRow(kpiPanel, 0);
        mainGrid.Children.Add(kpiPanel);

        // 2. Toolbar
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 12)
        };

        _searchBox = new TextBox
        {
            Watermark = "Cerca per simbolo o descrizione...",
            Width = 300,
            VerticalAlignment = VerticalAlignment.Center
        };
        _searchBox.KeyUp += (_, _) => FilterData();

        toolbar.Children.Add(_searchBox);
        Grid.SetRow(toolbar, 1);
        mainGrid.Children.Add(toolbar);

        // 3. DataGrid
        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = _rows,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };

        _grid.Columns.Add(new DataGridTextColumn { Header = "Simbolo Pubblico", Binding = new Binding(nameof(ApiDiffGridRow.QualifiedName)), Width = new DataGridLength(220) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Tipo Simbolo", Binding = new Binding(nameof(ApiDiffGridRow.KindText)), Width = new DataGridLength(110) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Tipo Modifica", Binding = new Binding(nameof(ApiDiffGridRow.ChangeKindText)), Width = new DataGridLength(140) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Impatto SemVer", Binding = new Binding(nameof(ApiDiffGridRow.ImpactText)), Width = new DataGridLength(140) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Dettaglio Compatibilità", Binding = new Binding(nameof(ApiDiffGridRow.Description)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        Grid.SetRow(_grid, 2);
        mainGrid.Children.Add(_grid);

        // 4. Footer stats
        _statsText = new TextBlock
        {
            Text = "Nessun dato caricato.",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(_statsText, 3);
        mainGrid.Children.Add(_statsText);

        Content = mainGrid;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ReportProperty)
        {
            OnReportChanged(change.GetNewValue<ApiDiffReport?>());
        }
    }

    private static Border CreateKpiCard(string title, out TextBlock valueBlock, string accentHex)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1A1A24")),
            BorderBrush = new SolidColorBrush(Color.Parse(accentHex)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(4)
        };

        var sp = new StackPanel { Spacing = 4 };
        var titleBlock = new TextBlock { Text = title, FontSize = 11, Foreground = Brushes.LightGray };
        valueBlock = new TextBlock { Text = "-", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brushes.White };

        sp.Children.Add(titleBlock);
        sp.Children.Add(valueBlock);
        border.Child = sp;
        return border;
    }

    private void OnReportChanged(ApiDiffReport? report)
    {
        if (report == null)
        {
            _allDifferences.Clear();
            _rows.Clear();
            _kpiRecommendedBumpValue.Text = "-";
            _kpiBreakingChangesValue.Text = "0";
            _kpiAdditionsValue.Text = "0";
            _kpiTotalChangesValue.Text = "0";
            _statsText.Text = "Nessun dato.";
            return;
        }

        _allDifferences = report.Differences;
        _kpiRecommendedBumpValue.Text = report.RecommendedBump.ToString();
        _kpiBreakingChangesValue.Text = report.BreakingChangesCount.ToString();
        _kpiAdditionsValue.Text = report.AdditionsCount.ToString();
        _kpiTotalChangesValue.Text = report.TotalChanges.ToString();

        FilterData();
    }

    private void FilterData()
    {
        var filter = _searchBox.Text?.Trim() ?? string.Empty;

        var query = _allDifferences.AsEnumerable();
        if (!string.IsNullOrEmpty(filter))
            query = query.Where(d => d.SymbolQualifiedName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                     d.Description.Contains(filter, StringComparison.OrdinalIgnoreCase));

        _rows.Clear();
        foreach (var d in query)
        {
            _rows.Add(new ApiDiffGridRow { Item = d });
        }

        _statsText.Text = $"Modifiche visualizzate: {_rows.Count} / {_allDifferences.Count}";
    }

    public void ApplyTheme(ThemeDefinition theme) { }
}
