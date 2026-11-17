using UnityEngine;

/// <summary>
/// 开发者模式数据表：F3 在屏幕右侧显示“小队 / 单位”两张表。
/// 仅在开发者模式（F2）开启时可见；用 IMGUI 绘制，不依赖 Canvas 与字体资产。
/// </summary>
[DisallowMultipleComponent]
public class DeveloperDataTable : MonoBehaviour
{
    private static DeveloperDataTable instance;

    [SerializeField] private KeyCode toggleKey = KeyCode.F3;
    [SerializeField] private float panelX = 340f;
    [SerializeField] private float panelWidth = 560f;

    private bool visible;
    private GUIStyle titleStyle;
    private GUIStyle headerStyle;
    private GUIStyle cellStyle;

    /// <summary>当前是否显示。</summary>
    public bool IsVisible => visible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<DeveloperDataTable>() != null)
        {
            return;
        }

        var go = new GameObject("DeveloperDataTable");
        go.AddComponent<DeveloperDataTable>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            DestroyNow(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoadIfPlaying();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        if (!DeveloperMode.Enabled)
        {
            visible = false;
            return;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(toggleKey))
        {
            visible = !visible;
        }
#endif
    }

    /// <summary>切换显示状态（F3 与测试可用）。</summary>
    public void Toggle()
    {
        if (!DeveloperMode.Enabled)
        {
            visible = false;
            return;
        }

        visible = !visible;
    }

    private void OnGUI()
    {
        if (!visible || !DeveloperMode.Enabled)
        {
            return;
        }

        EnsureStyles();

        float y = 10f;
        y = DrawTable(
            y,
            "小队表（全局）",
            DeveloperTableData.SquadHeaders,
            DeveloperTableData.BuildSquadRows(),
            new[] { 40f, 80f, 60f, 60f, 50f, 70f, 60f, 140f });

        DrawTable(
            y + 8f,
            "单位表（全局）",
            DeveloperTableData.UnitHeaders,
            DeveloperTableData.BuildUnitRows(),
            new[] { 130f, 60f, 70f, 110f, 110f, 50f });
    }

    private float DrawTable(float y, string title, string[] headers, System.Collections.Generic.List<string[]> rows, float[] widths)
    {
        float rowHeight = 20f;
        float height = 30f + (rows.Count + 1) * rowHeight;
        GUI.Box(new Rect(panelX, y, panelWidth, height), title, titleStyle);

        float lineY = y + 26f;
        DrawRow(headers, widths, lineY, headerStyle);
        lineY += rowHeight;

        for (int i = 0; i < rows.Count; i++)
        {
            DrawRow(rows[i], widths, lineY, cellStyle);
            lineY += rowHeight;
        }

        return y + height;
    }

    private void DrawRow(string[] values, float[] widths, float y, GUIStyle style)
    {
        float x = panelX + 8f;
        for (int i = 0; i < values.Length && i < widths.Length; i++)
        {
            GUI.Label(new Rect(x, y, widths[i], 18f), values[i], style);
            x += widths[i];
        }
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
        {
            return;
        }

        titleStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(8, 8, 4, 4)
        };

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
            normal = { textColor = new Color(0.95f, 0.85f, 0.55f) }
        };

        cellStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.white }
        };
    }

    private void DontDestroyOnLoadIfPlaying()
    {
        if (Application.isPlaying)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

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
}
