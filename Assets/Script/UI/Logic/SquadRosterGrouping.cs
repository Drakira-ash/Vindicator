using System.Collections.Generic;

namespace Vindicator.UI
{
    /// <summary>面板上单位快照。只含 UI 需要的原始数据，不依赖任何项目类型。</summary>
    public readonly struct RosterUnitSnapshot
    {
        public readonly int InstanceId;
        public readonly int SquadId;
        public readonly string DisplayName;
        public readonly int Team;
        public readonly bool Selected;
        public readonly float Health;
        public readonly float MaxHealth;

        public RosterUnitSnapshot(
            int instanceId,
            int squadId,
            string displayName,
            int team,
            bool selected,
            float health,
            float maxHealth)
        {
            InstanceId = instanceId;
            SquadId = squadId;
            DisplayName = displayName;
            Team = team;
            Selected = selected;
            Health = health;
            MaxHealth = maxHealth;
        }

        public bool IsSquadMember => SquadId > 0;
    }

    /// <summary>队伍卡/单卡的选中聚合状态。</summary>
    public enum RosterSelectionState
    {
        None,
        Partial,
        All
    }

    /// <summary>一个面板条目：完整小队合成一组，未编队单位各自一组。</summary>
    public class RosterGroupInfo
    {
        public bool IsSquad;
        public int SquadId = -1;
        public string DisplayName;
        public int Team;
        public readonly List<int> MemberIds = new List<int>();
        public int SelectedCount;
        public float Health;
        public float MaxHealth;

        public int MemberCount => MemberIds.Count;

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
    }

    /// <summary>
    /// 面板分组规则：SquadId &gt; 0 的单位收进同一张队伍卡；其余各自成组。
    /// 输入顺序即输出顺序（第一组成员决定队伍卡位置），成员顺序保持输入顺序。
    /// 纯计算，属于 Vindicator.UI.Logic，可被 EditMode 直接测试。
    /// </summary>
    public static class SquadRosterGrouping
    {
        public static List<RosterGroupInfo> Build(
            IReadOnlyList<RosterUnitSnapshot> source, bool groupSquads)
        {
            var groups = new List<RosterGroupInfo>();
            if (source == null)
            {
                return groups;
            }

            var squadIndex = new Dictionary<int, RosterGroupInfo>();

            for (int i = 0; i < source.Count; i++)
            {
                var unit = source[i];
                if (groupSquads && unit.IsSquadMember)
                {
                    if (!squadIndex.TryGetValue(unit.SquadId, out var group))
                    {
                        group = new RosterGroupInfo
                        {
                            IsSquad = true,
                            SquadId = unit.SquadId,
                            DisplayName = string.IsNullOrEmpty(unit.DisplayName)
                                ? "小队 " + unit.SquadId
                                : "小队 " + unit.SquadId,
                            Team = unit.Team
                        };
                        squadIndex.Add(unit.SquadId, group);
                        groups.Add(group);
                    }

                    group.MemberIds.Add(unit.InstanceId);
                    if (unit.Selected)
                    {
                        group.SelectedCount++;
                    }

                    group.Health += unit.Health;
                    group.MaxHealth += unit.MaxHealth;
                }
                else
                {
                    var singleton = new RosterGroupInfo
                    {
                        IsSquad = false,
                        SquadId = unit.SquadId,
                        DisplayName = unit.DisplayName,
                        Team = unit.Team,
                        SelectedCount = unit.Selected ? 1 : 0,
                        Health = unit.Health,
                        MaxHealth = unit.MaxHealth
                    };
                    singleton.MemberIds.Add(unit.InstanceId);
                    groups.Add(singleton);
                }
            }

            return groups;
        }

        public static int VisibleCount(int totalGroups, int capacity)
        {
            return RosterCounter.VisibleCount(totalGroups, capacity);
        }

        public static int OverflowCount(int totalGroups, int capacity)
        {
            return RosterCounter.OverflowCount(totalGroups, capacity);
        }
    }
}
