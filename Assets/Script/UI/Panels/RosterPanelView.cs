using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Vindicator.UI
{
    /// <summary>
    /// 部队预览面板。负责展开/收回、敌人/友军两栏卡片排布、+X 溢出角标与
    /// 完整列表弹层。数据全部来自 <see cref="RosterModel"/>，选择状态来自
    /// <see cref="SelectionService"/>，本组件不自己维护单位清单。
    /// </summary>
    [DisallowMultipleComponent]
    public class RosterPanelView : MonoBehaviour
    {
        // 布局常量统一放在 RosterLayout（Vindicator.UI.Logic），
        // 这里只做别名，避免同一套数字在多处各写一遍而互相漂移。
        private const int Columns = RosterLayout.Columns;
        private const int Rows = RosterLayout.Rows;
        private const float CardWidth = RosterLayout.CardWidth;
        private const float CardHeight = RosterLayout.CardHeight;
        private const float CardSpacing = RosterLayout.CardSpacing;

        // PanelHeight 由网格尺寸推算，是运行时算出来的属性而非编译期常量，
        // 因此只能是 static readonly，不能用 const 别名。
        private static readonly float PanelHeight = RosterLayout.PanelHeight;

        [Header("引用")]
        [SerializeField] private SelectionService selection;
        [SerializeField] private RectTransform panelRoot;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private Button toggleButton;
        [SerializeField] private Text toggleLabel;

        [Tooltip("展开/收回按钮所在的把手。它是面板的兄弟节点而非子节点，"
               + "因此收回时不会被一起推出屏幕，而是贴着面板顶边移动。")]
        [SerializeField] private RectTransform toggleHandle;

        [Header("分栏")]
        [SerializeField] private RectTransform enemyGrid;
        [SerializeField] private RectTransform allyGrid;
        [SerializeField] private Text enemyCountLabel;
        [SerializeField] private Text allyCountLabel;
        [SerializeField] private Button enemyOverflowButton;
        [SerializeField] private Button allyOverflowButton;
        [SerializeField] private Text enemyOverflowLabel;
        [SerializeField] private Text allyOverflowLabel;

        [Header("完整列表")]
        [SerializeField] private RectTransform fullListRoot;
        [SerializeField] private RectTransform fullListContent;
        [SerializeField] private Button fullListCloseButton;

        [Header("动画")]
        [SerializeField] private float collapsedOffset = -PanelHeight;
        [SerializeField] private float refreshInterval = 0.25f;

        private readonly RosterModel model = new RosterModel();
        private readonly PanelTween tween = new PanelTween();

        // 每个网格各自持有单卡/兵牌两份池。两栏曾共用同一个 List，
        // 导致卡片串栏；池必须按父网格隔离。友军栏同时可能出现队伍卡和单卡。
        private readonly Dictionary<RectTransform, List<UnitCardView>> unitCardPools =
            new Dictionary<RectTransform, List<UnitCardView>>();
        private readonly Dictionary<RectTransform, List<SquadCardView>> squadCardPools =
            new Dictionary<RectTransform, List<SquadCardView>>();

        private bool isExpanded;
        private bool fullListOpen;
        private bool pendingRefresh;
        private float refreshTimer;
        private Vector2 expandedPosition;
        private Vector2 handleExpandedPosition;

        // 队伍兵牌的成员浮层。
        private RectTransform squadFlyoutRoot;
        private Text squadFlyoutTitle;
        private RectTransform squadFlyoutContent;
        private int flyoutSquadId = -1;
        private readonly List<GameObject> flyoutRows = new List<GameObject>();
        private Button lineFormationButton;
        private Button columnFormationButton;
        private Button gridFormationButton;

        /// <summary>面板当前是否展开。</summary>
        public bool IsExpanded => isExpanded;

        /// <summary>数据模型（只读用途，测试可访问）。</summary>
        public RosterModel Model => model;

        private void Awake()
        {
            if (selection == null)
            {
                selection = GetComponentInParent<SelectionService>();
            }

            if (selection == null)
            {
                selection = FindFirstObjectByType<SelectionService>();
            }

            if (panelRoot == null)
            {
                panelRoot = transform as RectTransform;
            }

            if (panelRoot != null)
            {
                expandedPosition = panelRoot.anchoredPosition;
            }

            if (toggleHandle != null)
            {
                handleExpandedPosition = toggleHandle.anchoredPosition;
            }

            tween.ProgressChanged += OnTweenProgress;
            tween.Snap(0f);

            if (toggleButton != null)
            {
                toggleButton.onClick.AddListener(Toggle);
            }

            if (enemyOverflowButton != null)
            {
                enemyOverflowButton.onClick.AddListener(() => OpenFullList(RosterSection.Enemy));
            }

            if (allyOverflowButton != null)
            {
                allyOverflowButton.onClick.AddListener(() => OpenFullList(RosterSection.Player));
            }

            if (fullListCloseButton != null)
            {
                fullListCloseButton.onClick.AddListener(CloseFullList);
            }

            SetFullListVisible(false);
            EnsurePanelHitArea();
            EnsureFullListBackgroundBlocks();
        }

        /// <summary>面板空白处也要挡住场景点击，避免点面板等于点地面。</summary>
        private void EnsurePanelHitArea()
        {
            if (panelRoot == null)
            {
                return;
            }

            if (panelRoot.Find("PanelHitArea") != null)
            {
                return;
            }

            var go = new GameObject("PanelHitArea", typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(panelRoot, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;

            // 放在最底层：只接住没被卡片/按钮接走的点击。
            go.transform.SetAsFirstSibling();
        }

        private void EnsureFullListBackgroundBlocks()
        {
            if (fullListRoot == null)
            {
                return;
            }

            var background = fullListRoot.Find("Background");
            var image = background != null ? background.GetComponent<Image>() : null;
            if (image != null)
            {
                image.raycastTarget = true;
            }
        }

        private void OnEnable()
        {
            if (selection != null)
            {
                selection.SelectionChanged += OnSelectionChanged;
            }

            // 单位生成/死亡时立即刷新，不必等下一次轮询。
            UnitRegistry.Changed += OnRegistryChanged;

            RefreshNow();
        }

        private void OnDisable()
        {
            if (selection != null)
            {
                selection.SelectionChanged -= OnSelectionChanged;
            }

            UnitRegistry.Changed -= OnRegistryChanged;
        }

        private void OnRegistryChanged()
        {
            // 注册表在单位 OnEnable/OnDisable 里回调，此时对象状态尚未稳定；
            // 把刷新推迟到本帧末尾，避免读到半初始化的 UnitHealth。
            pendingRefresh = true;
        }

        private void OnDestroy()
        {
            tween.ProgressChanged -= OnTweenProgress;

            if (toggleButton != null)
            {
                toggleButton.onClick.RemoveAllListeners();
            }
        }

        private void Update()
        {
            tween.Tick(Time.unscaledDeltaTime);

            if (pendingRefresh)
            {
                pendingRefresh = false;
                RefreshNow();
                return;
            }

            // 单位会持续生成/死亡，定期重建，避免每帧全量扫描。
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = refreshInterval;
                RefreshNow();
            }
        }

        /// <summary>展开 / 收回切换。</summary>
        public void Toggle()
        {
            SetExpanded(!isExpanded);
        }

        /// <summary>设置展开状态（true 展开，false 收回）。</summary>
        public void SetExpanded(bool expanded)
        {
            if (isExpanded == expanded)
            {
                return;
            }

            isExpanded = expanded;
            tween.Play(expanded ? 1f : 0f);

            if (!expanded)
            {
                CloseFullList();
                HideSquadFlyout();
            }

            if (toggleLabel != null)
            {
                // 草图：展开态按钮显示「缩放」，收回态显示「展开」。
                toggleLabel.text = expanded ? "缩放" : "展开";
            }
        }

        /// <summary>强制立即刷新（生成/死亡事件回调里用）。</summary>
        public void RefreshNow()
        {
            model.VisibleCapacity = Columns * Rows;
            model.Rebuild(
                SelectionService.GatherCandidates(),
                selection != null ? selection.Selected : null);

            RebuildUnitGrid(enemyGrid, model.Enemies);
            RebuildAllyGrid(allyGrid);

            UpdateOverflowBadge(enemyOverflowButton, enemyOverflowLabel, model.EnemySection);
            UpdateOverflowBadge(
                allyOverflowButton, allyOverflowLabel,
                model.AllyGroups.Count, model.VisibleCapacity);

            // 卡片是运行时按需创建的，必然排在角标之后，会把角标盖住。
            // 角标贴着最后一格卡片的右下角，必须浮在最上层才看得见。
            RaiseOverflowBadges();

            if (enemyCountLabel != null)
            {
                enemyCountLabel.text = string.Format("敌人 ({0})", model.EnemySection.All.Count);
            }

            if (allyCountLabel != null)
            {
                allyCountLabel.text = string.Format("友军 ({0})", model.AllySection.All.Count);
            }

            SyncCardSelection();
            RefreshSquadFlyout();

            if (fullListOpen)
            {
                RefreshFullList();
            }
        }

        /// <summary>敌人栏：每个单位一张单卡。</summary>
        private void RebuildUnitGrid(RectTransform grid, IReadOnlyList<ISelectable> data)
        {
            if (grid == null)
            {
                return;
            }

            int visible = RosterCounter.VisibleCount(data.Count, Columns * Rows);
            var pool = EnsureUnitCardPool(grid, visible);

            for (int i = 0; i < pool.Count; i++)
            {
                var card = pool[i];
                if (card == null)
                {
                    continue;
                }

                bool active = i < visible;
                card.gameObject.SetActive(active);
                if (!active)
                {
                    card.Unbind();
                    continue;
                }

                card.Bind(data[i] as UnitSelectable);
                LayoutCard((RectTransform)card.transform, i);
            }
        }

        /// <summary>
        /// 友军栏：队伍合成一张兵牌，未编队单位仍是一张单卡。
        /// 单卡池与兵牌池按网格隔离；同一位置只会激活其中一种。
        /// </summary>
        private void RebuildAllyGrid(RectTransform grid)
        {
            if (grid == null)
            {
                return;
            }

            var groups = model.AllyGroups;
            int visible = RosterCounter.VisibleCount(groups.Count, Columns * Rows);
            var unitPool = EnsureUnitCardPool(grid, visible);
            var squadPool = EnsureSquadCardPool(grid, visible);

            var usedUnits = new HashSet<int>();
            var usedSquads = new HashSet<int>();

            for (int i = 0; i < visible; i++)
            {
                var group = groups[i];
                if (group.IsSquad)
                {
                    var card = squadPool[i];
                    card.gameObject.SetActive(true);
                    card.Bind(group);
                    LayoutCard((RectTransform)card.transform, i);
                    usedSquads.Add(i);
                }
                else
                {
                    var unit = group.SingleUnit;
                    if (unit == null)
                    {
                        continue;
                    }

                    var card = unitPool[i];
                    card.gameObject.SetActive(true);
                    card.Bind(unit);
                    LayoutCard((RectTransform)card.transform, i);
                    usedUnits.Add(i);
                }
            }

            for (int i = 0; i < unitPool.Count; i++)
            {
                if (usedUnits.Contains(i))
                {
                    continue;
                }

                unitPool[i].gameObject.SetActive(false);
                unitPool[i].Unbind();
            }

            for (int i = 0; i < squadPool.Count; i++)
            {
                if (usedSquads.Contains(i))
                {
                    continue;
                }

                squadPool[i].gameObject.SetActive(false);
                squadPool[i].Bind(null);
            }
        }

        /// <summary>取得（必要时创建）指定网格专属的单卡池。</summary>
        private List<UnitCardView> EnsureUnitCardPool(RectTransform grid, int needed)
        {
            if (!unitCardPools.TryGetValue(grid, out var pool))
            {
                pool = new List<UnitCardView>();
                unitCardPools.Add(grid, pool);
            }

            while (pool.Count < needed)
            {
                var go = new GameObject(
                    "UnitCard_" + pool.Count,
                    typeof(RectTransform),
                    typeof(UnitCardView));

                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(grid, false);
                rect.sizeDelta = new Vector2(CardWidth, CardHeight);

                var card = go.GetComponent<UnitCardView>();
                card.Build();
                card.Clicked += OnCardClicked;
                pool.Add(card);
            }

            return pool;
        }

        /// <summary>取得（必要时创建）指定网格专属的兵牌池。</summary>
        private List<SquadCardView> EnsureSquadCardPool(RectTransform grid, int needed)
        {
            if (!squadCardPools.TryGetValue(grid, out var pool))
            {
                pool = new List<SquadCardView>();
                squadCardPools.Add(grid, pool);
            }

            while (pool.Count < needed)
            {
                var go = new GameObject(
                    "SquadCard_" + pool.Count,
                    typeof(RectTransform),
                    typeof(SquadCardView));

                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(grid, false);
                rect.sizeDelta = new Vector2(CardWidth, CardHeight);

                var card = go.GetComponent<SquadCardView>();
                card.Build();
                card.Clicked += OnSquadCardClicked;
                card.PostureClicked += OnSquadPostureClicked;
                pool.Add(card);
            }

            return pool;
        }

        private static void LayoutCard(RectTransform rect, int index)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = RosterLayout.CardPosition(index);
        }

        private void OnCardClicked(UnitSelectable card)
        {
            if (selection == null || card == null)
            {
                return;
            }

            // 卡片点击不累加：符合“再次点击会替换上次选择”。
            selection.Select(card, false);

            // 卡片点击不算场景点击，收回完整列表，让用户看到场景中的高亮。
            CloseFullList();
            HideSquadFlyout();
        }

        /// <summary>兵牌点击：选中全队，并展开/收起成员浮层。</summary>
        private void OnSquadCardClicked(RosterGroupData group)
        {
            if (selection == null || group == null || !group.IsSquad)
            {
                return;
            }

            var members = new List<ISelectable>();
            for (int i = 0; i < group.Members.Count; i++)
            {
                if (group.Members[i] != null)
                {
                    members.Add(group.Members[i]);
                }
            }

            selection.SelectMany(members, false);
            CloseFullList();
            ToggleSquadFlyout(group);
        }

        /// <summary>兵牌上的剑/盾：驻守 ↔ 戒备。</summary>
        private void OnSquadPostureClicked(RosterGroupData group)
        {
            var squad = group != null ? group.Squad : null;
            if (squad == null)
            {
                return;
            }

            squad.SetPosture(
                squad.Posture == SquadPosture.Garrison
                    ? SquadPosture.Alert
                    : SquadPosture.Garrison);
            RefreshNow();
        }

        // -----------------------------------------------------------------
        // 队伍成员浮层
        // -----------------------------------------------------------------

        private void ToggleSquadFlyout(RosterGroupData group)
        {
            if (group == null || !group.IsSquad)
            {
                return;
            }

            if (flyoutSquadId == group.SquadId)
            {
                HideSquadFlyout();
                return;
            }

            flyoutSquadId = group.SquadId;
            EnsureSquadFlyout();
            SetFlyoutVisible(true);
            RefreshSquadFlyout();
        }

        private void HideSquadFlyout()
        {
            flyoutSquadId = -1;
            SetFlyoutVisible(false);
        }

        private void SetFlyoutVisible(bool visible)
        {
            if (squadFlyoutRoot != null)
            {
                squadFlyoutRoot.gameObject.SetActive(visible);
            }
        }

        private void EnsureSquadFlyout()
        {
            if (squadFlyoutRoot != null || panelRoot == null)
            {
                return;
            }

            var go = new GameObject("SquadFlyout", typeof(RectTransform), typeof(Image));
            squadFlyoutRoot = (RectTransform)go.transform;
            squadFlyoutRoot.SetParent(panelRoot, false);
            squadFlyoutRoot.anchorMin = new Vector2(0.5f, 1f);
            squadFlyoutRoot.anchorMax = new Vector2(0.5f, 1f);
            squadFlyoutRoot.pivot = new Vector2(0.5f, 0f);
            squadFlyoutRoot.sizeDelta = new Vector2(340f, 260f);
            squadFlyoutRoot.anchoredPosition = new Vector2(0f, 8f);
            squadFlyoutRoot.SetAsLastSibling();

            var background = go.GetComponent<Image>();
            background.color = new Color(0.06f, 0.07f, 0.1f, 0.97f);
            background.raycastTarget = true;

            squadFlyoutTitle = CreateFlyoutText("Title", squadFlyoutRoot, 12, TextAnchor.MiddleLeft);
            var titleRect = squadFlyoutTitle.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(10f, -30f);
            titleRect.offsetMax = new Vector2(-34f, -6f);

            var close = CreateFlyoutButton(
                "Close", squadFlyoutRoot, new Color(0.35f, 0.12f, 0.12f, 0.95f), "×", 15);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(24f, 24f);
            closeRect.anchoredPosition = new Vector2(-6f, -4f);
            close.onClick.AddListener(HideSquadFlyout);

            var content = new GameObject("Content", typeof(RectTransform));
            squadFlyoutContent = (RectTransform)content.transform;
            squadFlyoutContent.SetParent(squadFlyoutRoot, false);
            squadFlyoutContent.anchorMin = new Vector2(0f, 0f);
            squadFlyoutContent.anchorMax = new Vector2(1f, 1f);
            squadFlyoutContent.offsetMin = new Vector2(8f, 34f);
            squadFlyoutContent.offsetMax = new Vector2(-8f, -34f);

            lineFormationButton = CreateFlyoutButton(
                "Line", squadFlyoutRoot, new Color(0.2f, 0.28f, 0.36f, 0.95f), "横队", 10);
            columnFormationButton = CreateFlyoutButton(
                "Column", squadFlyoutRoot, new Color(0.2f, 0.28f, 0.36f, 0.95f), "纵队", 10);
            gridFormationButton = CreateFlyoutButton(
                "Grid", squadFlyoutRoot, new Color(0.2f, 0.28f, 0.36f, 0.95f), "方阵", 10);
            LayoutFlyoutButton(lineFormationButton, 0);
            LayoutFlyoutButton(columnFormationButton, 1);
            LayoutFlyoutButton(gridFormationButton, 2);
            lineFormationButton.onClick.AddListener(
                () => SetFlyoutFormation(FormationType.Line));
            columnFormationButton.onClick.AddListener(
                () => SetFlyoutFormation(FormationType.Column));
            gridFormationButton.onClick.AddListener(
                () => SetFlyoutFormation(FormationType.Grid));

            squadFlyoutRoot.gameObject.SetActive(false);
        }

        private void LayoutFlyoutButton(Button button, int index)
        {
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(96f, 24f);
            rect.anchoredPosition = new Vector2(10f + index * 104f, 6f);
        }

        private void SetFlyoutFormation(FormationType formation)
        {
            var group = FindAllyGroup(flyoutSquadId);
            var squad = group != null ? group.Squad : null;
            if (squad == null)
            {
                return;
            }

            squad.SetFormation(formation);
            RefreshNow();
        }

        private void RefreshSquadFlyout()
        {
            if (flyoutSquadId < 0 || squadFlyoutRoot == null)
            {
                return;
            }

            var group = FindAllyGroup(flyoutSquadId);
            if (group == null || !group.IsSquad)
            {
                HideSquadFlyout();
                return;
            }

            SetFlyoutVisible(true);
            var squad = group.Squad;
            squadFlyoutTitle.text = squad != null
                ? string.Format(
                    "{0}｜{1} 人｜{2}｜{3}",
                    squad.SquadName, group.MemberCount, squad.Formation, squad.Posture)
                : string.Format("{0}｜{1} 人", group.DisplayName, group.MemberCount);

            for (int i = 0; i < flyoutRows.Count; i++)
            {
                if (flyoutRows[i] != null)
                {
                    Destroy(flyoutRows[i]);
                }
            }

            flyoutRows.Clear();

            for (int i = 0; i < group.Members.Count; i++)
            {
                var member = group.Members[i];
                if (member == null)
                {
                    continue;
                }

                var health = member.Health;
                var agent = member.GetComponent<IUnitAgentState>();
                var row = CreateFlyoutButton(
                    "Member_" + i,
                    squadFlyoutContent,
                    new Color(0.13f, 0.15f, 0.19f, 0.92f),
                    string.Format(
                        "{0}   {1:F0}/{2:F0}   {3}",
                        member.DisplayName,
                        health != null ? health.Current : 0f,
                        health != null ? health.Max : 0f,
                        agent != null ? agent.StateName : "-"),
                    9);

                var rowRect = (RectTransform)row.transform;
                rowRect.anchorMin = new Vector2(0f, 1f);
                rowRect.anchorMax = new Vector2(1f, 1f);
                rowRect.pivot = new Vector2(0.5f, 1f);
                rowRect.sizeDelta = new Vector2(-4f, 22f);
                rowRect.anchoredPosition = new Vector2(0f, -i * 24f);

                var label = row.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.alignment = TextAnchor.MiddleLeft;
                    label.rectTransform.offsetMin = new Vector2(6f, 0f);
                }

                var captured = member;
                row.onClick.AddListener(() =>
                {
                    if (selection != null)
                    {
                        selection.Select(captured, false);
                    }
                });

                flyoutRows.Add(row.gameObject);
            }

            bool hasSquad = squad != null;
            lineFormationButton.interactable = hasSquad;
            columnFormationButton.interactable = hasSquad;
            gridFormationButton.interactable = hasSquad;

            if (hasSquad)
            {
                SetFormationButtonColor(lineFormationButton, squad.Formation == FormationType.Line);
                SetFormationButtonColor(columnFormationButton, squad.Formation == FormationType.Column);
                SetFormationButtonColor(gridFormationButton, squad.Formation == FormationType.Grid);
            }
        }

        private static void SetFormationButtonColor(Button button, bool current)
        {
            var image = button != null ? button.GetComponent<Image>() : null;
            if (image != null)
            {
                image.color = current
                    ? new Color(0.25f, 0.55f, 0.35f, 0.97f)
                    : new Color(0.2f, 0.28f, 0.36f, 0.95f);
            }
        }

        private RosterGroupData FindAllyGroup(int squadId)
        {
            var groups = model.AllyGroups;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].IsSquad && groups[i].SquadId == squadId)
                {
                    return groups[i];
                }
            }

            return null;
        }

        private static Text CreateFlyoutText(
            string name, Transform parent, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = UIFontProvider.Get();
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateFlyoutButton(
            string name, Transform parent, Color color, string label, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            var text = CreateFlyoutText("Label", rect, fontSize, TextAnchor.MiddleCenter);
            text.text = label;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(4f, 0f);
            text.rectTransform.offsetMax = new Vector2(-4f, 0f);

            return button;
        }

        private void OnSelectionChanged(IReadOnlyList<ISelectable> current)
        {
            SyncCardSelection();
        }

        private void SyncCardSelection()
        {
            var selected = selection != null ? selection.Selected : null;

            foreach (var pair in unitCardPools)
            {
                var pool = pair.Value;
                for (int i = 0; i < pool.Count; i++)
                {
                    var card = pool[i];
                    if (card == null || card.Bound == null)
                    {
                        continue;
                    }

                    card.SetSelected(ContainsSelected(selected, card.Bound));
                }
            }

            foreach (var pair in squadCardPools)
            {
                var pool = pair.Value;
                for (int i = 0; i < pool.Count; i++)
                {
                    var card = pool[i];
                    if (card == null || card.Bound == null)
                    {
                        continue;
                    }

                    card.SetSelected(SelectionStateFor(card.Bound, selected));
                }
            }

            if (fullListContent == null)
            {
                return;
            }

            var fullCards = fullListContent.GetComponentsInChildren<UnitCardView>(true);
            for (int i = 0; i < fullCards.Length; i++)
            {
                var card = fullCards[i];
                if (card != null && card.Bound != null)
                {
                    card.SetSelected(ContainsSelected(selected, card.Bound));
                }
            }

            var fullSquadCards = fullListContent.GetComponentsInChildren<SquadCardView>(true);
            for (int i = 0; i < fullSquadCards.Length; i++)
            {
                var card = fullSquadCards[i];
                if (card != null && card.Bound != null)
                {
                    card.SetSelected(SelectionStateFor(card.Bound, selected));
                }
            }
        }

        private static RosterSelectionState SelectionStateFor(
            RosterGroupData group, IReadOnlyList<ISelectable> selected)
        {
            if (group == null || group.Members.Count == 0 || selected == null)
            {
                return RosterSelectionState.None;
            }

            int count = 0;
            for (int i = 0; i < group.Members.Count; i++)
            {
                if (ContainsSelected(selected, group.Members[i]))
                {
                    count++;
                }
            }

            if (count <= 0)
            {
                return RosterSelectionState.None;
            }

            return count >= group.Members.Count
                ? RosterSelectionState.All
                : RosterSelectionState.Partial;
        }

        private static bool ContainsSelected(IReadOnlyList<ISelectable> list, ISelectable target)
        {
            if (list == null)
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], target))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>把 +X 角标提到所在网格的最上层，避免被卡片遮挡。</summary>
        private void RaiseOverflowBadges()
        {
            if (enemyOverflowButton != null)
            {
                enemyOverflowButton.transform.SetAsLastSibling();
            }

            if (allyOverflowButton != null)
            {
                allyOverflowButton.transform.SetAsLastSibling();
            }
        }

        private static void UpdateOverflowBadge(Button button, Text label, RosterSectionData data)
        {
            UpdateOverflowBadge(
                button, label,
                data.All == null ? 0 : data.All.Count,
                data.VisibleCapacity);
        }

        private static void UpdateOverflowBadge(
            Button button, Text label, int total, int capacity)
        {
            bool hasOverflow = RosterCounter.HasOverflow(total, capacity);
            int overflow = RosterCounter.OverflowCount(total, capacity);

            if (button != null)
            {
                button.gameObject.SetActive(hasOverflow);
            }

            if (label != null)
            {
                label.text = hasOverflow ? "+" + overflow : string.Empty;
            }
        }

        private void OpenFullList(RosterSection section)
        {
            fullListOpen = true;
            SetFullListVisible(true);
            RefreshFullList();
        }

        private void CloseFullList()
        {
            if (!fullListOpen)
            {
                return;
            }

            fullListOpen = false;
            SetFullListVisible(false);
        }

        private void SetFullListVisible(bool visible)
        {
            if (fullListRoot != null)
            {
                fullListRoot.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 完整列表把两栏所有单位铺开（不截断）。列表不做虚拟化，
        /// 对照草图的规模（几十个单位）足够；规模再大需要换成 ScrollRect + 对象池。
        /// </summary>
        private void RefreshFullList()
        {
            if (fullListContent == null)
            {
                return;
            }

            for (int i = fullListContent.childCount - 1; i >= 0; i--)
            {
                Destroy(fullListContent.GetChild(i).gameObject);
            }

            int index = 0;
            AddFullListUnitSection(model.EnemySection, ref index);
            AddFullListAllyGroups(ref index);
        }

        private void AddFullListUnitSection(RosterSectionData data, ref int index)
        {
            for (int i = 0; i < data.All.Count; i++)
            {
                var go = new GameObject(
                    "FullCard_" + index,
                    typeof(RectTransform),
                    typeof(UnitCardView));

                LayoutFullListCard((RectTransform)go.GetComponent<RectTransform>(), index);

                var card = go.GetComponent<UnitCardView>();
                card.Build();
                card.Clicked += OnCardClicked;
                card.Bind(data.All[i] as UnitSelectable);
                card.SetSelected(ContainsSelected(selection != null ? selection.Selected : null, card.Bound));

                index++;
            }
        }

        private void AddFullListAllyGroups(ref int index)
        {
            var groups = model.AllyGroups;
            var selected = selection != null ? selection.Selected : null;

            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group.IsSquad)
                {
                    var go = new GameObject(
                        "FullSquadCard_" + index,
                        typeof(RectTransform),
                        typeof(SquadCardView));
                    LayoutFullListCard((RectTransform)go.GetComponent<RectTransform>(), index);

                    var card = go.GetComponent<SquadCardView>();
                    card.Build();
                    card.Clicked += OnFullListSquadClicked;
                    card.PostureClicked += OnSquadPostureClicked;
                    card.Bind(group);
                    card.SetSelected(SelectionStateFor(group, selected));
                }
                else
                {
                    var unit = group.SingleUnit;
                    if (unit == null)
                    {
                        continue;
                    }

                    var go = new GameObject(
                        "FullCard_" + index,
                        typeof(RectTransform),
                        typeof(UnitCardView));
                    LayoutFullListCard((RectTransform)go.GetComponent<RectTransform>(), index);

                    var card = go.GetComponent<UnitCardView>();
                    card.Build();
                    card.Clicked += OnCardClicked;
                    card.Bind(unit);
                    card.SetSelected(ContainsSelected(selected, unit));
                }

                index++;
            }
        }

        private void OnFullListSquadClicked(RosterGroupData group)
        {
            if (selection == null || group == null)
            {
                return;
            }

            var members = new List<ISelectable>();
            for (int i = 0; i < group.Members.Count; i++)
            {
                if (group.Members[i] != null)
                {
                    members.Add(group.Members[i]);
                }
            }

            selection.SelectMany(members, false);
            CloseFullList();
        }

        private void LayoutFullListCard(RectTransform rect, int index)
        {
            rect.SetParent(fullListContent, false);
            rect.sizeDelta = new Vector2(RosterLayout.CardWidth, RosterLayout.CardHeight);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);

            const int fullListColumns = 8;
            int column = index % fullListColumns;
            int row = index / fullListColumns;
            rect.anchoredPosition = new Vector2(
                column * (RosterLayout.CardWidth + RosterLayout.CardSpacing),
                -row * (RosterLayout.CardHeight + RosterLayout.CardSpacing));
        }

        private void OnTweenProgress(float progress)
        {
            // 收回时整体下移一个面板高度，展开时回到原位。
            float offset = Mathf.Lerp(collapsedOffset, 0f, progress);

            if (panelRoot != null)
            {
                panelRoot.anchoredPosition = expandedPosition + new Vector2(0f, offset);
            }

            if (toggleHandle != null)
            {
                // 把手跟着面板顶边走：收回后悬浮在屏幕底边，点它即可展开。
                toggleHandle.anchoredPosition = handleExpandedPosition + new Vector2(0f, offset);
            }
        }
    }
}
