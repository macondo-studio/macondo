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
using Macondo.Semantic.Analytics.Maintainability;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

public sealed class MaintainabilityGridRow
{
    public required MaintainabilityItem Item { get; init; }

    public string Name => Item.Name;
    public string QualifiedName => Item.QualifiedName;
    public string FilePath => Item.FilePath;
    public double MaintainabilityIndex => Item.MaintainabilityIndex;
    public int LinesOfCode => Item.LinesOfCode;
    public int CyclomaticComplexity => Item.CyclomaticComplexity;
    public double Volume => Math.Round(Item.Halstead.Volume, 0);
    public double Difficulty => Math.Round(Item.Halstead.Difficulty, 1);
    public double Effort => Math.Round(Item.Halstead.Effort, 0);
    public double EstimatedBugs => Math.Round(Item.Halstead.EstimatedBugs, 2);

    public string RatingText => Item.Rating switch
    {
        MaintainabilityRating.High => LocalizationService.Instance["Maintainability_Rating_High"],
        MaintainabilityRating.Moderate => LocalizationService.Instance["Maintainability_Rating_Moderate"],
        _ => LocalizationService.Instance["Maintainability_Rating_Low"]
    };

    public string RatingBadgeBg => Item.Rating switch
    {
        MaintainabilityRating.High => "#2E7D32",
        MaintainabilityRating.Moderate => "#D97706",
        _ => "#B81D2D"
    };
}

public sealed class MaintainabilityControl : UserControl
{
    public static readonly StyledProperty<MaintainabilityDataset?> DatasetProperty =
        AvaloniaProperty.Register<MaintainabilityControl, MaintainabilityDataset?>(nameof(Dataset));

    public MaintainabilityDataset? Dataset
    {
        get => GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    private readonly ObservableCollection<MaintainabilityGridRow> _rows = new();
    private List<MaintainabilityItem> _allItems = new();

    private readonly TextBlock _kpiAvgMiValue;
    private readonly TextBlock _kpiHighMiValue;
    private readonly TextBlock _kpiModMiValue;
    private readonly TextBlock _kpiLowMiValue;

    private readonly TextBox _searchBox;
    private readonly DataGrid _grid;
    private readonly TextBlock _statsText;

    public MaintainabilityControl()
    {
        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(16)
        };

        // 1. KPI Cards Header
        var kpiPanel = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };

        var card1 = CreateKpiCard(LocalizationService.Instance["Maintainability_Kpi_AvgMi"], out _kpiAvgMiValue, "#3B82F6");
        var card2 = CreateKpiCard(LocalizationService.Instance["Maintainability_Kpi_HighQuality"], out _kpiHighMiValue, "#10B981");
        var card3 = CreateKpiCard(LocalizationService.Instance["Maintainability_Kpi_Moderate"], out _kpiModMiValue, "#F59E0B");
        var card4 = CreateKpiCard(LocalizationService.Instance["Maintainability_Kpi_Critical"], out _kpiLowMiValue, "#EF4444");

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
            Watermark = LocalizationService.Instance["Maintainability_SearchWatermark"],
            Width = 280,
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

        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Maintainability_Col_Name"], Binding = new Binding(nameof(MaintainabilityGridRow.Name)), Width = new DataGridLength(200) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Maintainability_Col_Mi"], Binding = new Binding(nameof(MaintainabilityGridRow.MaintainabilityIndex)), Width = new DataGridLength(90) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Maintainability_Col_Rating"], Binding = new Binding(nameof(MaintainabilityGridRow.RatingText)), Width = new DataGridLength(150) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Maintainability_Col_Loc"], Binding = new Binding(nameof(MaintainabilityGridRow.LinesOfCode)), Width = new DataGridLength(70) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Maintainability_Col_Cc"], Binding = new Binding(nameof(MaintainabilityGridRow.CyclomaticComplexity)), Width = new DataGridLength(70) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Maintainability_Col_Volume"], Binding = new Binding(nameof(MaintainabilityGridRow.Volume)), Width = new DataGridLength(110) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Maintainability_Col_Difficulty"], Binding = new Binding(nameof(MaintainabilityGridRow.Difficulty)), Width = new DataGridLength(90) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Maintainability_Col_Effort"], Binding = new Binding(nameof(MaintainabilityGridRow.Effort)), Width = new DataGridLength(90) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Maintainability_Col_Bugs"], Binding = new Binding(nameof(MaintainabilityGridRow.EstimatedBugs)), Width = new DataGridLength(90) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Social_Col_File"], Binding = new Binding(nameof(MaintainabilityGridRow.FilePath)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        Grid.SetRow(_grid, 2);
        mainGrid.Children.Add(_grid);

        // 4. Footer stats
        _statsText = new TextBlock
        {
            Text = LocalizationService.Instance["Maintainability_Placeholder"],
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
        if (change.Property == DatasetProperty)

            OnDatasetChanged(change.GetNewValue<MaintainabilityDataset?>());
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
        valueBlock = new TextBlock { Text = "0", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brushes.White };

        sp.Children.Add(titleBlock);
        sp.Children.Add(valueBlock);
        border.Child = sp;
        return border;
    }

    private void OnDatasetChanged(MaintainabilityDataset? dataset)
    {
        if (dataset == null)
        {
            _allItems.Clear();
            _rows.Clear();
            _kpiAvgMiValue.Text = "0.0";
            _kpiHighMiValue.Text = "0";
            _kpiModMiValue.Text = "0";
            _kpiLowMiValue.Text = "0";
            _statsText.Text = LocalizationService.Instance["Maintainability_Placeholder"];
            return;
        }

        _allItems = dataset.Items;
        _kpiAvgMiValue.Text = dataset.AverageMaintainabilityIndex.ToString("F1");
        _kpiHighMiValue.Text = dataset.HighCount.ToString();
        _kpiModMiValue.Text = dataset.ModerateCount.ToString();
        _kpiLowMiValue.Text = dataset.LowCount.ToString();

        FilterData();
    }

    private void FilterData()
    {
        var filter = _searchBox.Text?.Trim() ?? string.Empty;

        var query = _allItems.AsEnumerable();
        if (!string.IsNullOrEmpty(filter))
            query = query.Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || i.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase));

        _rows.Clear();
        foreach (var item in query)
        {
            _rows.Add(new MaintainabilityGridRow { Item = item });
        }

        _statsText.Text = LocalizationService.Instance.Get("Maintainability_StatsFormat", _rows.Count, _allItems.Count);
    }

    public void ApplyTheme(ThemeDefinition theme) { }
}
