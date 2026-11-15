using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 项目资产冒烟测试：只依赖 AssetDatabase 与游戏程序集，
/// 用来验证载入的工程结构、关键资产和模板数值没有断链。
/// </summary>
public class ProjectSmokeTests
{
    private const string UnitDataRoot = "Assets/UnitData";

    [Test]
    public void UnitTemplates_PerceptionMatchesDesign()
    {
        Assert.AreEqual(7f, LoadDefinition("Enemy_Normal").perceptionRange, 0.001f, "敌人感知应为 7");
        Assert.AreEqual(4f, LoadDefinition("Warrior_Guard").perceptionRange, 0.001f, "战士感知应为 4（11.8 计划下调）");
        Assert.AreEqual(2f, LoadDefinition("Citizen").perceptionRange, 0.001f, "公民感知应为 2");
    }

    [Test]
    public void CitizenTemplate_MatchesAllDesignValues()
    {
        var citizen = LoadDefinition("Citizen");
        Assert.AreEqual(30f, citizen.maxHealth, 0.001f, "公民血量应为 30");
        Assert.AreEqual(2f, citizen.attack, 0.001f, "公民攻击应为 2");
        Assert.AreEqual(2f, citizen.moveSpeed, 0.001f, "公民移速应为 2");
        Assert.AreEqual(2f, citizen.attackInterval, 0.001f, "公民攻击间隔应为 2");
        Assert.AreEqual(1.5f, citizen.range, 0.001f, "公民射程应为 1.5");
        Assert.AreEqual(2f, citizen.perceptionRange, 0.001f, "公民感知应为 2");
        Assert.AreEqual(100f, citizen.productivity, 0.001f, "公民生产力应为 100");
    }

    [Test]
    public void TowerTemplate_EffectiveAttackRangeIs20Point6()
    {
        var definition = LoadDefinition("Tower_Arrow");
        Assert.AreEqual(500f, definition.maxHealth, 0.001f, "箭塔血量应为 500");
        var probe = new GameObject("TowerRangeProbe");

        try
        {
            var health = probe.AddComponent<UnitHealth>();
            var unit = probe.AddComponent<Unit>();
            probe.AddComponent<AttackAbility>();

            SetField(unit, "definition", definition);
            Invoke(unit, "Awake");

            Assert.AreEqual(500f, health.Max, 0.001f, "建成后箭塔血量上限应为模板的 500");
            Assert.AreEqual(20.6f, unit.GetAttackRange(), 0.001f, "箭塔射程应为 20 + 命中容差 0.6");
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }
    }

    [TestCase("Assets/UnitData/Enemy_Normal.asset")]
    [TestCase("Assets/UnitData/Tower_Arrow.asset")]
    [TestCase("Assets/UnitData/Citizen.asset")]
    public void UnitDefinitionAssetsExist(string path)
    {
        Assert.IsNotNull(AssetDatabase.LoadMainAssetAtPath(path), "缺少单位模板: " + path);
    }

    [TestCase("Assets/Prefabs/Unit/Enemy_Normal.prefab")]
    [TestCase("Assets/Prefabs/Unit/Tower_Arrow.prefab")]
    [TestCase("Assets/Prefabs/Unit/Citizen.prefab")]
    [TestCase("Assets/Prefabs/Unit/Ruin_Tower.prefab")]
    public void UnitPrefabsExist(string path)
    {
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(path), "缺少单位预制体: " + path);
    }

    [Test]
    public void AcceptanceSceneExists()
    {
        Assert.IsNotNull(
            AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/RoadWorkflow_Test.unity"),
            "缺少验收场景 RoadWorkflow_Test.unity");
    }

    private static UnitDefinition LoadDefinition(string name)
    {
        var definition = AssetDatabase.LoadAssetAtPath<UnitDefinition>(
            UnitDataRoot + "/" + name + ".asset");
        Assert.IsNotNull(definition, "缺少单位模板: " + name);
        return definition;
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
}
