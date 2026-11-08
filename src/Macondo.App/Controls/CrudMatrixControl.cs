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
using Macondo.Semantic.Analytics.DataMatrix;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

public sealed class CrudCellGridRow
{
    public required CrudCell Cell { get; init; }

    public string EntityName => Cell.EntityName;
    public string RoutineName => Cell.RoutineSymbol.Name;
    public string ContainerName => Cell.ContainerSymbol?.Name ?? "-";
    public string FilePath => Cell.RoutineSymbol.FilePath;
    public string CrudString => Cell.CrudString;
    public string OperationsText
    {
        get
        {
            var ops = new List<string>();
            if (Cell.HasCreate) ops.Add(LocalizationService.Instance["Crud_Op_Create"]);
            if (Cell.HasRead) ops.Add(LocalizationService.Instance["Crud_Op_Read"]);
            if (Cell.HasUpdate) ops.Add(LocalizationService.Instance["Crud_Op_Update"]);
            if (Cell.HasDelete) ops.Add(LocalizationService.Instance["Crud_Op_Delete"]);
            return string.Join(", ", ops);
        }
    }

    public string CrudBadgeBg => Cell.Operations switch
    {
        CrudOperation.All => "#EF4444",
        CrudOperation.Create => "#10B981",
        CrudOperation.Read => "#3B82F6",
        CrudOperation.Update => "#F59E0B",
        CrudOperation.Delete => "#DC2626",
        _ => "#8B5CF6"
    };
}

public sealed class CrudMatrixControl : UserControl
{
    public static readonly StyledProperty<CrudMatrixDataset?> DatasetProperty =
        AvaloniaProperty.Register<CrudMatrixControl, CrudMatrixDataset?>(nameof(Dataset));

    public CrudMatrixDataset? Dataset
    {
        get => GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    private readonly ObservableCollection<CrudCellGridRow> _rows = new();
    private List<CrudCell> _allCells = new();

    private readonly TextBlock _kpiTotalEntitiesValue;
    private readonly TextBlock _kpiTotalOperationsValue;
    private readonly TextBlock _kpiCreateOpsValue;
    private readonly TextBlock _kpiDeleteOpsValue;

    private readonly TextBox _searchBox;
    private readonly DataGrid _grid;
    private readonly TextBlock _statsText;

    public CrudMatrixControl()
    {
        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(16)
        };

        // 1. KPI Cards Header
        var kpiPanel = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };

        var card1 = CreateKpiCard(LocalizationService.Instance["Crud_Kpi_Entities"], out _kpiTotalEntitiesValue, "#3B82F6");
        var card2 = CreateKpiCard(LocalizationService.Instance["Crud_Kpi_TotalOps"], out _kpiTotalOperationsValue, "#8B5CF6");
        var card3 = CreateKpiCard(LocalizationService.Instance["Crud_Kpi_CreateOps"], out _kpiCreateOpsValue, "#10B981");
        var card4 = CreateKpiCard(LocalizationService.Instance["Crud_Kpi_DeleteOps"], out _kpiDeleteOpsValue, "#EF4444");

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
            Watermark = LocalizationService.Instance["Crud_SearchWatermark"],
            Width = 320,
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

        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Crud_Col_Entity"], Binding = new Binding(nameof(CrudCellGridRow.EntityName)), Width = new DataGridLength(180) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Crud_Col_Crud"], Binding = new Binding(nameof(CrudCellGridRow.CrudString)), Width = new DataGridLength(110) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Crud_Col_Operations"], Binding = new Binding(nameof(CrudCellGridRow.OperationsText)), Width = new DataGridLength(160) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Crud_Col_Routine"], Binding = new Binding(nameof(CrudCellGridRow.RoutineName)), Width = new DataGridLength(180) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Crud_Col_Container"], Binding = new Binding(nameof(CrudCellGridRow.ContainerName)), Width = new DataGridLength(160) });
        _grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Instance["Social_Col_File"], Binding = new Binding(nameof(CrudCellGridRow.FilePath)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        Grid.SetRow(_grid, 2);
        mainGrid.Children.Add(_grid);

        // 4. Footer stats
        _statsText = new TextBlock
        {
            Text = LocalizationService.Instance["Crud_Placeholder"],
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

            OnDatasetChanged(change.GetNewValue<CrudMatrixDataset?>());
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

    private void OnDatasetChanged(CrudMatrixDataset? dataset)
    {
        if (dataset == null)
        {
            _allCells.Clear();
            _rows.Clear();
            _kpiTotalEntitiesValue.Text = "0";
            _kpiTotalOperationsValue.Text = "0";
            _kpiCreateOpsValue.Text = "0";
            _kpiDeleteOpsValue.Text = "0";
            _statsText.Text = LocalizationService.Instance["Crud_Placeholder"];
            return;
        }

        _allCells = dataset.AllCells;
        _kpiTotalEntitiesValue.Text = dataset.TotalEntities.ToString();
        _kpiTotalOperationsValue.Text = dataset.TotalCrudOperations.ToString();
        _kpiCreateOpsValue.Text = _allCells.Count(c => c.HasCreate).ToString();
        _kpiDeleteOpsValue.Text = _allCells.Count(c => c.HasDelete).ToString();

        FilterData();
    }

    private void FilterData()
    {
        var filter = _searchBox.Text?.Trim() ?? string.Empty;

        var query = _allCells.AsEnumerable();
        if (!string.IsNullOrEmpty(filter))
            query = query.Where(c => c.EntityName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                     c.RoutineSymbol.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                     c.RoutineSymbol.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase));

        _rows.Clear();
        foreach (var c in query)
        {
            _rows.Add(new CrudCellGridRow { Cell = c });
        }

        _statsText.Text = LocalizationService.Instance.Get("Crud_StatsFormat", _rows.Count, _allCells.Count);
    }

    public void ApplyTheme(ThemeDefinition theme) { }
}
