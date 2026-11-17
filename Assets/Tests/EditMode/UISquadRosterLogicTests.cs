using System.Collections.Generic;
using NUnit.Framework;
using Vindicator.UI;

/// <summary>兵牌分组纯逻辑测试（Vindicator.UI.Logic）。</summary>
public class UISquadRosterLogicTests
{
    [Test]
    public void GroupsMembersBySquad_AndKeepsSingletons()
    {
        var source = new List<RosterUnitSnapshot>
        {
            Snapshot(1, 10, "A", 0, true),
            Snapshot(2, 20, "B", 0, false),
            Snapshot(3, -1, "C", 0, false),
            Snapshot(4, 10, "D", 0, false)
        };

        var groups = SquadRosterGrouping.Build(source, true);

        Assert.AreEqual(3, groups.Count);
        Assert.IsTrue(groups[0].IsSquad);
        Assert.AreEqual(10, groups[0].SquadId);
        Assert.AreEqual(2, groups[0].MemberCount);
        CollectionAssert.AreEqual(new[] { 1, 4 }, groups[0].MemberIds);

        Assert.AreEqual(20, groups[1].SquadId);
        Assert.AreEqual(1, groups[1].MemberCount);

        Assert.IsFalse(groups[2].IsSquad);
        Assert.AreEqual("C", groups[2].DisplayName);
    }

    [Test]
    public void GroupingDisabled_KeepsEveryUnitSingleton()
    {
        var source = new List<RosterUnitSnapshot>
        {
            Snapshot(1, 10, "A", 0, false),
            Snapshot(2, 10, "B", 0, false)
        };

        var groups = SquadRosterGrouping.Build(source, false);

        Assert.AreEqual(2, groups.Count);
        Assert.IsFalse(groups[0].IsSquad);
        Assert.IsFalse(groups[1].IsSquad);
    }

    [Test]
    public void SelectionAggregate_AllPartialNone()
    {
        var source = new List<RosterUnitSnapshot>
        {
            Snapshot(1, 10, "A", 0, true),
            Snapshot(2, 10, "B", 0, true),
            Snapshot(3, 10, "C", 0, false)
        };

        var groups = SquadRosterGrouping.Build(source, true);

        Assert.AreEqual(RosterSelectionState.Partial, groups[0].Selection);

        var none = SquadRosterGrouping.Build(
            new List<RosterUnitSnapshot>
            {
                Snapshot(1, 10, "A", 0, false),
                Snapshot(2, 10, "B", 0, false)
            }, true);
        Assert.AreEqual(RosterSelectionState.None, none[0].Selection);

        var all = SquadRosterGrouping.Build(
            new List<RosterUnitSnapshot>
            {
                Snapshot(1, 10, "A", 0, true),
                Snapshot(2, 10, "B", 0, true)
            }, true);
        Assert.AreEqual(RosterSelectionState.All, all[0].Selection);
    }

    [Test]
    public void GroupCountsDriveOverflow()
    {
        Assert.AreEqual(6, SquadRosterGrouping.VisibleCount(7, 6));
        Assert.AreEqual(1, SquadRosterGrouping.OverflowCount(7, 6));
        Assert.AreEqual(6, SquadRosterGrouping.VisibleCount(6, 6));
        Assert.AreEqual(0, SquadRosterGrouping.OverflowCount(6, 6));
        Assert.AreEqual(0, SquadRosterGrouping.VisibleCount(0, 6));
    }

    [Test]
    public void AggregateHealthSumsMembers()
    {
        var source = new List<RosterUnitSnapshot>
        {
            new RosterUnitSnapshot(1, 10, "A", 0, false, 40f, 50f),
            new RosterUnitSnapshot(2, 10, "B", 0, false, 30f, 50f)
        };

        var groups = SquadRosterGrouping.Build(source, true);

        Assert.AreEqual(70f, groups[0].Health, 0.001f);
        Assert.AreEqual(100f, groups[0].MaxHealth, 0.001f);
    }

    [Test]
    public void NullOrEmptyInput_ReturnsEmptyList()
    {
        Assert.AreEqual(0, SquadRosterGrouping.Build(null, true).Count);
        Assert.AreEqual(0, SquadRosterGrouping.Build(new List<RosterUnitSnapshot>(), true).Count);
    }

    private static RosterUnitSnapshot Snapshot(
        int id, int squadId, string name, int team, bool selected)
    {
        return new RosterUnitSnapshot(id, squadId, name, team, selected, 50f, 50f);
    }
}
