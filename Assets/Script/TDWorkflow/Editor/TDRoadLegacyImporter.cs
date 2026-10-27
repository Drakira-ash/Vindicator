using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把早期手工搭建的 ProBuilder 道路块（Road_OldBackup）转换成可编辑的路网。
/// 相邻几何体共享边界顶点，按共享关系建图，得到与手绘路一致的连通拓扑。
/// 结果是启发性近似，导入后仍建议人工微调。
/// </summary>
public static class TDRoadLegacyImporter
{
    /// <summary>拾取旧路时使用的根对象名。</summary>
    private const string LegacyRootName = "Road_OldBackup";

    /// <summary>两个顶点相距小于该值即视为共享（米）。</summary>
    private const float ShareTolerance = 0.6f;

    /// <summary>两个连接点相距小于该值时合并成同一个节点（米）。</summary>
    private const float LinkMergeRadius = 1.2f;

    /// <summary>几何体末端离所有连接点超过该距离时补一个端点（米）。</summary>
    private const float TipThreshold = 4f;

    /// <summary>几何体主轴长度小于该值时跳过（米）。</summary>
    private const float MinSegmentLength = 0.5f;

    /// <summary>一块道路几何体的顶点信息。</summary>
    private class ShapeInfo
    {
        public string name;
        public List<Vector3> points = new List<Vector3>();
        public Vector3 centroid;
    }

    [MenuItem("Tools/TD Road/从旧路导入（Road_OldBackup）", false, 30)]
    private static void ImportFromMenu()
    {
        var network = TDRoadEditorUtility.GetSelectedNetwork();
        if (network == null)
        {
            Debug.LogWarning("请先选中含 TDRoadNetwork 的 Road 对象。");
            return;
        }

        ImportInto(network);
        SceneView.RepaintAll();
    }

    /// <summary>
    /// 把旧路导入指定路网；会先清空已有节点（可用撤销恢复）。
    /// </summary>
    /// <param name="network">目标路网。</param>
    public static void ImportInto(TDRoadNetwork network)
    {
        if (network == null)
        {
            return;
        }

        var root = FindLegacyRoot();
        if (root == null)
        {
            Debug.LogWarning($"场景里找不到 {LegacyRootName}，无法导入旧路。");
            return;
        }

        var shapes = CollectShapes(root.transform, network.transform);
        if (shapes.Count == 0)
        {
            Debug.LogWarning($"{LegacyRootName} 下没有可用的道路几何体。");
            return;
        }

        Undo.RecordObject(network, "Import Legacy Road");
        network.Clear();

        // 1. 找出所有相邻关系：共享顶点的一对几何体在两个形状之间生成一个连接点。
        var links = new List<(int a, int b, Vector3 point)>();
        for (int i = 0; i < shapes.Count; i++)
        {
            for (int j = i + 1; j < shapes.Count; j++)
            {
                if (TryFindSharedPoint(shapes[i], shapes[j], out Vector3 shared))
                {
                    links.Add((i, j, shared));
                }
            }
        }

        // 2. 每个连接点对应路网里的一个节点；挨得很近的连接点（同一处汇合）合并成一个。
        var linkPoints = new List<Vector3>();
        var linkNodes = new List<int>();
        var ports = new List<int>[shapes.Count];
        for (int i = 0; i < shapes.Count; i++)
        {
            ports[i] = new List<int>();
        }

        for (int i = 0; i < links.Count; i++)
        {
            int nodeId = GetOrCreateLinkNode(network, links[i].point, linkPoints, linkNodes);
            ports[links[i].a].Add(nodeId);
            ports[links[i].b].Add(nodeId);
        }

        // 3. 每块几何体沿主轴串成折线：连接点按投影排序后依次相连，
        //    两端若还有明显伸出的部分，就补一个端点。
        for (int i = 0; i < shapes.Count; i++)
        {
            var shape = shapes[i];
            var waypoints = new List<(int nodeId, Vector3 position)>();

            for (int p = 0; p < ports[i].Count; p++)
            {
                int nodeId = ports[i][p];
                if (waypoints.Exists(w => w.nodeId == nodeId))
                {
                    continue;
                }

                waypoints.Add((nodeId, network.GetNode(nodeId).position));
            }

            if (!TryGetPrincipalAxis(shape, out Vector3 axisA, out Vector3 axisB))
            {
                ConnectSequentially(network, waypoints, shape.centroid, Vector3.right);
                continue;
            }

            if (DistanceToNearest(waypoints, axisA) > TipThreshold)
            {
                waypoints.Add((network.AddNode(axisA), axisA));
            }

            if (DistanceToNearest(waypoints, axisB) > TipThreshold)
            {
                waypoints.Add((network.AddNode(axisB), axisB));
            }

            Vector3 axis = axisB - axisA;
            axis.y = 0f;
            ConnectSequentially(network, waypoints, shape.centroid, axis);
        }

        TDRoadEditorUtility.Rebuild(network, false);
        Debug.Log($"已从 {LegacyRootName} 导入 {shapes.Count} 块道路，生成 {network.NodeCount} 个节点、{network.EdgeCount} 条边。");
    }

