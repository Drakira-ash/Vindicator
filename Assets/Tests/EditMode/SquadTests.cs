using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 小队核心逻辑测试：建队/转队、槽位下发、到达判定、集火与解散。
/// 不依赖 NavMesh、输入或 UI。
/// </summary>
public class SquadTests
{
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
    public void Create_AssignsIdAndCommanderSquad()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(2f, 0f, 0f));

        var squad = SquadRegistry.Create(new[] { a, b });

        Assert.IsNotNull(squad);
        Assert.AreEqual(1, squad.SquadId);
        Assert.AreEqual(2, squad.MemberUnits().Count);
        Assert.AreEqual(1, a.GetComponent<UnitCommander>().SquadId);
        Assert.AreEqual(1, b.GetComponent<UnitCommander>().SquadId);
        Assert.IsNotNull(squad.GetComponent<SquadBrain>(), "建队时应同时挂上群体行为树");
    }

    [Test]
    public void MoveTo_IssuesDistinctSlotOrders()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        squad.MoveTo(new Vector3(10f, 0f, 10f), Vector3.forward, false);

        var ca = a.GetComponent<UnitCommander>();
        var cb = b.GetComponent<UnitCommander>();

        Assert.IsTrue(squad.MoveOrderActive);
        Assert.AreEqual(SquadState.Moving, squad.State);
        Assert.AreEqual(UnitOrderType.Move, ca.OrderType);
        Assert.AreEqual(UnitOrderType.Move, cb.OrderType);
        Assert.AreNotEqual(ca.Destination, cb.Destination, "两个成员应获得不同槽位");
        Assert.AreNotEqual(0, squad.Slots.Count);
    }

    [Test]
    public void AllMembersArrived_BecomesTrueWhenMembersReachSlots()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });
        squad.MoveTo(new Vector3(10f, 0f, 10f), Vector3.forward, false);

        Assert.IsFalse(squad.AllMembersArrived(), "初始位置不在槽位，应判未到达");

        a.transform.position = a.GetComponent<UnitCommander>().Destination;
        b.transform.position = b.GetComponent<UnitCommander>().Destination;

        Assert.IsTrue(squad.AllMembersArrived());
    }

    [Test]
    public void Attack_IssuesFocusFireToEveryMember()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(2f, 0f, 0f));
        var enemy = CreateUnit("Enemy", UnitTeam.Enemy, new Vector3(5f, 0f, 5f));
        var squad = SquadRegistry.Create(new[] { a, b });

        squad.Attack(enemy.Health);

        Assert.AreEqual(SquadState.Engaging, squad.State);
        Assert.AreSame(enemy.Health, squad.AttackTarget);
        Assert.AreEqual(UnitOrderType.Attack, a.GetComponent<UnitCommander>().OrderType);
        Assert.AreEqual(UnitOrderType.Attack, b.GetComponent<UnitCommander>().OrderType);
        Assert.AreSame(enemy.Health, a.GetComponent<UnitCommander>().AttackTarget);
        Assert.IsFalse(squad.MoveOrderActive);
    }

    [Test]
    public void AddMember_MovesUnitOutOfPreviousSquad()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(2f, 0f, 0f));
        var first = SquadRegistry.Create(new[] { a, b });

        var second = SquadRegistry.Create(new[] { b }, 5);

        Assert.AreEqual(5, second.SquadId);
        Assert.AreEqual(1, first.MemberUnits().Count, "旧小队应只剩一个成员");
        Assert.AreEqual(5, b.GetComponent<UnitCommander>().SquadId, "单位应转入新小队");
    }

    [Test]
    public void AddMember_RejectsHostileOrDeadUnits()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var enemy = CreateUnit("Enemy", UnitTeam.Enemy, Vector3.zero);
        var squad = SquadRegistry.Create(new[] { a });

        Assert.IsFalse(squad.AddMember(enemy), "不同阵营不能加入");

        SetField(a.GetComponent<UnitHealth>(), "destroyOnDeath", false);
        a.GetComponent<UnitHealth>().TakeDamage(999f);
        Assert.IsFalse(squad.AddMember(a), "已死亡单位不能加入");
    }

    [Test]
    public void Tick_PrunesDeadMembersAndCompletesWhenEmpty()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var squad = SquadRegistry.Create(new[] { a });
        squad.MoveTo(new Vector3(4f, 0f, 4f), Vector3.forward, false);

        SetField(a.GetComponent<UnitHealth>(), "destroyOnDeath", false);
        a.GetComponent<UnitHealth>().TakeDamage(999f);

        squad.Tick();

        Assert.AreEqual(0, squad.MemberUnits().Count);
        Assert.AreEqual(SquadState.Idle, squad.State);
        Assert.IsFalse(squad.MoveOrderActive);
    }

    [Test]
    public void Disband_ClearsCommanderSquadAndRegistry()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var squad = SquadRegistry.Create(new[] { a });

        squad.Disband();

        Assert.AreEqual(-1, a.GetComponent<UnitCommander>().SquadId);
        Assert.IsNull(SquadRegistry.Find(1));
    }

    private Unit CreateUnit(string name, UnitTeam team, Vector3 position)
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
