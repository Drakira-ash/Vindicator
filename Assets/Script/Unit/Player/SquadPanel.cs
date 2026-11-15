using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 编队面板：显示所有小队（编号、人数、队形、状态），支持组建、选择、解散。
/// 运行期用 uGUI 创建，不依赖场景里的 Canvas；按 Tab 显示/隐藏。
/// </summary>
[DisallowMultipleComponent]
public class SquadPanel : MonoBehaviour
{
    private static SquadPanel instance;
    private static Font uiFont;

    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    private readonly List<RowRef> rows = new List<RowRef>();
    private GameObject panelRoot;
    private Text header;
    private RectTransform rowsRoot;
    private float refreshTimer;

    /// <summary>当前面板是否可见。</summary>
    public bool IsVisible => panelRoot != null && panelRoot.activeSelf;

    /// <summary>当前列表行数（不含空提示）。</summary>
    public int RowCount => rows.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<SquadPanel>() != null)
        {
            return;
        }

        var go = new GameObject("SquadPanel");
        go.AddComponent<SquadPanel>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            DestroyNow(gameObject);
            return;
        }

        instance = this;
        BuildUi();
    }

    private void OnEnable()
    {
        SquadRegistry.Changed += RebuildRows;
        UnitSelection.Changed += RefreshHeader;
        RefreshHeader();
        RebuildRows();
    }

    private void OnDisable()
    {
        SquadRegistry.Changed -= RebuildRows;
        UnitSelection.Changed -= RefreshHeader;
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
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(toggleKey))
        {
            Toggle();
        }
