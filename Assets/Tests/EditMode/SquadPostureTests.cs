using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>小队姿态、集结点与感知/拴绳行为测试。</summary>
public class SquadPostureTests
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
    public void PlayerSquad_DefaultsToGarrison_EnemyRuleIsAlert()
    {
        var a = CreateWarrior("A", Vector3.zero, out _, out var brain);
        var squad = SquadRegistry.Create(new[] { a });

        Assert.AreEqual(SquadPosture.Garrison, squad.Posture);
        Assert.AreEqual(SquadPosture.Alert, SquadPostureRules.DefaultFor(UnitTeam.Enemy));
        Assert.IsTrue(brain.HasSquadPosture);
        Assert.AreEqual(SquadPosture.Garrison, brain.SquadPosture);
    }

    [Test]
    public void AlertPosture_PerceptionIsSix_GarrisonIsThree()
    {
        var a = CreateWarrior("A", Vector3.zero, out _, out var brain);
        var squad = SquadRegistry.Create(new[] { a });

        Assert.AreEqual(3f, brain.PerceptionRange, 0.001f, "驻守基础感知 3");

        squad.SetPosture(SquadPosture.Alert);
        Assert.AreEqual(6f, brain.PerceptionRange, 0.001f, "戒备感知 6");

        squad.SetPosture(SquadPosture.Garrison);
        Assert.AreEqual(3f, brain.PerceptionRange, 0.001f, "切回驻守恢复 3");
    }

    [Test]
    public void SetPosture_AppliesToExistingAndNewMembers()
    {
        var a = CreateWarrior("A", Vector3.zero, out _, out var brainA);
        var squad = SquadRegistry.Create(new[] { a });
        squad.SetPosture(SquadPosture.Alert);

        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f), out _, out var brainB);
        squad.AddMember(b);

        Assert.AreEqual(6f, brainA.PerceptionRange, 0.001f);
        Assert.IsTrue(brainB.HasSquadPosture);
        Assert.AreEqual(SquadPosture.Alert, brainB.SquadPosture);
        Assert.AreEqual(6f, brainB.PerceptionRange, 0.001f);
    }

    [Test]
    public void Garrison_PushesRallySlotGuardPositions()
    {
        var a = CreateWarrior("A", Vector3.zero, out _, out var brainA);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f), out _, out var brainB);
        var squad = SquadRegistry.Create(new[] { a, b });

        Assert.IsTrue(squad.HasRallyPoint);
        Assert.LessOrEqual(
            UnitCommander.HorizontalDistance(brainA.GuardPosition, squad.RallyPoint),
            squad.Spacing + 0.01f, "成员驻守点应落在集结点附近");
        Assert.LessOrEqual(
            UnitCommander.HorizontalDistance(brainB.GuardPosition, squad.RallyPoint),
            squad.Spacing + 0.01f);
    }

    [Test]
    public void MoveCompletion_UpdatesRallyAndPushesToMembers()
    {
        var a = CreateWarrior("A", Vector3.zero, out _, out var brainA);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f), out _, out var brainB);
        var squad = SquadRegistry.Create(new[] { a, b });

        squad.MoveTo(new Vector3(10f, 0f, 10f), Vector3.forward, false);
        for (int step = 0; step < 2000 && squad.MoveOrderActive; step++)
        {
            squad.Tick();
            squad.Advance(0.05f);
            SyncMembersToDestinations(squad);
        }

        Assert.IsFalse(squad.MoveOrderActive);
        Assert.AreEqual(10f, squad.RallyPoint.x, 0.15f, "集结点应更新为落点");
        Assert.AreEqual(10f, squad.RallyPoint.z, 0.15f);
        Assert.LessOrEqual(
            UnitCommander.HorizontalDistance(brainA.GuardPosition, squad.RallyPoint),
            squad.Spacing + 0.01f);
        Assert.LessOrEqual(
            UnitCommander.HorizontalDistance(brainB.GuardPosition, squad.RallyPoint),
            squad.Spacing + 0.01f);
    }

    [Test]
    public void Garrison_ReleasesTargetBeyondFiveMetersFromRally()
    {
        var a = CreateWarrior("A", Vector3.zero, out _, out _);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f), out _, out _);
        var squad = SquadRegistry.Create(new[] { a, b });
        var brain = squad.GetComponent<SquadBrain>();
        Invoke(brain, "Awake");

        var enemy = CreateEnemy(new Vector3(10f, 0f, 0f), out _);
        squad.Attack(enemy.Health);
        Assert.IsNotNull(squad.AttackTarget);

        brain.Tick(0.01f);

        Assert.IsNull(squad.AttackTarget, "敌人离集结点超过 5m 应停止追击");
        Assert.IsFalse(squad.MoveOrderActive);
        Assert.AreEqual(SquadState.Idle, squad.State);
    }

    [Test]
    public void Alert_DoesNotReturnAfterCombat()
    {
        var a = CreateWarrior("A", Vector3.zero, out var locomotion, out var brain);
        var squad = SquadRegistry.Create(new[] { a });
        squad.SetPosture(SquadPosture.Alert);

        var enemy = CreateEnemy(new Vector3(2f, 0f, 0f), out var enemyHealth);
        Invoke(brain, "Update");

        int moveCallsBefore = locomotion.MoveToCalls;
        Assert.Greater(moveCallsBefore, 0, "戒备单位应追击感知范围内的敌人");

        SetField(enemyHealth, "destroyOnDeath", false);
        enemyHealth.TakeDamage(999f);
        Invoke(brain, "Update");

        Assert.AreEqual("Idle", brain.StateName);
        Assert.Greater(locomotion.StopCalls, 0, "战斗结束应停下");
        Assert.IsTrue(locomotion.Paused);
        Assert.AreEqual(moveCallsBefore, locomotion.MoveToCalls,
            "戒备姿态战后不应尝试走回原位置");
        Assert.AreEqual(a.transform.position, brain.GuardPosition);
    }

    [Test]
    public void SetRallyPoint_UpdatesRallyAndReformsWhenIdle()
    {
        var a = CreateWarrior("A", Vector3.zero, out _, out _);
        var squad = SquadRegistry.Create(new[] { a });

        squad.SetRallyPoint(new Vector3(5f, 0f, 0f));

        Assert.IsTrue(squad.HasRallyPoint);
        Assert.AreEqual(5f, squad.RallyPoint.x, 0.001f);
        Assert.IsTrue(squad.MoveOrderActive, "空闲时手动设点应在集结点重组");
    }

    [Test]
    public void Garrison_OrderEnd_RestoresSquadGuardSlot()
    {
        var a = CreateWarrior("A", Vector3.zero, out _, out var brain);
        var squad = SquadRegistry.Create(new[] { a });
        Vector3 guardSlot = brain.GuardPosition;

        var enemy = CreateEnemy(new Vector3(20f, 0f, 0f), out var enemyHealth);
        squad.Attack(enemyHealth);

        // 先让成员进入攻击命令状态，再模拟追击后命令被小队统一清空。
        Invoke(brain, "Update");
        a.transform.position = new Vector3(4f, 0f, 0f);
        squad.CompleteOrders();
        Invoke(brain, "Update");

        Assert.AreEqual(
            UnitOrderType.None, a.GetComponent<UnitCommander>().OrderType);
        Assert.AreEqual(
            guardSlot,
            brain.GuardPosition,
            "驻守成员命令结束后应回到小队下发的集结点槽位，而不是停在追击位置");
    }

    [Test]
    public void SetFormation_WhenIdle_ReformsInPlace()
    {
        var a = CreateWarrior("A", Vector3.zero, out _, out _);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f), out _, out _);
        var c = CreateWarrior("C", new Vector3(0f, 0f, 2f), out _, out _);
        var d = CreateWarrior("D", new Vector3(2f, 0f, 2f), out _, out _);
        var squad = SquadRegistry.Create(new[] { a, b, c, d });

        // 戒备姿态不会推驻守槽位；不做原地整队的话，阵型按钮没有可见效果。
        squad.SetPosture(SquadPosture.Alert);
        Assert.IsFalse(squad.MoveOrderActive);

        squad.SetFormation(FormationType.Line);

        Assert.AreEqual(FormationType.Line, squad.Formation);
        Assert.IsFalse(squad.MoveOrderActive, "原地整队不应进入小队 Moving 状态");

        for (int i = 0; i < squad.Members.Count; i++)
        {
            Assert.AreEqual(
                UnitOrderType.Move,
                squad.Members[i].OrderType,
                "每名成员都应收到去新阵型槽位的移动命令");
        }
    }

    [Test]
    public void Disband_ClearsMemberPosture()
    {
        var a = CreateWarrior("A", Vector3.zero, out _, out var brain);
        var squad = SquadRegistry.Create(new[] { a });
        Assert.IsTrue(brain.HasSquadPosture);

        squad.Disband();

        Assert.IsFalse(brain.HasSquadPosture);
        Assert.AreEqual(3f, brain.PerceptionRange, 0.001f);
    }

    private Unit CreateWarrior(
        string name, Vector3 position, out StubLocomotion locomotion, out WarriorBrain brain)
    {
        var unit = CreateUnit(name, UnitTeam.Player, position);
        locomotion = unit.gameObject.AddComponent<StubLocomotion>();
        brain = unit.gameObject.AddComponent<WarriorBrain>();
        Invoke(brain, "Awake");

        unit.Stats.SetBase(StatType.MoveSpeed, 4f);
        unit.Stats.SetBase(StatType.PerceptionRange, 3f);
        return unit;
    }

    private Unit CreateEnemy(Vector3 position, out UnitHealth health)
    {
        var unit = CreateUnit("Enemy", UnitTeam.Enemy, position);
        health = unit.Health;
        return unit;
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
