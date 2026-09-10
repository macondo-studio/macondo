using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Macondo.App.Services;
using Macondo.Semantic.Analytics.ScreenFlow;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

/// <summary>
/// Controllo custom Avalonia Skia per il grafo di navigazione e flusso delle schermate / Form UI.
/// Visualizza le transizioni e gli eventi che scatenano l'apertura e chiusura di dialoghi e form.
/// </summary>
public sealed class ScreenFlowControl : Control
{
    public static readonly StyledProperty<ScreenFlowGraph?> GraphProperty =
        AvaloniaProperty.Register<ScreenFlowControl, ScreenFlowGraph?>(nameof(Graph));

    public ScreenFlowGraph? Graph
    {
        get => GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public event EventHandler<ScreenFlowNode?>? ScreenSelected;

    private double _scale = 1.0;
    private double _offsetX = 40.0;
    private double _offsetY = 40.0;

    private Point _pointerDownScreenPos;
    private bool _isPointerDown;
    private bool _isPanning;
    private const double PanThreshold = 4.0;

    private ScreenFlowNode? _selectedScreen;
    private ScreenFlowNode? _hoveredScreen;

    private static readonly Typeface HeaderTypeface = new("Inter", FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Medium);
    private static readonly Typeface BadgeTypeface = new("Inter", FontStyle.Normal, FontWeight.Bold);

    private IBrush _backgroundBrush = new SolidColorBrush(Color.Parse("#14141E"));
    private IBrush _cardBgBrush = new SolidColorBrush(Color.Parse("#1E1E2C"));
    private IBrush _cardHeaderBrush = new SolidColorBrush(Color.Parse("#26263A"));
    private IBrush _startupHeaderBrush = new SolidColorBrush(Color.Parse("#1E3830"));
    private IBrush _cardBorderBrush = new SolidColorBrush(Color.Parse("#38384E"));
    private IBrush _selectedBorderBrush = new SolidColorBrush(Color.Parse("#FFD700"));
    private IBrush _startupBadgeBrush = new SolidColorBrush(Color.Parse("#40C080"));
    private IBrush _textWhiteBrush = Brushes.WhiteSmoke;
    private IBrush _textMutedBrush = new SolidColorBrush(Color.Parse("#9A9AB0"));
    private IBrush _transSubTextBrush = new SolidColorBrush(Color.Parse("#4AE8D0"));
    private IBrush _badgeBgBrush = new SolidColorBrush(Color.FromArgb(200, 20, 20, 30));
    private IPen _transitionPen = new Pen(new SolidColorBrush(Color.Parse("#5088E0")), 1.8);
    private IPen _modalTransitionPen = new Pen(new SolidColorBrush(Color.Parse("#E08830")), 2.0, DashStyle.Dash);

    static ScreenFlowControl()
    {
        AffectsRender<ScreenFlowControl>(GraphProperty);
        FocusableProperty.OverrideDefaultValue<ScreenFlowControl>(true);
    }

    public ScreenFlowControl()
    {
        ClipToBounds = true;
    }

    public void ResetView()
    {
        _scale = 1.0;
        _offsetX = 40.0;
        _offsetY = 40.0;
        InvalidateVisual();
    }

    public void ApplyTheme(ThemeDefinition theme)
    {
        if (Color.TryParse(theme.GraphBackground, out var bgCol))
            _backgroundBrush = new SolidColorBrush(bgCol);

        if (Color.TryParse(theme.GraphNodeFill, out var cardFillCol))
            _cardBgBrush = new SolidColorBrush(cardFillCol);

        if (Color.TryParse(theme.BackgroundElevated, out var headerCol))
        {
            _cardHeaderBrush = new SolidColorBrush(headerCol);
            _badgeBgBrush = new SolidColorBrush(Color.FromArgb(220, headerCol.R, headerCol.G, headerCol.B));
        }

        if (Color.TryParse(theme.GraphNodeStroke, out var borderCol))
            _cardBorderBrush = new SolidColorBrush(borderCol);

        if (Color.TryParse(theme.TextPrimary, out var textPrimaryCol))
            _textWhiteBrush = new SolidColorBrush(textPrimaryCol);

        if (Color.TryParse(theme.TextMuted, out var textMutedCol))
            _textMutedBrush = new SolidColorBrush(textMutedCol);

        if (Color.TryParse(theme.AccentColor, out var accentCol))
            _transSubTextBrush = new SolidColorBrush(accentCol);

        if (theme.IsDark)
        {
            _startupHeaderBrush = new SolidColorBrush(Color.Parse("#1E3830"));
            _startupBadgeBrush = new SolidColorBrush(Color.Parse("#40C080"));
            _transitionPen = new Pen(new SolidColorBrush(Color.Parse("#5088E0")), 1.8);
            _modalTransitionPen = new Pen(new SolidColorBrush(Color.Parse("#E08830")), 2.0, DashStyle.Dash);
        }
        else
        {
            _startupHeaderBrush = new SolidColorBrush(Color.FromArgb(40, 40, 180, 100));
            _startupBadgeBrush = new SolidColorBrush(Color.Parse("#1B8A4D"));
            _transitionPen = new Pen(new SolidColorBrush(Color.Parse("#1976D2")), 1.8);
            _modalTransitionPen = new Pen(new SolidColorBrush(Color.Parse("#D97706")), 2.0, DashStyle.Dash);
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(_backgroundBrush, new Rect(Bounds.Size));

        var graph = Graph;
        if (graph is null || graph.Screens.Count == 0)
        {
            var placeholder = new FormattedText(
                "Nessun Form o Schermata UI individuata nel progetto corrente.",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                14,
                Brushes.Gray);
            context.DrawText(placeholder, new Point(20, 20));
            return;
        }

        using (context.PushTransform(Matrix.CreateTranslation(_offsetX, _offsetY)))
        using (context.PushTransform(Matrix.CreateScale(_scale, _scale)))
        {
            // 1. Disegna le frecce di transizione tra schermate
            foreach (var t in graph.Transitions)
            {
                var src = graph.FindScreen(t.SourceScreenId);
                var tgt = graph.FindScreen(t.TargetScreenId);
                if (src == null || tgt == null) continue;

                var start = new Point(src.X + src.Width, src.Y + src.Height / 2.0);
                var end = new Point(tgt.X, tgt.Y + tgt.Height / 2.0);

                var pen = t.TransitionKind == ScreenTransitionKind.OpenModal ? _modalTransitionPen : _transitionPen;

                // Linea curva di collegamento
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(start, false);
                    double dx = Math.Abs(end.X - start.X) * 0.5;
                    ctx.CubicBezierTo(new Point(start.X + dx, start.Y), new Point(end.X - dx, end.Y), end);
                }
                context.DrawGeometry(null, pen, geometry);

                // Freccia finale
                DrawArrowHead(context, end, pen.Brush ?? Brushes.DodgerBlue);

                // Badge con il nome dell'evento scatenante
                if (!string.IsNullOrEmpty(t.TriggerEventName))
                {
                    var midPoint = new Point((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0 - 8);
                    var evtFmt = new FormattedText(t.TriggerEventName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BadgeTypeface, 9, _textWhiteBrush);
                    var badgeRect = new Rect(midPoint.X - evtFmt.Width / 2.0 - 4, midPoint.Y - 2, evtFmt.Width + 8, evtFmt.Height + 4);
                    context.FillRectangle(_badgeBgBrush, badgeRect);
                    context.DrawRectangle(new Pen(_cardBorderBrush, 0.8), badgeRect);
                    context.DrawText(evtFmt, new Point(badgeRect.X + 4, badgeRect.Y + 2));
                }
            }

            // 2. Disegna le Card delle Schermate
            foreach (var screen in graph.Screens)
            {
                var cardRect = new Rect(screen.X, screen.Y, screen.Width, screen.Height);
                var headerRect = new Rect(screen.X, screen.Y, screen.Width, 26.0);

                // Sfondo Card
                context.FillRectangle(_cardBgBrush, cardRect);
                context.FillRectangle(screen.IsStartupScreen ? _startupHeaderBrush : _cardHeaderBrush, headerRect);

                // Bordo
                if (_selectedScreen?.Id == screen.Id)
                {
                    context.DrawRectangle(new Pen(_selectedBorderBrush, 2.5), cardRect);
                }
                else if (_hoveredScreen?.Id == screen.Id)
                {
                    context.DrawRectangle(new Pen(Brushes.White, 1.5), cardRect);
                }
                else
                {
                    context.DrawRectangle(new Pen(_cardBorderBrush, 1.0), cardRect);
                }

                // Titolo / Nome Screen
                string screenTitle = screen.Name.Length > 16 ? screen.Name[..14] + "…" : screen.Name;
                var titleFmt = new FormattedText(screenTitle, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, HeaderTypeface, 11, _textWhiteBrush);
                context.DrawText(titleFmt, new Point(screen.X + 8, screen.Y + 6));

                if (screen.IsStartupScreen)
                {
                    var startBadgeFmt = new FormattedText("STARTUP", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BadgeTypeface, 8, _startupBadgeBrush);
                    context.DrawText(startBadgeFmt, new Point(screen.X + screen.Width - startBadgeFmt.Width - 6, screen.Y + 8));
                }

                // Corpo Card: Metriche Controlli ed Eventi
                var ctrlFmt = new FormattedText($"Controlli UI: {screen.ControlsCount}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 10, _textMutedBrush);
                context.DrawText(ctrlFmt, new Point(screen.X + 8, screen.Y + 34));

                var evtCountFmt = new FormattedText($"Event Handlers: {screen.EventHandlersCount}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 10, _textMutedBrush);
                context.DrawText(evtCountFmt, new Point(screen.X + 8, screen.Y + 50));

                var transFmt = new FormattedText($"Transizioni: {screen.OutgoingTransitionsCount} out / {screen.IncomingTransitionsCount} in", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 9, _transSubTextBrush);
                context.DrawText(transFmt, new Point(screen.X + 8, screen.Y + 70));
            }
        }
    }

    private static void DrawArrowHead(DrawingContext context, Point tip, IBrush brush)
    {
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(tip, true);
            ctx.LineTo(new Point(tip.X - 8, tip.Y - 5));
            ctx.LineTo(new Point(tip.X - 8, tip.Y + 5));
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

        var hit = Graph?.Screens.FirstOrDefault(s => localX >= s.X && localX <= s.X + s.Width && localY >= s.Y && localY <= s.Y + s.Height);
        if (_hoveredScreen != hit)
        {
            _hoveredScreen = hit;
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

            var hit = Graph.Screens.FirstOrDefault(s => localX >= s.X && localX <= s.X + s.Width && localY >= s.Y && localY <= s.Y + s.Height);
            _selectedScreen = hit;
            ScreenSelected?.Invoke(this, hit);
            InvalidateVisual();
        }

        _isPointerDown = false;
        _isPanning = false;
    }
}
