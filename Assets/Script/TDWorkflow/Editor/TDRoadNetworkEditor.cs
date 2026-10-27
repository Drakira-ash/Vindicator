using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// 在场景视图里直接编辑路网：点地面放路点、拖拽移动节点、从节点起岔道。
/// 选中含 <see cref="TDRoadNetwork"/> 的 Road 对象时生效。
/// </summary>
[CustomEditor(typeof(TDRoadNetwork))]
public class TDRoadNetworkEditor : Editor
{
    /// <summary>拾取节点时的屏幕像素容差。</summary>
    private const float NodePickPixels = 14f;

    /// <summary>拾取边时的屏幕像素容差。</summary>
    private const float EdgePickPixels = 10f;

    /// <summary>区分“点击”和“拖拽”的像素阈值。</summary>
    private const float DragThresholdPixels = 4f;

    /// <summary>当前链条的末尾节点 id；-1 表示没有进行中的链条。</summary>
    private int m_chainNodeId = -1;

    /// <summary>按下鼠标时命中的节点，用于区分点击与拖拽。</summary>
    private int m_pendingDragNodeId = -1;

    private Vector2 m_dragStartMouse;
    private bool m_dragging;
    private bool m_dragUndoRecorded;

    private void OnDisable()
    {
        EndChain();
        m_pendingDragNodeId = -1;
        m_dragging = false;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "场景视图操作：\n" +
            "• 左键点地面 —— 继续画当前这条路\n" +
            "• 左键点已有节点 —— 连接过去并结束当前这条路\n" +
            "• 没有进行中的路时点节点 —— 从该点起一条新路（岔道）\n" +
            "• 拖拽节点 —— 移动（受吸附影响）\n" +
            "• Ctrl + 左键点节点 —— 删除节点\n" +
            "• Ctrl + 左键点路面 —— 删除这条边\n" +
            "• Esc / 右键 —— 结束当前这条路\n\n" +
            "吸附和尺寸参数在 Tools ▸ TD Road ▸ 道路编辑器窗口。",
            MessageType.Info);
    }

    private void OnSceneGUI()
    {
        var network = target as TDRoadNetwork;
        if (network == null || !network.isActiveAndEnabled)
        {
            return;
        }

        Event e = Event.current;
        if (e == null)
        {
            return;
        }

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        bool hasPoint = TryGetGroundPoint(e.mousePosition, out Vector3 worldPoint);
        Vector3 localRaw = network.transform.InverseTransformPoint(worldPoint);
        int hoverNodeId = FindNodeUnderMouse(network, e.mousePosition);

        int excludeNodeId = m_pendingDragNodeId >= 0 ? m_pendingDragNodeId : -1;
        int anchorNodeId = m_pendingDragNodeId >= 0 ? -1 : m_chainNodeId;
        Vector3 snapped = ApplySnap(network, localRaw, anchorNodeId, excludeNodeId, out int snappedNodeId);

        HandleInput(network, e, hasPoint, snapped, snappedNodeId, hoverNodeId);
        Draw(network, hasPoint, snapped, hoverNodeId, snappedNodeId);

        if (e.type == EventType.MouseMove)
        {
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// 处理场景视图里的鼠标与键盘输入。
    /// </summary>
    private void HandleInput(
        TDRoadNetwork network,
        Event e,
        bool hasPoint,
        Vector3 snapped,
        int snappedNodeId,
        int hoverNodeId)
    {
        switch (e.type)
        {
            case EventType.KeyDown:
                if (e.keyCode == KeyCode.Escape && m_chainNodeId >= 0)
                {
                    EndChain();
                    e.Use();
                    SceneView.RepaintAll();
                }

                break;

            case EventType.MouseDown:
                if (e.button == 1 && m_chainNodeId >= 0)
                {
                    EndChain();
                    e.Use();
                    SceneView.RepaintAll();
                    break;
                }

                if (e.button != 0 || e.alt)
                {
                    break;
                }

                if (hoverNodeId >= 0)
                {
                    if (e.control || e.command)
                    {
                        DeleteNode(network, hoverNodeId);
                        e.Use();
                        break;
                    }

                    m_pendingDragNodeId = hoverNodeId;
                    m_dragStartMouse = e.mousePosition;
                    m_dragging = false;
                    m_dragUndoRecorded = false;
                    e.Use();
                    break;
                }

                int edgeIndex = FindEdgeUnderMouse(network, e.mousePosition);
                if ((e.control || e.command) && edgeIndex >= 0)
                {
                    DeleteEdge(network, edgeIndex);
                    e.Use();
                    break;
                }

                if (hasPoint)
                {
                    CommitPoint(network, snapped, snappedNodeId);
                    e.Use();
                }

                break;

            case EventType.MouseDrag:
                if (m_pendingDragNodeId < 0 || e.button != 0)
                {
                    break;
                }

                if (!m_dragging && Vector2.Distance(m_dragStartMouse, e.mousePosition) > DragThresholdPixels)
                {
                    m_dragging = true;
                }

                if (!m_dragging)
                {
                    break;
                }

                if (!m_dragUndoRecorded)
                {
                    Undo.RecordObject(network, "Move Road Node");
                    m_dragUndoRecorded = true;
                }

                var dragged = network.GetNode(m_pendingDragNodeId);
                if (dragged != null)
                {
                    dragged.position = new Vector3(snapped.x, 0f, snapped.z);
                    network.Rebuild();
                    EditorUtility.SetDirty(network);
                    TDRoadEditorUtility.MarkSceneDirty(network);
                }

                SceneView.RepaintAll();
                e.Use();
                break;

            case EventType.MouseUp:
                if (m_pendingDragNodeId < 0 || e.button != 0)
                {
                    break;
                }

                int clickedNode = m_pendingDragNodeId;
                bool wasDragging = m_dragging;
                m_pendingDragNodeId = -1;
                m_dragging = false;

                if (wasDragging)
                {
                    TDRoadEditorUtility.Rebuild(network);
                }
                else
                {
                    HandleNodeClick(network, clickedNode);
                }

                e.Use();
                SceneView.RepaintAll();
                break;
        }
    }

    /// <summary>
    /// 在链条上追加一个点，或开始一条新链条。
    /// </summary>
    private void CommitPoint(TDRoadNetwork network, Vector3 localPoint, int snappedNodeId)
    {
        Undo.RecordObject(network, "Edit Road");

        if (m_chainNodeId < 0)
        {
            int id = snappedNodeId >= 0 ? snappedNodeId : network.AddNode(localPoint);
            m_chainNodeId = id;
        }
        else
        {
            int targetId = snappedNodeId >= 0 ? snappedNodeId : network.AddNode(localPoint);
            if (targetId != m_chainNodeId)
            {
                network.AddEdge(m_chainNodeId, targetId);
            }

            // 吸附到已有节点说明这条链条接上了别的路，到此结束；否则继续延伸。
            m_chainNodeId = snappedNodeId >= 0 ? -1 : targetId;
        }

        TDRoadEditorUtility.Rebuild(network, false);
        SceneView.RepaintAll();
    }

    /// <summary>
    /// 点击节点（没有拖动）时的行为：起链条、连接，或结束。
    /// </summary>
    private void HandleNodeClick(TDRoadNetwork network, int nodeId)
    {
        if (m_chainNodeId < 0)
        {
            m_chainNodeId = nodeId;
            return;
        }

        if (m_chainNodeId == nodeId)
        {
            EndChain();
            return;
        }

        Undo.RecordObject(network, "Connect Road");
        network.AddEdge(m_chainNodeId, nodeId);
        TDRoadEditorUtility.Rebuild(network, false);
        EndChain();
    }

    private void DeleteNode(TDRoadNetwork network, int nodeId)
    {
        Undo.RecordObject(network, "Delete Road Node");
        network.RemoveNode(nodeId);
        if (m_chainNodeId == nodeId)
        {
            EndChain();
        }

        TDRoadEditorUtility.Rebuild(network, false);
        SceneView.RepaintAll();
    }

    private void DeleteEdge(TDRoadNetwork network, int edgeIndex)
    {
        if (edgeIndex < 0 || edgeIndex >= network.Edges.Count)
        {
            return;
        }

        var edge = network.Edges[edgeIndex];
        Undo.RecordObject(network, "Delete Road Edge");
        network.RemoveEdge(edge.a, edge.b);
        TDRoadEditorUtility.Rebuild(network, false);
        SceneView.RepaintAll();
    }

    private void EndChain()
    {
        m_chainNodeId = -1;
    }

    /// <summary>
    /// 把原始鼠标位置按「节点 → 角度 → 格点」的优先级逐级吸附。
    /// </summary>
    private static Vector3 ApplySnap(
        TDRoadNetwork network,
        Vector3 localRaw,
        int anchorNodeId,
        int excludeNodeId,
        out int snappedNodeId)
    {
        snappedNodeId = -1;
        Vector3 result = new Vector3(localRaw.x, 0f, localRaw.z);

        if (TDRoadEditorUtility.SnapNodes)
        {
            var nearest = FindNearestNode(network, result, TDRoadEditorUtility.NodeSnapRadius, excludeNodeId);
            if (nearest != null)
            {
                snappedNodeId = nearest.id;
                return new Vector3(nearest.position.x, 0f, nearest.position.z);
            }
        }

        if (TDRoadEditorUtility.SnapAngle && anchorNodeId >= 0)
        {
            var anchor = network.GetNode(anchorNodeId);
            if (anchor != null)
            {
                Vector3 delta = result - anchor.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > 1e-4f)
                {
                    float step = Mathf.Max(1f, TDRoadEditorUtility.AngleStep) * Mathf.Deg2Rad;
                    float angle = Mathf.Atan2(delta.z, delta.x);
                    angle = Mathf.Round(angle / step) * step;

                    float length = delta.magnitude;
                    if (TDRoadEditorUtility.SnapGrid)
                    {
                        float grid = Mathf.Max(0.05f, TDRoadEditorUtility.GridSize);
                        length = Mathf.Max(grid, Mathf.Round(length / grid) * grid);
                    }

                    result = anchor.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * length;
                    result.y = 0f;
                    return result;
                }
            }
        }

        if (TDRoadEditorUtility.SnapGrid)
        {
            float grid = Mathf.Max(0.05f, TDRoadEditorUtility.GridSize);
            result.x = Mathf.Round(result.x / grid) * grid;
            result.z = Mathf.Round(result.z / grid) * grid;
        }

        return result;
    }

    /// <summary>
    /// 查找半径内最近的节点，可排除指定节点（拖拽时排除自己）。
    /// </summary>
    private static TDRoadNetwork.Node FindNearestNode(
        TDRoadNetwork network,
        Vector3 localPosition,
        float maxDistance,
        int excludeNodeId)
    {
        TDRoadNetwork.Node best = null;
        float bestSqr = maxDistance * maxDistance;

        for (int i = 0; i < network.Nodes.Count; i++)
        {
            var node = network.Nodes[i];
            if (node.id == excludeNodeId)
            {
                continue;
            }

            float sqr = (node.position - localPosition).sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                best = node;
            }
        }

        return best;
    }

    /// <summary>
    /// 屏幕空间拾取节点。
    /// </summary>
    private static int FindNodeUnderMouse(TDRoadNetwork network, Vector2 guiPoint)
    {
        int best = -1;
        float bestDistance = NodePickPixels;

        for (int i = 0; i < network.Nodes.Count; i++)
        {
            var node = network.Nodes[i];
            Vector3 world = network.transform.TransformPoint(node.position);
            Vector2 screen = HandleUtility.WorldToGUIPoint(world);
            float distance = Vector2.Distance(screen, guiPoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = node.id;
            }
        }

        return best;
    }

    /// <summary>
    /// 屏幕空间拾取边，返回边的索引。
    /// </summary>
    private static int FindEdgeUnderMouse(TDRoadNetwork network, Vector2 guiPoint)
    {
        int best = -1;
        float bestDistance = EdgePickPixels;

        for (int i = 0; i < network.Edges.Count; i++)
        {
            var edge = network.Edges[i];
            var a = network.GetNode(edge.a);
            var b = network.GetNode(edge.b);
            if (a == null || b == null)
            {
                continue;
            }

            Vector2 screenA = HandleUtility.WorldToGUIPoint(network.transform.TransformPoint(a.position));
            Vector2 screenB = HandleUtility.WorldToGUIPoint(network.transform.TransformPoint(b.position));
            float distance = DistanceToSegment(guiPoint, screenA, screenB);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// 点到线段的屏幕距离。
    /// </summary>
    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSqr = ab.sqrMagnitude;
        if (lengthSqr < 1e-4f)
        {
            return Vector2.Distance(point, a);
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSqr);
        return Vector2.Distance(point, a + ab * t);
    }

    /// <summary>
    /// 把屏幕坐标换算到地面上的世界坐标。
    /// </summary>
    private static bool TryGetGroundPoint(Vector2 guiPoint, out Vector3 world)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float distance))
        {
            world = ray.GetPoint(distance);
            return true;
        }

        world = Vector3.zero;
        return false;
    }

    /// <summary>
    /// 绘制路网、预览和节点手柄。
    /// </summary>
    private void Draw(
        TDRoadNetwork network,
        bool hasPoint,
        Vector3 snapped,
        int hoverNodeId,
        int snappedNodeId)
    {
        for (int i = 0; i < network.Edges.Count; i++)
        {
            DrawEdge(network, network.Edges[i]);
        }

        if (m_chainNodeId >= 0 && hasPoint)
        {
            var anchor = network.GetNode(m_chainNodeId);
            if (anchor != null)
            {
                Handles.color = new Color(0.2f, 0.9f, 1f, 0.95f);
                Handles.DrawAAPolyLine(
                    4f,
                    network.transform.TransformPoint(anchor.position),
                    network.transform.TransformPoint(snapped));
            }
        }

        for (int i = 0; i < network.Nodes.Count; i++)
        {
            DrawNode(network, network.Nodes[i], hoverNodeId, snappedNodeId);
        }
    }

    /// <summary>
    /// 画一条边的路宽轮廓与中心线。
    /// </summary>
    private static void DrawEdge(TDRoadNetwork network, TDRoadNetwork.Edge edge)
    {
        var spline = TDRoadMeshBuilder.BuildEdgeSpline(network, edge);
        if (spline.Count < 2)
        {
            return;
        }

        const int steps = 20;
        float half = network.GetEdgeWidth(edge) * 0.5f;
        var left = new Vector3[steps + 1];
        var right = new Vector3[steps + 1];
        var center = new Vector3[steps + 1];

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 point = spline.EvaluatePosition(t);
            Vector3 tangent = ((Vector3)spline.EvaluateTangent(t)).normalized;
            Vector3 lateral = Vector3.Cross(Vector3.up, tangent).normalized;
            point.y = network.RoadHeight + 0.01f;

            center[i] = network.transform.TransformPoint(point);
            left[i] = network.transform.TransformPoint(point - lateral * half);
            right[i] = network.transform.TransformPoint(point + lateral * half);
        }

        Handles.color = new Color(0.55f, 0.55f, 0.55f, 0.85f);
        Handles.DrawAAPolyLine(2f, left);
        Handles.DrawAAPolyLine(2f, right);
        Handles.color = new Color(0.85f, 0.85f, 0.85f, 0.45f);
        Handles.DrawAAPolyLine(1f, center);
    }

    /// <summary>
    /// 画一个节点手柄，颜色表示它在路网里的角色。
    /// </summary>
    private static void DrawNode(
        TDRoadNetwork network,
        TDRoadNetwork.Node node,
        int hoverNodeId,
        int snappedNodeId)
    {
        Vector3 world = network.transform.TransformPoint(node.position);
        world.y = network.RoadHeight + 0.02f;

        int degree = network.GetDegree(node.id);
        bool isHover = node.id == hoverNodeId;
        bool isSnap = node.id == snappedNodeId;

        Color color = degree >= 3
            ? new Color(1f, 0.6f, 0.1f)
            : degree == 2
                ? new Color(0.9f, 0.9f, 0.9f)
                : new Color(0.25f, 0.9f, 0.4f);

        if (isSnap)
        {
            color = new Color(0.2f, 1f, 1f);
        }
        else if (isHover)
        {
            color = Color.Lerp(color, Color.white, 0.5f);
        }

        float radius = isHover || isSnap ? 0.45f : 0.32f;
        Handles.color = new Color(color.r, color.g, color.b, 0.95f);
        Handles.DrawSolidDisc(world, Vector3.up, radius);
        Handles.color = new Color(0f, 0f, 0f, 0.6f);
        Handles.DrawWireDisc(world, Vector3.up, radius);
    }
}
