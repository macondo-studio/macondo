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
using Macondo.Semantic.Analytics.SharedState;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

/// <summary>
/// Riga per la griglia principale delle variabili condivise.
/// </summary>
public sealed class SharedVariableGridRow
{
    public required SharedVariableItem Item { get; init; }

    public string VariableName => Item.VariableName;
    public string ModuleName => Item.ModuleName;
    public string QualifiedName => Item.QualifiedName;
    public string FilePath => Item.FilePath;
    public int Line => Item.Line;
    public int TotalAccessorsCount => Item.TotalAccessorsCount;
    public int ReadersCount => Item.ReadersCount;
    public int WritersCount => Item.WritersCount;
    public int CrossModuleWritersCount => Item.CrossModuleWritersCount;

    public string RiskBadgeText => Item.MutationRisk switch
    {
        ImpactRiskLevel.Critical => LocalizationService.Instance["SharedState_Risk_Critical"],
        ImpactRiskLevel.High => LocalizationService.Instance["SharedState_Risk_High"],
        ImpactRiskLevel.Medium => LocalizationService.Instance["SharedState_Risk_Medium"],
        _ => LocalizationService.Instance["SharedState_Risk_Low"]
    };

    public string RiskBadgeBackground => Item.MutationRisk switch
    {
        ImpactRiskLevel.Critical => "#B81D2D",
        ImpactRiskLevel.High => "#C86400",
        ImpactRiskLevel.Medium => "#D97706",
        _ => "#2E7D32"
    };

    public string RiskBadgeForeground => "#FFFFFF";

    public string KindBadge => Item.Kind switch
    {
        SymbolKind.Field => "VAR",
        SymbolKind.Constant => "CONST",
        SymbolKind.Property => "PROP",
        _ => "SYM"
    };

    public string KindBadgeBackground => Item.Kind switch
    {
        SymbolKind.Field => "#2B6CB0",
        SymbolKind.Constant => "#975A16",
        SymbolKind.Property => "#4A5568",
        _ => "#4A5568"
    };
}

/// <summary>
/// Riga per la griglia di dettaglio degli accessor (routine che leggono o scrivono sulla variabile).
/// </summary>
public sealed class SharedAccessorGridRow
{
    public required SharedVariableAccessor Accessor { get; init; }

    public string RoutineName => Accessor.RoutineName;
    public string ContainerName => Accessor.ContainerName;
    public string FilePath => Accessor.FilePath;
    public int Line => Accessor.Line;
    public bool IsCrossModule => Accessor.IsCrossModule;

    public string AccessBadgeText => Accessor.AccessKind switch
    {
        SharedStateAccessKind.Write => LocalizationService.Instance["SharedState_Access_Write"],
        SharedStateAccessKind.ReadWrite => LocalizationService.Instance["SharedState_Access_ReadWrite"],
        _ => LocalizationService.Instance["SharedState_Access_Read"]
    };

    public string AccessBadgeBackground => Accessor.AccessKind switch
    {
        SharedStateAccessKind.Write => "#B81D2D",
        SharedStateAccessKind.ReadWrite => "#D97706",
        _ => "#2B6CB0"
    };

    public string CrossModuleBadgeText => Accessor.IsCrossModule 
        ? LocalizationService.Instance["SharedState_Scope_CrossModule"] 
        : LocalizationService.Instance["SharedState_Scope_Internal"];
    public string CrossModuleBadgeBackground => Accessor.IsCrossModule ? "#7C3AED" : "#4A5568";
}

/// <summary>
/// Controllo visuale completo per la Matrice di Stato Condiviso (Global &amp; Shared State Matrix).
/// Mostra variabili globali / statiche, chi le legge/modifica e il rischio di effetti collaterali mutazionali.
/// </summary>
public sealed class SharedStateControl : UserControl
{
    public static readonly StyledProperty<SharedStateDataset?> DatasetProperty =
        AvaloniaProperty.Register<SharedStateControl, SharedStateDataset?>(nameof(Dataset));

