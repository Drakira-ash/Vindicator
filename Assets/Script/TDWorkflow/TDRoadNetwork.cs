using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 塔防道路路网：由「节点 + 无向边」构成的图，自动生成整条道路的网格与碰撞体。
/// 节点度数由边派生：1 = 端点，2 = 转弯点，3 及以上 = 路口。
/// 节点坐标保存在本对象的本地空间，y 为地面高度（生成网格时统一抬到道路高度）。
/// </summary>
[DisallowMultipleComponent]
public class TDRoadNetwork : MonoBehaviour
{
    /// <summary>生成的道路网格子对象名称。</summary>
    public const string MeshChildName = "RoadMesh";

    /// <summary>路网中的一个点。</summary>
    [Serializable]
    public class Node
    {
        public int id;
        public Vector3 position;

        public Node(int id, Vector3 position)
        {
            this.id = id;
            this.position = position;
        }
    }

    /// <summary>连接两个节点的无向边。</summary>
    [Serializable]
    public class Edge
    {
        public int a;
        public int b;

        /// <summary>该边的路宽；小于等于 0 表示使用默认路宽。</summary>
        public float width;

        public Edge(int a, int b, float width = 0f)
        {
            this.a = a;
            this.b = b;
            this.width = width;
        }
    }

    [Header("路网数据")]
    [SerializeField] private List<Node> nodes = new List<Node>();
    [SerializeField] private List<Edge> edges = new List<Edge>();
    [SerializeField] private int nextNodeId = 1;

    [Header("生成参数")]
    [SerializeField, Min(0.1f)] private float defaultWidth = 5f;
    [SerializeField, Range(0.05f, 0.5f)] private float cornerSmoothness = 0.35f;
    [SerializeField, Min(0.25f)] private float samplesPerMeter = 1f;
    [SerializeField] private float roadHeight = 0.02f;
    [SerializeField] private Material roadMaterial;
    [SerializeField] private bool autoRebuild = true;

    /// <summary>所有节点（只读）。</summary>
    public IReadOnlyList<Node> Nodes => nodes;

    /// <summary>所有边（只读）。</summary>
    public IReadOnlyList<Edge> Edges => edges;

    /// <summary>默认路宽（米）。</summary>
    public float DefaultWidth => defaultWidth;

    /// <summary>弯道切线长度占边长的比例，越大转弯越平缓。</summary>
    public float CornerSmoothness => cornerSmoothness;

    /// <summary>每米采样点数，控制路面网格密度。</summary>
    public float SamplesPerMeter => samplesPerMeter;

    /// <summary>道路网格相对地面抬升的高度。</summary>
    public float RoadHeight => roadHeight;

    /// <summary>道路材质。</summary>
    public Material RoadMaterial => roadMaterial;

    /// <summary>路网是否为空。</summary>
    public bool IsEmpty => nodes.Count == 0;

    /// <summary>节点数量。</summary>
    public int NodeCount => nodes.Count;

    /// <summary>边数量。</summary>
    public int EdgeCount => edges.Count;

