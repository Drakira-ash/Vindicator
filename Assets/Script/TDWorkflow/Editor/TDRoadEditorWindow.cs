using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 道路编辑器的参数面板：吸附设置、生成参数和批量操作。
/// </summary>
public class TDRoadEditorWindow : EditorWindow
{
    private Vector2 m_scroll;
    private TDRoadNetwork m_network;
    private SerializedObject m_serialized;
    private readonly List<int> m_endpoints = new List<int>();

    /// <summary>
    /// 打开（或聚焦）道路编辑器窗口。
    /// </summary>
    public static void ShowWindow()
    {
        var window = GetWindow<TDRoadEditorWindow>("TD 道路编辑器");
        window.minSize = new Vector2(320f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        RefreshTarget();
        SceneView.duringSceneGui += OnSceneGuiHint;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGuiHint;
    }

    private void OnSelectionChange()
    {
        RefreshTarget();
        Repaint();
    }

    /// <summary>
    /// 同步当前选中的路网；选择变化时重建 SerializedObject。
    /// </summary>
    private void RefreshTarget()
    {
        var selected = TDRoadEditorUtility.GetSelectedNetwork();
        if (selected == m_network)
        {
            return;
        }

        m_network = selected;
        m_serialized = m_network != null ? new SerializedObject(m_network) : null;
    }

    /// <summary>
    /// 场景视图里的简短提示，避免用户忘了快捷键。
    /// </summary>
    private void OnSceneGuiHint(SceneView sceneView)
    {
        if (m_network == null || Selection.activeGameObject == null)
        {
            return;
        }

        if (Selection.activeGameObject.GetComponentInParent<TDRoadNetwork>() != m_network)
        {
            return;
        }

        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(10f, 10f, 320f, 80f), EditorStyles.helpBox);
        GUILayout.Label("TD 道路编辑", EditorStyles.boldLabel);
        GUILayout.Label("左键点地面画路 · 点节点连接/起岔道", EditorStyles.miniLabel);
        GUILayout.Label("拖拽移动 · Ctrl+左键删除 · Esc 结束", EditorStyles.miniLabel);
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    private void OnGUI()
    {
        RefreshTarget();

        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);

        DrawTargetHeader();
        if (m_network != null)
        {
            EditorGUILayout.Space();
            DrawSnapSettings();
            EditorGUILayout.Space();
            DrawNetworkSettings();
            EditorGUILayout.Space();
            DrawActions();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawTargetHeader()
    {
        EditorGUILayout.LabelField("目标路网", EditorStyles.boldLabel);

        if (m_network == null)
        {
            EditorGUILayout.HelpBox("在场景中选中 TD_Whitebox/Road 对象（或任意子对象）。", MessageType.Warning);
            if (GUILayout.Button("创建/选中 Road 对象"))
            {
                m_network = CreateOrFindRoad();
                m_serialized = m_network != null ? new SerializedObject(m_network) : null;
                Selection.activeGameObject = m_network != null ? m_network.gameObject : null;
            }

            return;
        }

        EditorGUILayout.ObjectField("对象", m_network, typeof(TDRoadNetwork), true);

        m_endpoints.Clear();
        m_network.GetEndpointNodeIds(m_endpoints);

        int junctions = 0;
        for (int i = 0; i < m_network.Nodes.Count; i++)
        {
            if (m_network.IsJunction(m_network.Nodes[i].id))
            {
                junctions++;
            }
        }

        EditorGUILayout.LabelField(
            $"节点 {m_network.NodeCount} · 边 {m_network.EdgeCount} · 端点 {m_endpoints.Count} · 路口 {junctions}",
            EditorStyles.miniLabel);
    }

    private void DrawSnapSettings()
    {
        EditorGUILayout.LabelField("吸附", EditorStyles.boldLabel);

        TDRoadEditorUtility.SnapGrid = EditorGUILayout.Toggle("格点吸附", TDRoadEditorUtility.SnapGrid);
        using (new EditorGUI.DisabledScope(!TDRoadEditorUtility.SnapGrid))
        {
            TDRoadEditorUtility.GridSize = EditorGUILayout.FloatField("格点大小 (m)", TDRoadEditorUtility.GridSize);
        }

        TDRoadEditorUtility.SnapNodes = EditorGUILayout.Toggle("节点吸附", TDRoadEditorUtility.SnapNodes);
        using (new EditorGUI.DisabledScope(!TDRoadEditorUtility.SnapNodes))
        {
            TDRoadEditorUtility.NodeSnapRadius = EditorGUILayout.FloatField("吸附半径 (m)", TDRoadEditorUtility.NodeSnapRadius);
        }

        TDRoadEditorUtility.SnapAngle = EditorGUILayout.Toggle("角度吸附", TDRoadEditorUtility.SnapAngle);
        using (new EditorGUI.DisabledScope(!TDRoadEditorUtility.SnapAngle))
        {
            TDRoadEditorUtility.AngleStep = EditorGUILayout.Slider("角度步长 (°)", TDRoadEditorUtility.AngleStep, 1f, 90f);
        }
    }

    private void DrawNetworkSettings()
    {
        EditorGUILayout.LabelField("生成参数", EditorStyles.boldLabel);

        if (m_serialized == null || m_serialized.targetObject != m_network)
        {
            m_serialized = new SerializedObject(m_network);
        }

        m_serialized.Update();

        // 只画生成参数；节点和边是编辑器的产物，不在这里铺开。
        string[] fields =
        {
            "defaultWidth",
            "cornerSmoothness",
            "samplesPerMeter",
            "roadHeight",
            "roadMaterial",
            "autoRebuild"
        };

        for (int i = 0; i < fields.Length; i++)
        {
            var property = m_serialized.FindProperty(fields[i]);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, true);
            }
        }

        bool changed = m_serialized.ApplyModifiedProperties();

        if (changed && m_network != null)
        {
            TDRoadEditorUtility.Rebuild(m_network);
        }
    }

