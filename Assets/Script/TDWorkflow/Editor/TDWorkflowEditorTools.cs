using System;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using UnityEngine.ProBuilder;
using System.Reflection;

public static class TDWorkflowEditorTools
{
    private const string ScenePath = "Assets/Scenes/TD_Workflow_Test.unity";
    private const string GeneratedRoot = "Assets/TDWorkflow/Generated";

    /// <summary>
    /// 创建或重建白盒测试场景结构，并初始化道路、ProBuilder 几何和参照物。
    /// </summary>
    [MenuItem("Tools/TD Workflow/Create TD Workflow Test Scene")]
    public static void CreateTDWorkflowTestScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        EnsureDirectory("Assets/TDWorkflow");
        EnsureDirectory(GeneratedRoot);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "TD_Workflow_Test";

        var roadMaterial = GetOrCreateMaterial("Road_Mat", new Color(0.18f, 0.18f, 0.18f));
        var cliffMaterial = GetOrCreateMaterial("Cliff_Mat", new Color(0.50f, 0.41f, 0.34f));
        var platformMaterial = GetOrCreateMaterial("Platform_Mat", new Color(0.62f, 0.62f, 0.62f));
        var landmarkMaterial = GetOrCreateMaterial("Landmark_Mat", new Color(0.54f, 0.28f, 0.16f));
        var referenceMaterial = GetOrCreateMaterial("Reference_Mat", new Color(0.20f, 0.52f, 0.86f));

        var rootRoute = CreateRoot("Route");
        var rootEditable = CreateRoot("EditableGeometry");
        var rootLandmarks = CreateRoot("Landmarks");
        var rootReferences = CreateRoot("References");

        var splineSourceGO = new GameObject("SplineSource");
        Undo.RegisterCreatedObjectUndo(splineSourceGO, "Create SplineSource");
        splineSourceGO.transform.SetParent(rootRoute.transform, false);
        var splineContainer = splineSourceGO.AddComponent<SplineContainer>();
        splineContainer.Spline = BuildDefaultRoadSpline();

        var roadVisualGO = new GameObject("RoadVisual");
        Undo.RegisterCreatedObjectUndo(roadVisualGO, "Create RoadVisual");
        roadVisualGO.transform.SetParent(rootRoute.transform, false);
        var roadFilter = roadVisualGO.AddComponent<MeshFilter>();
        var roadRenderer = roadVisualGO.AddComponent<MeshRenderer>();
        var roadCollider = roadVisualGO.AddComponent<MeshCollider>();
        roadRenderer.sharedMaterial = roadMaterial;

        var roadAuthoring = AddRoadAuthoringComponent(rootRoute);
        if (roadAuthoring == null)
        {
            Debug.LogError("未找到 TDRoadSplineAuthoring 类型，已中止场景创建。请等待脚本编译完成后重试。", rootRoute);
            return;
        }

        Undo.RegisterCompleteObjectUndo(roadAuthoring, "Configure Road Authoring");
        InvokeRoadAuthoringMethod(roadAuthoring, "Configure", splineContainer, roadFilter, roadRenderer, roadCollider, 3f);
        InvokeRoadAuthoringMethod(roadAuthoring, "RebuildRoadMesh");

        var cliff = CreateProBuilderCube("Cliff", new Vector3(8f, 4f, 4f), new Vector3(-8f, 4f, 8f), cliffMaterial);
        cliff.transform.SetParent(rootEditable.transform, true);

        var platform = CreateProBuilderCube("Platform", new Vector3(10f, 2f, 10f), new Vector3(8f, 1f, 4f), platformMaterial);
        platform.transform.SetParent(rootEditable.transform, true);

        var tavern = CreatePrimitiveLandmark("TavernPlaceholder", PrimitiveType.Cube, new Vector3(-2f, 2f, -6f), new Vector3(6f, 4f, 6f), landmarkMaterial);
        tavern.transform.SetParent(rootLandmarks.transform, true);

        var characterRef = CreatePrimitiveLandmark("CharacterReference", PrimitiveType.Capsule, new Vector3(12f, 0.9f, -4f), new Vector3(0.6f, 0.9f, 0.6f), referenceMaterial);
        characterRef.transform.SetParent(rootReferences.transform, true);

        var tower = CreatePrimitiveLandmark("TowerFootprint", PrimitiveType.Cube, new Vector3(15f, 0.05f, -4f), new Vector3(2f, 0.1f, 2f), referenceMaterial);
        tower.transform.SetParent(rootReferences.transform, true);

