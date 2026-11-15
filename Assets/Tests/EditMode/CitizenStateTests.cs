using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 公民状态机测试：建筑优先于敌人、废墟优先于已建成建筑、修建满 500 后进驻销毁、
/// 无建筑时索敌攻击、无目标时保持 Idle、死亡后停止行为。
/// </summary>
public class CitizenStateTests
{
    private const float Perception = 2f;
    private const float Productivity = 100f;

    private readonly List<GameObject> created = new List<GameObject>();
    private readonly List<UnitBuilding> buildings = new List<UnitBuilding>();
    private readonly List<UnitHealth> healths = new List<UnitHealth>();
    private readonly List<ScriptableObject> assets = new List<ScriptableObject>();

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < buildings.Count; i++)
        {
            if (buildings[i] != null)
            {
                Invoke(buildings[i], "OnDisable");
            }
        }

        buildings.Clear();

        for (int i = 0; i < healths.Count; i++)
        {
            if (healths[i] != null)
            {
                UnitRegistry.Unregister(healths[i]);
            }
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

        for (int i = 0; i < assets.Count; i++)
        {
            if (assets[i] != null)
            {
                Object.DestroyImmediate(assets[i]);
            }
        }

        assets.Clear();
    }

    [Test]
    public void RuinInRange_EntersRepair_AndAddsProductivityPerSecond()
    {
        var citizen = CreateCitizen(Vector3.zero);
        var ruin = CreateBuilding(new Vector3(0f, 0f, 1.1f), operational: false);

        citizen.Brain.Tick(0.1f);

        Assert.AreEqual("Repair", citizen.Brain.StateName);
        Assert.AreSame(ruin, citizen.Brain.BuildingTarget);
        Assert.AreEqual(10f, ruin.BuildProgress, 0.001f, "0.1 秒 × 生产力 100 应推进 10 点");
        Assert.IsTrue(citizen.Locomotion.Paused, "修建时应停下");
    }

    [Test]
    public void RuinCompletesInFiveSeconds_ThenCitizenGarrisonsAndIsRemoved()
    {
        var citizen = CreateCitizen(Vector3.zero);
        var ruin = CreateBuilding(new Vector3(0f, 0f, 1.1f), operational: false);

        // 5 秒 × 100/秒 = 500，正好完成。
        for (int i = 0; i < 50; i++)
        {
            citizen.Brain.Tick(0.1f);
        }

        Assert.IsTrue(ruin.IsOperational, "5 秒后废墟应修建完成");
        Assert.IsTrue(citizen.GameObject.activeSelf, "完成当帧公民仍在场");

        citizen.Brain.Tick(0.1f);

        Assert.IsFalse(citizen.GameObject.activeSelf, "建成后公民应进驻并被移出场景");
        Assert.IsFalse(citizen.Brain.enabled, "进驻后行为应停止");
    }

    [Test]
    public void SecondCitizen_ResumesFromStoredProgress_AndCompletes()
    {
        var first = CreateCitizen(Vector3.zero);
        var ruin = CreateBuilding(new Vector3(0f, 0f, 1.1f), operational: false);

        for (int i = 0; i < 20; i++)
        {
            first.Brain.Tick(0.1f);
        }

        Assert.AreEqual(200f, ruin.BuildProgress, 0.001f, "第一名公民应修到 200");

        // 修建途中第一名公民死亡，进度应保留在建筑上。
        SetField(first.Health, "destroyOnDeath", false);
        first.Health.TakeDamage(999f);
        Assert.IsFalse(first.Brain.enabled);
        Assert.AreEqual(200f, ruin.BuildProgress, 0.001f, "中断不应重置进度");

        // 第二名公民接手同一废墟，从 200 继续。
        var second = CreateCitizen(Vector3.zero);
        second.Brain.Tick(0.1f);
        Assert.AreSame(ruin, second.Brain.BuildingTarget, "第二名公民应接手同一废墟");
        Assert.AreEqual(210f, ruin.BuildProgress, 0.001f, "应从原值继续而非从 0 开始");

        for (int i = 0; i < 28; i++)
        {
            second.Brain.Tick(0.1f);
        }

        Assert.AreEqual(490f, ruin.BuildProgress, 0.001f);
        Assert.IsTrue(ruin.IsRuined, "490/500 尚未完成");

        second.Brain.Tick(0.1f);

        Assert.IsTrue(ruin.IsOperational, "累计 500 应完成");
        Assert.AreEqual(500f, ruin.BuildProgress, 0.001f);
    }

    [Test]
    public void RuinOutsideInteractRange_WalksToRepair()
    {
        var citizen = CreateCitizen(Vector3.zero);
        var ruin = CreateBuilding(new Vector3(0f, 0f, 1.9f), operational: false);

        citizen.Brain.Tick(0.1f);

        Assert.AreEqual(1.9f, ruin.DistanceTo(Vector3.zero), 0.001f);
        Assert.Greater(ruin.DistanceTo(Vector3.zero), ruin.InteractRange, "应落在交互距离之外");
        Assert.AreEqual("MoveToRepair", citizen.Brain.StateName);
        Assert.AreSame(ruin, citizen.Brain.BuildingTarget);
        Assert.IsFalse(citizen.Locomotion.Paused, "走向废墟时不应暂停");
        Assert.AreEqual(1, citizen.Locomotion.MoveToCalls);
        Assert.AreEqual(0f, ruin.BuildProgress, 0.001f, "尚未走到不应推进建造");
    }

    [Test]
    public void OperationalOutsideInteractRange_WalksToGarrison()
    {
        var citizen = CreateCitizen(Vector3.zero);
        var building = CreateBuilding(new Vector3(0f, 0f, 1.9f), operational: true);

        citizen.Brain.Tick(0.1f);

        Assert.Greater(building.DistanceTo(Vector3.zero), building.InteractRange, "应落在交互距离之外");
        Assert.AreEqual("MoveToGarrison", citizen.Brain.StateName);
        Assert.IsFalse(citizen.Locomotion.Paused, "走向建筑时不应暂停");
        Assert.AreEqual(1, citizen.Locomotion.MoveToCalls);
        Assert.IsTrue(citizen.GameObject.activeSelf, "未走到前不应进驻");
    }

    [Test]
    public void OperationalBuildingInRange_GarrisonsImmediately()
    {
        var citizen = CreateCitizen(Vector3.zero);
        CreateBuilding(new Vector3(0f, 0f, 1f), operational: true);

        citizen.Brain.Tick(0.1f);

        Assert.IsFalse(citizen.GameObject.activeSelf, "已建成建筑应立即进驻");
    }

    [Test]
    public void NoBuilding_EnemyInPerception_Chases()
    {
        var citizen = CreateCitizen(Vector3.zero);
        var enemy = CreateEnemy(new Vector3(1.5f, 0f, 0f));
        citizen.Attack.range = 0.5f;

        citizen.Brain.Tick(0.1f);

        Assert.AreEqual("Chase", citizen.Brain.StateName, "敌人在感知内但不在攻击范围应追击");
        Assert.AreSame(enemy.Health, citizen.Brain.Target);
        Assert.IsFalse(citizen.Locomotion.Paused);
        Assert.AreEqual(1, citizen.Locomotion.MoveToCalls);
    }

    [Test]
    public void NoBuilding_EnemyInAttackRange_Attacks()
    {
        var citizen = CreateCitizen(Vector3.zero);
        CreateEnemy(new Vector3(1f, 0f, 0f));
        citizen.Attack.range = 1.5f;

        citizen.Brain.Tick(0.1f);

        Assert.AreEqual("Attack", citizen.Brain.StateName);
        Assert.IsTrue(citizen.Locomotion.Paused);
        Assert.AreEqual(1, citizen.Attack.Executions);
    }

    [Test]
    public void NoBuildingNoEnemy_StaysIdle()
    {
        var citizen = CreateCitizen(Vector3.zero);

        citizen.Brain.Tick(0.1f);

        Assert.AreEqual("Idle", citizen.Brain.StateName);
        Assert.IsTrue(citizen.Locomotion.Paused);
    }

    [Test]
    public void BuildingTakesPriorityOverEnemy()
    {
        var citizen = CreateCitizen(Vector3.zero);
        CreateBuilding(new Vector3(0f, 0f, 1.1f), operational: false);
        CreateEnemy(new Vector3(1f, 0f, 0f));

        citizen.Brain.Tick(0.1f);

        Assert.AreEqual("Repair", citizen.Brain.StateName, "建筑应优先于敌人");
        Assert.IsNull(citizen.Brain.Target);
    }

    [Test]
    public void RuinedBuildingTakesPriorityOverOperationalBuilding()
    {
        var citizen = CreateCitizen(Vector3.zero);
        var operational = CreateBuilding(new Vector3(0f, 0f, 1f), operational: true);
        var ruin = CreateBuilding(new Vector3(1.4f, 0f, 0f), operational: false);

        citizen.Brain.Tick(0.1f);

        Assert.AreSame(ruin, citizen.Brain.BuildingTarget, "同时存在时应优先修废墟");
        Assert.AreNotSame(operational, citizen.Brain.BuildingTarget);
        Assert.AreEqual("Repair", citizen.Brain.StateName);
    }

    [Test]
    public void Death_StopsBehavior()
    {
        var citizen = CreateCitizen(Vector3.zero);
        CreateBuilding(new Vector3(0f, 0f, 1.1f), operational: false);

        citizen.Brain.Tick(0.1f);
        Assert.AreEqual("Repair", citizen.Brain.StateName);

        SetField(citizen.Health, "destroyOnDeath", false);
        citizen.Health.TakeDamage(999f);

        Assert.IsFalse(citizen.Health.IsAlive);
        Assert.IsFalse(citizen.Brain.enabled, "死亡后行为应停止");
        Assert.IsNull(citizen.Brain.BuildingTarget);
        Assert.IsTrue(citizen.Locomotion.Paused, "死亡后应保持暂停");
    }

    private CitizenRig CreateCitizen(Vector3 position)
    {
        var go = CreateGameObject("Citizen", position);

        var health = go.AddComponent<UnitHealth>();
        healths.Add(health);

        var definition = ScriptableObject.CreateInstance<UnitDefinition>();
        definition.displayName = "公民";
        definition.team = UnitTeam.Player;
        definition.maxHealth = 30f;
        definition.attack = 2f;
        definition.moveSpeed = 2f;
        definition.attackInterval = 2f;
        definition.range = 1.5f;
        definition.perceptionRange = Perception;
        definition.productivity = Productivity;
        definition.canMove = true;
        definition.canAttack = true;
        assets.Add(definition);

        var locomotion = go.AddComponent<StubLocomotion>();
        var attack = go.AddComponent<StubAttackAbility>();
        attack.range = 1.5f;

        var unit = go.AddComponent<Unit>();
        SetField(unit, "definition", definition);
        Invoke(unit, "Awake");

        var brain = go.AddComponent<CitizenBrain>();
        Invoke(brain, "Awake");
        Invoke(brain, "OnEnable");

        return new CitizenRig
        {
            GameObject = go,
            Unit = unit,
            Health = health,
            Locomotion = locomotion,
            Attack = attack,
            Brain = brain
        };
    }

    private UnitBuilding CreateBuilding(Vector3 position, bool operational)
    {
        var go = CreateGameObject("Building", position);

        var health = go.AddComponent<UnitHealth>();
        health.Initialize(500f, UnitTeam.Player);
        healths.Add(health);

        var unit = go.AddComponent<Unit>();
        go.AddComponent<UnitTurretBrain>();
        var building = go.AddComponent<UnitBuilding>();
        buildings.Add(building);

        Invoke(unit, "Awake");
        Invoke(building, "Awake");
        Invoke(building, "OnEnable");
        Invoke(building, "Start");

        // EditMode 下 UnitBuilding.Start 禁用血量不会自动触发 OnDisable，这里手动对齐注册表。
        UnitRegistry.Register(health);
        if (!health.enabled)
        {
            UnitRegistry.Unregister(health);
        }

        if (operational)
        {
            building.AddBuildProgress(building.BuildRequired);
            UnitRegistry.Register(health);
        }

        return building;
    }

    private EnemyRig CreateEnemy(Vector3 position)
    {
        var go = CreateGameObject("Enemy", position);

        var health = go.AddComponent<UnitHealth>();
        health.Initialize(100f, UnitTeam.Enemy);
        healths.Add(health);
        UnitRegistry.Register(health);

        return new EnemyRig { GameObject = go, Health = health };
    }

    private GameObject CreateGameObject(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        created.Add(go);
        return go;
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

    private sealed class CitizenRig
    {
        public GameObject GameObject;
        public Unit Unit;
        public UnitHealth Health;
        public StubLocomotion Locomotion;
        public StubAttackAbility Attack;
        public CitizenBrain Brain;
    }

    private sealed class EnemyRig
    {
        public GameObject GameObject;
        public UnitHealth Health;
    }
}
