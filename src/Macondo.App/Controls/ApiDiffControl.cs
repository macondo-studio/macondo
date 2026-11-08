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
    public string ChangeKindText => Item.ChangeKind switch
    {
        ApiChangeKind.AddedSymbol => LocalizationService.Instance["ApiDiff_Change_AddedSymbol"],
        ApiChangeKind.RemovedSymbol => LocalizationService.Instance["ApiDiff_Change_RemovedSymbol"],
        ApiChangeKind.ChangedAccessibility => LocalizationService.Instance["ApiDiff_Change_ChangedAccessibility"],
        ApiChangeKind.ChangedParameters => LocalizationService.Instance["ApiDiff_Change_ChangedParameters"],
        ApiChangeKind.ChangedReturnType => LocalizationService.Instance["ApiDiff_Change_ChangedReturnType"],
        ApiChangeKind.ModifiedBodyOnly => LocalizationService.Instance["ApiDiff_Change_ModifiedBodyOnly"],
        _ => Item.ChangeKind.ToString()
    };
    public string ImpactText => Item.Impact switch
    {
        ApiChangeImpact.MajorBreaking => LocalizationService.Instance["ApiDiff_Impact_Major"],
        ApiChangeImpact.Minor => LocalizationService.Instance["ApiDiff_Impact_Minor"],
        _ => LocalizationService.Instance["ApiDiff_Impact_Patch"]
    };
    public string Description => Item.ChangeKind switch
    {
        ApiChangeKind.RemovedSymbol => LocalizationService.Instance.Get("ApiDiff_Desc_Removed", Item.SymbolQualifiedName),
        ApiChangeKind.ChangedParameters => LocalizationService.Instance.Get("ApiDiff_Desc_Parameters", Item.SymbolQualifiedName),
        ApiChangeKind.ChangedAccessibility => LocalizationService.Instance.Get("ApiDiff_Desc_Accessibility", Item.SymbolQualifiedName),
        ApiChangeKind.ChangedReturnType => LocalizationService.Instance.Get("ApiDiff_Desc_ReturnType", Item.SymbolQualifiedName),
        ApiChangeKind.AddedSymbol => LocalizationService.Instance.Get("ApiDiff_Desc_Added", Item.SymbolQualifiedName),
        ApiChangeKind.ModifiedBodyOnly => LocalizationService.Instance.Get("ApiDiff_Desc_Body", Item.SymbolQualifiedName),
        _ => LocalizationService.Instance.Get("ApiDiff_Desc_Generic", Item.SymbolQualifiedName)
    };

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

    public event EventHandler? SetCurrentAsBaselineRequested;
    public event EventHandler? LoadBaselineRequested;
    public event EventHandler? ClearBaselineRequested;

    private readonly ObservableCollection<ApiDiffGridRow> _rows = new();
    private List<ApiDifferenceItem> _allDifferences = new();

    private string? _baselineName;
    private int? _baselineSymbolsCount;

    private readonly TextBlock _kpiRecommendedBumpValue;
    private readonly TextBlock _kpiBreakingChangesValue;
    private readonly TextBlock _kpiAdditionsValue;
    private readonly TextBlock _kpiTotalChangesValue;

    private readonly Border _baselineBadgeBorder;
    private readonly TextBlock _baselineBadgeText;
    private readonly Button _btnSetBaseline;
    private readonly Button _btnLoadBaseline;
    private readonly Button _btnClearBaseline;
    private readonly TextBox _searchBox;

    private readonly Border _emptyStateContainer;
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

        var card1 = CreateKpiCard(LocalizationService.Instance["ApiDiff_Kpi_RecommendedBump"], out _kpiRecommendedBumpValue, "#8B5CF6");
        var card2 = CreateKpiCard(LocalizationService.Instance["ApiDiff_Kpi_BreakingChanges"], out _kpiBreakingChangesValue, "#EF4444");
        var card3 = CreateKpiCard(LocalizationService.Instance["ApiDiff_Kpi_Additions"], out _kpiAdditionsValue, "#10B981");
        var card4 = CreateKpiCard(LocalizationService.Instance["ApiDiff_Kpi_TotalChanges"], out _kpiTotalChangesValue, "#3B82F6");

        kpiPanel.Children.Add(card1);
        kpiPanel.Children.Add(card2);
        kpiPanel.Children.Add(card3);
        kpiPanel.Children.Add(card4);
        Grid.SetRow(kpiPanel, 0);
        mainGrid.Children.Add(kpiPanel);

        // 2. Toolbar
        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };

        // Baseline Status Badge
        _baselineBadgeBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#262638")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3E3E56")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 10, 6),
            VerticalAlignment = VerticalAlignment.Center
        };
        _baselineBadgeText = new TextBlock
        {
            Text = LocalizationService.Instance["ApiDiff_Status_NoBaseline"],
            FontSize = 12,
            Foreground = Brushes.LightGray,
            VerticalAlignment = VerticalAlignment.Center
        };
        _baselineBadgeBorder.Child = _baselineBadgeText;
        toolbar.Children.Add(_baselineBadgeBorder);

        // Action Buttons
        _btnSetBaseline = new Button
        {
            Content = LocalizationService.Instance["ApiDiff_Btn_SetCurrentBaseline"],
            Padding = new Thickness(12, 6),
            Margin = new Thickness(0, 0, 8, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center
        };
        _btnSetBaseline.Click += (_, _) => SetCurrentAsBaselineRequested?.Invoke(this, EventArgs.Empty);
        toolbar.Children.Add(_btnSetBaseline);

        _btnLoadBaseline = new Button
        {
            Content = LocalizationService.Instance["ApiDiff_Btn_LoadBaseline"],
            Padding = new Thickness(12, 6),
            Margin = new Thickness(0, 0, 8, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center
        };
        _btnLoadBaseline.Click += (_, _) => LoadBaselineRequested?.Invoke(this, EventArgs.Empty);
        toolbar.Children.Add(_btnLoadBaseline);

        _btnClearBaseline = new Button
        {
            Content = LocalizationService.Instance["ApiDiff_Btn_ClearBaseline"],
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 0, 12, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false
        };
        _btnClearBaseline.Click += (_, _) => ClearBaselineRequested?.Invoke(this, EventArgs.Empty);
        toolbar.Children.Add(_btnClearBaseline);

        _searchBox = new TextBox
        {
            Watermark = LocalizationService.Instance["ApiDiff_SearchWatermark"],
            Width = 260,
            Margin = new Thickness(0, 0, 0, 6),
            VerticalAlignment = VerticalAlignment.Center
        };
        _searchBox.KeyUp += (_, _) => FilterData();
        toolbar.Children.Add(_searchBox);

        Grid.SetRow(toolbar, 1);
        mainGrid.Children.Add(toolbar);

        // 3. Main Area: DataGrid + Empty State
        var contentHost = new Grid();

        // 3a. DataGrid
        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = _rows,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            IsVisible = false
        };

        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["ApiDiff_Col_Symbol"], Binding = new Binding(nameof(ApiDiffGridRow.QualifiedName)), Width = new DataGridLength(240) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["ApiDiff_Col_Kind"], Binding = new Binding(nameof(ApiDiffGridRow.KindText)), Width = new DataGridLength(110) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["ApiDiff_Col_ChangeKind"], Binding = new Binding(nameof(ApiDiffGridRow.ChangeKindText)), Width = new DataGridLength(150) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["ApiDiff_Col_Impact"], Binding = new Binding(nameof(ApiDiffGridRow.ImpactText)), Width = new DataGridLength(140) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["ApiDiff_Col_Description"], Binding = new Binding(nameof(ApiDiffGridRow.Description)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        contentHost.Children.Add(_grid);

        // 3b. Empty State Overlay
        _emptyStateContainer = CreateEmptyStatePanel();
        contentHost.Children.Add(_emptyStateContainer);

        Grid.SetRow(contentHost, 2);
        mainGrid.Children.Add(contentHost);

        // 4. Footer stats
        _statsText = new TextBlock
        {
            Text = LocalizationService.Instance["ApiDiff_Placeholder"],
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0),
            IsVisible = false
        };
        Grid.SetRow(_statsText, 3);
        mainGrid.Children.Add(_statsText);

        Content = mainGrid;
    }

    private Border CreateEmptyStatePanel()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#14141E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#26263A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(32),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 640
        };

        var sp = new StackPanel
        {
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var title = new TextBlock
        {
            Text = LocalizationService.Instance["ApiDiff_Empty_Title"],
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var desc = new TextBlock
        {
            Text = LocalizationService.Instance["ApiDiff_Empty_Desc"],
            FontSize = 13,
            Foreground = Brushes.LightGray,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            LineHeight = 20
        };

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var btnSet = new Button
        {
            Content = LocalizationService.Instance["ApiDiff_Btn_SetCurrentBaseline"],
            Padding = new Thickness(16, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontWeight = FontWeight.SemiBold
        };
        btnSet.Click += (_, _) => SetCurrentAsBaselineRequested?.Invoke(this, EventArgs.Empty);

        var btnLoad = new Button
        {
            Content = LocalizationService.Instance["ApiDiff_Btn_LoadBaseline"],
            Padding = new Thickness(16, 8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        btnLoad.Click += (_, _) => LoadBaselineRequested?.Invoke(this, EventArgs.Empty);

        btnPanel.Children.Add(btnSet);
        btnPanel.Children.Add(btnLoad);

        sp.Children.Add(title);
        sp.Children.Add(desc);
        sp.Children.Add(btnPanel);

        border.Child = sp;
        return border;
    }

    public void SetBaselineInfo(string? baselineName, int? symbolsCount)
    {
        _baselineName = baselineName;
        _baselineSymbolsCount = symbolsCount;

        if (string.IsNullOrEmpty(baselineName))
        {
            _baselineBadgeText.Text = LocalizationService.Instance["ApiDiff_Status_NoBaseline"];
            _baselineBadgeBorder.Background = new SolidColorBrush(Color.Parse("#262638"));
            _baselineBadgeBorder.BorderBrush = new SolidColorBrush(Color.Parse("#3E3E56"));
            _btnClearBaseline.IsVisible = false;

            _emptyStateContainer.IsVisible = true;
            _grid.IsVisible = false;
            _statsText.IsVisible = false;

            _kpiRecommendedBumpValue.Text = "-";
            _kpiBreakingChangesValue.Text = "0";
            _kpiAdditionsValue.Text = "0";
            _kpiTotalChangesValue.Text = "0";
        }
        else
        {
            _baselineBadgeText.Text = LocalizationService.Instance.Get("ApiDiff_Status_Baseline", baselineName, symbolsCount ?? 0);
            _baselineBadgeBorder.Background = new SolidColorBrush(Color.Parse("#1E293B"));
            _baselineBadgeBorder.BorderBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
            _btnClearBaseline.IsVisible = true;

            _emptyStateContainer.IsVisible = false;
            _grid.IsVisible = true;
            _statsText.IsVisible = true;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ReportProperty)

            OnReportChanged(change.GetNewValue<ApiDiffReport?>());
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
        if (string.IsNullOrEmpty(_baselineName) || report == null)
        {
            _allDifferences.Clear();
            _rows.Clear();
            _kpiRecommendedBumpValue.Text = "-";
            _kpiBreakingChangesValue.Text = "0";
            _kpiAdditionsValue.Text = "0";
            _kpiTotalChangesValue.Text = "0";
            _statsText.Text = LocalizationService.Instance["ApiDiff_Placeholder"];

            if (string.IsNullOrEmpty(_baselineName))
            {
                _emptyStateContainer.IsVisible = true;
                _grid.IsVisible = false;
                _statsText.IsVisible = false;
            }
            return;
        }

        _emptyStateContainer.IsVisible = false;
        _grid.IsVisible = true;
        _statsText.IsVisible = true;

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

        _statsText.Text = LocalizationService.Instance.Get("ApiDiff_StatsFormat", _rows.Count, _allDifferences.Count);
    }

    public void ApplyTheme(ThemeDefinition theme) { }
}
