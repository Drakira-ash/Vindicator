using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 开发者模式单位面板：右侧可展开/收回的 IMGUI 面板。
/// 上部分按我方 / 敌方列出可生成单位（“放置”进入鼠标预览与点击放置），
/// 下部分列出场上所有单位并可逐个销毁。仅在开发者模式开启时显示。
/// </summary>
[DisallowMultipleComponent]
public class DeveloperSpawnPanel : MonoBehaviour
{
    private const float RowHeight = 24f;
    private const float HeaderHeight = 26f;

    private static DeveloperSpawnPanel instance;

    [SerializeField] private KeyCode toggleKey = KeyCode.F4;
    [SerializeField, Min(200f)] private float panelWidth = 330f;
    [SerializeField, Min(80f)] private float unitListHeight = 170f;

    private readonly List<UnitHealth> unitSnapshot = new List<UnitHealth>();
    private readonly List<DeveloperSpawnCatalog.Entry> entries = new List<DeveloperSpawnCatalog.Entry>();

    private bool expanded = true;
    private Vector2 unitScroll;
    private Rect panelBounds;
    private bool hasBounds;

    private GUIStyle titleStyle;
    private GUIStyle sectionStyle;
    private GUIStyle cellStyle;
    private GUIStyle hintStyle;
    private GUIStyle tipStyle;
    private GUIStyle playerStyle;
    private GUIStyle enemyStyle;

    /// <summary>当前面板（运行期唯一）。</summary>
    public static DeveloperSpawnPanel Instance => instance;

    /// <summary>面板是否展开。</summary>
    public bool IsExpanded => expanded;

    /// <summary>面板是否显示（开发者模式开启时）。</summary>
    public bool IsVisible => DeveloperMode.Enabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<DeveloperSpawnPanel>() != null)
        {
            return;
        }

        var go = new GameObject("DeveloperSpawnPanel");
        go.AddComponent<DeveloperSpawnPanel>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            DestroyNow(gameObject);
            return;
        }

        instance = this;

        if (Application.isPlaying)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            hasBounds = false;
        }
    }

    private void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (DeveloperMode.Enabled && Input.GetKeyDown(toggleKey))
        {
            ToggleExpanded();
        }
