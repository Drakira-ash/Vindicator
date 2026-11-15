using UnityEngine;

/// <summary>
/// 废墟建造条：独立跟随建筑（不挂在建筑根下），双 Quad 朝向主相机，
/// 宽度按 <see cref="UnitBuilding.Progress01"/> 缩放；建成后隐藏。
/// 不添加任何 Collider，避免改变建筑距离判定。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitBuilding))]
public class BuildProgressBar : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float width = 1.4f;
    [SerializeField, Min(0.01f)] private float height = 0.14f;
    [SerializeField, Min(0f)] private float heightOffset = 1.7f;

    private static Mesh quadMesh;
    private static Material backgroundMaterial;
    private static Material fillMaterial;

    private UnitBuilding building;
    private GameObject barObject;
    private Transform fillTransform;
    private Camera targetCamera;

    private void Awake()
    {
        building = GetComponent<UnitBuilding>();
        Build();
    }

    private void OnEnable()
    {
        if (building == null)
        {
            building = GetComponent<UnitBuilding>();
        }

        if (building != null)
        {
            building.ProgressChanged += OnProgressChanged;
            building.Completed += OnCompleted;
        }

        SetVisible(building == null || building.IsRuined);
        Refresh();
    }

    private void OnDisable()
    {
        if (building != null)
        {
            building.ProgressChanged -= OnProgressChanged;
            building.Completed -= OnCompleted;
        }

        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (building != null)
        {
            building.ProgressChanged -= OnProgressChanged;
            building.Completed -= OnCompleted;
        }

        if (barObject != null)
        {
            // 编辑模式（含 EditMode 测试）下 Destroy 不生效并报错，改用 DestroyImmediate。
            if (Application.isPlaying)
            {
                Destroy(barObject);
            }
            else
            {
                DestroyImmediate(barObject);
            }

            barObject = null;
        }
    }

    private void LateUpdate()
    {
        if (barObject == null || building == null)
        {
            return;
        }

        barObject.transform.position = building.transform.position + Vector3.up * heightOffset;

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null)
        {
            barObject.transform.rotation = targetCamera.transform.rotation;
        }
    }

    /// <summary>显示/隐藏建造条。</summary>
    public void SetVisible(bool visible)
    {
        if (barObject != null && barObject.activeSelf != visible)
        {
            barObject.SetActive(visible);
        }
    }

    private void OnProgressChanged(UnitBuilding changed)
    {
        Refresh();
    }

    private void OnCompleted(UnitBuilding completed)
    {
        SetVisible(false);
    }

    private void Refresh()
    {
        if (fillTransform == null || building == null)
        {
            return;
        }

        float ratio = Mathf.Clamp01(building.Progress01);
        float fillWidth = width * ratio;
        fillTransform.localScale = new Vector3(fillWidth, height * 0.7f, 1f);
        fillTransform.localPosition = new Vector3(-(width - fillWidth) * 0.5f, 0f, -0.01f);
    }

    private void Build()
    {
        barObject = new GameObject(name + "_BuildBar");
        barObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        if (barObject.layer < 0)
        {
            barObject.layer = 0;
        }

        CreateQuad(barObject.transform, "Background", backgroundMaterial ?? (backgroundMaterial = CreateMaterial(new Color(0.12f, 0.12f, 0.12f))),
            new Vector3(width, height, 1f), Vector3.zero);

        var fill = new GameObject("Fill");
        fill.layer = barObject.layer;
        fill.transform.SetParent(barObject.transform, false);
        fill.transform.localScale = new Vector3(0f, height * 0.7f, 1f);
        fill.transform.localPosition = new Vector3(-width * 0.5f, 0f, -0.01f);

        var filter = fill.AddComponent<MeshFilter>();
        filter.sharedMesh = QuadMesh;

        var renderer = fill.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = fillMaterial ?? (fillMaterial = CreateMaterial(new Color(0.95f, 0.72f, 0.18f)));
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        fillTransform = fill.transform;
    }

    private static void CreateQuad(Transform parent, string quadName, Material material, Vector3 localScale, Vector3 localPosition)
    {
        var quad = new GameObject(quadName);
        quad.layer = parent.gameObject.layer;
        quad.transform.SetParent(parent, false);
        quad.transform.localScale = localScale;
        quad.transform.localPosition = localPosition;

        var filter = quad.AddComponent<MeshFilter>();
        filter.sharedMesh = QuadMesh;

        var renderer = quad.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static Mesh QuadMesh
    {
        get
        {
            if (quadMesh == null)
            {
                quadMesh = new Mesh { name = "BuildProgressQuad", hideFlags = HideFlags.DontSave };
                quadMesh.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f)
                };
                quadMesh.uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f)
                };
                quadMesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                quadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            }

            return quadMesh;
        }
    }

    private static Material CreateMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        var material = new Material(shader) { hideFlags = HideFlags.DontSave };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", 0f);
        }

        return material;
    }
}
