using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 开发者模式两张全局数据表：小队表与单位表（含追逐/锁定指针展示）。
/// </summary>
public class DeveloperTableTests
{
    private readonly List<GameObject> created = new List<GameObject>();
    private readonly List<UnitHealth> healths = new List<UnitHealth>();

    [SetUp]
    public void SetUp()
    {
        SquadRegistry.Clear();
    }

    [TearDown]
    public void TearDown()
    {
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
    public void Headers_MatchDesign()
    {
        CollectionAssert.AreEqual(
            new[] { "ID", "名称", "阵营", "队形", "人数", "状态", "目标" },
            DeveloperTableData.SquadHeaders);

        CollectionAssert.AreEqual(
            new[] { "名称", "阵营", "状态", "追逐", "锁定", "小队" },
            DeveloperTableData.UnitHeaders);
    }

    [Test]
    public void UnitRows_ShowPointersAndSquadId()
    {
        var a = CreateUnit("战士", UnitTeam.Player, Vector3.zero);
        var enemy = CreateUnit("敌人", UnitTeam.Enemy, new Vector3(5f, 0f, 0f));
        a.gameObject.AddComponent<StubLocomotion>();
        a.gameObject.AddComponent<WarriorBrain>();

        var state = UnitTargetState.Ensure(a.gameObject);
        state.SetChase(enemy.transform);

        SquadRegistry.Create(new[] { a });

        var rows = DeveloperTableData.BuildUnitRows();
        var row = FindRow(rows, "战士");

        Assert.IsNotNull(row, "单位表应包含战士");
        Assert.AreEqual("Player", row[1]);
        Assert.AreEqual("敌人", row[3], "追逐列应显示目标名");
        Assert.AreEqual("-", row[4], "锁定列为空时应显示 -");
        Assert.AreEqual("1", row[5]);
    }

    [Test]
    public void UnitRows_SkipDeadUnits()
    {
        var a = CreateUnit("战士", UnitTeam.Player, Vector3.zero);
        SetField(a.Health, "destroyOnDeath", false);
        a.Health.TakeDamage(999f);

        var rows = DeveloperTableData.BuildUnitRows();

        Assert.IsNull(FindRow(rows, "战士"), "死亡单位不应出现在单位表");
    }

    [Test]
    public void SquadRows_ShowIdNameAndFormation()
    {
        var a = CreateUnit("A", UnitTeam.Player, Vector3.zero);
        var b = CreateUnit("B", UnitTeam.Player, new Vector3(2f, 0f, 0f));
        SquadRegistry.Create(new[] { a, b });

        var rows = DeveloperTableData.BuildSquadRows();

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("1", rows[0][0]);
        Assert.AreEqual("Player", rows[0][2]);
        Assert.AreEqual("Grid", rows[0][3]);
        Assert.AreEqual("2", rows[0][4]);
        Assert.AreEqual("-", rows[0][6]);
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

    private static string[] FindRow(List<string[]> rows, string name)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Length > 0 && rows[i][0] == name)
            {
                return rows[i];
            }
        }

        return null;
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