    private void DrawActions()
    {
        EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);

        if (GUILayout.Button("重建道路网格"))
        {
            TDRoadEditorUtility.Rebuild(m_network);
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("从旧路导入（Road_OldBackup）"))
        {
            TDRoadLegacyImporter.ImportInto(m_network);
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
        if (GUILayout.Button("清空路网（可撤销）"))
        {
            Undo.RecordObject(m_network, "Clear Road Network");
            m_network.Clear();
            TDRoadEditorUtility.Rebuild(m_network, false);
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = Color.white;

        TDRoadEditorUtility.SaveMeshAsset = EditorGUILayout.Toggle("保存网格资源", TDRoadEditorUtility.SaveMeshAsset);
        EditorGUILayout.HelpBox(
            "关闭「保存网格资源」后，重新打开场景路面会消失，仅在临时试验时使用。",
            MessageType.None);
    }

    /// <summary>
    /// 找到场景里已有的 Road 路网，没有就新建一个。
    /// </summary>
    private static TDRoadNetwork CreateOrFindRoad()
    {
        var existing = Object.FindFirstObjectByType<TDRoadNetwork>();
        if (existing != null)
        {
            return existing;
        }

        var parent = GameObject.Find("TD_Whitebox");
        var go = new GameObject("Road");
        Undo.RegisterCreatedObjectUndo(go, "Create Road");
        go.transform.SetParent(parent != null ? parent.transform : null, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/TDWorkflow/Generated/Road_Mat.mat");
        var network = Undo.AddComponent<TDRoadNetwork>(go);

        if (material != null)
        {
            var serialized = new SerializedObject(network);
            var materialProperty = serialized.FindProperty("roadMaterial");
            if (materialProperty != null)
            {
                materialProperty.objectReferenceValue = material;
                serialized.ApplyModifiedProperties();
            }
        }

        return network;
    }
}
