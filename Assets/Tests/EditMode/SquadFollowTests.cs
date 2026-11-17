using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 小队锚点跟随（P0 稳定槽位 + P1 跟随移动）的 EditMode 测试。
/// 不使用 NavMesh：没有烘焙数据时 Squad.Advance 会回退直线，正好覆盖回退路径。
/// </summary>
public class SquadFollowTests
{
    private const float Tolerance = 0.05f;

    private readonly List<GameObject> created = new List<GameObject>();
    private readonly List<UnitHealth> healths = new List<UnitHealth>();

    [SetUp]
    public void SetUp()
    {
        SquadRegistry.Clear();
        UnitSelection.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        UnitSelection.Clear();
        SquadRegistry.Clear();
        for (int i = 0; i < healths.Count; i++)
        {
            UnitRegistry.Unregister(healths[i]);
        }

        healths.Clear();
        for (int i = 0; i < created.Count; i++)
        {
            if (created[i] != null)
            {
                Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();
    }

    [Test]
    public void MoveTo_StartsAtCentroid_AndWaitsForFormation()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(4f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        squad.MoveTo(new Vector3(20f, 0f, 0f), Vector3.forward, false);

        Assert.AreEqual(2f, squad.Anchor.x, 0.01f, "锚点应从当前质心出发");
        Assert.IsTrue(squad.WaitingForFormation, "刚下达命令时应先等队形");

        squad.Advance(0.5f);
        Assert.AreEqual(2f, squad.Anchor.x, 0.01f, "队形散乱时锚点不应出发");

        SyncMembersToDestinations(squad);
        squad.Advance(0.1f);

        Assert.IsFalse(squad.WaitingForFormation);
        Assert.Greater(squad.Anchor.x, 2f, "队形成型后锚点应开始移动");
    }

    [Test]
    public void Advance_MovesAnchorAtSlowestMemberSpeedTimesFactor()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero, 4f);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(2f, 0f, 0f), 2f);
        var squad = SquadRegistry.Create(new[] { a, b });

        squad.MoveTo(new Vector3(20f, 0f, 0f), Vector3.forward, false);
        SyncMembersToDestinations(squad);

        float start = squad.Anchor.x;
        squad.Advance(1f);

