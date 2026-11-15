using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 建筑层测试：废墟不可被攻击、建造进度可累积、建成后启用血量/炮台、进驻规则与建筑查找优先级。
/// EditMode 下手动驱动 Awake/OnEnable/Start，并用显式注册模拟 UnitHealth 的启用/禁用生命周期。
/// </summary>
public class BuildingTests
{
    private readonly List<GameObject> created = new List<GameObject>();
    private readonly List<UnitBuilding> buildings = new List<UnitBuilding>();
    private readonly List<UnitHealth> healths = new List<UnitHealth>();
    private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();

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
                UnityEngine.Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();

        for (int i = 0; i < assets.Count; i++)
        {
            if (assets[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(assets[i]);
            }
        }

        assets.Clear();
    }

    [Test]
    public void RuinedBuilding_DisablesHealthAndTurret_AndIsNotTargetable()
    {
        var building = CreateBuilding(Vector3.zero, out var health);
        UnitRegistry.Register(health);

        Assert.IsTrue(building.IsRuined);
        Assert.IsFalse(health.enabled, "废墟阶段应禁用 UnitHealth");
        Assert.IsFalse(building.GetComponent<UnitTurretBrain>().enabled, "废墟阶段应禁用炮台");
        Assert.IsFalse(building.GetComponent<Unit>().AbilitiesEnabled, "废墟阶段应关闭攻击能力");

        // 模拟 Unity 在 UnitHealth 被禁用时调用 OnDisable（EditMode 不会自动触发）。
        Invoke(health, "OnDisable");
        Assert.IsNull(
            UnitRegistry.FindNearestHostile(Vector3.zero, UnitTeam.Enemy, 10f),
            "废墟不在注册表中，敌人不应选中它");
    }

    [Test]
    public void BuildProgress_AccumulatesAcrossInterruptions_AndCompletesAt500()
    {
        var building = CreateBuilding(Vector3.zero, out var health);
        UnitRegistry.Register(health);
        Invoke(health, "OnDisable");

        Assert.IsFalse(building.AddBuildProgress(200f), "200/500 不应完成");
        Assert.IsTrue(building.IsRuined);
        Assert.AreEqual(200f, building.BuildProgress, 0.001f);
        Assert.AreEqual(0.4f, building.Progress01, 0.001f);

        // 中断后由另一名公民继续修。
        Assert.IsFalse(building.AddBuildProgress(150f));
        Assert.AreEqual(350f, building.BuildProgress, 0.001f);

        Assert.IsTrue(building.AddBuildProgress(150f), "累计 500 应完成");
        Assert.IsTrue(building.IsOperational);
        Assert.AreEqual(500f, building.BuildProgress, 0.001f);
        Assert.IsTrue(health.enabled, "建成后应启用 UnitHealth");

        UnitRegistry.Register(health);
        Assert.AreSame(
            health,
            UnitRegistry.FindNearestHostile(Vector3.zero, UnitTeam.Enemy, 10f),
            "建成后应重新成为可攻击目标");
    }

    [Test]
    public void CompletingBuilding_ReenablesTurretAndAbilities()
    {
        var building = CreateBuilding(Vector3.zero, out _);
        var unit = building.GetComponent<Unit>();
        var turret = building.GetComponent<UnitTurretBrain>();

        Assert.IsFalse(turret.enabled, "废墟阶段炮台应禁用");
        Assert.IsFalse(unit.AbilitiesEnabled, "废墟阶段能力应禁用");

        building.AddBuildProgress(building.BuildRequired);

        Assert.IsTrue(turret.enabled, "建成后炮台应启用");
        Assert.IsTrue(unit.AbilitiesEnabled, "建成后能力应启用");
        Assert.Greater(unit.GetAttackRange(), 0f, "建成后应具备可用的攻击射程");
    }

    [Test]
    public void CompletedBuilding_CanBeWorkedByNobody()
    {
        var building = CreateBuilding(Vector3.zero, out var health);
        var worker = CreateUnit("Worker", new Vector3(1f, 0f, 0f), UnitTeam.Player);

        Assert.IsTrue(building.CanBeWorkedBy(worker.Unit), "废墟应允许同队公民修建");

        building.AddBuildProgress(building.BuildRequired);
        Assert.IsFalse(building.CanBeWorkedBy(worker.Unit), "建成后不应再被修建");
        Assert.IsFalse(building.AddBuildProgress(10f), "建成后进度不应再变化");
    }

    [Test]
    public void Garrison_RequiresOperationalAndSameTeam()
    {
        var building = CreateBuilding(Vector3.zero, out _);
        var friendly = CreateUnit("Friendly", new Vector3(1f, 0f, 0f), UnitTeam.Player);
        var hostile = CreateUnit("Hostile", new Vector3(-1f, 0f, 0f), UnitTeam.Enemy);

        Assert.IsFalse(building.Garrison(friendly.Unit), "废墟不能被进驻");

        building.AddBuildProgress(building.BuildRequired);
        Assert.IsFalse(building.Garrison(hostile.Unit), "敌对单位不能进驻");

        Unit garrisoned = null;
        building.Garrisoned += (b, unit) => garrisoned = unit;

        Assert.IsTrue(building.Garrison(friendly.Unit), "同队单位应能进驻");
        Assert.AreSame(friendly.Unit, garrisoned, "应触发进驻事件");

        // EditMode 下不能 Destroy，改为停用；运行时则销毁。
        Assert.IsFalse(friendly.GameObject.activeSelf, "进驻者应被移出场景");
        Assert.IsTrue(hostile.GameObject.activeSelf, "敌对单位不受影响");
    }

