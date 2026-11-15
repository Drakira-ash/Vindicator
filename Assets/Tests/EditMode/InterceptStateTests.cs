using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 拦截状态机测试：在 EditMode 中手动驱动 Brain 的 Awake/OnEnable/Update，
/// 用测试桩替代真实移动与攻击组件，断言状态切换与移动暂停。
/// </summary>
public class InterceptStateTests
{
    private readonly List<GameObject> created = new List<GameObject>();
    private readonly List<UnitHealth> healths = new List<UnitHealth>();
    private readonly List<MonoBehaviour> brains = new List<MonoBehaviour>();
    private Action<InterceptRequest> interceptHandler;

    [TearDown]
    public void TearDown()
    {
        if (interceptHandler != null)
        {
            UnitEvents.InterceptRequested -= interceptHandler;
            interceptHandler = null;
        }

        for (int i = 0; i < brains.Count; i++)
        {
            if (brains[i] != null)
            {
                Invoke(brains[i], "OnDisable");
            }
        }

        brains.Clear();

        for (int i = 0; i < healths.Count; i++)
        {
            UnitRegistry.Unregister(healths[i]);
        }

        healths.Clear();

        for (int i = 0; i < created.Count; i++)
        {
            if (created[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();
    }

    [Test]
    public void InterceptedOutOfRange_StaysIdleAndStopsMoving()
    {
        var enemy = CreateEnemy(Vector3.zero, 3f);
        var warrior = CreateWarrior(new Vector3(5f, 0f, 0f));

        UnitEvents.RaiseInterceptRequested(new InterceptRequest(warrior.Unit, enemy.Health));
        Tick(enemy.EnemyBrain);

        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName, "拦截者不在射程内时应进入 Idle");
        Assert.IsTrue(enemy.Locomotion.Paused, "被拦截后应暂停移动");
        Assert.AreEqual(0, enemy.Locomotion.MoveToCalls, "被拦截后不应继续移动");
    }

    [Test]
    public void InterceptedOutOfRange_ThenWalksIntoRange_EntersAttack()
    {
        var enemy = CreateEnemy(Vector3.zero, 3f);
        var warrior = CreateWarrior(new Vector3(5f, 0f, 0f));

        UnitEvents.RaiseInterceptRequested(new InterceptRequest(warrior.Unit, enemy.Health));
        Tick(enemy.EnemyBrain);
        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName, "拦截者未进入射程时应先待命");
        Assert.AreEqual(0, enemy.Attack.Executions, "射程外不应攻击");

        // 拦截者走进射程：下一轮判定应切换为攻击。
        warrior.GameObject.transform.position = new Vector3(2f, 0f, 0f);
        Tick(enemy.EnemyBrain);

        Assert.AreEqual("Attack", enemy.EnemyBrain.StateName, "拦截者进入射程后应切换为攻击");
        Assert.IsTrue(enemy.Locomotion.Paused, "攻击时应停下");
        Assert.AreEqual(1, enemy.Attack.Executions, "进入射程后应执行一次攻击");
    }

    [Test]
    public void InterceptedWhileMoving_StopsMovement()
    {
        var enemy = CreateEnemy(Vector3.zero, 3f);
        var goal = CreateGameObject("PatrolGoal", new Vector3(10f, 0f, 0f));
        enemy.EnemyBrain.SetPatrolGoal(goal.transform);

        // 先让敌人进入移动状态。
        Tick(enemy.EnemyBrain);
        Assert.AreEqual("Patrol", enemy.EnemyBrain.StateName);
        Assert.AreEqual(1, enemy.Locomotion.MoveToCalls, "应先开始移动");
        Assert.IsFalse(enemy.Locomotion.Paused, "移动中不应暂停");

        int movesBefore = enemy.Locomotion.MoveToCalls;
        int stopsBefore = enemy.Locomotion.StopCalls;

        // 移动中被拦截（拦截者不在射程内）。
        var warrior = CreateWarrior(new Vector3(5f, 0f, 0f));
        UnitEvents.RaiseInterceptRequested(new InterceptRequest(warrior.Unit, enemy.Health));
        Tick(enemy.EnemyBrain);

        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName, "移动中被拦截应停下并进入 Idle");
        Assert.IsTrue(enemy.Locomotion.Paused, "被拦截后应暂停移动");
        Assert.Greater(enemy.Locomotion.StopCalls, stopsBefore, "被拦截后应调用 Stop");
        Assert.AreEqual(movesBefore, enemy.Locomotion.MoveToCalls, "被拦截后不应再发起移动");
    }