    /// <summary>
    /// 查找旧路的根对象。
    /// </summary>
    private static GameObject FindLegacyRoot()
    {
        var direct = GameObject.Find($"TD_Whitebox/{LegacyRootName}");
        if (direct != null)
        {
            return direct;
        }

        foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate.name == LegacyRootName && candidate.scene.IsValid())
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 收集根节点下所有带网格的道路几何体，顶点转换到路网本地坐标并去重。
    /// </summary>
    private static List<ShapeInfo> CollectShapes(Transform root, Transform target)
    {
        var shapes = new List<ShapeInfo>();

        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            var filter = child.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                continue;
            }

            Matrix4x4 toWorld = child.localToWorldMatrix;
            var vertices = filter.sharedMesh.vertices;
            var shape = new ShapeInfo { name = child.name };
            var seen = new HashSet<Vector2Int>();
            Vector3 sum = Vector3.zero;

            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 world = toWorld.MultiplyPoint3x4(vertices[v]);
                var key = new Vector2Int(
                    Mathf.RoundToInt(world.x * 100f),
                    Mathf.RoundToInt(world.z * 100f));

                if (!seen.Add(key))
                {
                    continue;
                }

                Vector3 local = target.InverseTransformPoint(world);
                local.y = 0f;
                shape.points.Add(local);
                sum += local;
            }

            if (shape.points.Count < 4)
            {
                continue;
            }

            shape.centroid = sum / shape.points.Count;
            shapes.Add(shape);
        }

        return shapes;
    }

    /// <summary>
    /// 判断两块几何体是否相邻，并返回共享边界的中点。
    /// </summary>
    private static bool TryFindSharedPoint(ShapeInfo lhs, ShapeInfo rhs, out Vector3 point)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;

        for (int i = 0; i < lhs.points.Count; i++)
        {
            for (int j = 0; j < rhs.points.Count; j++)
            {
                if (Vector3.Distance(lhs.points[i], rhs.points[j]) > ShareTolerance)
                {
                    continue;
                }

                sum += (lhs.points[i] + rhs.points[j]) * 0.5f;
                count++;
            }
        }

        if (count == 0)
        {
            point = default;
            return false;
        }

        point = sum / count;
        point.y = 0f;
        return true;
    }

    /// <summary>
    /// 取得（或复用）连接点对应的节点：靠得很近的连接点视为同一个路口。
    /// </summary>
    private static int GetOrCreateLinkNode(
        TDRoadNetwork network,
        Vector3 point,
        List<Vector3> points,
        List<int> nodes)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if (Vector3.Distance(points[i], point) <= LinkMergeRadius)
            {
                return nodes[i];
            }
        }

        int id = network.AddNode(point);
        points.Add(point);
        nodes.Add(id);
        return id;
    }

    /// <summary>
    /// 计算给定点到一组路点的最近距离；没有路点时返回无穷大。
    /// </summary>
    private static float DistanceToNearest(List<(int nodeId, Vector3 position)> waypoints, Vector3 point)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < waypoints.Count; i++)
        {
            best = Mathf.Min(best, Vector3.Distance(waypoints[i].position, point));
        }

        return best;
    }

    /// <summary>
    /// 把一组路点沿主轴排序后依次连成边。
    /// </summary>
    private static void ConnectSequentially(
        TDRoadNetwork network,
        List<(int nodeId, Vector3 position)> waypoints,
        Vector3 centroid,
        Vector3 axis)
    {
        if (waypoints.Count < 2)
        {
            return;
        }

        axis.y = 0f;
        if (axis.sqrMagnitude < 1e-6f)
        {
            axis = Vector3.right;
        }

        axis.Normalize();
        waypoints.Sort((lhs, rhs) =>
            Vector3.Dot(lhs.position - centroid, axis)
                .CompareTo(Vector3.Dot(rhs.position - centroid, axis)));

        for (int i = 1; i < waypoints.Count; i++)
        {
            network.AddEdge(waypoints[i - 1].nodeId, waypoints[i].nodeId);
        }
    }

    /// <summary>
    /// 用主成分分析求一块几何体的主轴两端（仅用于孤立几何体兜底）。
    /// </summary>
    private static bool TryGetPrincipalAxis(ShapeInfo shape, out Vector3 axisA, out Vector3 axisB)
    {
        axisA = default;
        axisB = default;

        if (shape.points.Count < 4)
        {
            return false;
        }

        Vector3 center = shape.centroid;
        float sxx = 0f;
        float szz = 0f;
        float sxz = 0f;

        for (int i = 0; i < shape.points.Count; i++)
        {
            float dx = shape.points[i].x - center.x;
            float dz = shape.points[i].z - center.z;
            sxx += dx * dx;
            szz += dz * dz;
            sxz += dx * dz;
        }

        float angle = 0.5f * Mathf.Atan2(2f * sxz, sxx - szz);
        var axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        var order = new List<int>(shape.points.Count);
        var projections = new float[shape.points.Count];
        for (int i = 0; i < shape.points.Count; i++)
        {
            projections[i] = (shape.points[i].x - center.x) * axis.x
                             + (shape.points[i].z - center.z) * axis.y;
            order.Add(i);
        }

        order.Sort((lhs, rhs) => projections[lhs].CompareTo(projections[rhs]));

        float length = projections[order[shape.points.Count - 1]] - projections[order[0]];
        if (length < MinSegmentLength)
        {
            return false;
        }

        axisA = (shape.points[order[0]] + shape.points[order[1]]) * 0.5f;
        axisB = (shape.points[order[shape.points.Count - 1]] + shape.points[order[shape.points.Count - 2]]) * 0.5f;
        axisA.y = 0f;
        axisB.y = 0f;
        return true;
    }
}
