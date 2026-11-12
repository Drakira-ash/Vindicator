using System.Collections.Generic;

namespace Vindicator.UI
{
    /// <summary>面板分栏。敌人栏与友军栏各自独立计数与溢出。</summary>
    public enum RosterSection
    {
        Enemy,
        Player
    }

    /// <summary>
    /// 某分栏的一次快照数据。视图层只读这个结构，不直接查单位，
    /// 便于在没有场景的情况下单测排序与溢出计数逻辑。
    /// </summary>
    public readonly struct RosterSectionData
    {
        /// <summary>该栏的全部可选单位（已排序，未截断）。</summary>
        public readonly IReadOnlyList<ISelectable> All;

        /// <summary>当前可见的卡片数量上限（草图为 3 列 × 2 行 = 6）。</summary>
        public readonly int VisibleCapacity;

        public RosterSectionData(IReadOnlyList<ISelectable> all, int visibleCapacity)
        {
            All = all;
            VisibleCapacity = visibleCapacity;
        }

        /// <summary>实际展示的单位数量。</summary>
        public int VisibleCount => RosterCounter.VisibleCount(All == null ? 0 : All.Count, VisibleCapacity);

        /// <summary>未被展示、折叠在 +X 角标里的数量。</summary>
        public int OverflowCount => RosterCounter.OverflowCount(All == null ? 0 : All.Count, VisibleCapacity);

        /// <summary>是否存在溢出（决定 +X 角标是否显示）。</summary>
        public bool HasOverflow => RosterCounter.HasOverflow(All == null ? 0 : All.Count, VisibleCapacity);
    }

    /// <summary>
    /// 部队预览面板的纯数据模型：把当前世界的可选单位分成敌人 / 友军两栏，
    /// 排序并计算每栏的溢出数量。不引用任何 UI 控件，可直接单测。
    /// </summary>
    public class RosterModel
    {
        private readonly List<ISelectable> enemies = new List<ISelectable>();
        private readonly List<ISelectable> allies = new List<ISelectable>();

        /// <summary>每栏最多显示的卡片数。</summary>
        public int VisibleCapacity { get; set; } = 6;

        /// <summary>敌人栏的全部单位（已排序）。</summary>
        public IReadOnlyList<ISelectable> Enemies => enemies;

        /// <summary>友军栏的全部单位（已排序）。</summary>
        public IReadOnlyList<ISelectable> Allies => allies;

        /// <summary>敌人栏快照。</summary>
        public RosterSectionData EnemySection => new RosterSectionData(enemies, VisibleCapacity);

        /// <summary>友军栏快照。</summary>
        public RosterSectionData AllySection => new RosterSectionData(allies, VisibleCapacity);

        /// <summary>
        /// 用一批单位重建模型。排序规则：先按类型名，再按显示名，
        /// 保证同一批单位的卡片顺序稳定，不会因注册表顺序抖动而跳动。
        /// </summary>
        public void Rebuild(System.Collections.Generic.IReadOnlyList<ISelectable> source)
        {
            enemies.Clear();
            allies.Clear();

            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    var item = source[i];
                    if (item == null || !item.IsSelectable)
                    {
                        continue;
                    }

                    if (item.Team == UnitTeam.Enemy)
                    {
                        enemies.Add(item);
                    }
                    else if (item.Team == UnitTeam.Player)
                    {
                        allies.Add(item);
                    }
                    // Neutral 不进面板：草稿只定义了敌人 / 友军两栏。
                }
            }

            SortStable(enemies);
            SortStable(allies);
        }

        /// <summary>取指定分栏的完整列表。</summary>
        public IReadOnlyList<ISelectable> GetSection(RosterSection section)
        {
            return section == RosterSection.Enemy ? enemies : allies;
        }

        /// <summary>取指定分栏的快照。</summary>
        public RosterSectionData GetSectionData(RosterSection section)
        {
            return section == RosterSection.Enemy ? EnemySection : AllySection;
        }

        private static void SortStable(List<ISelectable> list)
        {
            list.Sort(Compare);
        }

        private static int Compare(ISelectable a, ISelectable b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a == null)
            {
                return 1;
            }

            if (b == null)
            {
                return -1;
            }

            // 只按显示名排序。用 Transform.name 做次级键会在同一批预制体实例
            // （名字带 _0/_1 后缀）之间产生不稳定的顺序，反而让卡片跳动。
            int byName = string.CompareOrdinal(a.DisplayName ?? string.Empty, b.DisplayName ?? string.Empty);
            if (byName != 0)
            {
                return byName;
            }

            // 名称完全相同时用实例 ID 兜底，保证排序结果确定（不依赖注册表顺序）。
            int aId = a.Transform != null ? a.Transform.GetInstanceID() : 0;
            int bId = b.Transform != null ? b.Transform.GetInstanceID() : 0;
            return aId.CompareTo(bId);
        }
    }
}