    [Test]
    public void InterceptedInRange_EntersAttack()
    {
        var enemy = CreateEnemy(Vector3.zero, 3f);
        var warrior = CreateWarrior(new Vector3(2f, 0f, 0f));

        UnitEvents.RaiseInterceptRequested(new InterceptRequest(warrior.Unit, enemy.Health));
        Tick(enemy.EnemyBrain);

        Assert.AreEqual("Attack", enemy.EnemyBrain.StateName, "拦截者进入射程后应攻击");
        Assert.IsTrue(enemy.Locomotion.Paused, "攻击时应停下");
        Assert.AreEqual(1, enemy.Attack.Executions, "应执行一次攻击能力");
    }

    [Test]
    public void AfterTargetLost_ReturnsToIdleThenReevaluatesTowardGoal()
    {
        var enemy = CreateEnemy(Vector3.zero, 3f);
        var goal = CreateGameObject("PatrolGoal", new Vector3(10f, 0f, 0f));
        enemy.EnemyBrain.SetPatrolGoal(goal.transform);

        var warrior = CreateWarrior(new Vector3(5f, 0f, 0f));
        UnitEvents.RaiseInterceptRequested(new InterceptRequest(warrior.Unit, enemy.Health));
        Tick(enemy.EnemyBrain);
        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName);

        SetField(warrior.Health, "destroyOnDeath", false);
        warrior.Health.TakeDamage(999f);
        Assert.IsFalse(warrior.Health.IsAlive, "拦截者应已死亡");

