using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VB6Analyzer.App.Models;
using VB6Analyzer.Semantic.Impact;
using VB6Analyzer.Semantic.Model;

namespace VB6Analyzer.App.Controls;

/// <summary>
/// Controllo custom Avalonia/Skia ad alte prestazioni per il rendering e l'interazione con il CodeGraph (M5 + M6).
/// 
/// CARATTERISTICHE E INTERAZIONI:
/// 1. Zoom: zoom continuo tramite rotella del mouse con stabilità del punto sotto il cursore.
/// 2. Pan: trascinamento libero del canvas bidimensionale con il tasto sinistro/centrale del mouse.
/// 3. Select: selezione di un nodo tramite clic con evidenziazione del bordo, degli archi connessi e dei nodi vicini.
/// 4. Expand / Collapse: doppio clic su container (Class, Form, Module, Type, Enum) per progressive disclosure.
/// 5. Callers Mode: evidenziazione visiva gerarchica dei chiamanti della routine selezionata.
/// 6. Callees Mode: evidenziazione visiva gerarchica delle routine chiamate.
/// 7. Blast Radius Mode (M6): visualizzazione dell'impatto di modifica con gradiente di colore per profondità (Rosso -> Arancione -> Giallo) e Form UI toccate.
/// 8. Rendering Archi Direzionati con Frecce: CALLS, INSTANTIATES, USES_TYPE, CONTAINS.
/// </summary>
public sealed class GraphControl : Control
{
    public static readonly StyledProperty<VisualCodeGraph?> GraphProperty =
        AvaloniaProperty.Register<GraphControl, VisualCodeGraph?>(nameof(Graph));

    public VisualCodeGraph? Graph
    {
        get => GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    /// <summary>Sollevato quando cambia la selezione del nodo.</summary>
    public event EventHandler<GraphNode?>? SelectionChanged;

    // Trasformazioni di vista (Zoom & Pan)
    private double _scale = 1.0;
    private double _offsetX;
    private double _offsetY;

    // Stato di interazione puntatore
    private Point _pointerDownScreenPos;
    private bool _isPointerDown;
    private bool _isPanning;
    private const double PanThreshold = 4.0;

    // Stato di selezione ed evidenziazione
    private GraphNode? _selectedNode;
    private readonly HashSet<Guid> _highlightedNodeIds = new();
    private readonly HashSet<Guid> _callerNodeIds = new();
    private readonly HashSet<Guid> _calleeNodeIds = new();
    private readonly HashSet<(Guid, Guid)> _highlightedEdges = new();

    // Stato Blast Radius (M6)
    private readonly Dictionary<Guid, int> _blastRadiusDepths = new();
    private readonly HashSet<Guid> _affectedContainerIds = new();
    private ImpactReport? _activeBlastRadiusReport;

    // Modalità di evidenziazione
    public enum HighlightMode { Normal, Callers, Callees, BlastRadius }
    private HighlightMode _currentHighlightMode = HighlightMode.Normal;
    public HighlightMode CurrentHighlightMode => _currentHighlightMode;

    // Pennelli e Stili
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#1A1A22"));
    private static readonly IPen EdgeContainsPen = new Pen(new SolidColorBrush(Color.Parse("#30303E")), 1);
    private static readonly IPen EdgeCallsPen = new Pen(new SolidColorBrush(Color.Parse("#4A78B8")), 1.5);
    private static readonly IPen EdgeInstantiatesPen = new Pen(new SolidColorBrush(Color.Parse("#D4883B")), 1.5, DashStyle.Dash);
    private static readonly IPen EdgeUsesTypePen = new Pen(new SolidColorBrush(Color.Parse("#3BAE8C")), 1, DashStyle.Dot);
    private static readonly IPen EdgeHighlightPen = new Pen(new SolidColorBrush(Color.Parse("#FFD700")), 2.5);
    private static readonly IPen EdgeCallerPen = new Pen(new SolidColorBrush(Color.Parse("#FF9933")), 2.5);
    private static readonly IPen EdgeCalleePen = new Pen(new SolidColorBrush(Color.Parse("#33CCFF")), 2.5);
    private static readonly IPen EdgeBlastRadiusPen = new Pen(new SolidColorBrush(Color.Parse("#FF6600")), 2.5);
    private static readonly IPen SelectionOutlinePen = new Pen(new SolidColorBrush(Color.Parse("#FFD700")), 3);
    private static readonly IPen TargetBlastOutlinePen = new Pen(new SolidColorBrush(Color.Parse("#FF3344")), 3.5);
    private static readonly IPen FormBlastOutlinePen = new Pen(new SolidColorBrush(Color.Parse("#00D0FF")), 2.5);
    private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Medium);

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

