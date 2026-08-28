using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VB6Analyzer.App.Models;

namespace VB6Analyzer.App.Controls;

/// <summary>
/// Custom control che disegna direttamente con Avalonia/Skia (opzione B della
/// sezione 24 del charter: niente controllo Avalonia di terze parti, niente WebView).
///
/// Responsabilità (sezione 24):
///   input | selection | zoom | pan | renderer
///
/// Nel POC il layout è già precalcolato da RadialLayoutEngine; questo controllo
/// si occupa solo di come i nodi vengono MOSTRATI (progressive disclosure via
/// expand/collapse) e RENDERIZZATI, non di dove sono posizionati.
/// </summary>
public sealed class GraphControl : Control
{
    public static readonly StyledProperty<CodeGraph?> GraphProperty =
        AvaloniaProperty.Register<GraphControl, CodeGraph?>(nameof(Graph));

    public CodeGraph? Graph
    {
        get => GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    /// <summary>Sollevato quando cambia il nodo selezionato (per l'Inspector, sezione 25).</summary>
    public event EventHandler<GraphNode?>? SelectionChanged;

    private double _scale = 1.0;
    private double _offsetX;
    private double _offsetY;

    private Point _pointerDownScreenPos;
    private bool _isPointerDown;
    private bool _isPanning;
    private const double PanThreshold = 4.0;

    private GraphNode? _selectedNode;
    private HashSet<Guid> _highlightedNodeIds = new();
    private HashSet<(Guid, Guid)> _highlightedEdges = new();

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#1E1E24"));
    private static readonly IPen EdgeContainsPen = new Pen(new SolidColorBrush(Color.Parse("#3A3A46")), 1);
    private static readonly IPen EdgeCallsPen = new Pen(new SolidColorBrush(Color.Parse("#5A6B8C")), 1);
    private static readonly IPen EdgeHighlightPen = new Pen(new SolidColorBrush(Color.Parse("#FFC857")), 2);
    private static readonly IPen SelectionOutlinePen = new Pen(new SolidColorBrush(Color.Parse("#FFC857")), 2.5);
    private static readonly Typeface LabelTypeface = new("Inter");

    static GraphControl()
    {
        AffectsRender<GraphControl>(GraphProperty);
        FocusableProperty.OverrideDefaultValue<GraphControl>(true);
    }

    public GraphControl()
    {
        ClipToBounds = true;
        DoubleTapped += OnDoubleTapped;
    }

    // ---------- Rendering ----------

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));

        var graph = Graph;
        if (graph is null || graph.Nodes.Count == 0)
        {
            var placeholder = new FormattedText(
                "Nessun CodeGraph caricato",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                14,
                Brushes.Gray);
            context.DrawText(placeholder, new Point(12, 12));
            return;
        }

        var visibleIds = ComputeVisibleNodeIds(graph);

        // 1) archi (sotto ai nodi)
        foreach (var edge in graph.Edges)
        {
            if (!visibleIds.Contains(edge.SourceId) || !visibleIds.Contains(edge.TargetId))
                continue;

            var source = graph.GetNode(edge.SourceId);
            var target = graph.GetNode(edge.TargetId);
            if (source is null || target is null) continue;

            var isHighlighted = _highlightedEdges.Contains((edge.SourceId, edge.TargetId));
            var pen = isHighlighted
                ? EdgeHighlightPen
                : edge.Kind == EdgeKind.Calls ? EdgeCallsPen : EdgeContainsPen;

            context.DrawLine(pen, GraphToScreen(source.X, source.Y), GraphToScreen(target.X, target.Y));
        }

        // 2) nodi
        foreach (var node in graph.Nodes)
        {
            if (!visibleIds.Contains(node.Id)) continue;

            var screenPos = GraphToScreen(node.X, node.Y);
            var radius = node.Radius * Math.Clamp(_scale, 0.15, 3.0);

            // culling economico: salta nodi fuori dal viewport
            if (screenPos.X < -radius || screenPos.Y < -radius ||
                screenPos.X - radius > Bounds.Width || screenPos.Y - radius > Bounds.Height)
                continue;

            var isSelected = node.Id == _selectedNode?.Id;
            var isHighlighted = _highlightedNodeIds.Contains(node.Id);
            var fill = NodeFill(node.Kind, isSelected, isHighlighted);

            context.DrawEllipse(fill, isSelected ? SelectionOutlinePen : null,
                screenPos, radius, radius);

            // etichette solo quando c'è abbastanza spazio (evita testo illeggibile a molto zoom-out)
            if (radius > 9)
            {
                var fontSize = node.Kind == NodeKind.Method ? 10 : 12;
                var label = new FormattedText(
                    node.Name,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    fontSize,
                    Brushes.WhiteSmoke);

                context.DrawText(label, screenPos + new Point(radius + 4, -label.Height / 2));
            }
        }
    }

    private static IBrush NodeFill(NodeKind kind, bool isSelected, bool isHighlighted)
    {
        if (isSelected) return new SolidColorBrush(Color.Parse("#FFC857"));
        if (isHighlighted) return new SolidColorBrush(Color.Parse("#8CA0D6"));

        return kind switch
        {
            NodeKind.Project => new SolidColorBrush(Color.Parse("#E85D75")),
            NodeKind.Form => new SolidColorBrush(Color.Parse("#4FA8E8")),
            NodeKind.Class => new SolidColorBrush(Color.Parse("#54C08C")),
            NodeKind.Module => new SolidColorBrush(Color.Parse("#C084F0")),
            NodeKind.Method => new SolidColorBrush(Color.Parse("#7A7A88")),
            _ => Brushes.Gray
        };
    }

    /// <summary>
    /// Progressive disclosure (sezione 21): Project e container sempre visibili;
    /// i Method di un container solo se quel container è espanso.
    /// </summary>
    private static HashSet<Guid> ComputeVisibleNodeIds(CodeGraph graph)
    {
        var visible = new HashSet<Guid>();
        foreach (var node in graph.Nodes)
        {
            if (node.Kind != NodeKind.Method)
            {
                visible.Add(node.Id);
                continue;
            }

            var parent = node.ParentId.HasValue ? graph.GetNode(node.ParentId.Value) : null;
            if (parent is { IsExpanded: true })
                visible.Add(node.Id);
        }
        return visible;
    }

    // ---------- Coordinate transform (pan + zoom) ----------

    private Point GraphToScreen(double x, double y) =>
        new(Bounds.Width / 2 + x * _scale + _offsetX, Bounds.Height / 2 + y * _scale + _offsetY);

    private Point ScreenToGraph(Point screen) => new(
        (screen.X - Bounds.Width / 2 - _offsetX) / _scale,
        (screen.Y - Bounds.Height / 2 - _offsetY) / _scale);

    public void ResetView()
    {
        _scale = 1.0;
        _offsetX = 0;
        _offsetY = 0;
        InvalidateVisual();
    }

    /// <summary>Centra la vista su un nodo (usato dall'Explorer per "focus selected node", sezione 22).</summary>
    public void FocusOnNode(Guid nodeId)
    {
        var node = Graph?.GetNode(nodeId);
        if (node is null) return;

        _offsetX = -node.X * _scale;
        _offsetY = -node.Y * _scale;
        InvalidateVisual();
    }

    // ---------- Input: pan, zoom, selection, expand/collapse ----------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _isPointerDown = true;
        _isPanning = false;
        _pointerDownScreenPos = e.GetPosition(this);
        e.Pointer.Capture(this);
        Focus();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isPointerDown) return;

        var current = e.GetPosition(this);
        var dx = current.X - _pointerDownScreenPos.X;
        var dy = current.Y - _pointerDownScreenPos.Y;

        if (!_isPanning && (Math.Abs(dx) > PanThreshold || Math.Abs(dy) > PanThreshold))
            _isPanning = true;

        if (_isPanning)
        {
            _offsetX += dx;
            _offsetY += dy;
            _pointerDownScreenPos = current;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);

        if (_isPointerDown && !_isPanning)
        {
            // click "pulito" (nessun trascinamento significativo) => selection / hit test
            var graphPoint = ScreenToGraph(e.GetPosition(this));
            var hit = HitTest(graphPoint);
            SelectNode(hit);
        }

        _isPointerDown = false;
        _isPanning = false;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        // expand/collapse: solo su container (Form/Class/Module), non su Project o Method
        var graphPoint = ScreenToGraph(e.GetPosition(this));
        var hit = HitTest(graphPoint);

        if (hit is { Kind: NodeKind.Form or NodeKind.Class or NodeKind.Module })
        {
            hit.IsExpanded = !hit.IsExpanded;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var pointerScreenPos = e.GetPosition(this);
        var graphPointUnderPointer = ScreenToGraph(pointerScreenPos);

        var zoomFactor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        var newScale = Math.Clamp(_scale * zoomFactor, 0.05, 6.0);

        // ricalcola l'offset in modo che il punto sotto il cursore resti fermo
        _offsetX = pointerScreenPos.X - Bounds.Width / 2 - graphPointUnderPointer.X * newScale;
        _offsetY = pointerScreenPos.Y - Bounds.Height / 2 - graphPointUnderPointer.Y * newScale;
        _scale = newScale;

        InvalidateVisual();
        e.Handled = true;
    }

    private GraphNode? HitTest(Point graphPoint)
    {
        var graph = Graph;
        if (graph is null) return null;

        var visibleIds = ComputeVisibleNodeIds(graph);
        GraphNode? best = null;
        var bestDistSq = double.MaxValue;

        foreach (var node in graph.Nodes)
        {
            if (!visibleIds.Contains(node.Id)) continue;

            var dx = node.X - graphPoint.X;
            var dy = node.Y - graphPoint.Y;
            var distSq = dx * dx + dy * dy;
            var hitRadius = node.Radius / Math.Max(_scale, 0.01) + 4; // tolleranza click

            if (distSq <= hitRadius * hitRadius && distSq < bestDistSq)
            {
                best = node;
                bestDistSq = distSq;
            }
        }

        return best;
    }

    public void SelectNode(GraphNode? node)
    {
        _selectedNode = node;
        _highlightedNodeIds.Clear();
        _highlightedEdges.Clear();

        if (node is not null && Graph is { } graph)
        {
            _highlightedNodeIds.Add(node.Id);

            foreach (var edge in graph.Edges)
            {
                if (edge.SourceId == node.Id)
                {
                    _highlightedNodeIds.Add(edge.TargetId);
                    _highlightedEdges.Add((edge.SourceId, edge.TargetId));
                }
                else if (edge.TargetId == node.Id)
                {
                    _highlightedNodeIds.Add(edge.SourceId);
                    _highlightedEdges.Add((edge.SourceId, edge.TargetId));
                }
            }
        }

        InvalidateVisual();
        SelectionChanged?.Invoke(this, node);
    }
}