        int movesBefore = enemy.Locomotion.MoveToCalls;
        Tick(enemy.EnemyBrain);

        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName, "目标丢失当帧应回到 Idle");
        Assert.IsTrue(enemy.Locomotion.Paused, "Idle 当帧应停下");
        Assert.AreEqual(movesBefore, enemy.Locomotion.MoveToCalls, "Idle 当帧不应移动");

        Tick(enemy.EnemyBrain);

        Assert.AreEqual("Patrol", enemy.EnemyBrain.StateName, "Idle 后应马上进入下一轮判定并朝终点移动");
        Assert.IsFalse(enemy.Locomotion.Paused, "前往终点时不应暂停");
        Assert.AreEqual(movesBefore + 1, enemy.Locomotion.MoveToCalls, "应向终点发起移动");
        Assert.AreEqual(goal.transform.position, enemy.Locomotion.LastDestination, "移动目标应为终点");
    }

    [Test]
    public void AfterInterceptReleased_EnemyReevaluatesAndMovesTowardGoal()
    {
        var enemy = CreateEnemy(Vector3.zero, 3f);
        var goal = CreateGameObject("PatrolGoal", new Vector3(10f, 0f, 0f));
        enemy.EnemyBrain.SetPatrolGoal(goal.transform);

        var warrior = CreateWarrior(new Vector3(5f, 0f, 0f));
        UnitEvents.RaiseInterceptRequested(new InterceptRequest(warrior.Unit, enemy.Health));
        Tick(enemy.EnemyBrain);
        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName);
        Assert.IsTrue(enemy.Locomotion.Paused);

        // 战士放弃追击（拉出感知范围），敌人应先回到 Idle。
        warrior.GameObject.transform.position = new Vector3(25f, 0f, 0f);
        int movesBefore = enemy.Locomotion.MoveToCalls;
        UnitEvents.RaiseInterceptReleased(new InterceptRequest(warrior.Unit, enemy.Health));
        Tick(enemy.EnemyBrain);

        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName, "解除拦截当帧应回到 Idle");
        Assert.IsTrue(enemy.Locomotion.Paused, "Idle 当帧应停下");
        Assert.AreEqual(movesBefore, enemy.Locomotion.MoveToCalls, "Idle 当帧不应移动");

        Tick(enemy.EnemyBrain);

        Assert.AreEqual("Patrol", enemy.EnemyBrain.StateName, "Idle 后应从 Idle 进入下一轮判定");
        Assert.IsFalse(enemy.Locomotion.Paused, "前往终点时不应暂停");
        Assert.AreEqual(movesBefore + 1, enemy.Locomotion.MoveToCalls, "应向终点发起移动");
        Assert.AreEqual(goal.transform.position, enemy.Locomotion.LastDestination, "移动目标应为终点");
    }

    [Test]
    public void InterceptBeyondLeash_ReleasesThenReevaluatesTowardGoal()
    {
        var enemy = CreateEnemy(Vector3.zero, 3f);
        var goal = CreateGameObject("PatrolGoal", new Vector3(10f, 0f, 0f));
        enemy.EnemyBrain.SetPatrolGoal(goal.transform);

        var warrior = CreateWarrior(new Vector3(5f, 0f, 0f));
        UnitEvents.RaiseInterceptRequested(new InterceptRequest(warrior.Unit, enemy.Health));
        Tick(enemy.EnemyBrain);
        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName);

        // 拦截者拉开超过 EnemyBrain.interceptLeash(20m)，应自动解除拦截。
        warrior.GameObject.transform.position = new Vector3(25f, 0f, 0f);
        int movesBefore = enemy.Locomotion.MoveToCalls;
        Tick(enemy.EnemyBrain);

        Assert.IsNull(enemy.EnemyBrain.Target, "超出 20m 应解除拦截");
        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName, "解除拦截当帧应回到 Idle");
        Assert.IsTrue(enemy.Locomotion.Paused, "Idle 当帧应停下");
        Assert.AreEqual(movesBefore, enemy.Locomotion.MoveToCalls, "Idle 当帧不应移动");

        Tick(enemy.EnemyBrain);

        Assert.AreEqual("Patrol", enemy.EnemyBrain.StateName, "Idle 后应从 Idle 进入下一轮判定");
        Assert.IsFalse(enemy.Locomotion.Paused, "前往终点时不应暂停");
        Assert.AreEqual(movesBefore + 1, enemy.Locomotion.MoveToCalls, "应向终点发起移动");
        Assert.AreEqual(goal.transform.position, enemy.Locomotion.LastDestination, "移动目标应为终点");
    }

    [Test]
    public void EnemyDeath_WhileIntercepted_DisablesBrainAndStopsMovement()
    {
        var enemy = CreateEnemy(Vector3.zero, 3f);
        var warrior = CreateWarrior(new Vector3(5f, 0f, 0f));
        UnitEvents.RaiseInterceptRequested(new InterceptRequest(warrior.Unit, enemy.Health));
        Tick(enemy.EnemyBrain);
        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName);

        int stopsBefore = enemy.Locomotion.StopCalls;
        SetField(enemy.Health, "destroyOnDeath", false);
        enemy.Health.TakeDamage(999f);

        Assert.IsFalse(enemy.Health.IsAlive, "敌人应已死亡");
        Assert.IsFalse(enemy.EnemyBrain.enabled, "死亡后 Brain 应禁用");
        Assert.IsNull(enemy.EnemyBrain.Target, "死亡后应清空目标");
        Assert.Greater(enemy.Locomotion.StopCalls, stopsBefore, "死亡后应停止移动");
        Assert.IsTrue(enemy.Locomotion.Paused, "死亡后应保持暂停");
    }

    [Test]
    public void EnemyDeath_WhileMoving_StopsMovement()
    {
        var enemy = CreateEnemy(Vector3.zero, 3f);
        var goal = CreateGameObject("PatrolGoal", new Vector3(10f, 0f, 0f));
        enemy.EnemyBrain.SetPatrolGoal(goal.transform);

        Tick(enemy.EnemyBrain);
        Assert.AreEqual("Patrol", enemy.EnemyBrain.StateName, "应先进入巡逻移动");
        Assert.IsFalse(enemy.Locomotion.Paused, "移动中不应暂停");

        int movesBefore = enemy.Locomotion.MoveToCalls;
        int stopsBefore = enemy.Locomotion.StopCalls;
        SetField(enemy.Health, "destroyOnDeath", false);
        enemy.Health.TakeDamage(999f);

        Assert.IsFalse(enemy.EnemyBrain.enabled, "死亡后 Brain 应禁用");
        Assert.Greater(enemy.Locomotion.StopCalls, stopsBefore, "死亡后应停止移动");
        Assert.IsTrue(enemy.Locomotion.Paused, "死亡后应保持暂停，不能回到路线跟随");
        Assert.AreEqual(movesBefore, enemy.Locomotion.MoveToCalls, "死亡后不应再发起移动");
    }

    [Test]
    public void PatrolGoalReached_EntersIdleAndStops()
    {
        var enemy = CreateEnemy(Vector3.zero, 3f);
        var goal = CreateGameObject("PatrolGoal", new Vector3(0.2f, 0f, 0f));
        enemy.EnemyBrain.SetPatrolGoal(goal.transform);

        Tick(enemy.EnemyBrain);

        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName, "到达终点应进入 Idle");
        Assert.IsTrue(enemy.Locomotion.Paused, "到达终点后应真正停下（不能回到路线跟随）");
        Assert.Greater(enemy.Locomotion.StopCalls, 0, "到达终点应停止移动");
        Assert.AreEqual(0, enemy.Locomotion.MoveToCalls, "到达终点后不应再发起移动");

        // 后续帧保持静止。
        Tick(enemy.EnemyBrain);
        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName);
        Assert.IsTrue(enemy.Locomotion.Paused, "Idle 期间应保持暂停");
        Assert.AreEqual(0, enemy.Locomotion.MoveToCalls, "Idle 期间不应发起移动");
    }

    [Test]
    public void NoPatrolGoal_DelegatesMovementToRouteAndResumesAfterIntercept()
    {
        // 没有 AI 巡逻点（如 UnitMover 演示敌人）：Idle 应把移动交还给路线跟随，而不是把单位钉死。
        var enemy = CreateEnemy(Vector3.zero, 3f);

        Tick(enemy.EnemyBrain);
        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName, "无巡逻点应进入 Idle");
        Assert.IsFalse(enemy.Locomotion.Paused, "无巡逻点时 Idle 应让路线继续移动");

        var warrior = CreateWarrior(new Vector3(5f, 0f, 0f));
        UnitEvents.RaiseInterceptRequested(new InterceptRequest(warrior.Unit, enemy.Health));
        Tick(enemy.EnemyBrain);
        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName, "被拦截且拦截者不在射程应待命");
        Assert.IsTrue(enemy.Locomotion.Paused, "被拦截时应真正停下");

        // 拦截解除：先回 Idle，下一帧把移动交还路线。
        int movesBefore = enemy.Locomotion.MoveToCalls;
        warrior.GameObject.transform.position = new Vector3(25f, 0f, 0f);
        UnitEvents.RaiseInterceptReleased(new InterceptRequest(warrior.Unit, enemy.Health));
        Tick(enemy.EnemyBrain);
        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName, "解除拦截当帧应回到 Idle");
        Assert.IsTrue(enemy.Locomotion.Paused, "Idle 当帧应停下");
        Assert.AreEqual(movesBefore, enemy.Locomotion.MoveToCalls, "Idle 当帧不应发起坐标移动");

        Tick(enemy.EnemyBrain);
        Assert.AreEqual("Idle", enemy.EnemyBrain.StateName, "无巡逻点时仍应留在 Idle");
        Assert.IsFalse(enemy.Locomotion.Paused, "应恢复路线跟随");
        Assert.AreEqual(movesBefore, enemy.Locomotion.MoveToCalls, "路线跟随不应经过 MoveTo 坐标移动");
    }

    [Test]
    public void WarriorDeath_WhileEngaged_DisablesBrainAndStopsMovement()
    {
        var warrior = CreateWarrior(Vector3.zero);
        var enemy = CreateEnemy(new Vector3(5f, 0f, 0f), 3f);
        Invoke(warrior.WarriorBrain, "AcquireTarget");
        Assert.AreSame(enemy.Health, warrior.WarriorBrain.Target, "应先锁定目标");

        int stopsBefore = warrior.Locomotion.StopCalls;
        SetField(warrior.Health, "destroyOnDeath", false);
        warrior.Health.TakeDamage(999f);

        Assert.IsFalse(warrior.Health.IsAlive, "战士应已死亡");
        Assert.IsFalse(warrior.WarriorBrain.enabled, "死亡后 Brain 应禁用");
        Assert.IsNull(warrior.WarriorBrain.Target, "死亡后应清空目标");
        Assert.Greater(warrior.Locomotion.StopCalls, stopsBefore, "死亡后应停止移动");
        Assert.IsTrue(warrior.Locomotion.Paused, "死亡后应保持暂停");
    }

    [Test]
    public void WarriorSkipsTargetBeyondLeashBeforeRequestingIntercept()
    {
        var warrior = CreateWarrior(Vector3.zero);
        warrior.GameObject.transform.position = new Vector3(10f, 0f, 0f);

        var enemy = CreateEnemy(new Vector3(20f, 0f, 0f), 3f);

        int requests = 0;
        interceptHandler = request =>
        {
            if (ReferenceEquals(request.Interceptor, warrior.Unit))
            {
                requests++;
            }
        };
        UnitEvents.InterceptRequested += interceptHandler;

        Invoke(warrior.WarriorBrain, "AcquireTarget");

        Assert.AreEqual(0, requests, "超出拴绳的目标不应发起拦截请求");
        Assert.IsNull(warrior.WarriorBrain.Target, "超出拴绳的目标不应被锁定");

        enemy.GameObject.transform.position = new Vector3(5f, 0f, 0f);
        Invoke(warrior.WarriorBrain, "AcquireTarget");

        Assert.AreEqual(1, requests, "拴绳内的目标应发起拦截请求");
        Assert.AreSame(enemy.Health, warrior.WarriorBrain.Target, "拴绳内的目标应被锁定");
    }

    private UnitRig CreateEnemy(Vector3 position, float attackRange)
    {
        var go = CreateGameObject("Enemy", position);

        var health = go.AddComponent<UnitHealth>();
        health.Initialize(100f, UnitTeam.Enemy);
        healths.Add(health);
        UnitRegistry.Register(health);

        var locomotion = go.AddComponent<StubLocomotion>();
        var attack = go.AddComponent<StubAttackAbility>();
        attack.range = attackRange;

        var unit = go.AddComponent<Unit>();
        Invoke(unit, "Awake");

        var brain = go.AddComponent<EnemyBrain>();
        brains.Add(brain);
        Invoke(brain, "Awake");
        Invoke(brain, "OnEnable");

        return new UnitRig
        {
            GameObject = go,
            Unit = unit,
            Health = health,
            Locomotion = locomotion,
            Attack = attack,
            EnemyBrain = brain
        };
    }

    private UnitRig CreateWarrior(Vector3 position)
    {
        var go = CreateGameObject("Warrior", position);

        var health = go.AddComponent<UnitHealth>();
        health.Initialize(100f, UnitTeam.Player);
        healths.Add(health);
        UnitRegistry.Register(health);

        var locomotion = go.AddComponent<StubLocomotion>();

        var unit = go.AddComponent<Unit>();
        Invoke(unit, "Awake");

        var brain = go.AddComponent<WarriorBrain>();
        brains.Add(brain);
        Invoke(brain, "Awake");
        Invoke(brain, "OnEnable");

        return new UnitRig
        {
            GameObject = go,
            Unit = unit,
            Health = health,
            Locomotion = locomotion,
            WarriorBrain = brain
        };
    }

    private GameObject CreateGameObject(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        created.Add(go);
        return go;
    }

    private static void Tick(EnemyBrain brain)
    {
        Invoke(brain, "Update");
    }

    private static void Invoke(object target, string methodName)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "未找到方法: " + methodName);
        method.Invoke(target, null);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "未找到字段: " + fieldName);
        field.SetValue(target, value);
    }

    private sealed class UnitRig
    {
        public GameObject GameObject;
        public Unit Unit;
        public UnitHealth Health;
        public StubLocomotion Locomotion;
        public StubAttackAbility Attack;
        public EnemyBrain EnemyBrain;
        public WarriorBrain WarriorBrain;
    }
}