    public SharedStateDataset? Dataset
    {
        get => GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    public event EventHandler<SharedVariableItem?>? VariableSelected;
    public event EventHandler<SharedVariableAccessor?>? AccessorDoubleTapped;

    private readonly ObservableCollection<SharedVariableGridRow> _variableRows = new();
    private readonly ObservableCollection<SharedAccessorGridRow> _accessorRows = new();
    private List<SharedVariableItem> _allItems = new();

    // UI Elements
    private readonly TextBlock _kpiTotalVarsTitle;
    private readonly TextBlock _kpiTotalVarsValue;
    private readonly TextBlock _kpiCriticalTitle;
    private readonly TextBlock _kpiCriticalValue;
    private readonly TextBlock _kpiWritersTitle;
    private readonly TextBlock _kpiWritersValue;
    private readonly TextBlock _kpiCrossModuleWritersTitle;
    private readonly TextBlock _kpiCrossModuleWritersValue;

    private readonly TextBlock _searchLabel;
    private readonly TextBox _searchBox;
    private readonly TextBlock _riskLabel;
    private readonly ComboBox _riskFilterCombo;
    private readonly CheckBox _crossModuleOnlyCheck;
    private readonly TextBlock _statsTextBlock;
    private readonly DataGrid _variablesDataGrid;
    private readonly DataGrid _accessorsDataGrid;
    private readonly TextBlock _selectedVarTitleText;
    private readonly Border _emptyStateBorder;
    private readonly TextBlock _emptyStateText;
    private readonly Grid _mainContentGrid;

    public SharedStateControl()
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

        (_kpiTotalVarsTitle, _kpiTotalVarsValue) = CreateKpiCard(kpiGrid, 0, "STATO GLOBALE TOTALE", "0", "#3B82F6");
        (_kpiCriticalTitle, _kpiCriticalValue) = CreateKpiCard(kpiGrid, 1, "VARIABILI CRITICHE", "0", "#EF4444");
        (_kpiWritersTitle, _kpiWritersValue) = CreateKpiCard(kpiGrid, 2, "ACCESSI IN SCRITTURA", "0", "#F59E0B");
        (_kpiCrossModuleWritersTitle, _kpiCrossModuleWritersValue) = CreateKpiCard(kpiGrid, 3, "MUTAZIONI CROSS-MODULE", "0", "#8B5CF6");

        kpiBorder.Child = kpiGrid;
        Grid.SetRow(kpiBorder, 0);
        rootGrid.Children.Add(kpiBorder);

        // ==========================================
        // 2. TOOLBAR FILTRI E RICERCA
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

        // Filtro di ricerca
        var searchPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        _searchLabel = new TextBlock
        {
            Text = "Cerca:",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("ThemeTextSecondaryBrush")
        };
        _searchBox = new TextBox
        {
            Width = 200,
            Watermark = "Nome variabile o modulo...",
            FontSize = 11
        };
        _searchBox.TextChanged += (s, e) => ApplyFilter();
        searchPanel.Children.Add(_searchLabel);
        searchPanel.Children.Add(_searchBox);
        Grid.SetColumn(searchPanel, 0);
        toolbarGrid.Children.Add(searchPanel);

        // Filtro Livello di Rischio
        var riskPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        _riskLabel = new TextBlock
        {
            Text = "Rischio Mutazione:",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("ThemeTextSecondaryBrush")
        };
        _riskFilterCombo = new ComboBox
        {
            FontSize = 11,
            Width = 140,
            SelectedIndex = 0
        };
        _riskFilterCombo.Items.Add(new ComboBoxItem { Content = "Tutti i Livelli" });
        _riskFilterCombo.Items.Add(new ComboBoxItem { Content = "Critico" });
        _riskFilterCombo.Items.Add(new ComboBoxItem { Content = "Alto" });
        _riskFilterCombo.Items.Add(new ComboBoxItem { Content = "Medio" });
        _riskFilterCombo.Items.Add(new ComboBoxItem { Content = "Basso" });
        _riskFilterCombo.SelectionChanged += (s, e) => ApplyFilter();
        riskPanel.Children.Add(_riskLabel);
        riskPanel.Children.Add(_riskFilterCombo);
        Grid.SetColumn(riskPanel, 1);
        toolbarGrid.Children.Add(riskPanel);

        // Checkbox Solo Cross-Module
        _crossModuleOnlyCheck = new CheckBox
        {
            Content = "Solo Scritture Cross-Module",
            FontSize = 11,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _crossModuleOnlyCheck.IsCheckedChanged += (s, e) => ApplyFilter();
        Grid.SetColumn(_crossModuleOnlyCheck, 2);
        toolbarGrid.Children.Add(_crossModuleOnlyCheck);

        // Statistiche raggruppate a destra
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
        // 3. CONTENUTO CENTRALE (GRIGLIA VARIABILI + DETTAGLIO ACCESSOR)
        // ==========================================
        _mainContentGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("3*,Auto,2*")
        };

        // Griglia Superiore: Variabili Condivise
        _variablesDataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderThickness = new Thickness(0),
            SelectionMode = DataGridSelectionMode.Single,
            ItemsSource = _variableRows,
            [!DataGrid.BackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundSurfaceBrush"),
            [!DataGrid.RowBackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundSurfaceBrush")
        };
        _variablesDataGrid.SelectionChanged += OnVariablesSelectionChanged;

        ConfigureVariablesColumns(_variablesDataGrid);
        Grid.SetRow(_variablesDataGrid, 0);
        _mainContentGrid.Children.Add(_variablesDataGrid);

        // Splitter
        var gridSplitter = new GridSplitter
        {
            Height = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            [!GridSplitter.BackgroundProperty] = new DynamicResourceExtension("ThemeBorderDefaultBrush")
        };
        Grid.SetRow(gridSplitter, 1);
        _mainContentGrid.Children.Add(gridSplitter);

        // Pannello Inferiore: Dettaglio Accessor della variabile selezionata
        var accessorDetailPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };

        var detailHeaderBorder = new Border
        {
            [!Border.BackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundSecondaryBrush"),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("ThemeBorderDefaultBrush"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(12, 6)
        };
        _selectedVarTitleText = new TextBlock
        {
            Text = "Routine Chiamanti / Mutatrici dello Stato Selezionato",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("ThemeTextSecondaryBrush")
        };
        detailHeaderBorder.Child = _selectedVarTitleText;
        Grid.SetRow(detailHeaderBorder, 0);
        accessorDetailPanel.Children.Add(detailHeaderBorder);

        _accessorsDataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderThickness = new Thickness(0),
            SelectionMode = DataGridSelectionMode.Single,
            ItemsSource = _accessorRows,
            [!DataGrid.BackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundSurfaceBrush"),
            [!DataGrid.RowBackgroundProperty] = new DynamicResourceExtension("ThemeBackgroundSurfaceBrush")
        };
        _accessorsDataGrid.DoubleTapped += (s, e) =>
        {
            if (_accessorsDataGrid.SelectedItem is SharedAccessorGridRow selected)
            {
                AccessorDoubleTapped?.Invoke(this, selected.Accessor);
            }
        };

        ConfigureAccessorsColumns(_accessorsDataGrid);
        Grid.SetRow(_accessorsDataGrid, 1);
        accessorDetailPanel.Children.Add(_accessorsDataGrid);

        Grid.SetRow(accessorDetailPanel, 2);
        _mainContentGrid.Children.Add(accessorDetailPanel);

        // Empty state
        _emptyStateBorder = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = false
        };
        _emptyStateText = new TextBlock
        {
            Text = "Nessuna variabile globale o stato condiviso individuato nel progetto.",
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
        var localizationService = LocalizationService.Instance;

        _kpiTotalVarsTitle.Text = localizationService["SharedState_Kpi_TotalVars"];
        _kpiCriticalTitle.Text = localizationService["SharedState_Kpi_Critical"];
        _kpiWritersTitle.Text = localizationService["SharedState_Kpi_Writers"];
        _kpiCrossModuleWritersTitle.Text = localizationService["SharedState_Kpi_CrossModule"];

        _searchLabel.Text = localizationService["SharedState_Filter_Search"];
        _searchBox.Watermark = localizationService["SharedState_Filter_Search_Watermark"];

        _riskLabel.Text = localizationService["SharedState_Filter_Risk"];
        if (_riskFilterCombo.Items.Count >= 5)
        {
            if (_riskFilterCombo.Items[0] is ComboBoxItem item0) item0.Content = localizationService["SharedState_Risk_All"];
            if (_riskFilterCombo.Items[1] is ComboBoxItem item1) item1.Content = localizationService["SharedState_Risk_Critical"];
            if (_riskFilterCombo.Items[2] is ComboBoxItem item2) item2.Content = localizationService["SharedState_Risk_High"];
            if (_riskFilterCombo.Items[3] is ComboBoxItem item3) item3.Content = localizationService["SharedState_Risk_Medium"];
            if (_riskFilterCombo.Items[4] is ComboBoxItem item4) item4.Content = localizationService["SharedState_Risk_Low"];
        }

        _crossModuleOnlyCheck.Content = localizationService["SharedState_Filter_CrossModuleOnly"];
        _emptyStateText.Text = localizationService["SharedState_EmptyState"];

        if (_variablesDataGrid.Columns.Count >= 7)
        {
            _variablesDataGrid.Columns[0].Header = localizationService["SharedState_Col_Risk"];
            _variablesDataGrid.Columns[1].Header = localizationService["SharedState_Col_Module"];
            _variablesDataGrid.Columns[2].Header = localizationService["SharedState_Col_VarName"];
            _variablesDataGrid.Columns[3].Header = localizationService["SharedState_Col_Type"];
            _variablesDataGrid.Columns[4].Header = localizationService["SharedState_Col_Readers"];
            _variablesDataGrid.Columns[5].Header = localizationService["SharedState_Col_Writers"];
            _variablesDataGrid.Columns[6].Header = localizationService["SharedState_Col_CrossWriters"];
        }

        if (_accessorsDataGrid.Columns.Count >= 5)
        {
            _accessorsDataGrid.Columns[0].Header = localizationService["SharedState_Col_AccessType"];
            _accessorsDataGrid.Columns[1].Header = localizationService["SharedState_Col_Scope"];
            _accessorsDataGrid.Columns[2].Header = localizationService["SharedState_Col_CallerModule"];
            _accessorsDataGrid.Columns[3].Header = localizationService["SharedState_Col_CallerRoutine"];
            _accessorsDataGrid.Columns[4].Header = localizationService["SharedState_Col_Line"];
        }

        if (_variablesDataGrid.SelectedItem is SharedVariableGridRow selectedRow)
        {
            var item = selectedRow.Item;
            _selectedVarTitleText.Text = localizationService.Get("SharedState_Detail_Selected", item.ModuleName, item.VariableName, item.TotalAccessorsCount, item.WritersCount);
        }
        else if (_variableRows.Count == 0 && _allItems.Count > 0)
        {
            _selectedVarTitleText.Text = localizationService["SharedState_Detail_NoMatches"];
        }
        else
        {
            _selectedVarTitleText.Text = localizationService["SharedState_Detail_Prompt"];
        }

        if (_allItems.Count > 0)
        {
            _statsTextBlock.Text = localizationService.Get("SharedState_Stats_Shown", _variableRows.Count, _allItems.Count);
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
        if (dataset == null || dataset.Items.Count == 0)
        {
            _allItems.Clear();
            _variableRows.Clear();
            _accessorRows.Clear();
            _kpiTotalVarsValue.Text = "0";
            _kpiCriticalValue.Text = "0";
            _kpiWritersValue.Text = "0";
            _kpiCrossModuleWritersValue.Text = "0";
            _statsTextBlock.Text = "";
            _emptyStateBorder.IsVisible = true;
            _mainContentGrid.IsVisible = false;
            return;
        }

        _allItems = dataset.Items;
        _emptyStateBorder.IsVisible = false;
        _mainContentGrid.IsVisible = true;

        _kpiTotalVarsValue.Text = dataset.TotalSharedVariablesCount.ToString("N0");
        _kpiCriticalValue.Text = dataset.CriticalVariablesCount.ToString("N0");
        _kpiWritersValue.Text = dataset.TotalWritersCount.ToString("N0");
        _kpiCrossModuleWritersValue.Text = dataset.TotalCrossModuleWritersCount.ToString("N0");

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_allItems.Count == 0) return;

        var search = _searchBox.Text?.Trim() ?? string.Empty;
        var selectedRiskIndex = _riskFilterCombo.SelectedIndex;
        var crossModuleOnly = _crossModuleOnlyCheck.IsChecked == true;

        var filtered = _allItems.AsEnumerable();

        if (!string.IsNullOrEmpty(search))
        {
            filtered = filtered.Where(i =>
                i.VariableName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                i.ModuleName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                i.QualifiedName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (crossModuleOnly)
        {
            filtered = filtered.Where(i => i.CrossModuleWritersCount > 0);
        }

        if (selectedRiskIndex > 0)
        {
            var targetRisk = selectedRiskIndex switch
            {
                1 => ImpactRiskLevel.Critical,
                2 => ImpactRiskLevel.High,
                3 => ImpactRiskLevel.Medium,
                4 => ImpactRiskLevel.Low,
                _ => ImpactRiskLevel.Low
            };
            filtered = filtered.Where(i => i.MutationRisk == targetRisk);
        }

        var resultList = filtered.OrderByDescending(i => i.CrossModuleWritersCount)
                                 .ThenByDescending(i => i.WritersCount)
                                 .ToList();

        _variableRows.Clear();
        foreach (var item in resultList)
        {
            _variableRows.Add(new SharedVariableGridRow { Item = item });
        }

        _statsTextBlock.Text = LocalizationService.Instance.Get("SharedState_Stats_Shown", _variableRows.Count, _allItems.Count);

        if (_variableRows.Count > 0)
        {
            _variablesDataGrid.SelectedIndex = 0;
        }
        else
        {
            _accessorRows.Clear();
            _selectedVarTitleText.Text = LocalizationService.Instance["SharedState_Detail_NoMatches"];
        }
    }

    private void OnVariablesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_variablesDataGrid.SelectedItem is SharedVariableGridRow selectedRow)
        {
            var item = selectedRow.Item;
            VariableSelected?.Invoke(this, item);

            _selectedVarTitleText.Text = LocalizationService.Instance.Get("SharedState_Detail_Selected", item.ModuleName, item.VariableName, item.TotalAccessorsCount, item.WritersCount);

            _accessorRows.Clear();
            foreach (var acc in item.Accessors.OrderByDescending(a => a.IsCrossModule).ThenBy(a => a.AccessKind != SharedStateAccessKind.Read ? 0 : 1))
            {
                _accessorRows.Add(new SharedAccessorGridRow { Accessor = acc });
            }
        }
        else
        {
            _accessorRows.Clear();
            _selectedVarTitleText.Text = LocalizationService.Instance["SharedState_Detail_Prompt"];
        }
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

    private static void ConfigureVariablesColumns(DataGrid grid)
    {
        // Rischio Mutazione (Badge)
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Rischio Mutazione",
            Width = new DataGridLength(130),
            SortMemberPath = "Item.MutationRisk",
            CellTemplate = new FuncDataTemplate<SharedVariableGridRow>((row, _) =>
            {
                if (row == null) return new Panel();
                var border = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = Brush.Parse(row.RiskBadgeBackground)
                };
                var tb = new TextBlock
                {
                    Text = row.RiskBadgeText,
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brush.Parse(row.RiskBadgeForeground)
                };
                border.Child = tb;
                return border;
            })
        });

