using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.TemporalCoupling;

namespace Macondo.App.Controls;

/// <summary>
/// Riga per il DataGrid dell'analisi di Temporal Coupling (co-modifica storica nei commit).
/// </summary>
public sealed class TemporalCouplingGridRow
{
    public required TemporalCouplingPair Pair { get; init; }

    public string FileAName => Pair.FileAName;
    public string FileBName => Pair.FileBName;
    public string FileAPath => Pair.FileA;
    public string FileBPath => Pair.FileB;
    public int CoChangeCount => Pair.CoChangeCount;
    public int TotalCommitsA => Pair.TotalCommitsA;
    public int TotalCommitsB => Pair.TotalCommitsB;
    public double JaccardIndex => Pair.JaccardIndex;
    public string JaccardPercentText => $"{Pair.JaccardIndex * 100:F0}%";
    public string ConfidenceAToBText => $"{Pair.ConfidenceAToB * 100:F0}%";
    public string ConfidenceBToAText => $"{Pair.ConfidenceBToA * 100:F0}%";
    public bool HasStaticDependency => Pair.HasStaticDependency;

    public string JaccardBadgeBackground => Pair.JaccardIndex switch
    {
        >= 0.70 => "#B81D2D",
        >= 0.50 => "#C86400",
        >= 0.35 => "#D97706",
        _ => "#2B6CB0"
    };

    public string StaticDependencyBadgeText => Pair.HasStaticDependency 
        ? LocalizationService.Instance["Temporal_Dep_Explicit"] 
        : LocalizationService.Instance["Temporal_Dep_Hidden"];
    public string StaticDependencyBadgeBackground => Pair.HasStaticDependency ? "#2E7D32" : "#B81D2D";
    public bool IsHiddenCoupling => !Pair.HasStaticDependency && Pair.JaccardIndex >= 0.40;
}

/// <summary>
/// Controllo visuale per l'analisi di Temporal Coupling &amp; Co-Change Matrix.
/// Estrae la cronologia da Git / VCS, calcola l'indice di Jaccard tra file e identifica gli accoppiamenti nascosti.
/// </summary>
public sealed class TemporalCouplingControl : UserControl
{
    public static readonly StyledProperty<TemporalCouplingDataset?> DatasetProperty =
        AvaloniaProperty.Register<TemporalCouplingControl, TemporalCouplingDataset?>(nameof(Dataset));

    public TemporalCouplingDataset? Dataset
    {
        get => GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    public event EventHandler<TemporalCouplingPair?>? PairDoubleTapped;

    private readonly ObservableCollection<TemporalCouplingGridRow> _rows = new();
    private List<TemporalCouplingPair> _allPairs = new();

    // UI Elements
    private readonly TextBlock _kpiPairsTitle;
    private readonly TextBlock _kpiPairsValue;
    private readonly TextBlock _kpiStrongTitle;
    private readonly TextBlock _kpiStrongValue;
    private readonly TextBlock _kpiHiddenTitle;
    private readonly TextBlock _kpiHiddenValue;
    private readonly TextBlock _kpiCommitsTitle;
    private readonly TextBlock _kpiCommitsValue;

    private readonly TextBlock _searchLabel;
    private readonly TextBox _searchBox;
    private readonly Slider _thresholdSlider;
    private readonly TextBlock _thresholdLabel;
    private readonly CheckBox _hiddenOnlyCheck;
    private readonly TextBlock _statsTextBlock;
    private readonly DataGrid _pairsDataGrid;
    private readonly Border _emptyStateBorder;
    private readonly TextBlock _emptyStateText;
    private readonly Grid _mainContentGrid;

    public TemporalCouplingControl()
    {
        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*")
        };

        // ==========================================
        // 1. CRUSCOTTO KPI SUPERIORE
        // ==========================================
        var kpiBorder = new Border
        {
            [!Border.BackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundSurfaceBrush"),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("ThemeBorderDefaultBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 12)
        };

        var kpiGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*")
        };

