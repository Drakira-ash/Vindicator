using UnityEngine;

/// <summary>
/// 简易世界空间血条：独立跟随物体（不挂在单位根下），双 Quad 朝向主相机，
/// 宽度按 <see cref="UnitHealth.Current"/> / <see cref="UnitHealth.Max"/> 缩放。
/// 不添加任何 Collider，避免改变 <see cref="UnitHealth.DistanceTo"/> 的判定。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitHealth))]
public class HealthBar : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float width = 1.2f;
    [SerializeField, Min(0.01f)] private float height = 0.16f;
    [SerializeField, Min(0f)] private float heightOffset = 1.4f;

    private static Mesh quadMesh;
    private static Material backgroundMaterial;
    private static Material fillMaterial;

    private UnitHealth health;
    private GameObject barObject;
    private Transform fillTransform;
    private Camera targetCamera;

    private void Awake()
    {
        health = GetComponent<UnitHealth>();
        Build();
    }

    private void OnEnable()
    {
        if (health == null)
        {
            health = GetComponent<UnitHealth>();
        }

        if (health != null)
        {
            health.HealthChanged += OnHealthChanged;
        }

        SetVisible(true);
        Refresh();
    }

    private void Start()
    {
        // Unit.Awake 可能在订阅前就初始化过血量，这里再对齐一次。
        Refresh();
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.HealthChanged -= OnHealthChanged;
        }

        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.HealthChanged -= OnHealthChanged;
        }

        if (barObject != null)
        {
            Destroy(barObject);
            barObject = null;
        }
    }

    private void LateUpdate()
    {
        if (barObject == null || health == null)
        {
            return;
        }

        barObject.transform.position = health.DamageTransform.position + Vector3.up * heightOffset;

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null)
        {
            barObject.transform.rotation = targetCamera.transform.rotation;
        }
    }

    private void OnHealthChanged(UnitHealth changed)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (fillTransform == null)
        {
            return;
        }

        float ratio = health != null && health.Max > 0f
            ? Mathf.Clamp01(health.Current / health.Max)
            : 0f;

        float fillWidth = width * ratio;
        fillTransform.localScale = new Vector3(fillWidth, height * 0.7f, 1f);
        fillTransform.localPosition = new Vector3(-(width - fillWidth) * 0.5f, 0f, -0.01f);
    }

    private void SetVisible(bool visible)
    {
        if (barObject != null && barObject.activeSelf != visible)
        {
            barObject.SetActive(visible);
        }
    }

    private void Build()
    {
        barObject = new GameObject(name + "_HealthBar");
        barObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        if (barObject.layer < 0)
        {
            barObject.layer = 0;
        }

        CreateQuad(barObject.transform, "Background", backgroundMaterial ?? (backgroundMaterial = CreateMaterial(new Color(0.35f, 0.05f, 0.05f))),
            new Vector3(width, height, 1f), Vector3.zero);

        var fill = new GameObject("Fill");
        fill.layer = barObject.layer;
        fill.transform.SetParent(barObject.transform, false);
        fill.transform.localScale = new Vector3(width, height * 0.7f, 1f);
        fill.transform.localPosition = new Vector3(0f, 0f, -0.01f);

        var filter = fill.AddComponent<MeshFilter>();
        filter.sharedMesh = QuadMesh;

        var renderer = fill.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = fillMaterial ?? (fillMaterial = CreateMaterial(new Color(0.2f, 0.9f, 0.3f)));
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
                quadMesh = new Mesh { name = "HealthBarQuad", hideFlags = HideFlags.DontSave };
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