        // Modulo / Container
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Modulo / Classe",
            Binding = new Binding(nameof(SharedVariableGridRow.ModuleName)),
            Width = new DataGridLength(1.5, DataGridLengthUnitType.Star)
        });

        // Nome Variabile
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Nome Variabile Globale",
            Binding = new Binding(nameof(SharedVariableGridRow.VariableName)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });

        // Tipo / Kind Badge
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Tipo",
            Width = new DataGridLength(75),
            CellTemplate = new FuncDataTemplate<SharedVariableGridRow>((row, _) =>
            {
                if (row == null) return new Panel();
                var border = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = Brush.Parse(row.KindBadgeBackground)
                };
                var tb = new TextBlock
                {
                    Text = row.KindBadge,
                    FontSize = 9,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White
                };
                border.Child = tb;
                return border;
            })
        });

        // Lettori
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Lettori",
            Binding = new Binding(nameof(SharedVariableGridRow.ReadersCount)),
            Width = new DataGridLength(80)
        });

        // Scrittori
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Scrittori",
            Binding = new Binding(nameof(SharedVariableGridRow.WritersCount)),
            Width = new DataGridLength(85)
        });

        // Scrittori Cross-Module
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Scrittori Cross-Mod",
            Binding = new Binding(nameof(SharedVariableGridRow.CrossModuleWritersCount)),
            Width = new DataGridLength(135)
        });
    }

    private static void ConfigureAccessorsColumns(DataGrid grid)
    {
        // Tipo di Accesso (Lettura vs Scrittura)
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Tipo Accesso",
            Width = new DataGridLength(120),
            CellTemplate = new FuncDataTemplate<SharedAccessorGridRow>((row, _) =>
            {
                if (row == null) return new Panel();
                var border = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = Brush.Parse(row.AccessBadgeBackground)
                };
                var tb = new TextBlock
                {
                    Text = row.AccessBadgeText,
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White
                };
                border.Child = tb;
                return border;
            })
        });

        // Cross-Module Badge
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Ambito",
            Width = new DataGridLength(110),
            CellTemplate = new FuncDataTemplate<SharedAccessorGridRow>((row, _) =>
            {
                if (row == null) return new Panel();
                var border = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = Brush.Parse(row.CrossModuleBadgeBackground)
                };
                var tb = new TextBlock
                {
                    Text = row.CrossModuleBadgeText,
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White
                };
                border.Child = tb;
                return border;
            })
        });

        // Modulo Chiamante
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Modulo / Container",
            Binding = new Binding(nameof(SharedAccessorGridRow.ContainerName)),
            Width = new DataGridLength(1.5, DataGridLengthUnitType.Star)
        });

        // Routine Chiamante
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Routine Chiamante / Mutatrice",
            Binding = new Binding(nameof(SharedAccessorGridRow.RoutineName)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });

        // Linea Sorgente
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Linea",
            Binding = new Binding(nameof(SharedAccessorGridRow.Line)),
            Width = new DataGridLength(70)
        });
    }

    public void ApplyTheme(ThemeDefinition theme)
    {
        // Theme updates if needed
    }
}

