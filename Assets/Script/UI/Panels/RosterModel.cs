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
    /// 面板上的一个展示分组：完整小队合成一张队伍卡，未编队单位各自一张单卡。
    /// 只保存视图需要的数据；选中/血量在每次 Rebuild 时刷新。
    /// </summary>
    public class RosterGroupData
    {
        public bool IsSquad;
        public int SquadId = -1;
        public string DisplayName;
        public UnitTeam Team;
        public Squad Squad;
        public int SelectedCount;
        public float HealthCurrent;
        public float HealthMax;
        public readonly List<UnitSelectable> Members = new List<UnitSelectable>();

        public int MemberCount => Members.Count;

        public RosterSelectionState Selection
        {
            get
            {
                if (SelectedCount <= 0)
                {
                    return RosterSelectionState.None;
                }

                return SelectedCount >= MemberCount
                    ? RosterSelectionState.All
                    : RosterSelectionState.Partial;
            }
        }

        public UnitSelectable SingleUnit => Members.Count == 1 ? Members[0] : null;
    }

    /// <summary>
    /// 部队预览面板的纯数据模型：把当前世界的可选单位分成敌人 / 友军两栏，
    /// 友军按小队合成分组；排序并计算每栏的溢出数量。
    /// </summary>
    public class RosterModel
    {
        private readonly List<ISelectable> enemies = new List<ISelectable>();
        private readonly List<ISelectable> allies = new List<ISelectable>();
        private readonly List<RosterGroupData> enemyGroups = new List<RosterGroupData>();
        private readonly List<RosterGroupData> allyGroups = new List<RosterGroupData>();

        /// <summary>每栏最多显示的卡片数。</summary>
        public int VisibleCapacity { get; set; } = 6;

        /// <summary>敌人栏的全部单位（已排序）。</summary>
        public IReadOnlyList<ISelectable> Enemies => enemies;

        /// <summary>友军栏的全部单位（已排序）。</summary>
        public IReadOnlyList<ISelectable> Allies => allies;

        /// <summary>敌人栏分组（当前全是单卡）。</summary>
        public IReadOnlyList<RosterGroupData> EnemyGroups => enemyGroups;

        /// <summary>友军栏分组（小队合成一个兵牌，未编队单位各一张单卡）。</summary>
        public IReadOnlyList<RosterGroupData> AllyGroups => allyGroups;

        /// <summary>敌人栏快照。</summary>
        public RosterSectionData EnemySection => new RosterSectionData(enemies, VisibleCapacity);

        /// <summary>友军栏快照。</summary>
        public RosterSectionData AllySection => new RosterSectionData(allies, VisibleCapacity);

        /// <summary>
        /// 用一批单位重建模型。排序规则：先按显示名，再按实例 ID，
        /// 保证同一批单位的卡片顺序稳定，不会因注册表顺序抖动而跳动。
        /// </summary>
        public void Rebuild(
            IReadOnlyList<ISelectable> source,
            IReadOnlyList<ISelectable> selected = null)
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
            RebuildGroups(selected);
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

        /// <summary>取指定分栏的分组列表。</summary>
        public IReadOnlyList<RosterGroupData> GetGroups(RosterSection section)
        {
            return section == RosterSection.Enemy ? enemyGroups : allyGroups;
        }

        /// <summary>友军栏按组计算的可见/溢出数量（+X 角标按队伍数而不是单位数）。</summary>
        public int AllyVisibleCount => RosterCounter.VisibleCount(allyGroups.Count, VisibleCapacity);

        public int AllyOverflowCount => RosterCounter.OverflowCount(allyGroups.Count, VisibleCapacity);

        public bool AllyHasOverflow => RosterCounter.HasOverflow(allyGroups.Count, VisibleCapacity);

        private void RebuildGroups(IReadOnlyList<ISelectable> selected)
        {
            enemyGroups.Clear();
            allyGroups.Clear();

            for (int i = 0; i < enemies.Count; i++)
            {
                var group = BuildSingleton(enemies[i], selected);
                if (group != null)
                {
                    enemyGroups.Add(group);
                }
            }

            var byInstanceId = new Dictionary<int, UnitSelectable>();
            var snapshots = new List<RosterUnitSnapshot>(allies.Count);

            for (int i = 0; i < allies.Count; i++)
            {
                var item = allies[i];
                var unit = item as UnitSelectable;
                int instanceId = item.Transform != null ? item.Transform.GetInstanceID() : 0;
                if (unit != null)
                {
                    byInstanceId[instanceId] = unit;
                }

                float current = 0f;
                float max = 0f;
                var health = unit != null ? unit.Health : null;
                if (health != null)
                {
                    current = health.Current;
                    max = health.Max;
                }

                snapshots.Add(new RosterUnitSnapshot(
                    instanceId,
                    unit != null ? unit.SquadId : -1,
                    item.DisplayName,
                    (int)item.Team,
                    ContainsSelected(selected, item),
                    current,
                    max));
            }

            var infos = SquadRosterGrouping.Build(snapshots, groupSquads: true);
            for (int i = 0; i < infos.Count; i++)
            {
                var info = infos[i];
                var group = new RosterGroupData
                {
                    IsSquad = info.IsSquad,
                    SquadId = info.SquadId,
                    DisplayName = info.DisplayName,
                    Team = (UnitTeam)info.Team,
                    SelectedCount = info.SelectedCount,
                    HealthCurrent = info.Health,
                    HealthMax = info.MaxHealth
                };

                if (group.IsSquad && group.SquadId > 0)
                {
                    group.Squad = SquadRegistry.Find(group.SquadId);
                    if (group.Squad != null)
                    {
                        group.DisplayName = group.Squad.SquadName;
                    }
                }

                for (int m = 0; m < info.MemberIds.Count; m++)
                {
                    if (byInstanceId.TryGetValue(info.MemberIds[m], out var member))
                    {
                        group.Members.Add(member);
                    }
                }

                allyGroups.Add(group);
            }
        }

        private static RosterGroupData BuildSingleton(
            ISelectable item, IReadOnlyList<ISelectable> selected)
        {
            var unit = item as UnitSelectable;
            if (unit == null)
            {
                return null;
            }

            var health = unit.Health;
            var group = new RosterGroupData
            {
                IsSquad = false,
                SquadId = unit.SquadId,
                DisplayName = item.DisplayName,
                Team = item.Team,
                SelectedCount = ContainsSelected(selected, item) ? 1 : 0,
                HealthCurrent = health != null ? health.Current : 0f,
                HealthMax = health != null ? health.Max : 0f
            };
            group.Members.Add(unit);
            return group;
        }

        private static bool ContainsSelected(IReadOnlyList<ISelectable> selected, ISelectable item)
        {
            if (selected == null)
            {
                return false;
            }

            for (int i = 0; i < selected.Count; i++)
            {
                if (ReferenceEquals(selected[i], item))
                {
                    return true;
                }
            }

            return false;
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
