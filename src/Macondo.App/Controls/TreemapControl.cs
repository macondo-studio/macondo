using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.Treemap;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

/// <summary>
/// High performance Avalonia Skia custom control for rendering and interacting with the Hierarchical Treemap of metrics.
/// Supports Squarified layout, color coding for complexity/risk, hover, selection and drill-down.
/// </summary>
public sealed class TreemapControl : Control
{
    public static readonly StyledProperty<TreemapItem?> RootItemProperty =
        AvaloniaProperty.Register<TreemapControl, TreemapItem?>(nameof(RootItem));

    public TreemapItem? RootItem
    {
        get => GetValue(RootItemProperty);
        set => SetValue(RootItemProperty, value);
    }

    public event EventHandler<TreemapItem?>? ItemSelected;

    private TreemapItem? _currentRoot;
    private TreemapItem? _hoveredItem;
    private TreemapItem? _selectedItem;

    private static readonly Typeface HeaderTypeface = new("Inter", FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Medium);
    private static readonly Typeface SubLabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Normal);

    private IBrush _backgroundBrush = new SolidColorBrush(Color.Parse("#14141E"));
    private IBrush _containerHeaderBrush = new SolidColorBrush(Color.Parse("#202030"));
    private IBrush _containerBorderBrush = new SolidColorBrush(Color.Parse("#35354A"));
    private IBrush _selectedBorderBrush = new SolidColorBrush(Color.Parse("#FFD700"));
    private IBrush _hoverBorderBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private IBrush _textWhiteBrush = Brushes.WhiteSmoke;
    private IBrush _textMutedBrush = new SolidColorBrush(Color.Parse("#A0A0B0"));
    private IBrush _hoverCardBgBrush = new SolidColorBrush(Color.FromArgb(235, 30, 30, 42));
    private IBrush _accentTextBrush = new SolidColorBrush(Color.Parse("#4AE8D0"));

    static TreemapControl()
    {
        AffectsRender<TreemapControl>(RootItemProperty);
        FocusableProperty.OverrideDefaultValue<TreemapControl>(true);
    }

    public TreemapControl()
    {
        ClipToBounds = true;
        DoubleTapped += OnDoubleTapped;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RootItemProperty)
        {
            _currentRoot = RootItem;
            _hoveredItem = null;
            _selectedItem = null;
            InvalidateLayoutAndVisual();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateLayoutAndVisual();
    }

    public void DrillUp()
    {
        if (_currentRoot?.Parent != null)
        {
            _currentRoot = _currentRoot.Parent;
            InvalidateLayoutAndVisual();
        }
    }

    public void ResetDrillDown()
    {
        _currentRoot = RootItem;
        InvalidateLayoutAndVisual();
    }

    private void InvalidateLayoutAndVisual()
    {
        if (_currentRoot != null && Bounds.Width > 50 && Bounds.Height > 50)

            TreemapLayoutEngine.Layout(_currentRoot, new TreemapRect(12, 36, Bounds.Width - 24, Bounds.Height - 48));
        InvalidateVisual();
    }

    public void ApplyTheme(ThemeDefinition theme)
    {
        if (Color.TryParse(theme.GraphBackground, out var bgCol))
            _backgroundBrush = new SolidColorBrush(bgCol);

        if (Color.TryParse(theme.BackgroundElevated, out var elevatedCol))
        {
            _containerHeaderBrush = new SolidColorBrush(elevatedCol);
            _hoverCardBgBrush = new SolidColorBrush(Color.FromArgb(240, elevatedCol.R, elevatedCol.G, elevatedCol.B));
        }

        if (Color.TryParse(theme.BorderDefault, out var borderCol))
        {
            _containerBorderBrush = new SolidColorBrush(borderCol);
            _containerBorderPen = new Pen(_containerBorderBrush, 1);
        }

        if (Color.TryParse(theme.TextPrimary, out var textPrimaryCol))
            _textWhiteBrush = new SolidColorBrush(textPrimaryCol);

        if (Color.TryParse(theme.TextMuted, out var textMutedCol))
            _textMutedBrush = new SolidColorBrush(textMutedCol);

        if (Color.TryParse(theme.AccentColor, out var accentCol))
            _accentTextBrush = new SolidColorBrush(accentCol);

        _hoverBorderBrush = theme.IsDark ? Brushes.White : new SolidColorBrush(Color.Parse("#1A1A24"));
        _hoverBorderPen = new Pen(_hoverBorderBrush, 1.5);

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(_backgroundBrush, new Rect(Bounds.Size));

        var localizationService = LocalizationService.Instance;
        var root = _currentRoot ?? RootItem;
        if (root is null || root.Children.Count == 0)
        {
            var placeholder = new FormattedText(
                localizationService["Treemap_Placeholder"],
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                14,
                Brushes.Gray);
            context.DrawText(placeholder, new Point(20, 20));
            return;
        }

        // Header / Breadcrumb superiore
        string breadcrumb = _currentRoot?.Parent != null 
            ? localizationService.Get("Treemap_BreadcrumbDrillUp", _currentRoot.Name, _currentRoot.Kind) 
            : localizationService["Treemap_BreadcrumbRoot"];
        var breadcrumbFmt = new FormattedText(
            breadcrumb,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            HeaderTypeface,
            12,
            _currentRoot?.Parent != null ? _accentTextBrush : _textMutedBrush);
        context.DrawText(breadcrumbFmt, new Point(16, 12));

        // Recursively render all nodes
        RenderItemRecursive(context, root);

        // Draw floating tooltip/card at bottom left for hovered element
        if (_hoveredItem != null && _hoveredItem != root)

            DrawHoverCard(context, _hoveredItem);
    }

    private static readonly IBrush BrushLow = new SolidColorBrush(Color.Parse("#164E3D"));
    private static readonly IBrush BrushMedLow = new SolidColorBrush(Color.Parse("#1D6B52"));
    private static readonly IBrush BrushMed = new SolidColorBrush(Color.Parse("#8C6B14"));
    private static readonly IBrush BrushHigh = new SolidColorBrush(Color.Parse("#A84B1A"));
    private static readonly IBrush BrushVeryHigh = new SolidColorBrush(Color.Parse("#961C2C"));
    private static readonly IBrush LeafBorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
    private static readonly IBrush SubLabelBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));

    private IPen _containerBorderPen = new Pen(new SolidColorBrush(Color.Parse("#35354A")), 1);
    private IPen _selectedBorderPen = new Pen(new SolidColorBrush(Color.Parse("#FFD700")), 2.5);
    private IPen _hoverBorderPen = new Pen(Brushes.White, 1.5);
    private IPen _leafBorderPen = new Pen(LeafBorderBrush, 1);

    private void RenderItemRecursive(DrawingContext context, TreemapItem item)
    {
        if (item.Bounds.Width <= 1 || item.Bounds.Height <= 1) return;

        var rect = new Rect(item.Bounds.X, item.Bounds.Y, item.Bounds.Width, item.Bounds.Height);

        if (!item.IsLeaf)
        {
            // Container (Module / Class)
            if (item.Depth > 0)
            {
                context.FillRectangle(_containerHeaderBrush, rect);
                context.DrawRectangle(_containerBorderPen, rect);

                // Title of the form if visible and legible
                if (rect.Width > 50 && rect.Height > 18)
                {
                    string header = $"{item.Name} ({LocalizationService.Instance.Get("Treemap_MembersCount", item.Children.Count)})";
                    var hFmt = new FormattedText(
                        header,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        HeaderTypeface,
                        10,
                        _textWhiteBrush);
                    context.DrawText(hFmt, new Point(rect.X + 6, rect.Y + 3));
                }
            }

            foreach (var child in item.Children)
            {
                RenderItemRecursive(context, child);
            }
        }
        else
        {
            // Leaf (Method, Property or Leaf Module)
            var fillBrush = GetComplexityBrush(item.MetricScore);
            context.FillRectangle(fillBrush, rect);

            if (_selectedItem?.Id == item.Id)

                context.DrawRectangle(_selectedBorderPen, rect);

            else if (_hoveredItem?.Id == item.Id)

    context.DrawRectangle(_hoverBorderPen, rect);

else
            {
                context.DrawRectangle(_leafBorderPen, rect);
            }

            // Label text if there is enough space
            if (rect.Width > 45 && rect.Height > 22)
            {
                string label = item.Name;
                if (rect.Width < 80 && label.Length > 8) label = label[..7] + "…";

                var nameFmt = new FormattedText(
                    label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    rect.Width > 90 ? 11 : 9,
                    _textWhiteBrush);
                context.DrawText(nameFmt, new Point(rect.X + 4, rect.Y + 3));

                if (rect.Height > 38 && rect.Width > 60)
                {
                    var valFmt = new FormattedText(
                        $"CC: {item.MetricScore:F1}",
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        SubLabelTypeface,
                        9,
                        SubLabelBrush);
                    context.DrawText(valFmt, new Point(rect.X + 4, rect.Y + nameFmt.Height + 2));
                }
            }
        }
    }

    /// <summary>
    /// Returns the color brush based on complexity score:
    /// &lt;= 2.0: Verde scuro (Bassa)
    /// &lt;= 4.5: Verde medio
    /// &lt;= 7.0: Giallo ambra (Media)
    /// &lt;= 11.0: Arancione (Alta)
    /// > 11.0: Rosso intenso (Altissima complessità ciclomatica / Rischio)
    /// </summary>
    private static IBrush GetComplexityBrush(double score)
    {
        if (score <= 2.0) return BrushLow;
        if (score <= 4.5) return BrushMedLow;
        if (score <= 7.0) return BrushMed;
        if (score <= 11.0) return BrushHigh;
        return BrushVeryHigh;
    }

    /// <summary>
    /// Draw the floating information card (Hover Card) with the node name, metric score and code/weight lines.
    /// </summary>
    private void DrawHoverCard(DrawingContext context, TreemapItem item)
    {
        var titleFmt = new FormattedText(
            $"{item.Kind}: {item.QualifiedName}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            HeaderTypeface,
            12,
            _textWhiteBrush);

        var subFmt = new FormattedText(
            LocalizationService.Instance.Get("Treemap_HoverSub", item.MetricScore, item.Value),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            _accentTextBrush);

        const double paddingH = 12;
        const double paddingV = 8;
        const double lineSpacing = 4;

        double maxTextWidth = Math.Max(titleFmt.Width, subFmt.Width);
        double maxWidthAvailable = Math.Max(100, Bounds.Width - 32);
        double cardWidth = Math.Min(Math.Max(320, maxTextWidth + (paddingH * 2)), maxWidthAvailable);

        double contentWidth = Math.Max(10, cardWidth - (paddingH * 2));
        titleFmt.MaxTextWidth = contentWidth;
        titleFmt.Trimming = TextTrimming.CharacterEllipsis;

        subFmt.MaxTextWidth = contentWidth;
        subFmt.Trimming = TextTrimming.CharacterEllipsis;

        double cardHeight = Math.Max(52, paddingV + titleFmt.Height + lineSpacing + subFmt.Height + paddingV);
        var cardRect = new Rect(16, Bounds.Height - cardHeight - 12, cardWidth, cardHeight);

        context.FillRectangle(_hoverCardBgBrush, cardRect);
        context.DrawRectangle(new Pen(_containerBorderBrush, 1), cardRect);

        context.DrawText(titleFmt, new Point(cardRect.X + paddingH, cardRect.Y + paddingV));
        context.DrawText(subFmt, new Point(cardRect.X + paddingH, cardRect.Y + paddingV + titleFmt.Height + lineSpacing));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        var hit = FindItemAt(_currentRoot ?? RootItem, pos.X, pos.Y);
        if (_hoveredItem != hit)
        {
            _hoveredItem = hit;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);

        if (pos.Y < 30 && _currentRoot?.Parent != null)
        {
            DrillUp();
            return;
        }

        var hit = FindItemAt(_currentRoot ?? RootItem, pos.X, pos.Y);
        if (hit != null)
        {
            _selectedItem = hit;
            ItemSelected?.Invoke(this, hit);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Handles double-clicking a non-leaf item (module or class) to drill down hierarchically.
    /// </summary>
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var pos = e.GetPosition(this);
        var hit = FindItemAt(_currentRoot ?? RootItem, pos.X, pos.Y);

        if (hit != null && !hit.IsLeaf)
        {
            _currentRoot = hit;
            InvalidateLayoutAndVisual();
        }
    }

    /// <summary>
    /// Recursive search for the leaf or container treemap element whose coordinates enclose the point (px, py).
    /// </summary>
    private TreemapItem? FindItemAt(TreemapItem? current, double px, double py)
    {
        if (current == null || !current.Bounds.Contains(px, py)) return null;

        // Look first in your children
        foreach (var child in current.Children)
        {
            var found = FindItemAt(child, px, py);
            if (found != null) return found;
        }

        return current;
    }
}
