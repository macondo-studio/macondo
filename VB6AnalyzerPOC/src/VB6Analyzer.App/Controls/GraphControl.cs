using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;

namespace Macondo.App.Controls;

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

    /// <summary>Sollevato quando cambiano i filtri di visibilità dei simboli.</summary>
    public event EventHandler? FilterChanged;

    // =========================================================================
    // OTTIMIZZAZIONI DI PERFORMANCE & CACHING DEI NODI VISIBILI
    // =========================================================================
    // L'insieme dei nodi visibili viene memorizzato nella cache per evitare di rieseguire
    // la scansione O(N) e le allocazioni di HashSet<Guid> ad ogni frame di rendering o
    // movimento del mouse (HitTest). La cache viene invalidata in modo mirato solo quando
    // cambiano i filtri, la modalità di evidenziazione, il grafo o lo stato di espansione dei nodi.
    private HashSet<Guid>? _cachedVisibleNodeIds;
    private bool _isVisibilityCacheDirty = true;

    /// <summary>
    /// Invalida la cache dei nodi visibili, forzandone il ricalcolo al frame successivo
    /// e richiedendo un ridisegno immediato del controllo.
    /// </summary>
    public void InvalidateVisibilityCache()
    {
        _isVisibilityCacheDirty = true;
        InvalidateVisual();
    }

    private bool _showMethods = true;
    public bool ShowMethods
    {
        get => _showMethods;
        set
        {
            if (_showMethods != value)
            {
                _showMethods = value;
                InvalidateVisibilityCache();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _showProperties = true;
    public bool ShowProperties
    {
        get => _showProperties;
        set
        {
            if (_showProperties != value)
            {
                _showProperties = value;
                InvalidateVisibilityCache();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _showFields = true;
    public bool ShowFields
    {
        get => _showFields;
        set
        {
            if (_showFields != value)
            {
                _showFields = value;
                InvalidateVisibilityCache();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _showConstants = true;
    public bool ShowConstants
    {
        get => _showConstants;
        set
        {
            if (_showConstants != value)
            {
                _showConstants = value;
                InvalidateVisibilityCache();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _showTypesAndEnums = true;
    public bool ShowTypesAndEnums
    {
        get => _showTypesAndEnums;
        set
        {
            if (_showTypesAndEnums != value)
            {
                _showTypesAndEnums = value;
                InvalidateVisibilityCache();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _enableHoverSelection = true;
    /// <summary>Indica se la selezione e ispezione dei nodi avviene automaticamente al passaggio del puntatore del mouse (Hover).</summary>
    public bool EnableHoverSelection
    {
        get => _enableHoverSelection;
        set => _enableHoverSelection = value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GraphProperty)
            InvalidateVisibilityCache();
    }

    /// <summary>
    /// Imposta simultaneamente tutti i filtri di visibilità per tipologia di simbolo.
    /// </summary>
    public void SetFilters(
        bool methods,
        bool properties,
        bool fields,
        bool constants,
        bool typesAndEnums)
    {
        _showMethods = methods;
        _showProperties = properties;
        _showFields = fields;
        _showConstants = constants;
        _showTypesAndEnums = typesAndEnums;
        InvalidateVisibilityCache();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Calcola il numero di nodi visibili e totali nel grafo corrente.
    /// </summary>
    public (int visibleNodes, int totalNodes) GetNodeCounts()
    {
        if (Graph is not { } graph) return (0, 0);
        var visible = ComputeVisibleNodeIds(graph);
        return (visible.Count, graph.Nodes.Count);
    }

    private readonly HashSet<Guid> _hiddenProjectIds = new();
    /// <summary>Insieme di ID dei progetti nascosti nel grafo.</summary>
    public IReadOnlySet<Guid> HiddenProjectIds => _hiddenProjectIds;

    /// <summary>Sollevato quando cambia la visibilità di un progetto nel grafo.</summary>
    public event EventHandler<Guid>? ProjectVisibilityChanged;

    /// <summary>Sollevato quando l'utente termina lo spostamento/trascinamento di un nodo con il mouse.</summary>
    public event EventHandler? NodeMoved;

    /// <summary>Verifica se un progetto è visibile nel grafo.</summary>
    public bool IsProjectVisible(Guid projectId) => !_hiddenProjectIds.Contains(projectId);

    /// <summary>Imposta la visibilità di un progetto nel grafo.</summary>
    public void SetProjectVisibility(Guid projectId, bool isVisible)
    {
        if (isVisible)
        {
            if (!_hiddenProjectIds.Remove(projectId))
                return;
        }
        else
        {
            if (!_hiddenProjectIds.Add(projectId))
                return;

            // Se il nodo selezionato appartiene al progetto nascosto, deselezionalo
            if (_selectedNode != null)
            {
                var isSelectedInProject = (_selectedNode.Kind == SymbolKind.Project && _selectedNode.Id == projectId) ||
                                          (_selectedNode.ProjectId == projectId);
                if (isSelectedInProject)
                    SelectNode(null);
            }
        }

        InvalidateVisibilityCache();
        ProjectVisibilityChanged?.Invoke(this, projectId);
    }

    /// <summary>Rende visibili tutti i progetti nel grafo.</summary>
    public void ClearHiddenProjects()
    {
        if (_hiddenProjectIds.Count == 0) return;
        _hiddenProjectIds.Clear();
        InvalidateVisibilityCache();
        ProjectVisibilityChanged?.Invoke(this, Guid.Empty);
    }

    /// <summary>Calcola il numero di progetti visibili e totali nel grafo corrente.</summary>
    public (int visibleProjects, int totalProjects) GetProjectCounts()
    {
        if (Graph is not { } graph) return (0, 0);
        var projectNodes = graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        var total = projectNodes.Count;
        if (total == 0) return (0, 0);
        var visible = projectNodes.Count(p => !_hiddenProjectIds.Contains(p.Id));
        return (visible, total);
    }

    // Trasformazioni di vista (Zoom & Pan)
    private double _scale = 1.0;
    private double _offsetX;
    private double _offsetY;

    // Stato di interazione puntatore e trascinamento nodi
    private Point _pointerDownScreenPos;
    private bool _isPointerDown;
    private bool _isPanning;
    private bool _isNodeDragging;
    private GraphNode? _draggedNode;
    private Point _lastDragGraphPos;
    private const double PanThreshold = 4.0;

    // Stato di selezione ed evidenziazione
    private GraphNode? _selectedNode;
    /// <summary>Nodo attualmente selezionato nel grafo.</summary>
    public GraphNode? SelectedNode => _selectedNode;
    private readonly HashSet<Guid> _highlightedNodeIds = new();
    private readonly HashSet<Guid> _callerNodeIds = new();
    private readonly HashSet<Guid> _calleeNodeIds = new();
    private readonly HashSet<Guid> _calleeContainerIds = new();
    private readonly HashSet<Guid> _callerContainerIds = new();
    private readonly HashSet<(Guid, Guid)> _calleeEdgeIds = new();
    private readonly HashSet<(Guid, Guid)> _callerEdgeIds = new();
    private readonly HashSet<(Guid, Guid)> _highlightedEdges = new();

    // Stato Blast Radius (M6)
    private readonly Dictionary<Guid, int> _blastRadiusDepths = new();
    private readonly HashSet<Guid> _affectedContainerIds = new();
    private ImpactReport? _activeBlastRadiusReport;

    // Modalità di evidenziazione
    private HighlightMode _currentHighlightMode = HighlightMode.Normal;
    public HighlightMode CurrentHighlightMode => _currentHighlightMode;

    // Pennelli e Stili
    private bool _isDarkTheme = true;
    private IBrush _backgroundBrush = new SolidColorBrush(Color.Parse("#181822"));
    private IBrush _defaultLabelBrush = Brushes.WhiteSmoke;
    private IBrush _dimmedLabelBrush = new SolidColorBrush(Color.Parse("#555566"));
    private IPen _edgeContainsPen = new Pen(new SolidColorBrush(Color.Parse("#30303E")), 1);
    private static readonly IPen EdgeCallsPen = new Pen(new SolidColorBrush(Color.Parse("#4A78B8")), 1.5);
    private static readonly IPen EdgeHandlesEventPen = new Pen(new SolidColorBrush(Color.Parse("#F08C4A")), 1.5, DashStyle.Dash);
    private static readonly IPen EdgeInstantiatesPen = new Pen(new SolidColorBrush(Color.Parse("#D4883B")), 1.5, DashStyle.Dash);
    private static readonly IPen EdgeUsesTypePen = new Pen(new SolidColorBrush(Color.Parse("#3BAE8C")), 1, DashStyle.Dot);
    private static readonly IPen EdgeHighlightPen = new Pen(new SolidColorBrush(Color.Parse("#FFD700")), 2.5);
    private static readonly IPen EdgeCallerPen = new Pen(new SolidColorBrush(Color.Parse("#FF9933")), 2.5);
    private static readonly IPen EdgeCalleePen = new Pen(new SolidColorBrush(Color.Parse("#33CCFF")), 2.5);
    private static readonly IPen EdgeBlastRadiusPen = new Pen(new SolidColorBrush(Color.Parse("#FF6600")), 2.5);
    private static readonly IPen EdgeCrossProjectPen = new Pen(new SolidColorBrush(Color.Parse("#E056FD")), 2.2, DashStyle.Dash);
    private IPen _edgeDimmedPen = new Pen(new SolidColorBrush(Color.Parse("#252532")), 0.8);
    private static readonly IPen SelectionOutlinePen = new Pen(new SolidColorBrush(Color.Parse("#FFD700")), 3);
    private static readonly IPen TargetBlastOutlinePen = new Pen(new SolidColorBrush(Color.Parse("#FF3344")), 3.5);
    private static readonly IPen FormBlastOutlinePen = new Pen(new SolidColorBrush(Color.Parse("#00D0FF")), 2.5);

    // Palette statica di pennelli pre-allocati per eliminare le allocazioni GC a ogni frame di rendering
    private static readonly IBrush BrushProject = new SolidColorBrush(Color.Parse("#E85D75"));
    private static readonly IBrush BrushForm = new SolidColorBrush(Color.Parse("#4FA8E8"));
    private static readonly IBrush BrushClass = new SolidColorBrush(Color.Parse("#54C08C"));
    private static readonly IBrush BrushModule = new SolidColorBrush(Color.Parse("#C084F0"));
    private static readonly IBrush BrushUserControl = new SolidColorBrush(Color.Parse("#F08C4A"));
    private static readonly IBrush BrushMethod = new SolidColorBrush(Color.Parse("#7D7D8E"));
    private static readonly IBrush BrushProperty = new SolidColorBrush(Color.Parse("#E8B838"));
    private static readonly IBrush BrushField = new SolidColorBrush(Color.Parse("#60A5FA"));
    private static readonly IBrush BrushConstant = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush BrushType = new SolidColorBrush(Color.Parse("#34D399"));
    private static readonly IBrush BrushEnum = new SolidColorBrush(Color.Parse("#A7F3D0"));
    private static readonly IBrush BrushSelected = new SolidColorBrush(Color.Parse("#FFD700"));
    private static readonly IBrush BrushHighlighted = new SolidColorBrush(Color.Parse("#90CAF9"));
    private static readonly IBrush BrushBlastTarget = new SolidColorBrush(Color.Parse("#FF3344"));
    private static readonly IBrush BrushBlastDepth1 = new SolidColorBrush(Color.Parse("#FF8800"));
    private static readonly IBrush BrushBlastDepth2 = new SolidColorBrush(Color.Parse("#FFBB00"));
    private static readonly IBrush BrushBlastDepth3 = new SolidColorBrush(Color.Parse("#FFE066"));
    private static readonly IBrush BrushAffectedForm = new SolidColorBrush(Color.Parse("#00D0FF"));
    private static readonly IBrush BrushAffectedContainer = new SolidColorBrush(Color.Parse("#54C08C"));
    private static readonly IBrush BrushDimmedDark = new SolidColorBrush(Color.Parse("#282834"));
    private static readonly IBrush BrushDimmedLight = new SolidColorBrush(Color.Parse("#E0E0EA"));
    private static readonly IBrush BrushCallee = new SolidColorBrush(Color.Parse("#00D0FF"));
    private static readonly IBrush BrushCaller = new SolidColorBrush(Color.Parse("#FF9933"));
    private static readonly IBrush BrushContainerCalleeDark = new SolidColorBrush(Color.Parse("#3A6073"));
    private static readonly IBrush BrushContainerCalleeLight = new SolidColorBrush(Color.Parse("#90C2DE"));
    private static readonly IBrush BrushContainerCallerDark = new SolidColorBrush(Color.Parse("#5C4335"));
    private static readonly IBrush BrushContainerCallerLight = new SolidColorBrush(Color.Parse("#DEB598"));
    private static readonly IBrush BrushContainerCurrentDark = new SolidColorBrush(Color.Parse("#5C5535"));
    private static readonly IBrush BrushContainerCurrentLight = new SolidColorBrush(Color.Parse("#D4C388"));
    private static readonly IBrush BrushRoutineInnerCall = new SolidColorBrush(Color.Parse("#FFE57F"));
    private static readonly IBrush BrushDimmedAltDark = new SolidColorBrush(Color.Parse("#22222E"));

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
        context.FillRectangle(_backgroundBrush, new Rect(Bounds.Size));

        var graph = Graph;
        if (graph is null || graph.Nodes.Count == 0)
        {
            var placeholder = new FormattedText(
                LocalizationService.Instance["Graph_Placeholder"],
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                14,
                Brushes.Gray);
            context.DrawText(placeholder, new Point(16, 16));
            return;
        }

        // Verifica e aggiorna la cache dei nodi visibili solo se contrassegnata come dirty.
        // Evita di rieseguire ComputeVisibleNodeIds (scansione lineare di tutti i simboli con allocazione di un nuovo HashSet)
        // a ogni singolo frame di rendering (es. durante pan o zoom continui).
        if (_isVisibilityCacheDirty || _cachedVisibleNodeIds == null)
        {
            _cachedVisibleNodeIds = ComputeVisibleNodeIds(graph);
            _isVisibilityCacheDirty = false;
        }

        var visibleIds = _cachedVisibleNodeIds;
        var viewWidth = Bounds.Width;
        var viewHeight = Bounds.Height;

        // 1. Disegna gli ARCHI (sotto ai nodi) con Viewport Culling bidimensionale
        foreach (var edge in graph.Edges)
        {
            // Salta gli archi i cui nodi estremi non sono entrambi visibili nella gerarchia attiva
            if (!visibleIds.Contains(edge.SourceId) || !visibleIds.Contains(edge.TargetId))
                continue;

            var source = graph.GetNode(edge.SourceId);
            var target = graph.GetNode(edge.TargetId);
            if (source is null || target is null) continue;

            var p1 = GraphToScreen(source.X, source.Y);
            var p2 = GraphToScreen(target.X, target.Y);

            // Viewport edge culling: scarta l'arco se entrambi i nodi estremi ricadono interamente all'esterno
            // dello stesso lato dell'area visibile del canvas (con margine di tolleranza di 50px per evitare glitch sui bordi).
            var minX = Math.Min(p1.X, p2.X);
            var maxX = Math.Max(p1.X, p2.X);
            var minY = Math.Min(p1.Y, p2.Y);
            var maxY = Math.Max(p1.Y, p2.Y);

            if (maxX < -50 || minX > viewWidth + 50 || maxY < -50 || minY > viewHeight + 50)
                continue;

            var isCalleeEdge = _currentHighlightMode == HighlightMode.Callees && _calleeEdgeIds.Contains((edge.SourceId, edge.TargetId));
            var isCallerEdge = _currentHighlightMode == HighlightMode.Callers && _callerEdgeIds.Contains((edge.SourceId, edge.TargetId));
            var isBlastRadiusEdge = _currentHighlightMode == HighlightMode.BlastRadius &&
                                    (_blastRadiusDepths.ContainsKey(edge.SourceId) || _selectedNode?.Id == edge.SourceId) &&
                                    (_blastRadiusDepths.ContainsKey(edge.TargetId) || _selectedNode?.Id == edge.TargetId);
            var isHighlighted = _highlightedEdges.Contains((edge.SourceId, edge.TargetId));

            IPen pen;
            if (isCalleeEdge)
                pen = EdgeCalleePen;
            else if (isCallerEdge)
                pen = EdgeCallerPen;
            else if (isBlastRadiusEdge)
                pen = EdgeBlastRadiusPen;
            else if (isHighlighted)
                pen = EdgeHighlightPen;
            else if (_currentHighlightMode is HighlightMode.Callees or HighlightMode.Callers or HighlightMode.BlastRadius)
                pen = _edgeDimmedPen;
            else if (edge.IsCrossProject)
                pen = EdgeCrossProjectPen;
            else
                pen = GetPenForEdgeKind(edge.Kind);

            context.DrawLine(pen, p1, p2);

            // Disegna freccia direzionale su chiamate, eventi, istanziazioni o archi attivi/cross-project
            if ((isCalleeEdge || isCallerEdge || edge.IsCrossProject || edge.Kind is RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.HandlesEvent) && _scale > 0.25)
                DrawArrowHead(context, pen.Brush, p1, p2, target.Radius * Math.Clamp(_scale, 0.2, 2.5));
        }

        // 2. Disegna i NODI
        foreach (var node in graph.Nodes)
        {
            if (!visibleIds.Contains(node.Id)) continue;

            var screenPos = GraphToScreen(node.X, node.Y);
            var radius = Math.Max(node.Radius * Math.Min(_scale, 2.5), 1.0);

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
            if (node.IsContainer && radius >= 4)
            {
                IBrush markerColor = node.IsExpanded 
                    ? (_isDarkTheme ? Brushes.WhiteSmoke : new SolidColorBrush(Color.Parse("#4A4A5E"))) 
                    : Brushes.Orange;
                var markerRadius = Math.Min(3.0, radius * 0.4);
                context.DrawEllipse(markerColor, null, screenPos, markerRadius, markerRadius);
            }

            // Etichetta del nodo (mostrata quando la dimensione a schermo è sufficiente)
            if (radius > 7)
            {
                var fontSize = node.Kind is SymbolKind.Method or SymbolKind.Property ? 10 : 12;
                IBrush labelBrush;
                if (isSelected)

                    labelBrush = _isDarkTheme ? Brushes.Gold : new SolidColorBrush(Color.Parse("#B36200"));

                else if (_currentHighlightMode == HighlightMode.Callees)

    labelBrush = isCallee ? (_isDarkTheme ? new SolidColorBrush(Color.Parse("#00D0FF")) : new SolidColorBrush(Color.Parse("#0088CC"))) :
                                 (_calleeContainerIds.Contains(node.Id) ? _defaultLabelBrush : _dimmedLabelBrush);

else if (_currentHighlightMode == HighlightMode.Callers)

    labelBrush = isCaller ? (_isDarkTheme ? new SolidColorBrush(Color.Parse("#FF9933")) : new SolidColorBrush(Color.Parse("#E65C00"))) :
                                 (_callerContainerIds.Contains(node.Id) ? _defaultLabelBrush : _dimmedLabelBrush);

else if (_currentHighlightMode == HighlightMode.BlastRadius)

    labelBrush = blastDepth.HasValue ? (_isDarkTheme ? Brushes.Gold : new SolidColorBrush(Color.Parse("#C06000"))) :
                                 (isAffectedContainer ? _defaultLabelBrush : _dimmedLabelBrush);

else
                {
                    labelBrush = _defaultLabelBrush;
                }

                var label = new FormattedText(
                    node.Name,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    fontSize,
                    labelBrush);

                context.DrawText(label, screenPos + new Point(radius + 4, -label.Height / 2));
            }
        }
    }

    /// <summary>
    /// Restituisce la penna di rendering per il tipo di relazione specificato.
    /// Distingue visivamente archi di chiamata (linea continua blu), gestione eventi (tratteggiata arancione),
    /// istanziazione (tratteggiata ambra), uso tipo (puntinata verde smeraldo) o contenimento (grigio scuro/chiaro).
    /// </summary>
    private IPen GetPenForEdgeKind(RelationshipKind kind) => kind switch
    {
        RelationshipKind.Calls => EdgeCallsPen,
        RelationshipKind.HandlesEvent => EdgeHandlesEventPen,
        RelationshipKind.Instantiates => EdgeInstantiatesPen,
        RelationshipKind.UsesType => EdgeUsesTypePen,
        _ => _edgeContainsPen
    };

    /// <summary>
    /// Disegna la cuspide triangolare di una freccia direzionale orientata dal punto di origine al punto di destinazione.
    /// Calcola il versore direzionale (ux, uy) e arresta la punta sul perimetro del nodo target tenendo conto del suo raggio.
    /// </summary>
    /// <param name="context">Il contesto di disegno di Avalonia.</param>
    /// <param name="brush">Il pennello di riempimento della freccia.</param>
    /// <param name="from">Coordinate a schermo del punto di partenza dell'arco.</param>
    /// <param name="to">Coordinate a schermo del punto di arrivo (centro del nodo target).</param>
    /// <param name="targetRadius">Raggio del nodo target a schermo, per evitare sovrapposizioni grafiche con il corpo del nodo.</param>
    private static void DrawArrowHead(
        DrawingContext context,
        IBrush? brush,
        Point from,
        Point to,
        double targetRadius)
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

    /// <summary>
    /// Determina il pennello di riempimento del nodo in base allo stato di interazione corrente:
    /// 1. Modalità Blast Radius: gradiente di rischio (Rosso = Target, Arancione/Ambra/Giallo = Chiamanti per livello di profondità, Ciano = Form UI impattate).
    /// 2. Modalità Callees: Oro per il nodo sorgente, Ciano per le routine chiamate, Slate per i relativi moduli.
    /// 3. Modalità Callers: Oro per il nodo target, Ambra per i chiamanti, Slate per i relativi moduli.
    /// 4. Modalità Normale: tavolozza semantica per SymbolKind (Form, Class, Module, Method, Property, Field, Constant, Type, Enum).
    /// </summary>
    private IBrush GetNodeBrush(
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
                return BrushBlastTarget; // Rosso acceso = Target di modifica

            if (blastDepth.HasValue)

                return blastDepth.Value switch
                {
                    1 => BrushBlastDepth1, // Arancione = Chiamante diretto
                    2 => BrushBlastDepth2, // Ambra = Chiamante 2° livello
                    _ => BrushBlastDepth3  // Giallo chiaro = Chiamante 3°+ livello
                };

            if (isAffectedContainer)

                return node.Kind == SymbolKind.Form
                    ? BrushAffectedForm // Form UI toccata
                    : BrushAffectedContainer; // Classe/Modulo toccato

            // Nodi non impattati: attenuati
            return _isDarkTheme ? BrushDimmedDark : BrushDimmedLight;
        }

        // Modalità Callees: evidenzia il chiamante sorgente, le routine chiamate (Cyan) e i loro container
        if (_currentHighlightMode == HighlightMode.Callees)
        {
            if (isSelected) return BrushSelected;
            if (isCallee) return BrushCallee;

            if (_selectedNode is { IsContainer: true } && node.ParentId == _selectedNode.Id)
            {
                // Routine interna del container che effettua chiamate
                if (_calleeEdgeIds.Any(e => e.Item1 == node.Id))
                    return BrushRoutineInnerCall;
            }

            if (node.IsContainer && _calleeContainerIds.Contains(node.Id))

                return _isDarkTheme ? BrushContainerCalleeDark : BrushContainerCalleeLight;

            if (_selectedNode?.ParentId == node.Id)

                return _isDarkTheme ? BrushContainerCurrentDark : BrushContainerCurrentLight;

            return _isDarkTheme ? BrushDimmedAltDark : BrushDimmedLight;
        }

        // Modalità Callers: evidenzia la routine target, i chiamanti (Amber/Orange) e i loro container
        if (_currentHighlightMode == HighlightMode.Callers)
        {
            if (isSelected) return BrushSelected;
            if (isCaller) return BrushCaller;

            if (_selectedNode is { IsContainer: true } && node.ParentId == _selectedNode.Id)
            {
                // Routine interna del container chiamata dall'esterno
                if (_callerEdgeIds.Any(e => e.Item2 == node.Id))
                    return BrushRoutineInnerCall;
            }

            if (node.IsContainer && _callerContainerIds.Contains(node.Id))

                return _isDarkTheme ? BrushContainerCallerDark : BrushContainerCallerLight;

            if (_selectedNode?.ParentId == node.Id)

                return _isDarkTheme ? BrushContainerCurrentDark : BrushContainerCurrentLight;

            return _isDarkTheme ? BrushDimmedAltDark : BrushDimmedLight;
        }

        // Modalità normale
        if (isSelected) return BrushSelected;
        if (isHighlighted) return BrushHighlighted;

        return node.Kind switch
        {
            SymbolKind.Project => BrushProject,
            SymbolKind.Form => BrushForm,
            SymbolKind.Class => BrushClass,
            SymbolKind.Module => BrushModule,
            SymbolKind.UserControl => BrushUserControl,
            SymbolKind.Method => BrushMethod,
            SymbolKind.Property => BrushProperty,
            SymbolKind.Field => BrushField,
            SymbolKind.Constant => BrushConstant,
            SymbolKind.Type => BrushType,
            SymbolKind.Enum => BrushEnum,
            _ => Brushes.Gray
        };
    }

    /// <summary>
    /// Progressive disclosure e filtraggio per livelli/categorie:
    /// - I container principali (Project, Form, Class, Module, UserControl) sono sempre presenti nello scheletro architetturale.
    /// - I membri figli e le tipologie sono visibili solo se il relativo container è espanso E la rispettiva categoria è abilitata nel filtro.
    /// - Quando un container o un suo membro è selezionato o espanso, tutti i membri di quel container sono resi visibili indipendentemente dal filtro di dettaglio.
    /// - In modalità Blast Radius o Callees/Callers, i nodi impattati e il percorso di chiamata vengono forzati visibili.
    /// </summary>
    private HashSet<Guid> ComputeVisibleNodeIds(VisualCodeGraph graph)
    {
        var visible = new HashSet<Guid>();
        foreach (var node in graph.Nodes)
        {
            // Se il progetto a cui appartiene il nodo o il nodo radice di progetto è nascosto, ignoralo
            if (node.ProjectId.HasValue && _hiddenProjectIds.Contains(node.ProjectId.Value))
                continue;
            if (node.Kind == SymbolKind.Project && _hiddenProjectIds.Contains(node.Id))
                continue;

            var isPrimaryContainer = node.Kind is SymbolKind.Project or SymbolKind.Class or SymbolKind.Form
                                              or SymbolKind.Module or SymbolKind.UserControl;

            if (isPrimaryContainer)
            {
                visible.Add(node.Id);
                continue;
            }

            // Un membro è forzato visibile se:
            // 1. È nel raggio di blast radius o nei percorsi di chiamata calcolati
            // 2. È il nodo attualmente selezionato
            // 3. È un membro del container attualmente selezionato o condivide il container con il membro selezionato
            var isChildOfSelected = _selectedNode != null && (node.ParentId == _selectedNode.Id || (_selectedNode.ParentId.HasValue && node.ParentId == _selectedNode.ParentId));

            var isForcedVisible = _blastRadiusDepths.ContainsKey(node.Id) ||
                                  _calleeNodeIds.Contains(node.Id) ||
                                  _callerNodeIds.Contains(node.Id) ||
                                  _selectedNode?.Id == node.Id ||
                                  isChildOfSelected;

            // Verifica se la categoria del simbolo è abilitata nel filtro attivo
            var isCategoryEnabled = node.Kind switch
            {
                SymbolKind.Method => ShowMethods,
                SymbolKind.Property => ShowProperties,
                SymbolKind.Field => ShowFields,
                SymbolKind.Constant => ShowConstants,
                SymbolKind.Type or SymbolKind.Enum or SymbolKind.Event => ShowTypesAndEnums,
                _ => true
            };

            // Se la categoria non è abilitata nel filtro e il nodo non è forzato/selezionato, non mostrarlo
            if (!isCategoryEnabled && !isForcedVisible)

                continue;

            var parent = node.ParentId.HasValue ? graph.GetNode(node.ParentId.Value) : null;
            if (parent is { IsExpanded: true } || isForcedVisible || !node.ParentId.HasValue)

                visible.Add(node.Id);
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
    // GESTIONE INPUT PUNTATORE (Pan, Zoom, Drag Nodi, Clic, Doppio Clic)
    // =========================================================================

    /// <summary>
    /// Sposta un nodo e ricorsivamente tutti i suoi nodi discendenti (figli diretti e indiretti) dello stesso vettore delta.
    /// </summary>
    private void MoveNodeAndDescendants(GraphNode rootNode, double deltaX, double deltaY)
    {
        if (Graph is null || (deltaX == 0 && deltaY == 0)) return;

        rootNode.X += deltaX;
        rootNode.Y += deltaY;

        var visited = new HashSet<Guid> { rootNode.Id };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootNode.Id);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            foreach (var child in Graph.GetChildren(currentId))
            {
                if (visited.Add(child.Id))
                {
                    child.X += deltaX;
                    child.Y += deltaY;
                    queue.Enqueue(child.Id);
                }
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsMiddleButtonPressed) return;

        _isPointerDown = true;
        _isPanning = false;
        _isNodeDragging = false;
        _draggedNode = null;
        _pointerDownScreenPos = e.GetPosition(this);

        if (point.Properties.IsLeftButtonPressed)
        {
            var graphPoint = ScreenToGraph(_pointerDownScreenPos);
            var hit = HitTest(graphPoint);
            if (hit != null)
            {
                _draggedNode = hit;
                _lastDragGraphPos = graphPoint;
            }
        }

        e.Pointer.Capture(this);
        Focus();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isPointerDown)
        {
            var currentScreen = e.GetPosition(this);
            var screenDx = currentScreen.X - _pointerDownScreenPos.X;
            var screenDy = currentScreen.Y - _pointerDownScreenPos.Y;
            var screenDistSq = screenDx * screenDx + screenDy * screenDy;

            if (_draggedNode != null)
            {
                if (!_isNodeDragging && screenDistSq > PanThreshold * PanThreshold)

                    _isNodeDragging = true;

                if (_isNodeDragging)
                {
                    var currentGraphPos = ScreenToGraph(currentScreen);
                    var deltaX = currentGraphPos.X - _lastDragGraphPos.X;
                    var deltaY = currentGraphPos.Y - _lastDragGraphPos.Y;

                    MoveNodeAndDescendants(_draggedNode, deltaX, deltaY);
                    _lastDragGraphPos = currentGraphPos;
                    InvalidateVisual();
                }
            }
            else
            {
                if (!_isPanning && screenDistSq > PanThreshold * PanThreshold)

                    _isPanning = true;

                if (_isPanning)
                {
                    var dx = currentScreen.X - _pointerDownScreenPos.X;
                    var dy = currentScreen.Y - _pointerDownScreenPos.Y;
                    _offsetX += dx;
                    _offsetY += dy;
                    _pointerDownScreenPos = currentScreen;
                    InvalidateVisual();
                }
            }
        }
        else if (EnableHoverSelection)
        {
            var graphPoint = ScreenToGraph(e.GetPosition(this));
            var hit = HitTest(graphPoint);

            if (hit != null)
            {
                Cursor = new Cursor(StandardCursorType.Hand);
                if (hit.Id != _selectedNode?.Id)

                    SelectNode(hit);
            }
            else
            {
                Cursor = Cursor.Default;
            }
        }
        else
        {
            var graphPoint = ScreenToGraph(e.GetPosition(this));
            var hit = HitTest(graphPoint);
            Cursor = hit != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_isPointerDown)

            Cursor = Cursor.Default;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);

        if (_isPointerDown)
        {
            if (_draggedNode != null)
            {
                if (!_isNodeDragging)

                    SelectNode(_draggedNode);

                else
                {
                    if (_selectedNode?.Id != _draggedNode.Id)

                        SelectNode(_draggedNode);
                    NodeMoved?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (!_isPanning)
            {
                var graphPoint = ScreenToGraph(e.GetPosition(this));
                var hit = HitTest(graphPoint);
                SelectNode(hit);
            }
        }

        _isPointerDown = false;
        _isPanning = false;
        _isNodeDragging = false;
        _draggedNode = null;
    }

    /// <summary>
    /// Gestisce l'evento di doppio clic per implementare il progressive disclosure:
    /// se il cursore si trova su un nodo container (Class, Form, Module, Type, Enum), ne alterna lo stato di espansione.
    /// </summary>
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var graphPoint = ScreenToGraph(e.GetPosition(this));
        var hit = HitTest(graphPoint);

        if (hit is { IsContainer: true })
        {
            hit.IsExpanded = !hit.IsExpanded;
            InvalidateVisibilityCache();
        }

        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var pointerScreenPos = e.GetPosition(this);
        var graphPointUnderPointer = ScreenToGraph(pointerScreenPos);

        var zoomFactor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        var newScale = Math.Clamp(_scale * zoomFactor, 0.002, 8.0);

        _offsetX = pointerScreenPos.X - Bounds.Width / 2 - graphPointUnderPointer.X * newScale;
        _offsetY = pointerScreenPos.Y - Bounds.Height / 2 - graphPointUnderPointer.Y * newScale;
        _scale = newScale;

        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>
    /// Esegue il rilevamento del nodo sotto il cursore (Hit Testing) nello spazio coordinate del grafo.
    /// Considera solo i nodi attualmente visibili, calcola la distanza euclidea al quadrato e adatta
    /// la tolleranza di selezione (hit radius) in funzione del fattore di zoom corrente.
    /// Utilizza la cache dei nodi visibili per garantire risposta istantanea a 0 allocazioni durante il puntamento del mouse.
    /// </summary>
    /// <param name="graphPoint">Coordinate del puntatore convertite nello spazio cartesiano del grafo.</param>
    /// <returns>Il <see cref="GraphNode"/> più vicino entro il raggio di tolleranza, oppure null.</returns>
    public GraphNode? HitTest(Point graphPoint)
    {
        var graph = Graph;
        if (graph is null) return null;

        // Riutilizza l'insieme dei nodi visibili in cache: fondamentale per non allocare
        // un nuovo HashSet<Guid> ad ogni micro-movimento del cursore del mouse (OnPointerMoved).
        if (_isVisibilityCacheDirty || _cachedVisibleNodeIds == null)
        {
            _cachedVisibleNodeIds = ComputeVisibleNodeIds(graph);
            _isVisibilityCacheDirty = false;
        }

        var visibleIds = _cachedVisibleNodeIds;
        GraphNode? best = null;
        var bestDistSq = double.MaxValue;

        // Scansiona solo i nodi appartenenti alla gerarchia visibile corrente
        foreach (var node in graph.Nodes)
        {
            if (!visibleIds.Contains(node.Id)) continue;

            var dx = node.X - graphPoint.X;
            var dy = node.Y - graphPoint.Y;
            var distSq = dx * dx + dy * dy;

            // Raggio di selezione adattivo scalato con il fattore di zoom (minimo 6px di tolleranza)
            var hitRadius = node.Radius / Math.Max(_scale, 0.002) + 6.0;

            if (distSq <= hitRadius * hitRadius && distSq < bestDistSq)
            {
                best = node;
                bestDistSq = distSq;
            }
        }

        return best;
    }

    /// <summary>
    /// Rileva il nodo posizionato sotto le coordinate a schermo specificate.
    /// </summary>
    public GraphNode? GetNodeAtScreenPos(Point screenPoint) => HitTest(ScreenToGraph(screenPoint));

    // =========================================================================
    // SELEZIONE ED EVIDENZIAZIONE CALLERS / CALLEES / BLAST RADIUS
    // =========================================================================

    /// <summary>
    /// Ripristina lo stato interno delle strutture dati di evidenziazione (chiamanti, chiamati, percorsi e blast radius).
    /// </summary>
    private void ClearInternalHighlightState()
    {
        _callerNodeIds.Clear();
        _calleeNodeIds.Clear();
        _calleeContainerIds.Clear();
        _callerContainerIds.Clear();
        _calleeEdgeIds.Clear();
        _callerEdgeIds.Clear();
        _blastRadiusDepths.Clear();
        _affectedContainerIds.Clear();
        _highlightedEdges.Clear();
    }

    /// <summary>
    /// Seleziona esplicitamente un nodo sul canvas, evidenziando i suoi archi e vicini diretti
    /// e notificando i listener tramite l'evento <see cref="SelectionChanged"/>.
    /// </summary>
    /// <param name="node">Il nodo da selezionare, oppure null per deselezionare.</param>
    public void SelectNode(GraphNode? node)
    {
        _selectedNode = node;
        _highlightedNodeIds.Clear();
        ClearInternalHighlightState();
        _currentHighlightMode = HighlightMode.Normal;

        if (node is not null && Graph is { } graph)
        {
            _highlightedNodeIds.Add(node.Id);

            if (node.IsContainer)
                node.IsExpanded = true;
            else if (node.ParentId.HasValue)
            {
                var parent = graph.GetNode(node.ParentId.Value);
                if (parent is not null)
                    parent.IsExpanded = true;
            }

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

        InvalidateVisibilityCache();
        SelectionChanged?.Invoke(this, node);
    }

    /// <summary>
    /// Evidenzia sul grafo tutti i chiamanti (callers diretti) del nodo selezionato (routine o container).
    /// Se è selezionato un container (Classe, Modulo, Form), aggrega i chiamanti di tutte le routine membro.
    /// Espande automaticamente i container dei chiamanti per rendere i nodi visibili.
    /// </summary>
    public IReadOnlyList<Symbol> HighlightCallersForSelected()
    {
        ClearInternalHighlightState();

        if (_selectedNode is null || Graph?.Queries is null)
            return Array.Empty<Symbol>();

        var symbolsToQuery = new List<Symbol>();

        if (_selectedNode.IsContainer)
        {
            _selectedNode.IsExpanded = true;
            foreach (var childNode in Graph.GetChildren(_selectedNode.Id))
            {
                if (childNode.SemanticSymbol is not null && childNode.Kind is SymbolKind.Method or SymbolKind.Property)
                    symbolsToQuery.Add(childNode.SemanticSymbol);
            }
        }
        else if (_selectedNode.SemanticSymbol is not null)
            symbolsToQuery.Add(_selectedNode.SemanticSymbol);

        if (symbolsToQuery.Count == 0)
            return Array.Empty<Symbol>();

        var callersSet = new HashSet<Guid>();
        var resultList = new List<Symbol>();

        foreach (var targetSym in symbolsToQuery)
        {
            var directCallers = Graph.Queries.GetDirectCallers(targetSym);
            foreach (var caller in directCallers)
            {
                _callerEdgeIds.Add((caller.Id, targetSym.Id));

                if (callersSet.Add(caller.Id))
                {
                    resultList.Add(caller);
                    _callerNodeIds.Add(caller.Id);

                    // Auto-espansione del container del caller
                    var callerNode = Graph.GetNode(caller.Id);
                    if (callerNode?.ParentId.HasValue == true)
                    {
                        _callerContainerIds.Add(callerNode.ParentId.Value);
                        if (Graph.GetNode(callerNode.ParentId.Value) is { } parentNode)
                            parentNode.IsExpanded = true;
                    }
                }
            }
        }

        // Espandi il container padre del nodo selezionato se presente
        if (_selectedNode.ParentId.HasValue && Graph.GetNode(_selectedNode.ParentId.Value) is { } selParent)
            selParent.IsExpanded = true;

        _currentHighlightMode = HighlightMode.Callers;
        InvalidateVisibilityCache();
        return resultList;
    }

    /// <summary>
    /// Evidenzia sul grafo tutti i chiamati (callees diretti) del nodo selezionato (routine o container).
    /// Se è selezionato un container (Classe, Modulo, Form), aggrega le chiamate di tutte le routine membro.
    /// Espande automaticamente i container di destinazione per rendere i nodi visibili.
    /// </summary>
    public IReadOnlyList<Symbol> HighlightCalleesForSelected()
    {
        ClearInternalHighlightState();

        if (_selectedNode is null || Graph?.Queries is null)
            return Array.Empty<Symbol>();

        var symbolsToQuery = new List<Symbol>();

        if (_selectedNode.IsContainer)
        {
            _selectedNode.IsExpanded = true;
            foreach (var childNode in Graph.GetChildren(_selectedNode.Id))
            {
                if (childNode.SemanticSymbol is not null && childNode.Kind is SymbolKind.Method or SymbolKind.Property)
                    symbolsToQuery.Add(childNode.SemanticSymbol);
            }
        }
        else if (_selectedNode.SemanticSymbol is not null)
            symbolsToQuery.Add(_selectedNode.SemanticSymbol);

        if (symbolsToQuery.Count == 0)
            return Array.Empty<Symbol>();

        var calleesSet = new HashSet<Guid>();
        var resultList = new List<Symbol>();

        foreach (var callerSym in symbolsToQuery)
        {
            var directCallees = Graph.Queries.GetDirectCallees(callerSym);
            foreach (var callee in directCallees)
            {
                _calleeEdgeIds.Add((callerSym.Id, callee.Id));

                if (calleesSet.Add(callee.Id))
                {
                    resultList.Add(callee);
                    _calleeNodeIds.Add(callee.Id);

                    // Auto-espansione del container del callee
                    var calleeNode = Graph.GetNode(callee.Id);
                    if (calleeNode?.ParentId.HasValue == true)
                    {
                        _calleeContainerIds.Add(calleeNode.ParentId.Value);
                        if (Graph.GetNode(calleeNode.ParentId.Value) is { } parentNode)
                            parentNode.IsExpanded = true;
                    }
                }
            }
        }

        // Espandi il container padre del nodo selezionato se presente
        if (_selectedNode.ParentId.HasValue && Graph.GetNode(_selectedNode.ParentId.Value) is { } selParent)
            selParent.IsExpanded = true;

        _currentHighlightMode = HighlightMode.Callees;
        InvalidateVisibilityCache();
        return resultList;
    }

    /// <summary>
    /// Evidenzia sul grafo l'intero Blast Radius (M6): target in rosso, chiamanti per livello di profondità e Form UI toccate.
    /// Espande automaticamente i container dei nodi impattati.
    /// </summary>
    public void HighlightBlastRadius(ImpactReport report)
    {
        ClearInternalHighlightState();
        _activeBlastRadiusReport = report;

        // Chiamanti diretti (Depth 1)
        foreach (var dc in report.DirectCallers)
        {
            _blastRadiusDepths[dc.Id] = 1;
            if (Graph?.GetNode(dc.Id) is { ParentId: { } pId } && Graph.GetNode(pId) is { } pNode)
                pNode.IsExpanded = true;
        }

        // Chiamanti indiretti (Depth >= 2)
        foreach (var ic in report.IndirectCallers)
        {
            _blastRadiusDepths[ic.Symbol.Id] = ic.Depth;
            if (Graph?.GetNode(ic.Symbol.Id) is { ParentId: { } pId } && Graph.GetNode(pId) is { } pNode)
                pNode.IsExpanded = true;
        }

        // Container impattati (Form, Classi, Moduli)
        foreach (var f in report.AffectedForms) _affectedContainerIds.Add(f.Id);
        foreach (var c in report.AffectedClasses) _affectedContainerIds.Add(c.Id);
        foreach (var m in report.AffectedModules) _affectedContainerIds.Add(m.Id);

        _currentHighlightMode = HighlightMode.BlastRadius;
        InvalidateVisibilityCache();
    }

    /// <summary>
    /// Ripristina l'evidenziazione normale.
    /// </summary>
    public void ClearHighlightMode()
    {
        ClearInternalHighlightState();
        _activeBlastRadiusReport = null;
        _currentHighlightMode = HighlightMode.Normal;
        InvalidateVisibilityCache();
    }

    /// <summary>
    /// Applica il tema al canvas del grafo (colore di sfondo ed etichette).
    /// </summary>
    public void ApplyTheme(ThemeDefinition theme)
    {
        _isDarkTheme = theme.IsDark;

        if (Color.TryParse(theme.GraphBackground, out var bgCol))
            _backgroundBrush = new SolidColorBrush(bgCol);

        if (theme.IsDark)
        {
            _defaultLabelBrush = Brushes.WhiteSmoke;
            _dimmedLabelBrush = new SolidColorBrush(Color.Parse("#555566"));
            _edgeContainsPen = new Pen(new SolidColorBrush(Color.Parse("#30303E")), 1);
            _edgeDimmedPen = new Pen(new SolidColorBrush(Color.Parse("#252532")), 0.8);
        }
        else
        {
            _defaultLabelBrush = new SolidColorBrush(Color.Parse("#1A1A24"));
            _dimmedLabelBrush = new SolidColorBrush(Color.Parse("#9E9EB0"));
            _edgeContainsPen = new Pen(new SolidColorBrush(Color.Parse("#D0D0DC")), 1);
            _edgeDimmedPen = new Pen(new SolidColorBrush(Color.Parse("#E4E4EE")), 0.8);
        }

        InvalidateVisual();
    }
}
