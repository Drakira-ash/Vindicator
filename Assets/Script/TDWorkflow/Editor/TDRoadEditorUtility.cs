using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 道路编辑器的共享设置与菜单入口。
/// 吸附参数保存在 EditorPrefs 里，不污染场景数据。
/// </summary>
public static class TDRoadEditorUtility
{
    private const string GeneratedRoot = "Assets/TDWorkflow/Generated";
    private const string PrefPrefix = "TDWorkflow.Road.";

    /// <summary>是否启用格点吸附。</summary>
    public static bool SnapGrid
    {
        get => EditorPrefs.GetBool(PrefPrefix + "SnapGrid", true);
        set => EditorPrefs.SetBool(PrefPrefix + "SnapGrid", value);
    }

    /// <summary>格点大小（米）。</summary>
    public static float GridSize
    {
        get => EditorPrefs.GetFloat(PrefPrefix + "GridSize", 0.5f);
        set => EditorPrefs.SetFloat(PrefPrefix + "GridSize", Mathf.Max(0.05f, value));
    }

    /// <summary>是否启用已有节点吸附。</summary>
    public static bool SnapNodes
    {
        get => EditorPrefs.GetBool(PrefPrefix + "SnapNodes", true);
        set => EditorPrefs.SetBool(PrefPrefix + "SnapNodes", value);
    }

    /// <summary>节点吸附半径（米）。</summary>
    public static float NodeSnapRadius
    {
        get => EditorPrefs.GetFloat(PrefPrefix + "NodeSnapRadius", 2f);
        set => EditorPrefs.SetFloat(PrefPrefix + "NodeSnapRadius", Mathf.Max(0.1f, value));
    }

    /// <summary>是否启用角度吸附。</summary>
    public static bool SnapAngle
    {
        get => EditorPrefs.GetBool(PrefPrefix + "SnapAngle", true);
        set => EditorPrefs.SetBool(PrefPrefix + "SnapAngle", value);
    }

    /// <summary>角度吸附步长（度）。</summary>
    public static float AngleStep
    {
        get => EditorPrefs.GetFloat(PrefPrefix + "AngleStep", 15f);
        set => EditorPrefs.SetFloat(PrefPrefix + "AngleStep", Mathf.Clamp(value, 1f, 90f));
    }

    /// <summary>是否把画好的路网导出成网格资源。</summary>
    public static bool SaveMeshAsset
    {
        get => EditorPrefs.GetBool(PrefPrefix + "SaveMeshAsset", true);
        set => EditorPrefs.SetBool(PrefPrefix + "SaveMeshAsset", value);
    }

    /// <summary>
    /// 重建道路网格，并（可选）把网格保存成资源，保证重新打开场景后仍然可见。
    /// </summary>
    /// <param name="network">目标路网。</param>
    /// <param name="recordUndo">是否记录撤销。</param>
    public static void Rebuild(TDRoadNetwork network, bool recordUndo = true)
    {
        if (network == null)
        {
            return;
        }

        if (recordUndo)
        {
            Undo.RegisterCompleteObjectUndo(network, "Rebuild Road");
        }

        EnsureMeshObject(network, recordUndo);
        network.Rebuild();
        PersistMesh(network);

        EditorUtility.SetDirty(network);
        MarkSceneDirty(network);
    }

    /// <summary>
    /// 确保 Road 下存在承载网格的子对象，并让撤销系统记录创建操作。
    /// </summary>
    private static void EnsureMeshObject(TDRoadNetwork network, bool recordUndo)
    {
        if (network.transform.Find(TDRoadNetwork.MeshChildName) != null)
        {
            return;
        }

        network.GetOrCreateMeshObject(out _, out _, out _);
        var child = network.transform.Find(TDRoadNetwork.MeshChildName);
        if (recordUndo && child != null)
        {
            Undo.RegisterCreatedObjectUndo(child.gameObject, "Create Road Mesh Object");
        }
    }

    /// <summary>
    /// 把网格保存成独立资源。场景无法序列化运行时创建的 Mesh，不存盘重新打开场景后路面会消失。
    /// </summary>
    private static void PersistMesh(TDRoadNetwork network)
    {
        network.GetOrCreateMeshObject(out var filter, out _, out _);
        var mesh = filter.sharedMesh;
        if (mesh == null)
        {
            return;
        }

        if (!SaveMeshAsset)
        {
            return;
        }

        if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh)))
        {
            EditorUtility.SetDirty(mesh);
            return;
        }

        EnsureDirectory(GeneratedRoot);
        string sceneName = network.gameObject.scene.IsValid() && !string.IsNullOrEmpty(network.gameObject.scene.name)
            ? network.gameObject.scene.name
            : "NoScene";
        string path = $"{GeneratedRoot}/{sceneName}_{network.name}.asset";

        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            filter.sharedMesh = existing;
            return;
        }

        mesh.name = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(mesh, path);
    }

    /// <summary>
    /// 标记路网所属场景为已修改，让保存提示能正常出现。
    /// </summary>
    /// <param name="network">目标路网。</param>
    public static void MarkSceneDirty(TDRoadNetwork network)
    {
        if (network == null)
        {
            return;
        }

        var scene = network.gameObject.scene;
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    /// <summary>
    /// 递归创建资源目录。
    /// </summary>
    /// <param name="assetPath">Unity 资源路径。</param>
    private static void EnsureDirectory(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
        {
            return;
        }

        string parent = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        string folder = Path.GetFileName(assetPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folder))
        {
            return;
        }

        EnsureDirectory(parent);
        AssetDatabase.CreateFolder(parent, folder);
    }

    /// <summary>
    /// 取当前选中的路网对象。
    /// </summary>
    /// <returns>选中的路网；未选中时返回 null。</returns>
    public static TDRoadNetwork GetSelectedNetwork()
    {
        var selected = Selection.activeGameObject;
        return selected != null ? selected.GetComponentInParent<TDRoadNetwork>() : null;
    }

    [MenuItem("Tools/TD Road/道路编辑器窗口", false, 0)]
    private static void OpenWindow()
    {
        TDRoadEditorWindow.ShowWindow();
    }

    [MenuItem("Tools/TD Road/重建选中道路", false, 20)]
    private static void RebuildSelected()
    {
        var network = GetSelectedNetwork();
        if (network == null)
        {
            Debug.LogWarning("请先选中含 TDRoadNetwork 的 Road 对象。");
            return;
        }

        Rebuild(network);
        SceneView.RepaintAll();
    }

    [MenuItem("Tools/TD Road/清空选中路网", false, 21)]
    private static void ClearSelected()
    {
        var network = GetSelectedNetwork();
        if (network == null)
        {
            Debug.LogWarning("请先选中含 TDRoadNetwork 的 Road 对象。");
            return;
        }

        Undo.RecordObject(network, "Clear Road Network");
        network.Clear();
        Rebuild(network, false);
        SceneView.RepaintAll();
    }
}
