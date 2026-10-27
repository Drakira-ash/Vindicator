using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// 把 <see cref="TDRoadNetwork"/> 的图结构转换成单个道路网格。
/// 纯几何计算，不依赖编辑器，可在运行时直接调用。
/// </summary>
public static class TDRoadMeshBuilder
{
    /// <summary>路口补片相对路面的抬升量，避免与路面共面闪烁。</summary>
    private const float JunctionLift = 0.002f;

    /// <summary>路口补片最远能伸出多远（相对路宽倍数），防止夹角极小时炸开。</summary>
    private const float JunctionMaxExtent = 3f;

    /// <summary>
    /// 为整张路网生成网格（路面条带 + 路口补片）。
    /// </summary>
    /// <param name="network">源路网。</param>
    /// <param name="mesh">要写入的网格，会被清空。</param>
    public static void Build(TDRoadNetwork network, Mesh mesh)
    {
        if (network == null || mesh == null)
        {
            return;
        }

        var vertices = new List<Vector3>(256);
        var normals = new List<Vector3>(256);
        var uvs = new List<Vector2>(256);
        var triangles = new List<int>(512);

        for (int i = 0; i < network.Edges.Count; i++)
        {
            AppendEdge(network, network.Edges[i], vertices, normals, uvs, triangles);
        }

        for (int i = 0; i < network.Nodes.Count; i++)
        {
            var node = network.Nodes[i];
            if (network.GetDegree(node.id) >= 3)
            {
                AppendJunction(network, node, vertices, normals, uvs, triangles);
            }
        }

        mesh.Clear();
        if (vertices.Count == 0)
        {
            mesh.RecalculateBounds();
            return;
        }

        mesh.indexFormat = vertices.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// 为一条边生成两节点的三次贝塞尔样条（本地空间）。
    /// 两端切线由节点度数决定：度数为 2 的节点取角平分线形成圆滑转弯，
    /// 端点与路口则沿边直出，让各条路从路口向外辐射。
    /// </summary>
    /// <param name="network">所属路网。</param>
    /// <param name="edge">目标边。</param>
    /// <returns>样条；数据非法时返回空样条。</returns>
    public static Spline BuildEdgeSpline(TDRoadNetwork network, TDRoadNetwork.Edge edge)
    {
        var spline = new Spline();
        if (network == null || edge == null)
        {
            return spline;
        }

        var a = network.GetNode(edge.a);
        var b = network.GetNode(edge.b);
        if (a == null || b == null)
        {
            return spline;
        }

        float length = Vector3.Distance(a.position, b.position);
        if (length < 1e-3f)
        {
            return spline;
        }

        float handle = Mathf.Clamp(network.CornerSmoothness, 0.05f, 0.45f) * length;
        Vector3 departure = network.GetDepartureDirection(edge.a, edge.b);
        Vector3 arrival = network.GetArrivalDirection(edge.b, edge.a);

        spline.Add(new BezierKnot(
            (float3)a.position,
            (float3)(-departure * handle),
            (float3)(departure * handle)));

        spline.Add(new BezierKnot(
            (float3)b.position,
            (float3)(-arrival * handle),
            (float3)(arrival * handle)));

        return spline;
    }

    /// <summary>
    /// 沿一条边采样并追加带状路面。
    /// </summary>
    private static void AppendEdge(
        TDRoadNetwork network,
        TDRoadNetwork.Edge edge,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        var spline = BuildEdgeSpline(network, edge);
        if (spline.Count < 2)
        {
            return;
        }

        float length = spline.GetLength();
        float halfWidth = network.GetEdgeWidth(edge) * 0.5f;
        float density = Mathf.Max(0.25f, network.SamplesPerMeter);
        int segments = Mathf.Max(2, Mathf.CeilToInt(length * density));
        int start = vertices.Count;

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 center = spline.EvaluatePosition(t);
            Vector3 tangent = ((Vector3)spline.EvaluateTangent(t)).normalized;

            if (tangent.sqrMagnitude < 1e-6f)
            {
                tangent = Vector3.forward;
            }

            Vector3 lateral = Vector3.Cross(Vector3.up, tangent).normalized;
            if (lateral.sqrMagnitude < 1e-6f)
            {
                lateral = Vector3.right;
            }

            center.y = network.RoadHeight;

            vertices.Add(center - lateral * halfWidth);
            vertices.Add(center + lateral * halfWidth);
            normals.Add(Vector3.up);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0f, t));
            uvs.Add(new Vector2(1f, t));

            if (i == segments)
            {
                continue;
            }

