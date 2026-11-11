using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Visual graph model for rendering and UI interaction.
/// Maintains the list of positioned <see cref="GraphNode"/> and <see cref="GraphEdge"/>,
/// and acts as a bridge to the <see cref="SemanticGraph"/> and <see cref="CallGraphQueries"/> below.
/// </summary>
public sealed class VisualCodeGraph
{
    public List<GraphNode> Nodes { get; } = new();
    public List<GraphEdge> Edges { get; } = new();

    /// <summary>Reference to the underlying real semantic CodeGraph.</summary>
    public Macondo.Semantic.Model.CodeGraph? SemanticGraph { get; set; }

    /// <summary>Semantic query engine on the Call Graph.</summary>
    public CallGraphQueries? Queries { get; set; }

    private Dictionary<Guid, GraphNode>? _nodesById;
    private Dictionary<Guid, List<GraphNode>>? _childrenByParentId;
    private Dictionary<Guid, List<GraphEdge>>? _incidentEdgesByNodeId;

    /// <summary>Retrieves a graph node by its unique ID.</summary>
    public GraphNode? GetNode(Guid id)
    {
        _nodesById ??= Nodes.ToDictionary(n => n.Id);
        return _nodesById.TryGetValue(id, out var node) ? node : null;
    }

    /// <summary>
    /// Returns the incident edges (incoming and outgoing) connected to the specified node in O(1) time.
    /// Builds and maintains an adjacency cache (Incidence List) on the first request to avoid
    /// the O(M) linear scan of the entire list of edges of the graph during node selection.
    /// </summary>
    /// <param name="nodeId">Unique identifier of the node whose connected relationships are to be retrieved.</param>
    /// <returns>The read-only list of all the edges in which the node appears as source or destination.</returns>
    public IReadOnlyList<GraphEdge> GetIncidentEdges(Guid nodeId)
    {
        // If the cache is not yet built, index all edges by end node IDs
        if (_incidentEdgesByNodeId == null)
        {
            _incidentEdgesByNodeId = new Dictionary<Guid, List<GraphEdge>>(Nodes.Count);
            foreach (var edge in Edges)
            {
                // Record the edge for the source node
                if (!_incidentEdgesByNodeId.TryGetValue(edge.SourceId, out var srcList))
                {
                    srcList = new List<GraphEdge>();
                    _incidentEdgesByNodeId[edge.SourceId] = srcList;
                }
                srcList.Add(edge);

                // Record the edge for the destination node
                if (!_incidentEdgesByNodeId.TryGetValue(edge.TargetId, out var tgtList))
                {
                    tgtList = new List<GraphEdge>();
                    _incidentEdgesByNodeId[edge.TargetId] = tgtList;
                }
                tgtList.Add(edge);
            }
        }

        return _incidentEdgesByNodeId.TryGetValue(nodeId, out var edges) ? edges : Array.Empty<GraphEdge>();
    }

    /// <summary>
    /// Returns the child nodes contained directly in the specified node.
    /// Instant O(1) execution via cached internal hierarchical index.
    /// </summary>
    public IEnumerable<GraphNode> GetChildren(Guid parentId)
    {
        if (_childrenByParentId == null)
        {
            _childrenByParentId = new Dictionary<Guid, List<GraphNode>>();
            foreach (var node in Nodes)
            {
                if (node.ParentId.HasValue)
                {
                    if (!_childrenByParentId.TryGetValue(node.ParentId.Value, out var list))
                    {
                        list = new List<GraphNode>();
                        _childrenByParentId[node.ParentId.Value] = list;
                    }
                    list.Add(node);
                }
            }
        }

        return _childrenByParentId.TryGetValue(parentId, out var children) ? children : Enumerable.Empty<GraphNode>();
    }

    /// <summary>Invalidate ID, hierarchical tree, and query cache after graph structure changes.</summary>
    public void Invalidate()
    {
        _nodesById = null;
        _childrenByParentId = null;
        _incidentEdgesByNodeId = null;
        Queries?.ClearCache();
    }
}