        (_kpiPairsTitle, _kpiPairsValue) = CreateKpiCard(kpiGrid, 0, "COPPIE ACCOPPIATE", "0", "#3B82F6");
        (_kpiStrongTitle, _kpiStrongValue) = CreateKpiCard(kpiGrid, 1, "ACCOPPIAMENTO FORTE (>=50%)", "0", "#F59E0B");
        (_kpiHiddenTitle, _kpiHiddenValue) = CreateKpiCard(kpiGrid, 2, "ACCOPPIAMENTI NASCOSTI", "0", "#EF4444");
        (_kpiCommitsTitle, _kpiCommitsValue) = CreateKpiCard(kpiGrid, 3, "COMMIT ANALIZZATI", "0", "#8B5CF6");

        kpiBorder.Child = kpiGrid;
        Grid.SetRow(kpiBorder, 0);
        rootGrid.Children.Add(kpiBorder);

        // ==========================================
        // 2. TOOLBAR FILTRI E CONTROLLI
        // ==========================================
        var toolbarBorder = new Border
        {
            [!Border.BackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundSurfaceBrush"),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("ThemeBorderDefaultBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8)
        };

        var toolbarGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto")
        };

        // Filtro di ricerca testuale
        var searchPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        _searchLabel = new TextBlock
        {
            Text = "Cerca File:",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("ThemeTextSecondaryBrush")
        };
        _searchBox = new TextBox
        {
            Width = 180,
            Watermark = "Nome file o modulo...",
            FontSize = 11
        };
        _searchBox.TextChanged += (s, e) => ApplyFilter();
        searchPanel.Children.Add(_searchLabel);
        searchPanel.Children.Add(_searchBox);
        Grid.SetColumn(searchPanel, 0);
        toolbarGrid.Children.Add(searchPanel);

        // Slider Soglia Jaccard
        var sliderPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        _thresholdLabel = new TextBlock
        {
            Text = "Soglia Jaccard: 30%",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("ThemeTextSecondaryBrush")
        };
        _thresholdSlider = new Slider
        {
            Minimum = 10,
            Maximum = 90,
            Value = 30,
            Width = 120,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        _thresholdSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                _thresholdLabel.Text = $"{LocalizationService.Instance["Temporal_Filter_Threshold"]}: {_thresholdSlider.Value:F0}%";
                ApplyFilter();
            }
        };
        sliderPanel.Children.Add(_thresholdLabel);
        sliderPanel.Children.Add(_thresholdSlider);
        Grid.SetColumn(sliderPanel, 1);
        toolbarGrid.Children.Add(sliderPanel);

        // Checkbox Solo Hidden Coupling
        _hiddenOnlyCheck = new CheckBox
        {
            Content = "Solo Accoppiamenti Nascosti (Senza Dipendenza Statica)",
            FontSize = 11,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _hiddenOnlyCheck.IsCheckedChanged += (s, e) => ApplyFilter();
        Grid.SetColumn(_hiddenOnlyCheck, 2);
        toolbarGrid.Children.Add(_hiddenOnlyCheck);

        // Statistiche
        _statsTextBlock = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("ThemeTextSecondaryBrush")
        };
        Grid.SetColumn(_statsTextBlock, 4);
        toolbarGrid.Children.Add(_statsTextBlock);

        toolbarBorder.Child = toolbarGrid;
        Grid.SetRow(toolbarBorder, 1);
        rootGrid.Children.Add(toolbarBorder);

        // ==========================================
        // 3. CONTENUTO CENTRALE (DATAGRID)
        // ==========================================
        _mainContentGrid = new Grid();

        _pairsDataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderThickness = new Thickness(0),
            SelectionMode = DataGridSelectionMode.Single,
            ItemsSource = _rows,
            [!DataGrid.BackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundSurfaceBrush"),
            [!DataGrid.RowBackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundSurfaceBrush")
        };
        _pairsDataGrid.DoubleTapped += (s, e) =>
        {
            if (_pairsDataGrid.SelectedItem is TemporalCouplingGridRow selected)
            {
                PairDoubleTapped?.Invoke(this, selected.Pair);
            }
        };

        ConfigureColumns(_pairsDataGrid);
        _mainContentGrid.Children.Add(_pairsDataGrid);

        // Empty state
        _emptyStateBorder = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = false
        };
        _emptyStateText = new TextBlock
        {
            Text = "Nessuna cronologia repository Git trovata per calcolare il Temporal Coupling.",
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("ThemeTextSecondaryBrush")
        };
        _emptyStateBorder.Child = _emptyStateText;

        var contentContainer = new Grid();
        contentContainer.Children.Add(_mainContentGrid);
        contentContainer.Children.Add(_emptyStateBorder);

        Grid.SetRow(contentContainer, 2);
        rootGrid.Children.Add(contentContainer);

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

        _kpiPairsTitle.Text = loc["Temporal_Kpi_Pairs"];
        _kpiStrongTitle.Text = loc["Temporal_Kpi_Strong"];
        _kpiHiddenTitle.Text = loc["Temporal_Kpi_Hidden"];
        _kpiCommitsTitle.Text = loc["Temporal_Kpi_Commits"];

        _searchLabel.Text = loc["Temporal_Filter_Search"];
        _searchBox.Watermark = loc["Temporal_Filter_Search_Watermark"];

        _thresholdLabel.Text = $"{loc["Temporal_Filter_Threshold"]}: {_thresholdSlider.Value:F0}%";
        _hiddenOnlyCheck.Content = loc["Temporal_Filter_HiddenOnly"];

        if (_pairsDataGrid.Columns.Count >= 7)
        {
            _pairsDataGrid.Columns[0].Header = loc["Temporal_Col_Jaccard"];
            _pairsDataGrid.Columns[1].Header = loc["Temporal_Col_StaticDep"];
            _pairsDataGrid.Columns[2].Header = loc["Temporal_Col_FileA"];
            _pairsDataGrid.Columns[3].Header = loc["Temporal_Col_FileB"];
            _pairsDataGrid.Columns[4].Header = loc["Temporal_Col_CoChanges"];
            _pairsDataGrid.Columns[5].Header = loc["Temporal_Col_ConfAtoB"];
            _pairsDataGrid.Columns[6].Header = loc["Temporal_Col_ConfBtoA"];
        }

        if (_allPairs.Count > 0)
        {
            _statsTextBlock.Text = loc.Get("Temporal_Stats_Shown", _rows.Count, _allPairs.Count);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DatasetProperty)
        {
            ReloadDataset();
        }
    }

    private void ReloadDataset()
    {
        var dataset = Dataset;
        if (dataset == null || !dataset.HasGitHistory || dataset.Pairs.Count == 0)
        {
            _allPairs.Clear();
            _rows.Clear();
            _kpiPairsValue.Text = "0";
            _kpiStrongValue.Text = "0";
            _kpiHiddenValue.Text = "0";
            _kpiCommitsValue.Text = dataset?.TotalCommitsAnalyzed.ToString("N0") ?? "0";
            _statsTextBlock.Text = "";
            _emptyStateText.Text = dataset != null && dataset.TotalCommitsAnalyzed > 0
                ? LocalizationService.Instance["Temporal_Empty_NoCoupling"]
                : LocalizationService.Instance["Temporal_Empty_NoGit"];
            _emptyStateBorder.IsVisible = true;
            _mainContentGrid.IsVisible = false;
            return;
        }

        _allPairs = dataset.Pairs;
        _emptyStateBorder.IsVisible = false;
        _mainContentGrid.IsVisible = true;

        _kpiPairsValue.Text = dataset.TotalCoupledPairsCount.ToString("N0");
        _kpiStrongValue.Text = dataset.StrongCouplingCount.ToString("N0");
        _kpiHiddenValue.Text = dataset.HiddenCouplingCount.ToString("N0");
        _kpiCommitsValue.Text = dataset.TotalCommitsAnalyzed.ToString("N0");

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_allPairs.Count == 0) return;

        var search = _searchBox.Text?.Trim() ?? string.Empty;
        var minJaccard = _thresholdSlider.Value / 100.0;
        var hiddenOnly = _hiddenOnlyCheck.IsChecked == true;

        var filtered = _allPairs.Where(p => p.JaccardIndex >= minJaccard);

        if (!string.IsNullOrEmpty(search))
        {
            filtered = filtered.Where(p =>
                p.FileA.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                p.FileB.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (hiddenOnly)
        {
            filtered = filtered.Where(p => !p.HasStaticDependency);
        }

        var resultList = filtered.OrderByDescending(p => p.JaccardIndex)
                                 .ThenByDescending(p => p.CoChangeCount)
                                 .ToList();

        _rows.Clear();
        foreach (var p in resultList)
        {
            _rows.Add(new TemporalCouplingGridRow { Pair = p });
        }

        _statsTextBlock.Text = LocalizationService.Instance.Get("Temporal_Stats_Shown", _rows.Count, _allPairs.Count);
    }

    private static (TextBlock titleBlock, TextBlock valueBlock) CreateKpiCard(
        Grid grid,
        int col,
        string title,
        string value,
        string accentColorHex)
    {
        var card = new Border
        {
            [!Border.BackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundSecondaryBrush"),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("ThemeBorderDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10),
            Margin = new Thickness(col == 0 ? 0 : 6, 0, col == 3 ? 0 : 6, 0)
        };

        var sp = new StackPanel { Spacing = 4 };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("ThemeTextSecondaryBrush")
        };

        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse(accentColorHex)
        };

        sp.Children.Add(titleBlock);
        sp.Children.Add(valueBlock);
        card.Child = sp;

        Grid.SetColumn(card, col);
        grid.Children.Add(card);

        return (titleBlock, valueBlock);
    }

    private static void ConfigureColumns(DataGrid grid)
    {
        // Jaccard Index % Badge
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Indice Jaccard",
            Width = new DataGridLength(110),
            SortMemberPath = "Pair.JaccardIndex",
            CellTemplate = new FuncDataTemplate<TemporalCouplingGridRow>((row, _) =>
            {
                if (row == null) return new Panel();
                var border = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8, 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = Brush.Parse(row.JaccardBadgeBackground)
                };
                var tb = new TextBlock
                {
                    Text = row.JaccardPercentText,
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White
                };
                border.Child = tb;
                return border;
            })
        });

        // Dipendenza Statica (Esplicita vs Nascosta)
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Dipendenza Statica",
            Width = new DataGridLength(135),
            SortMemberPath = "Pair.HasStaticDependency",
            CellTemplate = new FuncDataTemplate<TemporalCouplingGridRow>((row, _) =>
            {
                if (row == null) return new Panel();
                var border = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = Brush.Parse(row.StaticDependencyBadgeBackground)
                };
                var tb = new TextBlock
                {
                    Text = row.StaticDependencyBadgeText,
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White
                };
                border.Child = tb;
                return border;
            })
        });

        // File A
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "File Sorgente A",
            Binding = new Binding(nameof(TemporalCouplingGridRow.FileAName)),
            Width = new DataGridLength(1.8, DataGridLengthUnitType.Star)
        });

        // File B
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "File Sorgente B",
            Binding = new Binding(nameof(TemporalCouplingGridRow.FileBName)),
            Width = new DataGridLength(1.8, DataGridLengthUnitType.Star)
        });

        // Co-modifiche condivise
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Co-Modifiche",
            Binding = new Binding(nameof(TemporalCouplingGridRow.CoChangeCount)),
            Width = new DataGridLength(100)
        });

        // Confidenza A -> B
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "P(B|A)",
            Binding = new Binding(nameof(TemporalCouplingGridRow.ConfidenceAToBText)),
            Width = new DataGridLength(75)
        });

        // Confidenza B -> A
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "P(A|B)",
            Binding = new Binding(nameof(TemporalCouplingGridRow.ConfidenceBToAText)),
            Width = new DataGridLength(75)
        });
    }

    public void ApplyTheme(ThemeDefinition theme)
    {
        // Theme updates if needed
    }
}