#endif

        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = 0.5f;
            RefreshHeader();
            RefreshRows();
        }
    }

    /// <summary>显示/隐藏面板。</summary>
    public void Toggle()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(!panelRoot.activeSelf);
        }
    }

    private void BuildUi()
    {
        var canvasGo = new GameObject(
            "Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        EnsureEventSystem();

        panelRoot = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelRoot.transform.SetParent(canvasGo.transform, false);

        var panelRect = panelRoot.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(12f, -12f);
        panelRect.sizeDelta = new Vector2(330f, 0f);

        panelRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.66f);

        var panelLayout = panelRoot.GetComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(10, 10, 10, 10);
        panelLayout.spacing = 6f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        var fitter = panelRoot.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        header = CreateText(panelRoot.transform, "Header", "小队面板  已选 0 人", 16, true);

        var buttonRow = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        buttonRow.transform.SetParent(panelRoot.transform, false);
        var buttonLayout = buttonRow.GetComponent<HorizontalLayoutGroup>();
        buttonLayout.spacing = 6f;
        buttonLayout.childControlWidth = true;
        buttonLayout.childControlHeight = true;
        buttonLayout.childForceExpandWidth = true;
        buttonLayout.childForceExpandHeight = false;

        CreateButton(buttonRow.transform, "组建编队", OnCreateSquad);
        CreateButton(buttonRow.transform, "清空选择", UnitSelection.Clear);

        var rowsGo = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        rowsGo.transform.SetParent(panelRoot.transform, false);
        rowsRoot = rowsGo.GetComponent<RectTransform>();

        var rowsLayout = rowsGo.GetComponent<VerticalLayoutGroup>();
        rowsLayout.spacing = 4f;
        rowsLayout.childControlWidth = true;
        rowsLayout.childControlHeight = true;
        rowsLayout.childForceExpandWidth = true;
        rowsLayout.childForceExpandHeight = false;

        var rowsFitter = rowsGo.GetComponent<ContentSizeFitter>();
        rowsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateText(panelRoot.transform, "Hint", "左键框选 / Shift 追加 / F1 全选 / G 编队（编号自增）/ 1-9 选队 / Ctrl+1-9 指定编号 / 右键移动或攻击 / Tab 收起", 11, false);
    }

    private void OnCreateSquad()
    {
        // 与 G 热键走同一条路径：编号由注册表自增，并写日志。
        var commands = PlayerCommandController.Instance;
        if (commands != null)
        {
            commands.CreateSquadFromSelection();
            return;
        }

        UnitSelection.Prune();
        if (UnitSelection.Count > 0)
        {
            SquadRegistry.Create(UnitSelection.All);
        }
    }

    private void RefreshHeader()
    {
        if (header != null)
        {
            // 同时显示场上可指挥人数：为 0 时说明框选不到人是“士兵已全部阵亡”，而非输入失效。
            // 已选人数只统计存活单位，避免单位阵亡后标题仍显示旧人数。
            header.text = string.Format(
                "小队面板  已选 {0} 人 / 可指挥 {1} 人",
                CountAliveSelected(),
                CountCommandable());
        }
    }

    private static int CountAliveSelected()
    {
        int count = 0;
        var selected = UnitSelection.All;
        for (int i = 0; i < selected.Count; i++)
        {
            var unit = selected[i];
            if (unit != null && unit.Health != null && unit.Health.IsAlive)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountCommandable()
    {
        int count = 0;
        var all = UnitRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var health = all[i];
            if (health != null && health.IsAlive &&
                PlayerCommandController.IsSelectable(health.GetComponent<Unit>()))
            {
                count++;
            }
        }

        return count;
    }

    private void RebuildRows()
    {
        if (rowsRoot == null)
        {
            return;
        }

        for (int i = rowsRoot.childCount - 1; i >= 0; i--)
        {
            var child = rowsRoot.GetChild(i).gameObject;
            child.SetActive(false);
            DestroyNow(child);
        }

        rows.Clear();

        var all = SquadRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var squad = all[i];
            if (squad == null)
            {
                continue;
            }

            var captured = squad;
            var rowGo = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(rowsRoot, false);

            var rowLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 4f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            var element = rowGo.GetComponent<LayoutElement>();
            element.minHeight = 26f;

            var label = CreateText(rowGo.transform, "Info", Describe(captured), 13, false);
            label.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var selectButton = CreateButton(rowGo.transform, "选择", () => UnitSelection.Set(captured.MemberUnits()));
            selectButton.GetComponent<LayoutElement>().preferredWidth = 46f;

            var disbandButton = CreateButton(rowGo.transform, "解散", () => captured.Disband());
            disbandButton.GetComponent<LayoutElement>().preferredWidth = 46f;

            rows.Add(new RowRef(captured, label));
        }

        if (rows.Count == 0)
        {
            CreateText(rowsRoot, "Empty", "（尚无编队：框选士兵后按 G 或点“组建编队”）", 12, false);
        }
    }

    private void RefreshRows()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Squad == null)
            {
                RebuildRows();
                return;
            }

            if (rows[i].Label != null)
            {
                rows[i].Label.text = Describe(rows[i].Squad);
            }
        }
    }

    private static string Describe(Squad squad)
    {
        int count = squad.MemberUnits().Count;
        return string.Format(
            "小队 {0}｜{1} 人｜{2}｜{3}",
            squad.SquadId,
            count,
            squad.Formation,
            squad.State);
    }

    private Text CreateText(Transform parent, string name, string content, int size, bool bold)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<Text>();
        text.font = UiFont;
        text.fontSize = size;
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = content;
        text.raycastTarget = false;

        var element = go.GetComponent<LayoutElement>();
        element.minHeight = size + 8f;

        return text;
    }

    private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        go.GetComponent<Image>().color = new Color(0.20f, 0.38f, 0.62f, 0.95f);

        var button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);

        var element = go.GetComponent<LayoutElement>();
        element.minHeight = 26f;
        element.minWidth = 40f;

        var text = CreateText(go.transform, "Label", label, 13, false);
        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        text.alignment = TextAnchor.MiddleCenter;

        return button;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoadIfPlaying(go);
    }

    private static Font UiFont
    {
        get
        {
            if (uiFont != null)
            {
                return uiFont;
            }

            uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
            if (uiFont == null)
            {
                uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return uiFont;
        }
    }

    private static void DontDestroyOnLoadIfPlaying(GameObject go)
    {
        if (Application.isPlaying)
        {
            DontDestroyOnLoad(go);
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

    private sealed class RowRef
    {
        public readonly Squad Squad;
        public readonly Text Label;

        public RowRef(Squad squad, Text label)
        {
            Squad = squad;
            Label = label;
        }
    }
}