    /// <summary>
    /// 按 id 查找节点。
    /// </summary>
    /// <param name="id">节点 id。</param>
    /// <returns>找到的节点；不存在时返回 null。</returns>
    public Node GetNode(int id)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].id == id)
            {
                return nodes[i];
            }
        }

        return null;
    }

    /// <summary>
    /// 添加一个节点。
    /// </summary>
    /// <param name="localPosition">相对本对象的本地坐标。</param>
    /// <returns>新节点的 id。</returns>
    public int AddNode(Vector3 localPosition)
    {
        var node = new Node(nextNodeId++, localPosition);
        nodes.Add(node);
        return node.id;
    }

    /// <summary>
    /// 查找距离指定位置最近的节点。
    /// </summary>
    /// <param name="localPosition">查询位置（本地坐标）。</param>
    /// <param name="maxDistance">最大搜索半径，超出则不算命中。</param>
    /// <returns>命中的节点；没有命中返回 null。</returns>
    public Node FindNearestNode(Vector3 localPosition, float maxDistance)
    {
        Node best = null;
        float bestSqr = maxDistance * maxDistance;

        for (int i = 0; i < nodes.Count; i++)
        {
            float sqr = (nodes[i].position - localPosition).sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                best = nodes[i];
            }
        }

        return best;
    }

    /// <summary>
    /// 删除节点以及所有与它相连的边。
    /// </summary>
    /// <param name="id">要删除的节点 id。</param>
    /// <returns>是否确实删除了节点。</returns>
    public bool RemoveNode(int id)
    {
        int index = nodes.FindIndex(n => n.id == id);
        if (index < 0)
        {
            return false;
        }

        nodes.RemoveAt(index);
        edges.RemoveAll(e => e.a == id || e.b == id);
        return true;
    }

    /// <summary>
    /// 添加一条边；自环、重复边和非法节点都会被拒绝。
    /// </summary>
    /// <param name="a">起点节点 id。</param>
    /// <param name="b">终点节点 id。</param>
    /// <param name="width">该边路宽，0 表示使用默认路宽。</param>
    /// <returns>是否成功添加。</returns>
    public bool AddEdge(int a, int b, float width = 0f)
    {
        if (a == b || GetNode(a) == null || GetNode(b) == null || HasEdge(a, b))
        {
            return false;
        }

        edges.Add(new Edge(a, b, width));
        return true;
    }

    /// <summary>
    /// 删除两个节点之间的边。
    /// </summary>
    /// <param name="a">节点 a 的 id。</param>
    /// <param name="b">节点 b 的 id。</param>
    /// <returns>是否确实删除了边。</returns>
    public bool RemoveEdge(int a, int b)
    {
        return edges.RemoveAll(e => (e.a == a && e.b == b) || (e.a == b && e.b == a)) > 0;
    }

    /// <summary>
    /// 判断两个节点之间是否已有边。
    /// </summary>
    /// <param name="a">节点 a 的 id。</param>
    /// <param name="b">节点 b 的 id。</param>
    /// <returns>是否存在该边。</returns>
    public bool HasEdge(int a, int b)
    {
        for (int i = 0; i < edges.Count; i++)
        {
            if ((edges[i].a == a && edges[i].b == b) || (edges[i].a == b && edges[i].b == a))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 获取指定节点的所有相邻节点 id。
    /// </summary>
    /// <param name="id">节点 id。</param>
    /// <param name="results">用于接收结果的列表，会被清空。</param>
    public void GetNeighbors(int id, List<int> results)
    {
        results.Clear();
        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i].a == id)
            {
                results.Add(edges[i].b);
            }
            else if (edges[i].b == id)
            {
                results.Add(edges[i].a);
            }
        }
    }

    /// <summary>
    /// 获取节点的度数（相连边的数量）：1 = 端点，2 = 转弯点，3 及以上 = 路口。
    /// </summary>
    /// <param name="id">节点 id。</param>
    /// <returns>度数。</returns>
    public int GetDegree(int id)
    {
        int degree = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i].a == id || edges[i].b == id)
            {
                degree++;
            }
        }

        return degree;
    }

    /// <summary>
    /// 判断节点是否为路口（度数 3 及以上）。
    /// </summary>
    /// <param name="id">节点 id。</param>
    /// <returns>是否为路口。</returns>
    public bool IsJunction(int id)
    {
        return GetDegree(id) >= 3;
    }

    /// <summary>
    /// 取一个节点上、除去指定邻居之外的另一个邻居；只在度数为 2 时有意义。
    /// </summary>
    /// <param name="nodeId">节点 id。</param>
    /// <param name="neighborId">要排除的邻居 id。</param>
    /// <returns>另一个邻居的 id；不存在时返回 -1。</returns>
    public int GetOtherNeighbor(int nodeId, int neighborId)
    {
        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i].a == nodeId && edges[i].b != neighborId)
            {
                return edges[i].b;
            }

            if (edges[i].b == nodeId && edges[i].a != neighborId)
            {
                return edges[i].a;
            }
        }

        return -1;
    }

    /// <summary>
    /// 获取一条边的实际路宽。
    /// </summary>
    /// <param name="edge">目标边。</param>
    /// <returns>路宽（米）。</returns>
    public float GetEdgeWidth(Edge edge)
    {
        return edge.width > 0f ? edge.width : defaultWidth;
    }

    /// <summary>
    /// 计算从节点出发、朝指定邻居前进时的出发切线方向（本地空间单位向量）。
    /// 度数为 2 时取两条相邻边的角平分线，形成圆滑转弯；端点与路口则沿边直出。
    /// </summary>
    /// <param name="nodeId">当前节点 id。</param>
    /// <param name="towardNeighborId">前进方向上的邻居 id。</param>
    /// <returns>单位方向向量；数据异常时返回 Vector3.forward。</returns>
    public Vector3 GetDepartureDirection(int nodeId, int towardNeighborId)
    {
        var node = GetNode(nodeId);
        var toward = GetNode(towardNeighborId);
        if (node == null || toward == null)
        {
            return Vector3.forward;
        }

        Vector3 toNeighbor = (toward.position - node.position).normalized;
        if (GetDegree(nodeId) != 2)
        {
            return toNeighbor;
        }

        int other = GetOtherNeighbor(nodeId, towardNeighborId);
        if (other < 0)
        {
            return toNeighbor;
        }

        Vector3 toOther = (GetNode(other).position - node.position).normalized;
        Vector3 bisector = toNeighbor - toOther;
        return bisector.sqrMagnitude < 1e-6f ? toNeighbor : bisector.normalized;
    }

    /// <summary>
    /// 计算从指定邻居驶入节点时的到达切线方向（本地空间单位向量）。
    /// </summary>
    /// <param name="nodeId">驶入的节点 id。</param>
    /// <param name="fromNeighborId">来源邻居 id。</param>
    /// <returns>单位方向向量；数据异常时返回 Vector3.forward。</returns>
    public Vector3 GetArrivalDirection(int nodeId, int fromNeighborId)
    {
        var node = GetNode(nodeId);
        var from = GetNode(fromNeighborId);
        if (node == null || from == null)
        {
            return Vector3.forward;
        }

        Vector3 toFrom = (from.position - node.position).normalized;
        Vector3 inward = -toFrom;
        if (GetDegree(nodeId) != 2)
        {
            return inward;
        }

        int other = GetOtherNeighbor(nodeId, fromNeighborId);
        if (other < 0)
        {
            return inward;
        }

        Vector3 toOther = (GetNode(other).position - node.position).normalized;
        Vector3 bisector = toOther - toFrom;
        return bisector.sqrMagnitude < 1e-6f ? inward : bisector.normalized;
    }

    /// <summary>
    /// 用广度优先搜索求两个节点之间的最短路径（按边数）。
    /// </summary>
    /// <param name="fromId">起点节点 id。</param>
    /// <param name="toId">终点节点 id。</param>
    /// <param name="path">用于接收路径节点 id 的列表，会被清空。</param>
    /// <returns>是否找到路径。</returns>
    public bool TryGetNodePath(int fromId, int toId, List<int> path)
    {
        path.Clear();
        if (GetNode(fromId) == null || GetNode(toId) == null)
        {
            return false;
        }

        var previous = new Dictionary<int, int> { { fromId, -1 } };
        var queue = new Queue<int>();
        queue.Enqueue(fromId);
        var neighbors = new List<int>();

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (current == toId)
            {
                break;
            }

            GetNeighbors(current, neighbors);
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (previous.ContainsKey(neighbors[i]))
                {
                    continue;
                }

                previous[neighbors[i]] = current;
                queue.Enqueue(neighbors[i]);
            }
        }

        if (!previous.ContainsKey(toId))
        {
            return false;
        }

        for (int cursor = toId; cursor != -1; cursor = previous[cursor])
        {
            path.Add(cursor);
        }

        path.Reverse();
        return true;
    }

    /// <summary>
    /// 获取所有端点（度数为 1 的节点），通常就是入口与出口。
    /// </summary>
    /// <param name="results">用于接收结果的列表，会被清空。</param>
    public void GetEndpointNodeIds(List<int> results)
    {
        results.Clear();
        for (int i = 0; i < nodes.Count; i++)
        {
            if (GetDegree(nodes[i].id) == 1)
            {
                results.Add(nodes[i].id);
            }
        }
    }

    /// <summary>
    /// 清空整个路网。
    /// </summary>
    public void Clear()
    {
        nodes.Clear();
        edges.Clear();
        nextNodeId = 1;
    }

    /// <summary>
    /// 确保存在承载道路网格的子对象，并返回它的各个组件。
    /// </summary>
    /// <param name="filter">网格过滤器。</param>
    /// <param name="renderer">网格渲染器。</param>
    /// <param name="collider">网格碰撞体。</param>
    /// <returns>子对象 Transform。</returns>
    public Transform GetOrCreateMeshObject(out MeshFilter filter, out MeshRenderer renderer, out MeshCollider collider)
    {
        var existing = transform.Find(MeshChildName);
        GameObject go;
        if (existing == null)
        {
            go = new GameObject(MeshChildName);
            go.transform.SetParent(transform, false);
        }
        else
        {
            go = existing.gameObject;
        }

        filter = go.GetComponent<MeshFilter>();
        if (filter == null)
        {
            filter = go.AddComponent<MeshFilter>();
        }

        renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null)
        {
            renderer = go.AddComponent<MeshRenderer>();
        }

        collider = go.GetComponent<MeshCollider>();
        if (collider == null)
        {
            collider = go.AddComponent<MeshCollider>();
        }

        if (roadMaterial != null && renderer.sharedMaterial != roadMaterial)
        {
            renderer.sharedMaterial = roadMaterial;
        }

        return go.transform;
    }

    /// <summary>
    /// 按当前路网重建道路网格与碰撞体。
    /// </summary>
    public void Rebuild()
    {
        var meshTransform = GetOrCreateMeshObject(out var filter, out var renderer, out var collider);
        if (meshTransform == null)
        {
            return;
        }

        var mesh = filter.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh { name = name + "_RoadMesh" };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
        }

        TDRoadMeshBuilder.Build(this, mesh);
        collider.sharedMesh = null;
        collider.sharedMesh = mesh;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 在编辑器中修改参数后自动重建网格。
    /// </summary>
    private void OnValidate()
    {
        if (autoRebuild && !Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += RebuildIfAlive;
        }
    }

    private void RebuildIfAlive()
    {
        if (this != null && isActiveAndEnabled)
        {
            Rebuild();
        }
    }
#endif
}