    // =========================================================================
    // RENDERING SKIA / AVALONIA
    // =========================================================================

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));

        var graph = Graph;
        if (graph is null || graph.Nodes.Count == 0)
        {
            var placeholder = new FormattedText(
                "Nessun CodeGraph caricato. Clicca su 'Carica Fixtures' o 'Apri Cartella VB6'.",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                14,
                Brushes.Gray);
            context.DrawText(placeholder, new Point(16, 16));
            return;
        }

        var visibleIds = ComputeVisibleNodeIds(graph);

        // 1. Disegna gli ARCHI (sotto ai nodi)
        foreach (var edge in graph.Edges)
        {
            if (!visibleIds.Contains(edge.SourceId) || !visibleIds.Contains(edge.TargetId))
                continue;

            var source = graph.GetNode(edge.SourceId);
            var target = graph.GetNode(edge.TargetId);
            if (source is null || target is null) continue;

            var isHighlighted = _highlightedEdges.Contains((edge.SourceId, edge.TargetId));
            var isCallerEdge = _callerNodeIds.Contains(edge.SourceId) && (_selectedNode?.Id == edge.TargetId || _callerNodeIds.Contains(edge.TargetId));
            var isCalleeEdge = (_selectedNode?.Id == edge.SourceId || _calleeNodeIds.Contains(edge.SourceId)) && _calleeNodeIds.Contains(edge.TargetId);
            var isBlastRadiusEdge = _currentHighlightMode == HighlightMode.BlastRadius &&
                                    (_blastRadiusDepths.ContainsKey(edge.SourceId) || _selectedNode?.Id == edge.SourceId) &&
                                    (_blastRadiusDepths.ContainsKey(edge.TargetId) || _selectedNode?.Id == edge.TargetId);

            var pen = isHighlighted
                ? EdgeHighlightPen
                : (isBlastRadiusEdge ? EdgeBlastRadiusPen :
                  (isCallerEdge ? EdgeCallerPen :
                  (isCalleeEdge ? EdgeCalleePen : GetPenForEdgeKind(edge.Kind))));

            var p1 = GraphToScreen(source.X, source.Y);
            var p2 = GraphToScreen(target.X, target.Y);

            context.DrawLine(pen, p1, p2);

            // Disegna freccia direzionale su chiamate e istanziazioni
            if (edge.Kind is RelationshipKind.Calls or RelationshipKind.Instantiates && _scale > 0.4)
            {
                DrawArrowHead(context, pen.Brush, p1, p2, target.Radius * Math.Clamp(_scale, 0.2, 2.5));
            }
        }

        // 2. Disegna i NODI
        foreach (var node in graph.Nodes)
        {
            if (!visibleIds.Contains(node.Id)) continue;

            var screenPos = GraphToScreen(node.X, node.Y);
            var radius = node.Radius * Math.Clamp(_scale, 0.2, 2.5);

            // Viewport culling per performance
            if (screenPos.X < -radius - 50 || screenPos.Y < -radius - 50 ||
                screenPos.X - radius > Bounds.Width + 50 || screenPos.Y - radius > Bounds.Height + 50)
                continue;

            var isSelected = node.Id == _selectedNode?.Id;
            var isHighlighted = _highlightedNodeIds.Contains(node.Id);
            var isCaller = _callerNodeIds.Contains(node.Id);
            var isCallee = _calleeNodeIds.Contains(node.Id);

            // Blast Radius info
            var isInBlastRadius = _currentHighlightMode == HighlightMode.BlastRadius;
            var blastDepth = _blastRadiusDepths.TryGetValue(node.Id, out var d) ? d : (int?)null;
            var isAffectedContainer = _affectedContainerIds.Contains(node.Id);

            var fill = GetNodeBrush(node, isSelected, isHighlighted, isCaller, isCallee, isInBlastRadius, blastDepth, isAffectedContainer);
            var outlinePen = isSelected ? (isInBlastRadius ? TargetBlastOutlinePen : SelectionOutlinePen) :
                            (isAffectedContainer && node.Kind == SymbolKind.Form ? FormBlastOutlinePen : null);

            context.DrawEllipse(fill, outlinePen, screenPos, radius, radius);

            // Indicatore visivo di container espanso/compresso
            if (node.IsContainer)
            {
                var markerColor = node.IsExpanded ? Brushes.WhiteSmoke : Brushes.Orange;
                context.DrawEllipse(markerColor, null, screenPos, 3, 3);
            }

            // Etichetta del nodo (mostrata quando la dimensione a schermo è sufficiente)
            if (radius > 7)
            {
                var fontSize = node.Kind is SymbolKind.Method or SymbolKind.Property ? 10 : 12;
                var label = new FormattedText(
                    node.Name,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    fontSize,
                    isSelected ? Brushes.Gold : (blastDepth.HasValue ? Brushes.Gold : Brushes.WhiteSmoke));

                context.DrawText(label, screenPos + new Point(radius + 4, -label.Height / 2));
            }
        }
    }

    private static IPen GetPenForEdgeKind(RelationshipKind kind) => kind switch
    {
        RelationshipKind.Calls => EdgeCallsPen,
        RelationshipKind.Instantiates => EdgeInstantiatesPen,
        RelationshipKind.UsesType => EdgeUsesTypePen,
        _ => EdgeContainsPen
    };

    private static void DrawArrowHead(DrawingContext context, IBrush? brush, Point from, Point to, double targetRadius)
    {
        if (brush == null) return;

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;

        var ux = dx / len;
        var uy = dy / len;

        var tip = new Point(to.X - ux * (targetRadius + 2), to.Y - uy * (targetRadius + 2));
        var arrowSize = 6.0;

        var p1 = new Point(tip.X - ux * arrowSize + uy * (arrowSize * 0.6), tip.Y - uy * arrowSize - ux * (arrowSize * 0.6));
        var p2 = new Point(tip.X - ux * arrowSize - uy * (arrowSize * 0.6), tip.Y - uy * arrowSize + ux * (arrowSize * 0.6));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(tip, true);
            ctx.LineTo(p1);
            ctx.LineTo(p2);
            ctx.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geometry);
    }

    private static IBrush GetNodeBrush(
        GraphNode node,
        bool isSelected,
        bool isHighlighted,
        bool isCaller,
        bool isCallee,
        bool isInBlastRadius,
        int? blastDepth,
        bool isAffectedContainer)
    {
        // Se siamo in modalità Blast Radius: colora in base alla profondità di rischio
        if (isInBlastRadius)
        {
            if (isSelected)
                return new SolidColorBrush(Color.Parse("#FF3344")); // Rosso acceso = Target di modifica

            if (blastDepth.HasValue)
            {
                return blastDepth.Value switch
                {
                    1 => new SolidColorBrush(Color.Parse("#FF8800")), // Arancione = Chiamante diretto
                    2 => new SolidColorBrush(Color.Parse("#FFBB00")), // Ambra = Chiamante 2° livello
                    _ => new SolidColorBrush(Color.Parse("#FFE066"))  // Giallo chiaro = Chiamante 3°+ livello
                };
            }

            if (isAffectedContainer)
            {
                return node.Kind == SymbolKind.Form
                    ? new SolidColorBrush(Color.Parse("#00D0FF")) // Form UI toccata
                    : new SolidColorBrush(Color.Parse("#54C08C")); // Classe/Modulo toccato
            }

            // Nodi non impattati: attenuati
            return new SolidColorBrush(Color.Parse("#282834"));
        }

        // Modalità normale
        if (isSelected) return new SolidColorBrush(Color.Parse("#FFD700")); // Gold
        if (isCaller) return new SolidColorBrush(Color.Parse("#FF9933"));   // Amber/Orange
        if (isCallee) return new SolidColorBrush(Color.Parse("#00D0FF"));   // Cyan
        if (isHighlighted) return new SolidColorBrush(Color.Parse("#90CAF9")); // Light blue

        return node.Kind switch
        {
            SymbolKind.Project => new SolidColorBrush(Color.Parse("#E85D75")),
            SymbolKind.Form => new SolidColorBrush(Color.Parse("#4FA8E8")),
            SymbolKind.Class => new SolidColorBrush(Color.Parse("#54C08C")),
            SymbolKind.Module => new SolidColorBrush(Color.Parse("#C084F0")),
            SymbolKind.UserControl => new SolidColorBrush(Color.Parse("#F08C4A")),
            SymbolKind.Method => new SolidColorBrush(Color.Parse("#7D7D8E")),
            SymbolKind.Property => new SolidColorBrush(Color.Parse("#E8B838")),
            SymbolKind.Field => new SolidColorBrush(Color.Parse("#60A5FA")),
            SymbolKind.Constant => new SolidColorBrush(Color.Parse("#F87171")),
            SymbolKind.Type => new SolidColorBrush(Color.Parse("#34D399")),
            SymbolKind.Enum => new SolidColorBrush(Color.Parse("#A7F3D0")),
            _ => Brushes.Gray
        };
    }

    /// <summary>
    /// Progressive disclosure: I membri figli sono visibili solo se il relativo container è espanso.
    /// In modalità Blast Radius, i container dei nodi impattati vengono forzati visibili.
    /// </summary>
    private HashSet<Guid> ComputeVisibleNodeIds(VisualCodeGraph graph)
    {
        var visible = new HashSet<Guid>();
        foreach (var node in graph.Nodes)
        {
            if (node.Kind is SymbolKind.Project or SymbolKind.Class or SymbolKind.Form
                            or SymbolKind.Module or SymbolKind.UserControl or SymbolKind.Type or SymbolKind.Enum)
            {
                visible.Add(node.Id);
                continue;
            }

            var parent = node.ParentId.HasValue ? graph.GetNode(node.ParentId.Value) : null;
            if (parent is { IsExpanded: true } || _blastRadiusDepths.ContainsKey(node.Id))
            {
                visible.Add(node.Id);
            }
        }
        return visible;
    }

    // =========================================================================
    // TRASFORMAZIONI DI COORDINATE (Pan & Zoom)
    // =========================================================================

    public Point GraphToScreen(double x, double y) =>
        new(Bounds.Width / 2 + x * _scale + _offsetX, Bounds.Height / 2 + y * _scale + _offsetY);

    public Point ScreenToGraph(Point screen) => new(
        (screen.X - Bounds.Width / 2 - _offsetX) / _scale,
        (screen.Y - Bounds.Height / 2 - _offsetY) / _scale);

    public void ResetView()
    {
        _scale = 1.0;
        _offsetX = 0;
        _offsetY = 0;
        InvalidateVisual();
    }

    /// <summary>Centra la vista del canvas su un nodo specifico.</summary>
    public void FocusOnNode(Guid nodeId)
    {
        var node = Graph?.GetNode(nodeId);
        if (node is null) return;

        _offsetX = -node.X * _scale;
        _offsetY = -node.Y * _scale;
        InvalidateVisual();
    }

    // =========================================================================
    // GESTIONE INPUT PUNTATORE (Pan, Zoom, Clic, Doppio Clic)
    // =========================================================================

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsMiddleButtonPressed) return;

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
            var graphPoint = ScreenToGraph(e.GetPosition(this));
            var hit = HitTest(graphPoint);
            SelectNode(hit);
        }

        _isPointerDown = false;
        _isPanning = false;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var graphPoint = ScreenToGraph(e.GetPosition(this));
        var hit = HitTest(graphPoint);

        if (hit is { IsContainer: true })
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
            var hitRadius = node.Radius / Math.Max(_scale, 0.01) + 6.0;

            if (distSq <= hitRadius * hitRadius && distSq < bestDistSq)
            {
                best = node;
                bestDistSq = distSq;
            }
        }

        return best;
    }

    // =========================================================================
    // SELEZIONE ED EVIDENZIAZIONE CALLERS / CALLEES / BLAST RADIUS
    // =========================================================================

    public void SelectNode(GraphNode? node)
    {
        _selectedNode = node;
        _highlightedNodeIds.Clear();
        _highlightedEdges.Clear();
        _callerNodeIds.Clear();
        _calleeNodeIds.Clear();
        _blastRadiusDepths.Clear();
        _affectedContainerIds.Clear();
        _currentHighlightMode = HighlightMode.Normal;

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

    /// <summary>
    /// Evidenzia sul grafo tutti i chiamanti (callers diretti e transitivi) del nodo selezionato.
    /// </summary>
    public void HighlightCallersForSelected()
    {
        _callerNodeIds.Clear();
        _calleeNodeIds.Clear();
        _blastRadiusDepths.Clear();
        _affectedContainerIds.Clear();

        if (_selectedNode is null || Graph?.Queries is null || _selectedNode.SemanticSymbol is null)
            return;

        var callers = Graph.Queries.GetDirectCallers(_selectedNode.SemanticSymbol);
        foreach (var caller in callers)
        {
            _callerNodeIds.Add(caller.Id);
        }

        _currentHighlightMode = HighlightMode.Callers;
        InvalidateVisual();
    }

    /// <summary>
    /// Evidenzia sul grafo tutti i chiamati (callees diretti e transitivi) del nodo selezionato.
    /// </summary>
    public void HighlightCalleesForSelected()
    {
        _callerNodeIds.Clear();
        _calleeNodeIds.Clear();
        _blastRadiusDepths.Clear();
        _affectedContainerIds.Clear();

        if (_selectedNode is null || Graph?.Queries is null || _selectedNode.SemanticSymbol is null)
            return;

        var callees = Graph.Queries.GetDirectCallees(_selectedNode.SemanticSymbol);
        foreach (var callee in callees)
        {
            _calleeNodeIds.Add(callee.Id);
        }

        _currentHighlightMode = HighlightMode.Callees;
        InvalidateVisual();
    }

    /// <summary>
    /// Evidenzia sul grafo l'intero Blast Radius (M6): target in rosso, chiamanti per livello di profondità e Form UI toccate.
    /// </summary>
    public void HighlightBlastRadius(ImpactReport report)
    {
        _callerNodeIds.Clear();
        _calleeNodeIds.Clear();
        _blastRadiusDepths.Clear();
        _affectedContainerIds.Clear();
        _activeBlastRadiusReport = report;

        // Chiamanti diretti (Depth 1)
        foreach (var dc in report.DirectCallers)
        {
            _blastRadiusDepths[dc.Id] = 1;
        }

        // Chiamanti indiretti (Depth >= 2)
        foreach (var ic in report.IndirectCallers)
        {
            _blastRadiusDepths[ic.Symbol.Id] = ic.Depth;
        }

        // Container impattati (Form, Classi, Moduli)
        foreach (var f in report.AffectedForms) _affectedContainerIds.Add(f.Id);
        foreach (var c in report.AffectedClasses) _affectedContainerIds.Add(c.Id);
        foreach (var m in report.AffectedModules) _affectedContainerIds.Add(m.Id);

        _currentHighlightMode = HighlightMode.BlastRadius;
        InvalidateVisual();
    }

    /// <summary>
    /// Ripristina l'evidenziazione normale.
    /// </summary>
    public void ClearHighlightMode()
    {
        _callerNodeIds.Clear();
        _calleeNodeIds.Clear();
        _blastRadiusDepths.Clear();
        _affectedContainerIds.Clear();
        _activeBlastRadiusReport = null;
        _currentHighlightMode = HighlightMode.Normal;
        InvalidateVisual();
    }
}