            int root = start + i * 2;
            triangles.Add(root);
            triangles.Add(root + 2);
            triangles.Add(root + 1);
            triangles.Add(root + 1);
            triangles.Add(root + 2);
            triangles.Add(root + 3);
        }
    }

    /// <summary>
    /// 在路口追加一块圆盘补片，盖住多条路交汇处的重叠并填平楔形缺口。
    /// 半径按相邻两条路的夹角计算：夹角越尖，路口越大。
    /// </summary>
    private static void AppendJunction(
        TDRoadNetwork network,
        TDRoadNetwork.Node node,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        var neighbors = new List<int>();
        network.GetNeighbors(node.id, neighbors);
        if (neighbors.Count < 3)
        {
            return;
        }

        // 收集每个方向的手臂：方向 + 半宽，并按角度排序。
        var directions = new List<Vector3>(neighbors.Count);
        var halfWidths = new List<float>(neighbors.Count);
        for (int i = 0; i < neighbors.Count; i++)
        {
            var other = network.GetNode(neighbors[i]);
            if (other == null)
            {
                continue;
            }

            Vector3 direction = other.position - node.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f)
            {
                continue;
            }

            directions.Add(direction.normalized);
            halfWidths.Add(GetEdgeHalfWidth(network, node.id, neighbors[i]));
        }

        if (directions.Count < 3)
        {
            return;
        }

        var order = new List<int>(directions.Count);
        for (int i = 0; i < directions.Count; i++)
        {
            order.Add(i);
        }

        order.Sort((lhs, rhs) =>
            Mathf.Atan2(directions[lhs].z, directions[lhs].x)
                .CompareTo(Mathf.Atan2(directions[rhs].z, directions[rhs].x)));

        Vector3 center = node.position;
        center.y = network.RoadHeight + JunctionLift;

        int centerIndex = vertices.Count;
        vertices.Add(center);
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(0.5f, 0.5f));

        // 每对相邻手臂之间填一个楔形：两侧边缘延长直到相交，用两个三角形盖住缺口。
        for (int i = 0; i < order.Count; i++)
        {
            int a = order[i];
            int b = order[(i + 1) % order.Count];

            Vector3 dirA = directions[a];
            Vector3 dirB = directions[b];
            Vector3 bisector = (dirA + dirB).normalized;
            if (bisector.sqrMagnitude < 1e-6f)
            {
                continue;
            }

            Vector3 perpA = Vector3.Cross(Vector3.up, dirA).normalized;
            Vector3 perpB = Vector3.Cross(Vector3.up, dirB).normalized;
            float signA = Vector3.Dot(perpA, bisector) >= 0f ? 1f : -1f;
            float signB = Vector3.Dot(perpB, bisector) >= 0f ? 1f : -1f;

            Vector3 cornerA = center + perpA * (halfWidths[a] * signA);
            Vector3 cornerB = center + perpB * (halfWidths[b] * signB);

            if (!TryLineIntersection(cornerA, dirA, cornerB, dirB, out Vector3 tip))
            {
                continue;
            }

            float maxExtent = Mathf.Max(halfWidths[a], halfWidths[b]) * JunctionMaxExtent;
            Vector3 fromCenter = tip - center;
            if (fromCenter.sqrMagnitude > maxExtent * maxExtent)
            {
                tip = center + fromCenter.normalized * maxExtent;
            }

            int indexA = AddVertex(vertices, normals, uvs, cornerA);
            int indexB = AddVertex(vertices, normals, uvs, cornerB);
            int indexTip = AddVertex(vertices, normals, uvs, tip);

            AddTriangleUp(vertices, triangles, centerIndex, indexA, indexTip);
            AddTriangleUp(vertices, triangles, centerIndex, indexTip, indexB);
        }
    }

    /// <summary>
    /// 追加一个朝上的顶点，返回它的索引。
    /// </summary>
    private static int AddVertex(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        Vector3 position)
    {
        int index = vertices.Count;
        vertices.Add(position);
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(position.x * 0.1f, position.z * 0.1f));
        return index;
    }

    /// <summary>
    /// 追加一个法线朝上的三角形；输入顺序不正时自动翻转。
    /// </summary>
    private static void AddTriangleUp(List<Vector3> vertices, List<int> triangles, int a, int b, int c)
    {
        Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
        if (normal.y < 0f)
        {
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
            return;
        }

        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    /// <summary>
    /// 求两条 XZ 平面上直线的交点。
    /// </summary>
    private static bool TryLineIntersection(
        Vector3 originA,
        Vector3 directionA,
        Vector3 originB,
        Vector3 directionB,
        out Vector3 intersection)
    {
        float denominator = directionA.x * directionB.z - directionA.z * directionB.x;
        if (Mathf.Abs(denominator) < 1e-4f)
        {
            intersection = default;
            return false;
        }

        float t = ((originB.x - originA.x) * directionB.z - (originB.z - originA.z) * directionB.x)
                  / denominator;
        intersection = originA + directionA * t;
        intersection.y = originA.y;
        return true;
    }

    /// <summary>
    /// 查询两个节点之间那条边的半宽。
    /// </summary>
    private static float GetEdgeHalfWidth(TDRoadNetwork network, int a, int b)
    {
        for (int i = 0; i < network.Edges.Count; i++)
        {
            var edge = network.Edges[i];
            if ((edge.a == a && edge.b == b) || (edge.a == b && edge.b == a))
            {
                return network.GetEdgeWidth(edge) * 0.5f;
            }
        }

        return network.DefaultWidth * 0.5f;
    }
}