    [Test]
    public void Registry_PrefersRuinedBuilding_WhenRequested()
    {
        var operational = CreateBuilding(Vector3.zero, out _);
        operational.AddBuildProgress(operational.BuildRequired);

        var ruined = CreateBuilding(new Vector3(3f, 0f, 0f), out _);

        var preferred = UnitBuildingRegistry.FindNearestFriendly(
            Vector3.zero, UnitTeam.Player, 10f, preferRuined: true);
        Assert.AreSame(ruined, preferred, "范围内有废墟时应优先废墟");

        var nearest = UnitBuildingRegistry.FindNearestFriendly(
            Vector3.zero, UnitTeam.Player, 10f, preferRuined: false);
        Assert.AreSame(operational, nearest, "不优先废墟时应选择最近的建筑");
    }

    [Test]
    public void BarVisibility_TogglesBetweenRuinedAndOperational()
    {
        var building = CreateBuilding(Vector3.zero, out _, withBars: true);
        var healthBar = building.GetComponent<HealthBar>();
        var buildBar = building.GetComponent<BuildProgressBar>();

        Assert.IsNotNull(healthBar, "测试建筑应挂血条");
        Assert.IsNotNull(buildBar, "测试建筑应挂建造条");
        Assert.IsFalse(BarObject(healthBar).activeSelf, "废墟阶段血条应隐藏");
        Assert.IsTrue(BarObject(buildBar).activeSelf, "废墟阶段建造条应显示");

        building.AddBuildProgress(building.BuildRequired);

        Assert.IsTrue(BarObject(healthBar).activeSelf, "建成后血条应显示");
        Assert.IsFalse(BarObject(buildBar).activeSelf, "建成后建造条应隐藏");
    }

    [Test]
    public void TurretPerception_IsAttackRange_AndZeroWhenDisabled()
    {
        var unitRig = CreateUnit("Tower", Vector3.zero, UnitTeam.Player);
        var attack = unitRig.GameObject.AddComponent<StubAttackAbility>();
        attack.range = 20f;
        Invoke(unitRig.Unit, "Awake");

        var turret = unitRig.GameObject.AddComponent<UnitTurretBrain>();
        Invoke(turret, "Awake");

        Assert.AreEqual(20f, turret.PerceptionRange, 0.001f, "塔应把攻击范围作为感知范围");

        turret.enabled = false;
        Assert.AreEqual(0f, turret.PerceptionRange, 0.001f, "炮台禁用时不应显示范围");
    }

    private UnitBuilding CreateBuilding(Vector3 position, out UnitHealth health, bool withBars = false)
    {
        var go = CreateGameObject("Building", position);

        health = go.AddComponent<UnitHealth>();
        health.Initialize(500f, UnitTeam.Player);
        healths.Add(health);

        var unit = go.AddComponent<Unit>();
        go.AddComponent<UnitTurretBrain>();
        go.AddComponent<StubAttackAbility>();
        var building = go.AddComponent<UnitBuilding>();
        buildings.Add(building);

        HealthBar healthBar = null;
        BuildProgressBar buildBar = null;
        if (withBars)
        {
            healthBar = go.AddComponent<HealthBar>();
            buildBar = go.AddComponent<BuildProgressBar>();
        }

        Invoke(unit, "Awake");
        Invoke(building, "Awake");

        if (withBars)
        {
            Invoke(healthBar, "Awake");
            Invoke(healthBar, "OnEnable");
            Invoke(buildBar, "Awake");
            Invoke(buildBar, "OnEnable");
        }

        Invoke(building, "OnEnable");
        Invoke(building, "Start");

        return building;
    }

    private static GameObject BarObject(Component bar)
    {
        var field = bar.GetType().GetField(
            "barObject", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "未找到 barObject 字段");
        var value = field.GetValue(bar) as GameObject;
        Assert.IsNotNull(value, "血条/建造条对象尚未创建");
        return value;
    }

    private UnitRig CreateUnit(string name, Vector3 position, UnitTeam team)
    {
        var go = CreateGameObject(name, position);

        var health = go.AddComponent<UnitHealth>();
        health.Initialize(100f, team);
        healths.Add(health);

        var definition = ScriptableObject.CreateInstance<UnitDefinition>();
        definition.team = team;
        definition.maxHealth = 100f;
        definition.range = 1f;
        definition.perceptionRange = 5f;
        assets.Add(definition);

        var unit = go.AddComponent<Unit>();
        SetField(unit, "definition", definition);
        Invoke(unit, "Awake");

        return new UnitRig { GameObject = go, Unit = unit, Health = health };
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

    private sealed class UnitRig
    {
        public GameObject GameObject;
        public Unit Unit;
        public UnitHealth Health;
    }
}
