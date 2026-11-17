using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 小队群体行为树测试：攻击命令 > 移动命令 > 自动接敌 > 待命。
/// </summary>
public class SquadBrainTests
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
        SquadRegistry.Clear();
        UnitSelection.Clear();

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
    public void NoEnemy_KeepsSquadIdle()
    {
        var squad = CreateSquad(out var brain, new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f));

        brain.Tick(0.3f);

        Assert.AreEqual(SquadState.Idle, squad.State);
        Assert.IsNull(brain.PendingEnemy);
        Assert.AreEqual(BehaviorStatus.Success, brain.LastStatus);
    }

    [Test]
    public void EnemyNearby_TriggersAutoEngage()
    {
        var squad = CreateSquad(out var brain, new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f));
        var enemy = CreateUnit("Enemy", UnitTeam.Enemy, new Vector3(1f, 0f, 1f));

        brain.Tick(0.3f);

        Assert.AreEqual(SquadState.Engaging, squad.State, "范围内有敌人时应自动接敌");
        Assert.AreSame(enemy.Health, squad.AttackTarget);
        Assert.AreEqual(UnitOrderType.Attack, squad.Members[0].OrderType);
        Assert.AreEqual(UnitOrderType.Attack, squad.Members[1].OrderType);
    }

    [Test]
    public void MoveOrder_TakesPriorityOverAutoEngage()
    {
        var squad = CreateSquad(out var brain, new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f));
        CreateUnit("Enemy", UnitTeam.Enemy, new Vector3(1f, 0f, 1f));

        squad.MoveTo(new Vector3(20f, 0f, 20f), Vector3.forward, false);
        brain.Tick(0.3f);

        Assert.AreEqual(SquadState.Moving, squad.State, "移动命令优先于自动接敌");
        Assert.IsNull(squad.AttackTarget);
        Assert.AreEqual(UnitOrderType.Move, squad.Members[0].OrderType);
    }

    [Test]
    public void MoveOrder_CompletesWhenAllMembersArrive()
    {
        var squad = CreateSquad(out var brain, new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f));
        squad.MoveTo(new Vector3(20f, 0f, 20f), Vector3.forward, false);

        // 跟随移动是逐帧推进的：每帧把成员同步到当前槽位，模拟成员跟队。
        for (int step = 0; step < 2000 && squad.MoveOrderActive; step++)
        {
            brain.Tick(0.05f);
            for (int i = 0; i < squad.Members.Count; i++)
            {
                squad.Members[i].transform.position = squad.Members[i].Destination;
            }
        }

        Assert.AreEqual(SquadState.Idle, squad.State);
        Assert.IsFalse(squad.MoveOrderActive);
        Assert.AreEqual(UnitOrderType.None, squad.Members[0].OrderType, "到达后应清空成员命令");
    }

    [Test]
    public void ManualControl_SuppressesAutoEngage_AndKeepsIndividualOrders()
    {
        var squad = CreateSquad(out var brain, new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f));
        CreateUnit("Enemy", UnitTeam.Enemy, new Vector3(1f, 0f, 1f));

        squad.CompleteOrders();
        squad.SetManualControl(true);
        squad.Members[0].IssueMove(new Vector3(10f, 0f, 10f));

        brain.Tick(0.3f);

        Assert.IsNull(squad.AttackTarget, "手动接管期间不应自动接敌");
        Assert.AreEqual(SquadState.Idle, squad.State);
        Assert.AreEqual(UnitOrderType.Move, squad.Members[0].OrderType, "个体移动命令不应被集火覆盖");
        Assert.AreEqual(UnitOrderType.None, squad.Members[1].OrderType, "未下达个体命令的成员不应被拉去集火");
    }

    [Test]
    public void SquadLevelCommand_ClearsManualControl()
    {
        var squad = CreateSquad(out _, new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f));

        squad.SetManualControl(true);
        squad.MoveTo(new Vector3(20f, 0f, 20f), Vector3.forward, false);
        Assert.IsFalse(squad.ManualControl, "小队级移动命令应解除手动接管");

        squad.SetManualControl(true);
        var enemy = CreateUnit("Enemy", UnitTeam.Enemy, new Vector3(1f, 0f, 1f));
        squad.Attack(enemy.Health);
        Assert.IsFalse(squad.ManualControl, "小队级攻击命令应解除手动接管");
    }

    [Test]
    public void AllMembersDead_DisbandsSquadAndRemovesFromRegistry()
    {
        var squad = CreateSquad(out var brain, new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f));
        int squadId = squad.SquadId;

        for (int i = 0; i < squad.Members.Count; i++)
        {
            var health = squad.Members[i].GetComponent<UnitHealth>();
            SetField(health, "destroyOnDeath", false);
            health.TakeDamage(999f);
        }

        brain.Tick(0.01f);

        Assert.IsNull(SquadRegistry.Find(squadId), "全员阵亡后小队应自动解散，不在面板留 0 人空壳");
    }

    [Test]
    public void AttackOrder_EndsWhenTargetDies()
    {
        var squad = CreateSquad(out var brain, new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f));
        // 放在集结点 5m 内：默认驻守姿态下超出 5m 会立即放弃目标。
        var enemy = CreateUnit("Enemy", UnitTeam.Enemy, new Vector3(3f, 0f, 0f));

        squad.Attack(enemy.Health);
        brain.Tick(0.01f);
        Assert.AreEqual(SquadState.Engaging, squad.State);

        SetField(enemy.Health, "destroyOnDeath", false);
        enemy.Health.TakeDamage(999f);
        brain.Tick(0.01f);

        Assert.AreEqual(SquadState.Idle, squad.State, "目标死亡后应结束集火");
        Assert.IsNull(squad.AttackTarget);
    }

    private Squad CreateSquad(out SquadBrain brain, params Vector3[] memberPositions)
    {
        var units = new List<Unit>();
        for (int i = 0; i < memberPositions.Length; i++)
        {
            units.Add(CreateUnit("Member" + i, UnitTeam.Player, memberPositions[i]));
        }

        var squad = SquadRegistry.Create(units);
        brain = squad.GetComponent<SquadBrain>();
        Invoke(brain, "Awake");
        return squad;
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