        // 最慢成员 2 m/s × 0.9 = 1.8 m/s。
        Assert.AreEqual(start + 1.8f, squad.Anchor.x, 0.06f);
    }

    [Test]
    public void Advance_ReissuesSlots_KeepingSpacing()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        squad.MoveTo(new Vector3(20f, 0f, 0f), Vector3.forward, false);
        SyncMembersToDestinations(squad);
        squad.Advance(0.5f);

        var ca = a.GetComponent<UnitCommander>();
        var cb = b.GetComponent<UnitCommander>();

        Assert.AreEqual(UnitOrderType.Move, ca.OrderType);
        Assert.AreEqual(UnitOrderType.Move, cb.OrderType);
        Assert.IsTrue(ca.FollowOrder && cb.FollowOrder, "跟随移动必须保留命令");
        Assert.AreEqual(FormationLayout.DefaultSpacing,
            Vector3.Distance(ca.Destination, cb.Destination), 0.05f,
            "两列槽位间距应保持 spacing");

        Vector3 center = (ca.Destination + cb.Destination) * 0.5f;
        Assert.AreEqual(squad.Anchor.x, center.x, 0.05f, "槽位中心应跟随锚点");
        Assert.AreEqual(squad.Anchor.z, center.z, 0.05f);
    }

    [Test]
    public void Advance_SlowsAndStops_WhenFormationStretches()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        squad.MoveTo(new Vector3(20f, 0f, 0f), Vector3.forward, false);
        SyncMembersToDestinations(squad);
        squad.Advance(0.2f);
        float before = squad.Anchor.x;

        // 让一个成员严重落后，锚点应停车等待。
        a.transform.position = new Vector3(squad.Anchor.x - 10f, 0f, 0f);
        squad.Advance(0.2f);
        Assert.AreEqual(before, squad.Anchor.x, 0.01f, "队形误差超过停车阈值时锚点应停下");

        // 成员追上后恢复移动。
        SyncMembersToDestinations(squad);
        squad.Advance(0.2f);
        Assert.Greater(squad.Anchor.x, before, "成员追上后锚点应恢复移动");
    }

    [Test]
    public void Advance_ReachesTarget_WaitsForMembers_ThenCompletes()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        squad.MoveTo(new Vector3(10f, 0f, 0f), Vector3.forward, false);

        for (int step = 0; step < 2000 && squad.MoveOrderActive; step++)
        {
            squad.Tick();
            squad.Advance(0.05f);
            SyncMembersToDestinations(squad);
        }

        Assert.IsFalse(squad.MoveOrderActive);
        Assert.AreEqual(SquadState.Idle, squad.State);
        Assert.AreEqual(10f, squad.Anchor.x, 0.15f, "锚点应到达落点");
        Assert.AreEqual(UnitOrderType.None, squad.Members[0].OrderType, "完成后应清空命令");
        Assert.Greater(Vector3.Dot(squad.Facing, Vector3.forward), 0.999f,
            "完成后小队朝向最终朝向");
        Assert.Greater(Vector3.Dot(a.transform.forward, Vector3.forward), 0.999f,
            "成员也应转向最终朝向");
    }

    [Test]
    public void Advance_TimesOutWhenMembersCannotReachSlots()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(4f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        // 目标就是当前质心：锚点原地不动，只有成员落位条件不满足。
        squad.MoveTo(new Vector3(2f, 0f, 0f), Vector3.forward, false);
        for (int step = 0; step < 100 && squad.MoveOrderActive; step++)
        {
            squad.Advance(0.1f);
        }

        Assert.IsFalse(
            squad.MoveOrderActive,
            "成员够不到槽位时应超时结束，而不是永久停在 Moving");
        Assert.AreEqual(SquadState.Idle, squad.State);
    }

    [Test]
    public void CompleteMoveOrder_FacesMembersAtFinalFacing()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var squad = SquadRegistry.Create(new[] { a });

        squad.MoveTo(new Vector3(10f, 0f, 0f), Vector3.back, false);
        squad.CompleteMoveOrder();

        Assert.Greater(Vector3.Dot(a.transform.forward, Vector3.back), 0.999f);
    }

    [Test]
    public void MemberDeathMidFollow_PreservesSurvivorSlotIndices()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(2f, 0f, 0f));
        var c = CreateUnit("C", UnitTeam.Player, new Vector3(4f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b, c });

        squad.MoveTo(new Vector3(20f, 0f, 0f), Vector3.forward, false);
        SyncMembersToDestinations(squad);
        squad.Advance(0.2f);

        var ca = a.GetComponent<UnitCommander>();
        var cb = b.GetComponent<UnitCommander>();
        var cc = c.GetComponent<UnitCommander>();
        int indexA = ca.SlotIndex;
        int indexB = cb.SlotIndex;
        int indexC = cc.SlotIndex;

        // 杀掉一个中间下标成员，验证只有必要的幸存者补位。
        UnitCommander victim = indexA == 1 ? ca : (indexB == 1 ? cb : cc);
        var victimHealth = victim.GetComponent<UnitHealth>();
        SetField(victimHealth, "destroyOnDeath", false);
        victimHealth.TakeDamage(999f);

        squad.Tick();
        squad.Advance(0.05f);

        var survivors = new List<UnitCommander>();
        if (victim != ca) survivors.Add(ca);
        if (victim != cb) survivors.Add(cb);
        if (victim != cc) survivors.Add(cc);

        Assert.AreEqual(2, survivors.Count);
        Assert.AreNotEqual(survivors[0].SlotIndex, survivors[1].SlotIndex, "槽位不能重复");
        Assert.IsTrue(
            survivors[0].SlotIndex == 0 || survivors[1].SlotIndex == 0,
            "空出来的最低槽位应被补上");

        // 原本下标为 0 的成员不应换位。
        var zeroMember = indexA == 0 ? ca : (indexB == 0 ? cb : cc);
        if (zeroMember != victim)
        {
            Assert.AreEqual(0, zeroMember.SlotIndex, "下标 0 的幸存者应保持原位");
        }
    }

    [Test]
    public void SetFormationMidMove_ReissuesWithoutLosingOrder()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        squad.MoveTo(new Vector3(20f, 0f, 0f), Vector3.forward, false);
        SyncMembersToDestinations(squad);
        squad.Advance(0.2f);

        squad.SetFormation(FormationType.Column);

        var ca = a.GetComponent<UnitCommander>();
        var cb = b.GetComponent<UnitCommander>();
        Assert.AreEqual(UnitOrderType.Move, ca.OrderType);
        Assert.AreEqual(UnitOrderType.Move, cb.OrderType);
        Assert.IsTrue(ca.FollowOrder && cb.FollowOrder);
        Assert.AreEqual(2, squad.Slots.Count);

        squad.Advance(0.1f);
        Assert.AreEqual(UnitOrderType.Move, ca.OrderType, "换阵型不应丢失跟随命令");
    }

    [Test]
    public void MoveDestinationFlag_ScaleCurve_AppearsThenFades()
    {
        Assert.AreEqual(0.2f, MoveDestinationFlag.ScaleAt(0f), 0.001f);
        Assert.Greater(MoveDestinationFlag.ScaleAt(0.1f), 0.5f);
        Assert.AreEqual(1f, MoveDestinationFlag.ScaleAt(0.5f), 0.001f);
        Assert.AreEqual(0f, MoveDestinationFlag.ScaleAt(MoveDestinationFlag.Lifetime), 0.001f);
    }

    [Test]
    public void Advance_WithoutNavMesh_FallsBackToStraightLine()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var squad = SquadRegistry.Create(new[] { a });

        squad.MoveTo(new Vector3(20f, 0f, 0f), Vector3.forward, false);
        SyncMembersToDestinations(squad);
        squad.Advance(0.5f);

        Assert.Greater(squad.Anchor.x, 0f);
        Assert.AreEqual(0f, squad.Anchor.z, 0.001f, "无 NavMesh 时应沿直线回退");
    }

    private static void SyncMembersToDestinations(Squad squad)
    {
        for (int i = 0; i < squad.Members.Count; i++)
        {
            var member = squad.Members[i];
            if (member != null && member.OrderType == UnitOrderType.Move)
            {
                member.transform.position = member.Destination;
            }
        }
    }

    private Unit CreateUnit(string name, UnitTeam team, Vector3 position, float speed = 4f)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        created.Add(go);

        var health = go.AddComponent<UnitHealth>();
        health.Initialize(100f, team);
        healths.Add(health);
        UnitRegistry.Register(health);

        var unit = go.AddComponent<Unit>();
        Invoke(unit, "Awake");
        unit.Stats.SetBase(StatType.MoveSpeed, speed);
        return unit;
    }

    private static void Invoke(object target, string methodName)
    {
        var method = target.GetType().GetMethod(
            methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "未找到方法: " + methodName);
        method.Invoke(target, null);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(
            fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "未找到字段: " + fieldName);
        field.SetValue(target, value);
    }
}
