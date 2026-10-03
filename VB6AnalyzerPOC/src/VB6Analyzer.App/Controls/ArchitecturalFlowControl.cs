using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.ArchitecturalFlow;

namespace Macondo.App.Controls;

/// <summary>
/// Controllo visuale interattivo Skia per il Diagramma Chord dei Flussi Architetturali tra moduli e layer.
/// Mostra dipendenze, volumi di chiamata e violazioni di confine architetturale.
/// </summary>
public sealed class ArchitecturalFlowControl : Control
{
    public static readonly StyledProperty<ArchitecturalFlowDataset?> DatasetProperty =
        AvaloniaProperty.Register<ArchitecturalFlowControl, ArchitecturalFlowDataset?>(nameof(Dataset));

    public ArchitecturalFlowDataset? Dataset
    {
        get => GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    public event EventHandler<FlowNode?>? NodeSelected;

    private FlowNode? _hoveredNode;
    private FlowLink? _hoveredLink;
    private FlowNode? _selectedNode;
    private Point _mousePosition;

    private static readonly Typeface NodeFont = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface TooltipFont = new("Inter", FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface KpiFont = new("Inter", FontStyle.Normal, FontWeight.Bold);

    private IBrush _backgroundBrush = new SolidColorBrush(Color.Parse("#14141E"));
    private IBrush _textBrush = new SolidColorBrush(Color.Parse("#E0E0E0"));
    private IBrush _secondaryTextBrush = new SolidColorBrush(Color.Parse("#9090A0"));
    private IBrush _violationBrush = new SolidColorBrush(Color.Parse("#EF4444"));

    public ArchitecturalFlowControl()
    {
        ClipToBounds = true;
        LocalizationService.Instance.LanguageChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DatasetProperty)
        {
            _hoveredNode = null;
            _hoveredLink = null;
            _selectedNode = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _mousePosition = e.GetPosition(this);

        var dataset = Dataset;
        if (dataset == null || dataset.Nodes.Count == 0) return;

        double cx = Bounds.Width / 2.0;
        double cy = Bounds.Height / 2.0 + 15.0;
        double radius = Math.Min(Bounds.Width, Bounds.Height) * 0.36;
        double innerRadius = radius - 18.0;

        double dx = _mousePosition.X - cx;
        double dy = _mousePosition.Y - cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        FlowNode? hitNode = null;

        // Hit testing anello esterno nodi
        if (dist >= innerRadius - 5 && dist <= radius + 25)
        {
            double angle = Math.Atan2(dy, dx);
            if (angle < 0) angle += 2 * Math.PI;

            hitNode = dataset.Nodes.FirstOrDefault(n => angle >= n.StartAngle && angle <= n.EndAngle);
        }

        if (hitNode != _hoveredNode)
        {
            _hoveredNode = hitNode;
            _hoveredLink = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoveredNode != null || _hoveredLink != null)
        {
            _hoveredNode = null;
            _hoveredLink = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_hoveredNode != null)
        {
            _selectedNode = _hoveredNode;
            NodeSelected?.Invoke(this, _selectedNode);
            InvalidateVisual();
        }
        else
        {
            _selectedNode = null;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 10 || bounds.Height <= 10) return;

        // Sfondo
        context.FillRectangle(_backgroundBrush, new Rect(0, 0, bounds.Width, bounds.Height));

        var dataset = Dataset;
        if (dataset == null || dataset.Nodes.Count == 0)
        {
            RenderEmptyState(context, bounds);
            return;
        }

        // 1. Barra KPI superiore
        RenderKpiHeader(context, bounds, dataset);

        // 2. Geometria del Diagramma Chord
        double cx = bounds.Width / 2.0;
        double cy = bounds.Height / 2.0 + 20.0;
        double outerRadius = Math.Min(bounds.Width, bounds.Height) * 0.36;
        double innerRadius = outerRadius - 16.0;

        if (outerRadius < 50) return;

        var activeFilterNode = _hoveredNode ?? _selectedNode;

        // 3. Disegno dei nastri di connessione (Links)
        foreach (var link in dataset.Links.OrderBy(l => l.IsLayerViolation ? 1 : 0))
        {
            bool isConnected = activeFilterNode == null || link.SourceId == activeFilterNode.Id || link.TargetId == activeFilterNode.Id;
            double alpha = activeFilterNode == null ? 0.35 : (isConnected ? 0.75 : 0.06);

            var srcNode = dataset.FindNodeById(link.SourceId);
            if (srcNode == null) continue;

            Color linkColor = link.IsLayerViolation
                ? Color.FromArgb((byte)(alpha * 255), 239, 68, 68) // Rosso per violazioni
                : Color.FromArgb((byte)(alpha * 255),
                    Color.Parse(srcNode.ColorHex).R,
                    Color.Parse(srcNode.ColorHex).G,
                    Color.Parse(srcNode.ColorHex).B);

            RenderChordRibbon(context, cx, cy, innerRadius, link, linkColor, isConnected && link.IsLayerViolation);
        }

        // 4. Disegno dell'anello esterno con i nodi e label
        foreach (var node in dataset.Nodes)
        {
            bool isHighlighted = activeFilterNode == null || node.Id == activeFilterNode.Id ||
                                 dataset.Links.Any(l => (l.SourceId == activeFilterNode.Id && l.TargetId == node.Id) || (l.TargetId == activeFilterNode.Id && l.SourceId == node.Id));

            double nodeAlpha = isHighlighted ? 1.0 : 0.25;
            Color nodeColor = Color.FromArgb((byte)(nodeAlpha * 255),
                Color.Parse(node.ColorHex).R,
                Color.Parse(node.ColorHex).G,
                Color.Parse(node.ColorHex).B);

            RenderNodeArc(context, cx, cy, innerRadius, outerRadius, node, nodeColor, node == _hoveredNode || node == _selectedNode);
            RenderNodeLabel(context, cx, cy, outerRadius + 8, node, isHighlighted);
        }

        // 5. Tooltip di dettaglio su hover
        if (_hoveredNode != null)

            RenderTooltip(context, bounds, _hoveredNode, dataset);
    }

    private void RenderKpiHeader(DrawingContext context, Rect bounds, ArchitecturalFlowDataset dataset)
    {
        double headerH = 44.0;
        var headerRect = new Rect(12, 8, bounds.Width - 24, headerH);
        var bg = new SolidColorBrush(Color.FromArgb(80, 30, 32, 44));
        var border = new Pen(new SolidColorBrush(Color.FromArgb(120, 60, 65, 80)), 1);

        context.DrawRectangle(bg, border, new RoundedRect(headerRect, 6));

        double colW = headerRect.Width / 4.0;
        var localizationService = LocalizationService.Instance;

        RenderKpiItem(context, headerRect.X, headerRect.Y, colW, localizationService["Flow_Kpi_Modules"], dataset.TotalNodesCount.ToString("N0"), "#3B82F6");
        RenderKpiItem(context, headerRect.X + colW, headerRect.Y, colW, localizationService["Flow_Kpi_TotalLinks"], dataset.TotalLinksCount.ToString("N0"), "#10B981");
        RenderKpiItem(context, headerRect.X + colW * 2, headerRect.Y, colW, localizationService["Flow_Kpi_Volume"], dataset.TotalFlowVolume.ToString("N0"), "#8B5CF6");
        RenderKpiItem(context, headerRect.X + colW * 3, headerRect.Y, colW, localizationService["Flow_Kpi_Violations"], dataset.LayerViolationsCount.ToString("N0"), dataset.LayerViolationsCount > 0 ? "#EF4444" : "#10B981");
    }

    private static void RenderKpiItem(
        DrawingContext context,
        double x,
        double y,
        double w,
        string label,
        string val,
        string colorHex)
    {
        var lblText = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, KpiFont, 9.5, new SolidColorBrush(Color.Parse("#9090A0")));
        var valText = new FormattedText(val, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, KpiFont, 15.0, new SolidColorBrush(Color.Parse(colorHex)));

        context.DrawText(lblText, new Point(x + 16, y + 6));
        context.DrawText(valText, new Point(x + 16, y + 20));
    }

    private static void RenderNodeArc(
        DrawingContext context,
        double cx,
        double cy,
        double innerR,
        double outerR,
        FlowNode node,
        Color color,
        bool isSelected)
    {
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            double startAngle = node.StartAngle;
            double endAngle = node.EndAngle;

            double p1x = cx + outerR * Math.Cos(startAngle);
            double p1y = cy + outerR * Math.Sin(startAngle);
            double p2x = cx + outerR * Math.Cos(endAngle);
            double p2y = cy + outerR * Math.Sin(endAngle);

            double p3x = cx + innerR * Math.Cos(endAngle);
            double p3y = cy + innerR * Math.Sin(endAngle);
            double p4x = cx + innerR * Math.Cos(startAngle);
            double p4y = cy + innerR * Math.Sin(startAngle);

            bool isLargeArc = (endAngle - startAngle) > Math.PI;

            ctx.BeginFigure(new Point(p1x, p1y), true);
            ctx.ArcTo(new Point(p2x, p2y), new Size(outerR, outerR), 0, isLargeArc, SweepDirection.Clockwise);
            ctx.LineTo(new Point(p3x, p3y));
            ctx.ArcTo(new Point(p4x, p4y), new Size(innerR, innerR), 0, isLargeArc, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
        }

        var brush = new SolidColorBrush(color);
        var pen = isSelected ? new Pen(Brushes.White, 2) : new Pen(new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)), 1);
        context.DrawGeometry(brush, pen, geom);
    }

    private static void RenderChordRibbon(
        DrawingContext context,
        double cx,
        double cy,
        double r,
        FlowLink link,
        Color color,
        bool isViolation)
    {
        double p1x = cx + r * Math.Cos(link.SourceStartAngle);
        double p1y = cy + r * Math.Sin(link.SourceStartAngle);
        double p2x = cx + r * Math.Cos(link.SourceEndAngle);
        double p2y = cy + r * Math.Sin(link.SourceEndAngle);

        double p3x = cx + r * Math.Cos(link.TargetStartAngle);
        double p3y = cy + r * Math.Sin(link.TargetStartAngle);
        double p4x = cx + r * Math.Cos(link.TargetEndAngle);
        double p4y = cy + r * Math.Sin(link.TargetEndAngle);

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(p1x, p1y), true);
            ctx.ArcTo(new Point(p2x, p2y), new Size(r, r), 0, false, SweepDirection.Clockwise);
            ctx.CubicBezierTo(new Point(cx, cy), new Point(cx, cy), new Point(p3x, p3y));
            ctx.ArcTo(new Point(p4x, p4y), new Size(r, r), 0, false, SweepDirection.Clockwise);
            ctx.CubicBezierTo(new Point(cx, cy), new Point(cx, cy), new Point(p1x, p1y));
            ctx.EndFigure(true);
        }

        var brush = new SolidColorBrush(color);
        var pen = isViolation ? new Pen(Brushes.Red, 1.5) : null;
        context.DrawGeometry(brush, pen, geom);
    }

    private void RenderNodeLabel(
        DrawingContext context,
        double cx,
        double cy,
        double r,
        FlowNode node,
        bool isHighlighted)
    {
        double midAngle = node.MidAngle;
        double lx = cx + r * Math.Cos(midAngle);
        double ly = cy + r * Math.Sin(midAngle);

        bool isRightHalf = Math.Cos(midAngle) >= 0;

        string name = node.Name.Length > 16 ? node.Name[..14] + ".." : node.Name;
        var brush = isHighlighted ? _textBrush : _secondaryTextBrush;
        var ft = new FormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NodeFont, 10.0, brush);

        double textX = isRightHalf ? lx : lx - ft.Width;
        double textY = ly - ft.Height / 2.0;

        context.DrawText(ft, new Point(textX, textY));
    }

    private void RenderTooltip(
        DrawingContext context,
        Rect bounds,
        FlowNode node,
        ArchitecturalFlowDataset dataset)
    {
        var localizationService = LocalizationService.Instance;
        var outLinks = dataset.Links.Where(l => l.SourceId == node.Id).ToList();
        var inLinks = dataset.Links.Where(l => l.TargetId == node.Id).ToList();
        int violations = outLinks.Count(l => l.IsLayerViolation) + inLinks.Count(l => l.IsLayerViolation);

        string title = $"{node.Name} ({node.LayerName})";
        string sub = $"{localizationService["General_Loc"]}: {node.Loc:N0} | {localizationService["General_CC"]}: {node.CyclomaticComplexity:F1} | {localizationService["General_Members"]}: {node.MemberCount}\n" +
                     $"{localizationService.Get("Flow_Tooltip_Inflow", node.InflowVolume, inLinks.Count)}\n" +
                     $"{localizationService.Get("Flow_Tooltip_Outflow", node.OutflowVolume, outLinks.Count)}\n" +
                     $"{localizationService.Get("Flow_Tooltip_Violations", violations)}";

        var ftTitle = new FormattedText(title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, KpiFont, 12.0, Brushes.White);
        var ftSub = new FormattedText(sub, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TooltipFont, 10.5, new SolidColorBrush(Color.Parse("#D0D0E0")));

        double cardW = Math.Max(ftTitle.Width, ftSub.Width) + 24.0;
        double cardH = ftTitle.Height + ftSub.Height + 18.0;

        double tipX = _mousePosition.X + 16.0;
        double tipY = _mousePosition.Y + 16.0;

        if (tipX + cardW > bounds.Width - 10) tipX = bounds.Width - cardW - 10;
        if (tipY + cardH > bounds.Height - 10) tipY = bounds.Height - cardH - 10;

        var cardRect = new Rect(tipX, tipY, cardW, cardH);
        var cardBg = new SolidColorBrush(Color.FromArgb(235, 20, 22, 30));
        var cardBorder = new Pen(new SolidColorBrush(Color.Parse(node.ColorHex)), 1.5);

        context.DrawRectangle(cardBg, cardBorder, new RoundedRect(cardRect, 6));
        context.DrawText(ftTitle, new Point(tipX + 12, tipY + 8));
        context.DrawText(ftSub, new Point(tipX + 12, tipY + 10 + ftTitle.Height));
    }

    private void RenderEmptyState(DrawingContext context, Rect bounds)
    {
        var localizationService = LocalizationService.Instance;
        var text = new FormattedText(
            localizationService["Flow_Placeholder"],
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            TooltipFont,
            13.0,
            _secondaryTextBrush);

        context.DrawText(text, new Point((bounds.Width - text.Width) / 2.0, (bounds.Height - text.Height) / 2.0));
    }

    public void ApplyTheme(ThemeDefinition theme)
    {
        _backgroundBrush = new SolidColorBrush(Color.Parse(theme.BackgroundPrimary));
        _textBrush = new SolidColorBrush(Color.Parse(theme.TextPrimary));
        _secondaryTextBrush = new SolidColorBrush(Color.Parse(theme.TextSecondary));
        InvalidateVisual();
    }
}