        var cameraGo = new GameObject("Main Camera");
        Undo.RegisterCreatedObjectUndo(cameraGo, "Create Main Camera");
        var camera = cameraGo.AddComponent<Camera>();
        camera.tag = "MainCamera";
        ConfigureTopDownCamera(camera, rootRoute, rootEditable, rootLandmarks, rootReferences);

        var lightGo = new GameObject("Directional Light");
        Undo.RegisterCreatedObjectUndo(lightGo, "Create Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Selection.activeGameObject = rootRoute;
        Debug.Log("TD_Workflow_Test 场景已创建。后续调整道路请选中 Route 并执行 Tools/TD Workflow/Update Selected Road。", rootRoute);
    }

    /// <summary>
    /// 对当前选中的 Route 对象执行道路网格重建，仅更新 RoadVisual 自有网格。
    /// </summary>
    [MenuItem("Tools/TD Workflow/Update Selected Road")]
    public static void UpdateSelectedRoad()
    {
        var selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogWarning("请先选中含 TDRoadSplineAuthoring 的 Route 对象。");
            return;
        }

        var authoring = GetRoadAuthoringComponent(selected);
        if (authoring == null)
        {
            authoring = GetRoadAuthoringComponent(selected.transform.parent != null ? selected.transform.parent.gameObject : null);
        }

        if (authoring == null)
        {
            Debug.LogWarning("当前选择中未找到 TDRoadSplineAuthoring。", selected);
            return;
        }

        Undo.RegisterCompleteObjectUndo(authoring, "Rebuild Road Mesh");
        InvokeRoadAuthoringMethod(authoring, "RebuildRoadMesh");
        EditorUtility.SetDirty(authoring);

        var scene = authoring.gameObject.scene;
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    /// <summary>
    /// 构造带两个转弯的默认样条并将控制点放置在整数米坐标。
    /// </summary>
    /// <returns>用于道路初始形状的样条对象。</returns>
    private static Spline BuildDefaultRoadSpline()
    {
        var spline = new Spline
        {
            Closed = false
        };

        spline.Add(new BezierKnot(new float3(0f, 0f, 0f)));
        spline.Add(new BezierKnot(new float3(0f, 0f, 8f)));
        spline.Add(new BezierKnot(new float3(6f, 0f, 14f)));
        spline.Add(new BezierKnot(new float3(14f, 0f, 14f)));
        spline.SetTangentMode(TangentMode.AutoSmooth);
        return spline;
    }

    /// <summary>
    /// 创建顶层分类父节点，并保持单位变换。
    /// </summary>
    /// <param name="name">父节点名称。</param>
    /// <returns>创建后的父节点对象。</returns>
    private static GameObject CreateRoot(string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go;
    }

    /// <summary>
    /// 通过 ProBuilder 生成可编辑立方体，并将枢轴对齐到顶面中心以保证表面标高可直观控制。
    /// </summary>
    /// <param name="name">对象名称。</param>
    /// <param name="size">几何体真实尺寸（米）。</param>
    /// <param name="position">世界坐标位置。</param>
    /// <param name="material">共享材质。</param>
    /// <returns>创建完成的 ProBuilder 对象。</returns>
    private static GameObject CreateProBuilderCube(string name, Vector3 size, Vector3 position, Material material)
    {
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
        Undo.RegisterCreatedObjectUndo(pb.gameObject, $"Create {name}");
        pb.gameObject.name = name;
        AlignProBuilderPivotToTopCenter(pb, size.y);
        pb.transform.position = position;
        pb.transform.rotation = Quaternion.identity;
        pb.transform.localScale = Vector3.one;
        pb.gameObject.isStatic = true;

        var renderer = pb.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;

        var meshFilter = pb.GetComponent<MeshFilter>();
        var collider = pb.GetComponent<MeshCollider>();
        if (collider == null)
        {
            collider = pb.gameObject.AddComponent<MeshCollider>();
        }

        collider.sharedMesh = meshFilter.sharedMesh;
        return pb.gameObject;
    }

    /// <summary>
    /// 创建用于占位和比例对照的基础参照物，并添加静态碰撞体。
    /// </summary>
    /// <param name="name">对象名称。</param>
    /// <param name="type">Unity 基础体类型。</param>
    /// <param name="position">对象中心位置。</param>
    /// <param name="scale">对象缩放。</param>
    /// <param name="material">共享材质。</param>
    /// <returns>创建完成的参照对象。</returns>
    private static GameObject CreatePrimitiveLandmark(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        go.name = name;
        go.transform.position = position;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = scale;
        go.isStatic = true;

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        var collider = go.GetComponent<Collider>();
        if (collider == null)
        {
            go.AddComponent<BoxCollider>();
        }

        return go;
    }