#endif
    }

    /// <summary>展开/收起面板。</summary>
    public void ToggleExpanded()
    {
        expanded = !expanded;
    }

    /// <summary>直接设置展开状态。</summary>
    public void SetExpanded(bool value)
    {
        expanded = value;
    }

    /// <summary>
    /// 鼠标是否落在面板上（屏幕坐标，原点左下）。
    /// 面板用 IMGUI 绘制，不经过 EventSystem，需要单独告知玩家控制器不要吃这次点击。
    /// </summary>
    public static bool IsPointerOverPanel(Vector2 screenPosition)
    {
        var panel = instance;
        if (panel == null || !DeveloperMode.Enabled || !panel.hasBounds)
        {
            return false;
        }

        var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        return panel.panelBounds.Contains(guiPoint);
    }

    private void OnGUI()
    {
        if (!DeveloperMode.Enabled)
        {
            hasBounds = false;
            return;
        }

        EnsureStyles();

        float x = Mathf.Max(12f, Screen.width - panelWidth - 12f);
        float y = 10f;

        var headerRect = new Rect(x, y, panelWidth, HeaderHeight);
        if (GUI.Button(headerRect, expanded ? "开发者 · 单位生成 ▲" : "开发者 · 单位生成 ▼", titleStyle))
        {
            expanded = !expanded;
        }

        panelBounds = headerRect;
        hasBounds = true;

        if (!expanded)
        {
            return;
        }

        var controller = DeveloperSpawnController.Instance;
        var catalog = controller != null ? controller.Catalog : null;
        float bodyHeight = MeasureBodyHeight(controller, catalog);
        var bodyRect = new Rect(x, y + HeaderHeight + 2f, panelWidth, bodyHeight);
        GUI.Box(bodyRect, GUIContent.none);
        panelBounds = Rect.MinMaxRect(
            panelBounds.xMin,
            panelBounds.yMin,
            panelBounds.xMax,
            bodyRect.yMax);

        float lineY = bodyRect.y + 6f;
        lineY = DrawPresetSection(lineY, x, "我方单位", UnitTeam.Player, controller, catalog);
        lineY = DrawPresetSection(lineY, x, "敌方单位", UnitTeam.Enemy, controller, catalog);

        GUI.Label(new Rect(x + 8f, lineY, panelWidth - 16f, 20f),
            "S 鼠标处民兵 · D 鼠标处敌人 · K 销毁选中 · F4 收起", hintStyle);
        lineY += 20f;

        lineY = DrawUnitList(lineY, x, controller);
        DrawPlacementHint(controller);
    }

    private float MeasureBodyHeight(DeveloperSpawnController controller, DeveloperSpawnCatalog catalog)
    {
        float height = 6f;

        if (catalog == null)
        {
            // 两个分组各一行“未找到生成目录”，再加提示与单位列表。
            return height + (RowHeight * 2f + 4f) * 2f + 20f + RowHeight + unitListHeight + 6f;
        }

        for (int team = 0; team < 2; team++)
        {
            entries.Clear();
            catalog.CollectEntries(team == 0 ? UnitTeam.Player : UnitTeam.Enemy, entries);
            height += RowHeight;                                  // 分组标题
            height += Mathf.Max(1, entries.Count) * RowHeight;    // 条目行（空分组也有提示行）
            height += 4f;
        }

        height += 20f;                                   // 热键提示
        height += RowHeight;                             // “场上单位”标题 + 销毁选中按钮
        height += unitListHeight;                        // 单位滚动列表
        height += 6f;
        return height;
    }

    private float DrawPresetSection(
        float y,
        float x,
        string title,
        UnitTeam team,
        DeveloperSpawnController controller,
        DeveloperSpawnCatalog catalog)
    {
        int alive = CountAlive(team);
        GUI.Label(new Rect(x + 8f, y, panelWidth - 16f, RowHeight),
            string.Format("{0} · 场上 {1}", title, alive), sectionStyle);
        y += RowHeight;

        if (catalog == null)
        {
            GUI.Label(new Rect(x + 16f, y, panelWidth - 24f, 20f), "未找到生成目录", hintStyle);
            y += RowHeight;
            return y + 4f;
        }

        entries.Clear();
        catalog.CollectEntries(team, entries);
        if (entries.Count == 0)
        {
            GUI.Label(new Rect(x + 16f, y, panelWidth - 24f, 20f), "（无条目）", hintStyle);
            y += RowHeight;
            return y + 4f;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            GUI.Label(new Rect(x + 16f, y + 3f, panelWidth - 110f, 20f), entry.DisplayName, cellStyle);

            bool armed = controller != null && controller.IsPlacing && controller.ArmedEntry == entry;
            if (GUI.Button(new Rect(x + panelWidth - 84f, y + 1f, 76f, 21f), armed ? "取消" : "放置"))
            {
                if (controller != null)
                {
                    controller.ArmEntry(entry);
                }
            }

            y += RowHeight;
        }

        return y + 4f;
    }

    private float DrawUnitList(float y, float x, DeveloperSpawnController controller)
    {
        RebuildSnapshot();

        if (GUI.Button(new Rect(x + panelWidth - 116f, y + 1f, 108f, 21f), "销毁选中 (K)"))
        {
            if (controller != null)
            {
                controller.DestroySelectedUnits();
            }
        }

        GUI.Label(new Rect(x + 8f, y + 2f, panelWidth - 130f, 20f),
            string.Format("场上单位（{0}）", unitSnapshot.Count), sectionStyle);
        y += RowHeight;

        float contentHeight = Mathf.Max(unitListHeight, unitSnapshot.Count * 20f + 4f);
        var viewRect = new Rect(x + 8f, y, panelWidth - 16f, unitListHeight);
        var contentRect = new Rect(0f, 0f, panelWidth - 34f, contentHeight);
        unitScroll = GUI.BeginScrollView(viewRect, unitScroll, contentRect);

        for (int i = 0; i < unitSnapshot.Count; i++)
        {
            var health = unitSnapshot[i];
            if (health == null)
            {
                continue;
            }

            float rowY = i * 20f;
            var style = health.Team == UnitTeam.Enemy ? enemyStyle : playerStyle;
            GUI.Label(new Rect(2f, rowY + 2f, contentRect.width - 62f, 18f),
                string.Format("{0} · {1}", health.name, TeamLabel(health.Team)), style);

            if (GUI.Button(new Rect(contentRect.width - 56f, rowY + 1f, 52f, 18f), "销毁"))
            {
                if (controller != null)
                {
                    controller.DestroyUnit(health);
                }
            }
        }

        GUI.EndScrollView();
        return y + unitListHeight + 6f;
    }

    private void DrawPlacementHint(DeveloperSpawnController controller)
    {
        if (controller == null || !controller.IsPlacing || controller.ArmedEntry == null)
        {
            return;
        }

        var mouse = Event.current.mousePosition;
        string text = string.Format("待放置：{0} · 左键放置 / 右键取消", controller.ArmedEntry.DisplayName);
        var rect = new Rect(mouse.x + 18f, mouse.y + 14f, 300f, 22f);
        GUI.Box(rect, text, tipStyle);
    }

    private void RebuildSnapshot()
    {
        unitSnapshot.Clear();
        var all = UnitRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var health = all[i];
            if (health != null && health.IsAlive)
            {
                unitSnapshot.Add(health);
            }
        }

        unitSnapshot.Sort((a, b) =>
        {
            int teamCompare = a.Team.CompareTo(b.Team);
            return teamCompare != 0 ? teamCompare : string.CompareOrdinal(a.name, b.name);
        });
    }

    private static int CountAlive(UnitTeam team)
    {
        int count = 0;
        var all = UnitRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var health = all[i];
            if (health != null && health.IsAlive && health.Team == team)
            {
                count++;
            }
        }

        return count;
    }

    private static string TeamLabel(UnitTeam team)
    {
        switch (team)
        {
            case UnitTeam.Player:
                return "我方";
            case UnitTeam.Enemy:
                return "敌方";
            default:
                return "中立";
        }
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
        {
            return;
        }

        titleStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(8, 8, 2, 2)
        };

        sectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.95f, 0.85f, 0.55f) }
        };

        cellStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.white }
        };

        hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            wordWrap = false,
            normal = { textColor = new Color(0.78f, 0.78f, 0.78f) }
        };

        tipStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12,
            padding = new RectOffset(8, 8, 2, 2)
        };

        playerStyle = new GUIStyle(cellStyle)
        {
            normal = { textColor = new Color(0.55f, 0.9f, 0.6f) }
        };

        enemyStyle = new GUIStyle(cellStyle)
        {
            normal = { textColor = new Color(1f, 0.55f, 0.5f) }
        };
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
