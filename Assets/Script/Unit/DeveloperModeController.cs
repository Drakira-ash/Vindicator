using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 开发者模式可视化：为每个单位画一个半透明感知范围球体。
/// 球体挂在独立的跟随物体上，不带 Collider，不参与射线与目标判定。
/// 场景里不必手动放置：首次加载会自动创建；也可手动挂到任意物体上并在 Inspector 配置初始开关。
/// </summary>
[DisallowMultipleComponent]
public class DeveloperModeController : MonoBehaviour
{
    private static DeveloperModeController instance;
    private static Mesh sphereMesh;

    [SerializeField] private bool startEnabled = false;
    [SerializeField] private KeyCode toggleKey = KeyCode.F2;
    [SerializeField] private Color playerColor = new Color(0.2f, 0.6f, 1f);
    [SerializeField] private Color enemyColor = new Color(1f, 0.25f, 0.2f);
    [SerializeField] private Color neutralColor = new Color(0.85f, 0.85f, 0.2f);

    // 填充很淡、边缘提亮：几十个球重叠时仍然看得清场景。
    [SerializeField, Range(0.01f, 0.5f)] private float sphereAlpha = 0.1f;
    [SerializeField, Range(0f, 1f)] private float rimAlpha = 0.55f;
    [SerializeField, Range(0.5f, 8f)] private float rimPower = 2.5f;

    private const string PerceptionShaderName = "Vindicator/PerceptionVolume";

    private readonly Dictionary<UnitHealth, GameObject> spheres = new Dictionary<UnitHealth, GameObject>();
    private readonly Dictionary<UnitTeam, Material> materials = new Dictionary<UnitTeam, Material>();
    private readonly List<UnitHealth> stale = new List<UnitHealth>();

    /// <summary>当前开关。</summary>
    public bool IsEnabled => DeveloperMode.Enabled;

    /// <summary>当前显示中的球体数量，便于验收。</summary>
    public int SphereCount => spheres.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<DeveloperModeController>() != null)
        {
            return;
        }

        var go = new GameObject("DeveloperMode");
        go.AddComponent<DeveloperModeController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            DestroyNow(gameObject);
            return;
        }

        instance = this;

        // EditMode 测试会直接调用 Awake；DontDestroyOnLoad 在非播放模式下会抛异常。
        if (Application.isPlaying)
        {
            DontDestroyOnLoad(gameObject);
        }

        DeveloperMode.SetEnabled(startEnabled);
    }

    private void OnDestroy()
    {
        if (instance != this)
        {
            return;
        }

        instance = null;
        ClearSpheres();

        foreach (var material in materials.Values)
        {
            if (material != null)
            {
                DestroyNow(material);
            }
        }

        materials.Clear();
    }

    private void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(toggleKey))
        {
            Toggle();
        }
#endif

        if (DeveloperMode.Enabled)
        {
            SyncSpheres();
        }
        else if (spheres.Count > 0)
        {
            ClearSpheres();
        }
    }

    /// <summary>切换开发者模式。</summary>
    public void Toggle()
    {
        SetEnabled(!DeveloperMode.Enabled);
    }

    /// <summary>设置开发者模式开关。</summary>
    public void SetEnabled(bool enabled)
    {
        DeveloperMode.SetEnabled(enabled);

        if (enabled)
        {
            SyncSpheres();
        }
        else
        {
            ClearSpheres();
        }
    }

    private void SyncSpheres()
    {
        var units = UnitRegistry.All;

        stale.Clear();
        foreach (var key in spheres.Keys)
        {
            stale.Add(key);
        }

        for (int i = 0; i < units.Count; i++)
        {
            var health = units[i];
            if (health == null || !health.IsAlive)
            {
                continue;
            }

            var source = health.GetComponent<IPerceptionSource>();
            if (source == null)
            {
                continue;
            }

            float radius = source.PerceptionRange;
            if (radius <= 0f)
            {
                continue;
            }

            if (!spheres.TryGetValue(health, out var sphere) || sphere == null)
            {
                sphere = CreateSphere(health);
                spheres[health] = sphere;
            }

            sphere.transform.position = health.transform.position;
            sphere.transform.localScale = Vector3.one * (radius * 2f);
            stale.Remove(health);
        }

        for (int i = 0; i < stale.Count; i++)
        {
            if (spheres.TryGetValue(stale[i], out var sphere))
            {
                if (sphere != null)
                {
                    DestroyNow(sphere);
                }

                spheres.Remove(stale[i]);
            }
        }
    }

    /// <summary>编辑模式下 Destroy 会报错且不生效；统一走 DestroyImmediate，运行时仍用 Destroy。</summary>
    private static void DestroyNow(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(target);
        }
        else
        {
            Object.DestroyImmediate(target);
        }
    }

    private GameObject CreateSphere(UnitHealth owner)
    {
        var go = new GameObject("Perception_" + owner.name);
        go.layer = LayerMask.NameToLayer("Ignore Raycast");
        if (go.layer < 0)
        {
            go.layer = 0;
        }

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = SphereMesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = MaterialFor(owner.Team);
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        return go;
    }

    private Material MaterialFor(UnitTeam team)
    {
        if (materials.TryGetValue(team, out var cached) && cached != null)
        {
            return cached;
        }

        Color color;
        switch (team)
        {
            case UnitTeam.Player:
                color = playerColor;
                break;
            case UnitTeam.Enemy:
                color = enemyColor;
                break;
            default:
                color = neutralColor;
                break;
        }

        color.a = sphereAlpha;
        var material = CreatePerceptionMaterial(color, rimAlpha, rimPower);
        materials[team] = material;
        return material;
    }

    /// <summary>
    /// 优先用专用 shader：它靠模板缓冲做“同一像素只画一层”，重叠的球不会越叠越深。
    /// 找不到时退回通用半透明 Unlit（旧行为，重叠仍会累积）。
    /// </summary>
    private static Material CreatePerceptionMaterial(Color color, float rimAlpha, float rimPower)
    {
        var perceptionShader = Shader.Find(PerceptionShaderName);
        if (perceptionShader != null)
        {
            var perception = new Material(perceptionShader) { hideFlags = HideFlags.DontSave };
            perception.SetColor("_BaseColor", color);

            var rim = color;
            rim.a = rimAlpha;
            perception.SetColor("_RimColor", rim);
            perception.SetFloat("_RimPower", rimPower);
            return perception;
        }

        return CreateTransparentMaterial(color);
    }

    private static Material CreateTransparentMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Transparent");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            return null;
        }

        var material = new Material(shader) { hideFlags = HideFlags.DontSave };
        material.SetOverrideTag("RenderType", "Transparent");

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        return material;
    }

    private void ClearSpheres()
    {
        foreach (var sphere in spheres.Values)
        {
            if (sphere != null)
            {
                DestroyNow(sphere);
            }
        }

        spheres.Clear();
    }

    private static Mesh SphereMesh
    {
        get
        {
            if (sphereMesh == null)
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var filter = temp.GetComponent<MeshFilter>();
                sphereMesh = filter != null ? filter.sharedMesh : null;

                // 播放模式下 Destroy 要到帧末生效；先停用，避免这一帧出现带 Collider 的不透明临时球。
                temp.SetActive(false);
                DestroyNow(temp);
            }

            return sphereMesh;
        }
    }
}
