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
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

/// <summary>
/// Riga visualizzata nel DataGrid dell'analisi Dead Code / Orfani.
/// </summary>
public sealed class DeadCodeGridRow
{
    public required DeadCodeItem Item { get; init; }

    public string Name => Item.Name;
    public string QualifiedName => Item.QualifiedName;
    public string ContainerName => Item.ContainerName;
    public string FilePath => Item.FilePath;
    public int LineStart => Item.LineStart;
    public int EstimatedLoc => Item.EstimatedLoc;
    public string Reason => Item.Category switch
    {
        DeadCodeCategory.UnreachableRoutine => LocalizationService.Instance["DeadCode_Reason_UnreachableRoutine"],
        DeadCodeCategory.UnusedFieldOrConstant => LocalizationService.Instance["DeadCode_Reason_UnusedFieldOrConstant"],
        DeadCodeCategory.OrphanContainer => LocalizationService.Instance["DeadCode_Reason_OrphanContainer"],
        DeadCodeCategory.UnusedType => LocalizationService.Instance["DeadCode_Reason_UnusedType"],
        _ => LocalizationService.Instance["DeadCode_Reason_UnreachableSymbol"]
    };
    public string Kind => Item.Kind.ToString();

    public string CategoryBadgeText => Item.Category switch
    {
        DeadCodeCategory.UnreachableRoutine => LocalizationService.Instance["DeadCode_Badge_Routine"],
        DeadCodeCategory.UnusedFieldOrConstant => LocalizationService.Instance["DeadCode_Badge_Field"],
        DeadCodeCategory.OrphanContainer => LocalizationService.Instance["DeadCode_Badge_Container"],
        DeadCodeCategory.UnusedType => LocalizationService.Instance["DeadCode_Badge_Type"],
        _ => LocalizationService.Instance["DeadCode_Badge_Dead"]
    };

    public string CategoryBadgeBackground => Item.Category switch
    {
        DeadCodeCategory.UnreachableRoutine => "#B81D2D",
        DeadCodeCategory.UnusedFieldOrConstant => "#C86400",
        DeadCodeCategory.OrphanContainer => "#8E24AA",
        DeadCodeCategory.UnusedType => "#1E88E5",
        _ => "#555555"
    };

    public string KindBadge => Item.Kind switch
    {
        SymbolKind.Method => "M",
        SymbolKind.Property => "P",
        SymbolKind.Field => "F",
        SymbolKind.Constant => "C",
        SymbolKind.Class => "CLS",
        SymbolKind.Module => "MOD",
        SymbolKind.Form => "FRM",
        SymbolKind.UserControl => "CTL",
        SymbolKind.Interface => "INT",
        SymbolKind.Type => "UDT",
        SymbolKind.Enum => "ENM",
        _ => "SYM"
    };

    public string KindBadgeBackground => Item.Kind switch
    {
        SymbolKind.Method or SymbolKind.Property => "#4A5568",
        SymbolKind.Field => "#2B6CB0",
        SymbolKind.Constant => "#975A16",
        SymbolKind.Class => "#22543D",
        SymbolKind.Module => "#553C9A",
        SymbolKind.Form => "#2C5282",
        SymbolKind.UserControl => "#7B341E",
        SymbolKind.Interface => "#234E52",
        _ => "#4A5568"
    };
}

/// <summary>
/// Controllo visuale completo per la consultazione e il filtraggio del Dead Code e componenti orfani.
/// Include un cruscotto KPI superiore, filtri rapidi di ricerca/categoria e DataGrid interattivo.
/// </summary>
public sealed class DeadCodeControl : UserControl
{
    public static readonly StyledProperty<DeadCodeDataset?> DatasetProperty =
        AvaloniaProperty.Register<DeadCodeControl, DeadCodeDataset?>(nameof(Dataset));