    /// <summary>
    /// 将 ProBuilder 顶点整体下移半高，使对象原点位于顶面中心，便于按整数米直接控制表面高度。
    /// </summary>
    /// <param name="mesh">目标 ProBuilder 网格。</param>
    /// <param name="height">几何体高度（米）。</param>
    private static void AlignProBuilderPivotToTopCenter(ProBuilderMesh mesh, float height)
    {
        var positions = new System.Collections.Generic.List<Vector3>(mesh.positions);
        for (int i = 0; i < positions.Count; i++)
        {
            positions[i] += Vector3.down * (height * 0.5f);
        }

        mesh.positions = positions;
        mesh.ToMesh();
        mesh.Refresh();
    }

    /// <summary>
    /// 通过反射获取道路 Authoring 组件类型，避免编辑器程序集直接类型绑定失败。
    /// </summary>
    /// <returns>道路组件类型；未找到时返回 null。</returns>
    private static Type GetRoadAuthoringType()
    {
        return Type.GetType("TDRoadSplineAuthoring, Assembly-CSharp");
    }

    /// <summary>
    /// 在指定对象上添加道路 Authoring 组件。
    /// </summary>
    /// <param name="target">目标对象。</param>
    /// <returns>添加后的组件；失败返回 null。</returns>
    private static Component AddRoadAuthoringComponent(GameObject target)
    {
        if (target == null)
        {
            return null;
        }

        var type = GetRoadAuthoringType();
        if (type == null)
        {
            return null;
        }

        return target.AddComponent(type);
    }

    /// <summary>
    /// 在对象及其父级查找道路 Authoring 组件。
    /// </summary>
    /// <param name="target">起始对象。</param>
    /// <returns>找到的组件；未找到返回 null。</returns>
    private static Component GetRoadAuthoringComponent(GameObject target)
    {
        if (target == null)
        {
            return null;
        }

        var type = GetRoadAuthoringType();
        if (type == null)
        {
            return null;
        }

        return target.GetComponentInParent(type);
    }

    /// <summary>
    /// 调用道路 Authoring 指定方法。
    /// </summary>
    /// <param name="component">目标组件。</param>
    /// <param name="methodName">方法名。</param>
    /// <param name="args">参数列表。</param>
    private static void InvokeRoadAuthoringMethod(Component component, string methodName, params object[] args)
    {
        if (component == null)
        {
            return;
        }

        var method = component.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        if (method == null)
        {
            Debug.LogError($"TDRoadSplineAuthoring 缺少方法: {methodName}", component);
            return;
        }

        method.Invoke(component, args);
    }

    /// <summary>
    /// 基于场景内容边界计算俯视主相机参数，确保角色、道路与 2x2 占地比例清晰可见。
    /// </summary>
    /// <param name="camera">待配置的主相机。</param>
    /// <param name="roots">用于计算可视范围的根节点集合。</param>
    private static void ConfigureTopDownCamera(Camera camera, params GameObject[] roots)
    {
        var bounds = new Bounds(Vector3.zero, Vector3.one);
        bool initialized = false;

        foreach (var root in roots)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!initialized)
        {
            bounds = new Bounds(new Vector3(6f, 0f, 6f), new Vector3(24f, 2f, 24f));
        }

        float margin = 3f;
        float aspect = Mathf.Max(0.1f, camera.aspect);
        float sizeByDepth = bounds.extents.z + margin;
        float sizeByWidth = bounds.extents.x / aspect + margin;
        float orthographicSize = Mathf.Max(sizeByDepth, sizeByWidth);

        camera.orthographic = true;
        camera.orthographicSize = orthographicSize;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 200f;
        camera.transform.position = new Vector3(bounds.center.x, 30f, bounds.center.z);
        camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    /// <summary>
    /// 在缺失目录时创建目录，避免资源写入失败。
    /// </summary>
    /// <param name="assetPath">Unity 资源路径。</param>
    private static void EnsureDirectory(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
        {
            return;
        }

        var parent = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        var folder = Path.GetFileName(assetPath);

        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folder))
        {
            return;
        }

        EnsureDirectory(parent);
        AssetDatabase.CreateFolder(parent, folder);
    }

    /// <summary>
    /// 获取已有共享材质；若不存在则按指定颜色新建材质并保存为资源。
    /// </summary>
    /// <param name="name">材质文件名（不含扩展名）。</param>
    /// <param name="baseColor">材质基础颜色。</param>
    /// <returns>可复用的共享材质。</returns>
    private static Material GetOrCreateMaterial(string name, Color baseColor)
    {
        string path = $"{GeneratedRoot}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            return existing;
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader)
        {
            color = baseColor
        };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
