using UnityEngine;

/// <summary>
/// 玩家移动命令的落点反馈：在目标点插一面小旗，短暂停留后缩小消失。
/// 运行时用 primitive 拼出，无 Collider、Ignore Raycast 层，不影响选择/射线。
/// </summary>
public static class MoveDestinationFlag
{
    public const float Lifetime = 1.5f;

    private const float AppearDuration = 0.15f;
    private const float FadeDuration = 0.35f;

    private static Material poleMaterial;
    private static Material playerClothMaterial;
    private static Material enemyClothMaterial;

    /// <summary>在落点生成旗子，返回根对象（测试/调试用）。</summary>
    public static GameObject Show(Vector3 position, UnitTeam team)
    {
        var root = new GameObject("MoveDestinationFlag");
        int layer = LayerMask.NameToLayer("Ignore Raycast");
        if (layer < 0)
        {
            layer = 2;
        }

        root.layer = layer;
        root.transform.position = new Vector3(position.x, position.y + 0.01f, position.z);

        var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Prepare(pole, root.transform, layer);
        pole.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        pole.transform.localScale = new Vector3(0.04f, 0.7f, 0.04f);
        var poleRenderer = pole.GetComponent<Renderer>();
        if (poleRenderer != null)
        {
            poleRenderer.sharedMaterial = GetPoleMaterial();
        }

        var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Prepare(cloth, root.transform, layer);
        cloth.transform.localPosition = new Vector3(0.27f, 1.2f, 0f);
        cloth.transform.localScale = new Vector3(0.5f, 0.3f, 0.03f);
        var clothRenderer = cloth.GetComponent<Renderer>();
        if (clothRenderer != null)
        {
            clothRenderer.sharedMaterial = GetClothMaterial(team);
        }

        var view = root.AddComponent<MoveDestinationFlagView>();
        view.Initialize(Lifetime);
        return root;
    }

    /// <summary>
    /// 纯缩放曲线：出现时从 0.2 放大到 1.1，随后保持 1，最后在 0.35s 内缩到 0。
    /// 公开以便 EditMode 直接断言。
    /// </summary>
    public static float ScaleAt(float elapsed)
    {
        if (elapsed <= 0f)
        {
            return 0.2f;
        }

        if (elapsed < AppearDuration)
        {
            return Mathf.Lerp(0.2f, 1.1f, elapsed / AppearDuration);
        }

        float fadeStart = Lifetime - FadeDuration;
        if (elapsed < fadeStart)
        {
            return 1f;
        }

        if (elapsed >= Lifetime)
        {
            return 0f;
        }

        return Mathf.Lerp(1f, 0f, (elapsed - fadeStart) / FadeDuration);
    }

    private static void Prepare(GameObject part, Transform parent, int layer)
    {
        var collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(collider);
            }
            else
            {
                Object.DestroyImmediate(collider);
            }
        }

        part.layer = layer;
        part.transform.SetParent(parent, false);
    }

    private static Material GetPoleMaterial()
    {
        if (poleMaterial == null)
        {
            poleMaterial = CreateMaterial(new Color(0.22f, 0.22f, 0.22f, 1f));
        }

        return poleMaterial;
    }

    private static Material GetClothMaterial(UnitTeam team)
    {
        if (team == UnitTeam.Enemy)
        {
            if (enemyClothMaterial == null)
            {
                enemyClothMaterial = CreateMaterial(new Color(1f, 0.35f, 0.12f, 1f));
            }

            return enemyClothMaterial;
        }

        if (playerClothMaterial == null)
        {
            playerClothMaterial = CreateMaterial(new Color(0.2f, 0.8f, 1f, 1f));
        }

        return playerClothMaterial;
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        var material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        return material;
    }
}

/// <summary>旗子的生命周期与缩放动画；Update 驱动，Tick 供测试直接调用。</summary>
public class MoveDestinationFlagView : MonoBehaviour
{
    private float lifetime = MoveDestinationFlag.Lifetime;
    private float elapsed;
    private Vector3 baseScale = Vector3.one;

    public float Elapsed => elapsed;

    public void Initialize(float life)
    {
        lifetime = Mathf.Max(0.1f, life);
        elapsed = 0f;
        baseScale = transform.localScale;
        ApplyScale(MoveDestinationFlag.ScaleAt(0f));
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>推进动画；elapsed 到达 lifetime 后销毁自身。</summary>
    public void Tick(float deltaTime)
    {
        elapsed += Mathf.Max(0f, deltaTime);
        ApplyScale(MoveDestinationFlag.ScaleAt(elapsed));

        if (elapsed >= lifetime)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(gameObject);
            }
            else
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }

    private void ApplyScale(float scale)
    {
        transform.localScale = baseScale * Mathf.Max(0f, scale);
    }
}