    public DeadCodeDataset? Dataset
    {
        get => GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    public event EventHandler<DeadCodeItem?>? ItemSelected;
    public event EventHandler<DeadCodeItem?>? ItemDoubleTapped;

    private readonly ObservableCollection<DeadCodeGridRow> _rows = new();
    private List<DeadCodeItem> _allItems = new();

    private readonly TextBlock _kpiTotalDeadTitle;
    private readonly TextBlock _kpiTotalDeadText;
    private readonly TextBlock _kpiRatioTitle;
    private readonly TextBlock _kpiRatioText;
    private readonly TextBlock _kpiRoutinesTitle;
    private readonly TextBlock _kpiRoutinesText;
    private readonly TextBlock _kpiFieldsTitle;
    private readonly TextBlock _kpiFieldsText;
    private readonly TextBlock _kpiContainersTitle;
    private readonly TextBlock _kpiContainersText;
    private readonly TextBlock _kpiLocTitle;
    private readonly TextBlock _kpiLocText;
    private readonly TextBox _searchBox;
    private readonly ComboBox _categoryFilter;
    private readonly DataGrid _grid;

    public DeadCodeControl()
    {
        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Margin = new Thickness(14)
        };

        // 1. Cruscotto KPI Superiore
        var kpiPanel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*"),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var card1 = CreateKpiCard("Dead Symbols", "0", "#FF5555", out _kpiTotalDeadTitle, out _kpiTotalDeadText);
        var card2 = CreateKpiCard("Dead Code Ratio", "0%", "#FFAA00", out _kpiRatioTitle, out _kpiRatioText);
        var card3 = CreateKpiCard("Unreachable Routines", "0", "#E87A90", out _kpiRoutinesTitle, out _kpiRoutinesText);
        var card4 = CreateKpiCard("Unused Fields/Const", "0", "#FFD54F", out _kpiFieldsTitle, out _kpiFieldsText);
        var card5 = CreateKpiCard("Orphan Containers", "0", "#C084F0", out _kpiContainersTitle, out _kpiContainersText);
        var card6 = CreateKpiCard("Total Dead LOC", "0", "#4AE8D0", out _kpiLocTitle, out _kpiLocText);

        Grid.SetColumn(card1, 0);
        Grid.SetColumn(card2, 1);
        Grid.SetColumn(card3, 2);
        Grid.SetColumn(card4, 3);
        Grid.SetColumn(card5, 4);
        Grid.SetColumn(card6, 5);

        kpiPanel.Children.Add(card1);
        kpiPanel.Children.Add(card2);
        kpiPanel.Children.Add(card3);
        kpiPanel.Children.Add(card4);
        kpiPanel.Children.Add(card5);
        kpiPanel.Children.Add(card6);

        Grid.SetRow(kpiPanel, 0);
        rootGrid.Children.Add(kpiPanel);

        // 2. Barra Filtri e Ricerca
        var filterBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 10)
        };

        _searchBox = new TextBox
        {
            Watermark = "Cerca per nome, container o percorso...",
            FontSize = 12,
            Padding = new Thickness(8, 6),
            Margin = new Thickness(0, 0, 10, 0)
        };
        _searchBox.PropertyChanged += (s, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                ApplyFilter();
            }
        };
        Grid.SetColumn(_searchBox, 0);

        _categoryFilter = new ComboBox
        {
            SelectedIndex = 0,
            FontSize = 12,
            Width = 180
        };
        _categoryFilter.SelectionChanged += (s, e) => ApplyFilter();
        Grid.SetColumn(_categoryFilter, 1);

        filterBar.Children.Add(_searchBox);
        filterBar.Children.Add(_categoryFilter);

        Grid.SetRow(filterBar, 1);
        rootGrid.Children.Add(filterBar);

        // 3. DataGrid
        _grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Single
        };

        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Tipo",
            Width = new DataGridLength(60, DataGridLengthUnitType.Pixel),
            CellTemplate = new FuncDataTemplate<DeadCodeGridRow>((row, _) =>
            {
                if (row == null) return new Panel();
                var border = new Border
                {
                    Background = Brush.Parse(row.KindBadgeBackground),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = row.KindBadge,
                        FontSize = 9,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.White
                    }
                };
                return border;
            })
        });

        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Categoria",
            Width = new DataGridLength(130, DataGridLengthUnitType.Pixel),
            CellTemplate = new FuncDataTemplate<DeadCodeGridRow>((row, _) =>
            {
                if (row == null) return new Panel();
                var border = new Border
                {
                    Background = Brush.Parse(row.CategoryBadgeBackground),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = row.CategoryBadgeText,
                        FontSize = 10,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White
                    }
                };
                return border;
            })
        });

        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Nome Simbolo",
            Binding = new Binding(nameof(DeadCodeGridRow.Name)),
            Width = new DataGridLength(160, DataGridLengthUnitType.Pixel)
        });

        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Container / Modulo",
            Binding = new Binding(nameof(DeadCodeGridRow.ContainerName)),
            Width = new DataGridLength(160, DataGridLengthUnitType.Pixel)
        });

        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Riga",
            Binding = new Binding(nameof(DeadCodeGridRow.LineStart)),
            Width = new DataGridLength(60, DataGridLengthUnitType.Pixel)
        });

        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "LOC",
            Binding = new Binding(nameof(DeadCodeGridRow.EstimatedLoc)),
            Width = new DataGridLength(60, DataGridLengthUnitType.Pixel)
        });

        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Motivo Diagnostico",
            Binding = new Binding(nameof(DeadCodeGridRow.Reason)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        _grid.SelectionChanged += (s, e) =>
        {
            if (_grid.SelectedItem is DeadCodeGridRow row)
            {
                ItemSelected?.Invoke(this, row.Item);
            }
        };

        _grid.DoubleTapped += (s, e) =>
        {
            if (_grid.SelectedItem is DeadCodeGridRow row)
            {
                ItemDoubleTapped?.Invoke(this, row.Item);
            }
        };

        Grid.SetRow(_grid, 2);
        rootGrid.Children.Add(_grid);

        Content = rootGrid;

        UpdateLocalizedTexts();
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            UpdateLocalizedTexts();
            ApplyFilter();
        };
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;

        _kpiTotalDeadTitle.Text = loc["DeadCode_Kpi_Total"];
        _kpiRatioTitle.Text = loc["DeadCode_Kpi_Ratio"];
        _kpiRoutinesTitle.Text = loc["DeadCode_Kpi_Routines"];
        _kpiFieldsTitle.Text = loc["DeadCode_Kpi_Fields"];
        _kpiContainersTitle.Text = loc["DeadCode_Kpi_Containers"];
        _kpiLocTitle.Text = loc["DeadCode_Kpi_Loc"];

        _searchBox.Watermark = loc["DeadCode_Filter_Search_Watermark"];

        int sel = _categoryFilter.SelectedIndex;
        _categoryFilter.ItemsSource = new[]
        {
            loc["DeadCode_Filter_All"],
            loc["DeadCode_Filter_Routines"],
            loc["DeadCode_Filter_Fields"],
            loc["DeadCode_Filter_Containers"],
            loc["DeadCode_Filter_Types"]
        };
        _categoryFilter.SelectedIndex = sel >= 0 ? sel : 0;

        if (_grid.Columns.Count >= 7)
        {
            _grid.Columns[0].Header = loc["DeadCode_Col_Type"];
            _grid.Columns[1].Header = loc["DeadCode_Col_Category"];
            _grid.Columns[2].Header = loc["DeadCode_Col_Name"];
            _grid.Columns[3].Header = loc["DeadCode_Col_Container"];
            _grid.Columns[4].Header = loc["DeadCode_Col_Line"];
            _grid.Columns[5].Header = loc["DeadCode_Col_Loc"];
            _grid.Columns[6].Header = loc["DeadCode_Col_Reason"];
        }
    }

    private static Border CreateKpiCard(
        string title,
        string initialValue,
        string accentHex,
        out TextBlock titleBlock,
        out TextBlock valueBlock)
    {
        valueBlock = new TextBlock
        {
            Text = initialValue,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse(accentHex),
            Margin = new Thickness(0, 2, 0, 0)
        };

        titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#9090A5")
        };

        var sp = new StackPanel
        {
            Spacing = 2,
            Children = { titleBlock, valueBlock }
        };

        return new Border
        {
            Background = Brush.Parse("#1A1A26"),
            BorderBrush = Brush.Parse("#2C2C3E"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(3, 0),
            Child = sp
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DatasetProperty)
        {
            LoadDataset(change.GetNewValue<DeadCodeDataset?>());
        }
    }

    private void LoadDataset(DeadCodeDataset? dataset)
    {
        if (dataset == null)
        {
            _allItems = new List<DeadCodeItem>();
            _rows.Clear();
            _kpiTotalDeadText.Text = "0";
            _kpiRatioText.Text = "0%";
            _kpiRoutinesText.Text = "0";
            _kpiFieldsText.Text = "0";
            _kpiContainersText.Text = "0";
            _kpiLocText.Text = "0";
            return;
        }

        _allItems = dataset.Items;
        _kpiTotalDeadText.Text = dataset.DeadSymbolsCount.ToString("N0");
        _kpiRatioText.Text = $"{dataset.DeadCodeRatio:F1}%";
        _kpiRoutinesText.Text = dataset.UnreachableRoutinesCount.ToString("N0");
        _kpiFieldsText.Text = dataset.UnusedFieldsCount.ToString("N0");
        _kpiContainersText.Text = dataset.OrphanContainersCount.ToString("N0");
        _kpiLocText.Text = dataset.TotalDeadLoc.ToString("N0");

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _rows.Clear();
        var search = _searchBox.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var catIndex = _categoryFilter.SelectedIndex;

        foreach (var item in _allItems)
        {
            if (catIndex == 1 && item.Category != DeadCodeCategory.UnreachableRoutine) continue;
            if (catIndex == 2 && item.Category != DeadCodeCategory.UnusedFieldOrConstant) continue;
            if (catIndex == 3 && item.Category != DeadCodeCategory.OrphanContainer) continue;
            if (catIndex == 4 && item.Category != DeadCodeCategory.UnusedType) continue;

            if (!string.IsNullOrEmpty(search))
            {
                if (!item.Name.ToLowerInvariant().Contains(search) &&
                    !item.QualifiedName.ToLowerInvariant().Contains(search) &&
                    !item.ContainerName.ToLowerInvariant().Contains(search) &&
                    !item.FilePath.ToLowerInvariant().Contains(search))
                {
                    continue;
                }
            }

            _rows.Add(new DeadCodeGridRow { Item = item });
        }
    }

    public void ApplyTheme(ThemeDefinition theme)
    {
        // Theme updates if needed
    }
}
