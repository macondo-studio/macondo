using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.ControlFlow;

namespace Macondo.App.Controls;

/// <summary>
/// Controllo custom Avalonia Skia per il rendering del Control Flow Graph (CFG) a livello di singola procedura/metodo.
/// Visualizza i blocchi base, i rami condizionali True/False, cicli, handler di errore e la complessità ciclomatica.
/// </summary>
public sealed class ControlFlowControl : Control
{
    public static readonly StyledProperty<ProcedureControlFlowGraph?> GraphProperty =
        AvaloniaProperty.Register<ControlFlowControl, ProcedureControlFlowGraph?>(nameof(Graph));

    public ProcedureControlFlowGraph? Graph
    {
        get => GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public event EventHandler<ControlFlowNode?>? BlockSelected;

    private double _scale = 1.0;
    private double _offsetX = 30.0;
    private double _offsetY = 30.0;

    private Point _pointerDownScreenPos;
    private bool _isPointerDown;
    private bool _isPanning;
    private const double PanThreshold = 4.0;

    private ControlFlowNode? _selectedBlock;
    private ControlFlowNode? _hoveredBlock;

    private static readonly Typeface HeaderTypeface = new("Inter", FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface CodeTypeface = new("Consolas, Courier New, monospace", FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface BadgeTypeface = new("Inter", FontStyle.Normal, FontWeight.Bold);

    private IBrush _backgroundBrush = new SolidColorBrush(Color.Parse("#14141E"));
    private IBrush _blockBgBrush = new SolidColorBrush(Color.Parse("#1E1E2C"));
    private IBrush _entryBgBrush = new SolidColorBrush(Color.Parse("#164032"));
    private IBrush _exitBgBrush = new SolidColorBrush(Color.Parse("#252538"));
    private IBrush _decisionBgBrush = new SolidColorBrush(Color.Parse("#382A14"));
    private IBrush _errorBgBrush = new SolidColorBrush(Color.Parse("#441820"));
    private IBrush _blockBorderBrush = new SolidColorBrush(Color.Parse("#38384E"));
    private IBrush _decisionBorderBrush = new SolidColorBrush(Color.Parse("#D49020"));
    private IBrush _errorBorderBrush = new SolidColorBrush(Color.Parse("#E04050"));
    private IBrush _selectedBorderBrush = new SolidColorBrush(Color.Parse("#FFD700"));
    private IBrush _textWhiteBrush = Brushes.WhiteSmoke;
    private IBrush _textCodeBrush = new SolidColorBrush(Color.Parse("#4AE8D0"));
    private IBrush _textMutedBrush = new SolidColorBrush(Color.Parse("#9A9AB0"));
    private IBrush _badgeBgBrush = new SolidColorBrush(Color.FromArgb(220, 20, 20, 30));

    private IPen _trueBranchPen = new Pen(new SolidColorBrush(Color.Parse("#30C060")), 1.8);
    private IPen _falseBranchPen = new Pen(new SolidColorBrush(Color.Parse("#E04050")), 1.8);
    private IPen _errorBranchPen = new Pen(new SolidColorBrush(Color.Parse("#FFA020")), 1.8, DashStyle.Dash);
    private IPen _fallthroughPen = new Pen(new SolidColorBrush(Color.Parse("#5088E0")), 1.5);

    static ControlFlowControl()
    {
        AffectsRender<ControlFlowControl>(GraphProperty);
        FocusableProperty.OverrideDefaultValue<ControlFlowControl>(true);
    }

    public ControlFlowControl()
    {
        ClipToBounds = true;
    }

    public void ResetView()
    {
        _scale = 1.0;
        _offsetX = 30.0;
        _offsetY = 30.0;
        InvalidateVisual();
    }

    public void ApplyTheme(ThemeDefinition theme)
    {
        if (Color.TryParse(theme.GraphBackground, out var bgCol))
            _backgroundBrush = new SolidColorBrush(bgCol);

        if (Color.TryParse(theme.GraphNodeFill, out var blockFillCol))
            _blockBgBrush = new SolidColorBrush(blockFillCol);

        if (Color.TryParse(theme.GraphNodeStroke, out var borderCol))
            _blockBorderBrush = new SolidColorBrush(borderCol);

        if (Color.TryParse(theme.TextPrimary, out var textPrimaryCol))
            _textWhiteBrush = new SolidColorBrush(textPrimaryCol);

        if (Color.TryParse(theme.TextMuted, out var textMutedCol))
            _textMutedBrush = new SolidColorBrush(textMutedCol);

        if (Color.TryParse(theme.AccentColor, out var accentCol))
            _textCodeBrush = new SolidColorBrush(accentCol);

        if (Color.TryParse(theme.BackgroundElevated, out var elevatedCol))
            _badgeBgBrush = new SolidColorBrush(Color.FromArgb(220, elevatedCol.R, elevatedCol.G, elevatedCol.B));

        if (theme.IsDark)
        {
            _entryBgBrush = new SolidColorBrush(Color.Parse("#164032"));
            _exitBgBrush = new SolidColorBrush(Color.Parse("#252538"));
            _decisionBgBrush = new SolidColorBrush(Color.Parse("#382A14"));
            _errorBgBrush = new SolidColorBrush(Color.Parse("#441820"));
            _trueBranchPen = new Pen(new SolidColorBrush(Color.Parse("#30C060")), 1.8);
            _falseBranchPen = new Pen(new SolidColorBrush(Color.Parse("#E04050")), 1.8);
            _errorBranchPen = new Pen(new SolidColorBrush(Color.Parse("#FFA020")), 1.8, DashStyle.Dash);
            _fallthroughPen = new Pen(new SolidColorBrush(Color.Parse("#5088E0")), 1.5);
        }
        else
        {
            _entryBgBrush = new SolidColorBrush(Color.FromArgb(35, 40, 180, 100));
            _exitBgBrush = new SolidColorBrush(Color.FromArgb(30, 80, 80, 100));
            _decisionBgBrush = new SolidColorBrush(Color.FromArgb(35, 255, 180, 30));
            _errorBgBrush = new SolidColorBrush(Color.FromArgb(35, 255, 60, 80));
            _trueBranchPen = new Pen(new SolidColorBrush(Color.Parse("#1B8A4D")), 1.8);
            _falseBranchPen = new Pen(new SolidColorBrush(Color.Parse("#D32F2F")), 1.8);
            _errorBranchPen = new Pen(new SolidColorBrush(Color.Parse("#D97706")), 1.8, DashStyle.Dash);
            _fallthroughPen = new Pen(new SolidColorBrush(Color.Parse("#1976D2")), 1.5);
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(_backgroundBrush, new Rect(Bounds.Size));

        var loc = LocalizationService.Instance;
        var graph = Graph;
        if (graph is null || graph.Blocks.Count == 0)
        {
            var placeholder = new FormattedText(
                loc["ControlFlow_Placeholder"],
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                HeaderTypeface,
                14,
                Brushes.Gray);
            context.DrawText(placeholder, new Point(20, 20));
            return;
        }

        // Header superiore con metadati della procedura
        string header = loc.Get("ControlFlow_Header", graph.ProcedureName, graph.ContainerName, graph.CyclomaticComplexity, graph.Blocks.Count);
        var hFmt = new FormattedText(header, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, HeaderTypeface, 12, _textWhiteBrush);
        context.DrawText(hFmt, new Point(16, 12));

        using (context.PushTransform(Matrix.CreateTranslation(_offsetX, _offsetY)))
        using (context.PushTransform(Matrix.CreateScale(_scale, _scale)))
        {
            // 1. Disegna i rami e salti orientati
            foreach (var edge in graph.Edges)
            {
                var src = graph.FindBlock(edge.SourceBlockId);
                var tgt = graph.FindBlock(edge.TargetBlockId);
                if (src == null || tgt == null) continue;

                var start = new Point(src.X + src.Width / 2.0, src.Y + src.Height);
                var end = new Point(tgt.X + tgt.Width / 2.0, tgt.Y);

                var pen = edge.BranchKind switch
                {
                    ControlFlowBranchKind.TrueBranch => _trueBranchPen,
                    ControlFlowBranchKind.FalseBranch => _falseBranchPen,
                    ControlFlowBranchKind.ExceptionJump => _errorBranchPen,
                    _ => _fallthroughPen
                };

                // Curva Bezier verticale
                var geom = new StreamGeometry();
                using (var ctx = geom.Open())
                {
                    ctx.BeginFigure(start, false);
                    double dy = Math.Abs(end.Y - start.Y) * 0.5;
                    ctx.CubicBezierTo(new Point(start.X, start.Y + dy), new Point(end.X, end.Y - dy), end);
                }
                context.DrawGeometry(null, pen, geom);

                // Freccia finale
                DrawArrowHead(context, end, pen.Brush ?? Brushes.DodgerBlue);

                // Etichetta del ramo (True / False / On Error)
                if (!string.IsNullOrEmpty(edge.ConditionLabel))
                {
                    var midPoint = new Point((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);
                    var lblFmt = new FormattedText(edge.ConditionLabel, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BadgeTypeface, 9, pen.Brush ?? _textWhiteBrush);
                    var badgeRect = new Rect(midPoint.X - lblFmt.Width / 2.0 - 4, midPoint.Y - lblFmt.Height / 2.0 - 2, lblFmt.Width + 8, lblFmt.Height + 4);
                    context.FillRectangle(_badgeBgBrush, badgeRect);
                    context.DrawRectangle(new Pen(_blockBorderBrush, 0.8), badgeRect);
                    context.DrawText(lblFmt, new Point(badgeRect.X + 4, badgeRect.Y + 2));
                }
            }

            // 2. Disegna i Blocchi Base
            foreach (var block in graph.Blocks)
            {
                var blockRect = new Rect(block.X, block.Y, block.Width, block.Height);
                var (bgBrush, borderPen) = GetBlockStyling(block.BlockKind);

                context.FillRectangle(bgBrush, blockRect, 4.0f);

                if (_selectedBlock?.Id == block.Id)
                {
                    context.DrawRectangle(new Pen(_selectedBorderBrush, 2.5), blockRect, 4.0f);
                }
                else if (_hoveredBlock?.Id == block.Id)
                {
                    context.DrawRectangle(new Pen(Brushes.White, 1.5), blockRect, 4.0f);
                }
                else
                {
                    context.DrawRectangle(borderPen, blockRect, 4.0f);
                }

                // Titolo del blocco
                var titleFmt = new FormattedText(block.Title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, HeaderTypeface, 11, _textWhiteBrush);
                context.DrawText(titleFmt, new Point(block.X + 8, block.Y + 6));

                // Istruzioni contenute nel blocco
                double stmtY = block.Y + 24;
                foreach (var stmt in block.Statements.Take(2))
                {
                    string shortStmt = stmt.Length > 28 ? stmt[..26] + "…" : stmt;
                    var codeFmt = new FormattedText(shortStmt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, CodeTypeface, 9, _textCodeBrush);
                    context.DrawText(codeFmt, new Point(block.X + 8, stmtY));
                    stmtY += 14;
                }
            }
        }
    }

    private (IBrush Bg, IPen Border) GetBlockStyling(ControlFlowBlockKind kind) => kind switch
    {
        ControlFlowBlockKind.Entry => (_entryBgBrush, new Pen(new SolidColorBrush(Color.Parse("#30A070")), 1.5)),
        ControlFlowBlockKind.Exit => (_exitBgBrush, new Pen(new SolidColorBrush(Color.Parse("#606080")), 1.5)),
        ControlFlowBlockKind.DecisionIf or ControlFlowBlockKind.DecisionSelect or ControlFlowBlockKind.LoopHeader => (_decisionBgBrush, new Pen(_decisionBorderBrush, 1.5)),
        ControlFlowBlockKind.ExceptionHandler => (_errorBgBrush, new Pen(_errorBorderBrush, 1.5)),
        _ => (_blockBgBrush, new Pen(_blockBorderBrush, 1.0))
    };

    private static void DrawArrowHead(DrawingContext context, Point tip, IBrush brush)
    {
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(tip, true);
            ctx.LineTo(new Point(tip.X - 5, tip.Y - 8));
            ctx.LineTo(new Point(tip.X + 5, tip.Y - 8));
        }
        context.DrawGeometry(brush, null, geom);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos = e.GetPosition(this);

        double zoomFactor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        double newScale = Math.Clamp(_scale * zoomFactor, 0.3, 3.0);

        _offsetX = pos.X - (pos.X - _offsetX) * (newScale / _scale);
        _offsetY = pos.Y - (pos.Y - _offsetY) * (newScale / _scale);
        _scale = newScale;

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var currentPoint = e.GetCurrentPoint(this);
        if (currentPoint.Properties.IsLeftButtonPressed || currentPoint.Properties.IsMiddleButtonPressed)
        {
            _pointerDownScreenPos = e.GetPosition(this);
            _isPointerDown = true;
            _isPanning = false;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_isPointerDown)
        {
            var delta = pos - _pointerDownScreenPos;
            if (!_isPanning && (Math.Abs(delta.X) > PanThreshold || Math.Abs(delta.Y) > PanThreshold))
            {
                _isPanning = true;
            }

            if (_isPanning)
            {
                _offsetX += delta.X;
                _offsetY += delta.Y;
                _pointerDownScreenPos = pos;
                InvalidateVisual();
                return;
            }
        }

        var localX = (pos.X - _offsetX) / _scale;
        var localY = (pos.Y - _offsetY) / _scale;

        var hit = Graph?.Blocks.FirstOrDefault(b => localX >= b.X && localX <= b.X + b.Width && localY >= b.Y && localY <= b.Y + b.Height);
        if (_hoveredBlock != hit)
        {
            _hoveredBlock = hit;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isPointerDown && !_isPanning && Graph != null)
        {
            var pos = e.GetPosition(this);
            var localX = (pos.X - _offsetX) / _scale;
            var localY = (pos.Y - _offsetY) / _scale;

            var hit = Graph.Blocks.FirstOrDefault(b => localX >= b.X && localX <= b.X + b.Width && localY >= b.Y && localY <= b.Y + b.Height);
            _selectedBlock = hit;
            BlockSelected?.Invoke(this, hit);
            InvalidateVisual();
        }

        _isPointerDown = false;
        _isPanning = false;
    }
}
