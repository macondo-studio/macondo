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
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

public sealed class CohesionGridRow
{
    public required ContainerCohesionReport Item { get; init; }

    public string Name => Item.Name;
    public string QualifiedName => Item.QualifiedName;
    public string FilePath => Item.FilePath;
    public int Lcom4 => Item.Lcom4;
    public double Tcc => Math.Round(Item.TightClassCohesion * 100.0, 1);
    public double Lcc => Math.Round(Item.LooseClassCohesion * 100.0, 1);
    public int MethodCount => Item.MethodCount;
    public int FieldCount => Item.FieldCount;
    public int Wmc => Item.WeightedMethodsCount;
    public int Cbo => Item.CouplingBetweenObjects;
    public int Rfc => Item.ResponseForClass;
    public int ClusterCount => Item.Clusters.Count;

    public string StatusBadgeText => Item.IsRefactoringCandidate
        ? LocalizationService.Instance["Cohesion_CandidateBadge"]
        : LocalizationService.Instance["Cohesion_CohesiveBadge"];
    public string StatusBadgeBg => Item.IsRefactoringCandidate ? "#B81D2D" : "#2E7D32";
}

public sealed class CohesionControl : UserControl
{
    public static readonly StyledProperty<CohesionDataset?> DatasetProperty =
        AvaloniaProperty.Register<CohesionControl, CohesionDataset?>(nameof(Dataset));

    public CohesionDataset? Dataset
    {
        get => GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    private readonly ObservableCollection<CohesionGridRow> _rows = new();
    private List<ContainerCohesionReport> _allItems = new();

    private readonly TextBlock _kpiTotalContainersValue;
    private readonly TextBlock _kpiAvgLcomValue;
    private readonly TextBlock _kpiAvgTccValue;
    private readonly TextBlock _kpiCandidatesValue;

    private readonly TextBox _searchBox;
    private readonly CheckBox _candidatesOnlyCheck;
    private readonly DataGrid _grid;
    private readonly TextBlock _statsText;

    public CohesionControl()
    {
        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(16)
        };

        // 1. KPI Cards Header
        var kpiPanel = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };

        var card1 = CreateKpiCard(LocalizationService.Instance["Cohesion_Kpi_Containers"], out _kpiTotalContainersValue, "#3B82F6");
        var card2 = CreateKpiCard(LocalizationService.Instance["Cohesion_Kpi_AvgLcom"], out _kpiAvgLcomValue, "#10B981");
        var card3 = CreateKpiCard(LocalizationService.Instance["Cohesion_Kpi_AvgTcc"], out _kpiAvgTccValue, "#8B5CF6");
        var card4 = CreateKpiCard(LocalizationService.Instance["Cohesion_Kpi_Candidates"], out _kpiCandidatesValue, "#EF4444");

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
            Watermark = LocalizationService.Instance["Cohesion_SearchWatermark"],
            Width = 280,
            VerticalAlignment = VerticalAlignment.Center
        };
        _searchBox.KeyUp += (_, _) => FilterData();

        _candidatesOnlyCheck = new CheckBox
        {
            Content = LocalizationService.Instance["Cohesion_CandidatesOnly"],
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        _candidatesOnlyCheck.IsCheckedChanged += (_, _) => FilterData();

        toolbar.Children.Add(_searchBox);
        toolbar.Children.Add(_candidatesOnlyCheck);
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

        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Cohesion_Col_Name"], Binding = new Binding(nameof(CohesionGridRow.Name)), Width = new DataGridLength(200) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Cohesion_Col_Lcom4"], Binding = new Binding(nameof(CohesionGridRow.Lcom4)), Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Cohesion_Col_Tcc"], Binding = new Binding(nameof(CohesionGridRow.Tcc)), Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Cohesion_Col_Lcc"], Binding = new Binding(nameof(CohesionGridRow.Lcc)), Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Cohesion_Col_Methods"], Binding = new Binding(nameof(CohesionGridRow.MethodCount)), Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Cohesion_Col_Fields"], Binding = new Binding(nameof(CohesionGridRow.FieldCount)), Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Cohesion_Col_Wmc"], Binding = new Binding(nameof(CohesionGridRow.Wmc)), Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Cohesion_Col_Cbo"], Binding = new Binding(nameof(CohesionGridRow.Cbo)), Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Cohesion_Col_Rfc"], Binding = new Binding(nameof(CohesionGridRow.Rfc)), Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Social_Col_File"], Binding = new Binding(nameof(CohesionGridRow.FilePath)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        Grid.SetRow(_grid, 2);
        mainGrid.Children.Add(_grid);

        // 4. Footer stats
        _statsText = new TextBlock
        {
            Text = LocalizationService.Instance["Cohesion_Placeholder"],
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

            OnDatasetChanged(change.GetNewValue<CohesionDataset?>());
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

    private void OnDatasetChanged(CohesionDataset? dataset)
    {
        if (dataset == null)
        {
            _allItems.Clear();
            _rows.Clear();
            _kpiTotalContainersValue.Text = "0";
            _kpiAvgLcomValue.Text = "0.0";
            _kpiAvgTccValue.Text = "0.0";
            _kpiCandidatesValue.Text = "0";
            _statsText.Text = LocalizationService.Instance["Cohesion_Placeholder"];
            return;
        }

        _allItems = dataset.Items;
        _kpiTotalContainersValue.Text = _allItems.Count.ToString();
        _kpiAvgLcomValue.Text = dataset.AverageLcom4.ToString("F1");
        _kpiAvgTccValue.Text = (dataset.AverageTcc * 100.0).ToString("F1");
        _kpiCandidatesValue.Text = dataset.RefactoringCandidateCount.ToString();

        FilterData();
    }

    private void FilterData()
    {
        var filter = _searchBox.Text?.Trim() ?? string.Empty;
        var onlyCandidates = _candidatesOnlyCheck.IsChecked == true;

        var query = _allItems.AsEnumerable();
        if (onlyCandidates)
            query = query.Where(i => i.IsRefactoringCandidate);

        if (!string.IsNullOrEmpty(filter))
            query = query.Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || i.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase));

        _rows.Clear();
        foreach (var item in query)
        {
            _rows.Add(new CohesionGridRow { Item = item });
        }

        _statsText.Text = LocalizationService.Instance.Get("Cohesion_StatsFormat", _rows.Count, _allItems.Count);
    }

    public void ApplyTheme(ThemeDefinition theme) { }
}
