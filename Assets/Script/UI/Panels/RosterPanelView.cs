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

        // 每个网格各自持有一份卡片池。曾经两栏共用同一个 List，结果
        // RebuildGrid(allyGrid) 复用了挂在 enemyGrid 下的卡片 —— 友军卡片
        // 显示在敌人栏里、友军栏空白。池必须按父网格隔离。
        private readonly Dictionary<RectTransform, List<UnitCardView>> cardPools =
            new Dictionary<RectTransform, List<UnitCardView>>();

        private bool isExpanded;
        private bool fullListOpen;
        private bool pendingRefresh;
        private float refreshTimer;
        private Vector2 expandedPosition;
        private Vector2 handleExpandedPosition;

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
            model.Rebuild(SelectionService.GatherCandidates());

            RebuildGrid(enemyGrid, model.EnemySection);
            RebuildGrid(allyGrid, model.AllySection);

            UpdateOverflowBadge(enemyOverflowButton, enemyOverflowLabel, model.EnemySection);
            UpdateOverflowBadge(allyOverflowButton, allyOverflowLabel, model.AllySection);

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

            if (fullListOpen)
            {
                RefreshFullList();
            }
        }

        private void RebuildGrid(RectTransform grid, RosterSectionData data)
        {
            if (grid == null)
            {
                return;
            }

            int visible = data.VisibleCount;
            var pool = EnsureCardPool(grid, visible);

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

                card.Bind(data.All[i] as UnitSelectable);
                LayoutCard(card, i);
            }
        }

        /// <summary>取得（必要时创建）指定网格专属的卡片池。</summary>
        private List<UnitCardView> EnsureCardPool(RectTransform grid, int needed)
        {
            if (!cardPools.TryGetValue(grid, out var pool))
            {
                pool = new List<UnitCardView>();
                cardPools.Add(grid, pool);
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

        private static void LayoutCard(UnitCardView card, int index)
        {
            var rect = (RectTransform)card.transform;
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
        }

        private void OnSelectionChanged(IReadOnlyList<ISelectable> current)
        {
            SyncCardSelection();
        }

        private void SyncCardSelection()
        {
            var selected = selection != null ? selection.Selected : null;

            foreach (var pair in cardPools)
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

            if (fullListContent != null)
            {
                var fullCards = fullListContent.GetComponentsInChildren<UnitCardView>(true);
                for (int i = 0; i < fullCards.Length; i++)
                {
                    var card = fullCards[i];
                    if (card != null && card.Bound != null)
                    {
                        card.SetSelected(ContainsSelected(selected, card.Bound));
                    }
                }
            }
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
            if (button != null)
            {
                button.gameObject.SetActive(data.HasOverflow);
            }

            if (label != null)
            {
                label.text = data.HasOverflow ? "+" + data.OverflowCount : string.Empty;
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
            AddFullListSection(model.EnemySection, ref index);
            AddFullListSection(model.AllySection, ref index);
        }

        private void AddFullListSection(RosterSectionData data, ref int index)
        {
            for (int i = 0; i < data.All.Count; i++)
            {
                var go = new GameObject(
                    "FullCard_" + index,
                    typeof(RectTransform),
                    typeof(UnitCardView));

                var rect = go.GetComponent<RectTransform>();
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

                var card = go.GetComponent<UnitCardView>();
                card.Build();
                card.Clicked += OnCardClicked;
                card.Bind(data.All[i] as UnitSelectable);
                card.SetSelected(ContainsSelected(selection != null ? selection.Selected : null, card.Bound));

                index++;
            }
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
