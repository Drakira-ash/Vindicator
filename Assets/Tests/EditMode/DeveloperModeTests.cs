using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 开发者模式测试：每个可感知单位一个半透明球、半径 = 范围 × 2、无 Collider、
/// 关闭后全部清理、重复开关不泄漏、死亡单位不再显示。
/// EditMode 下不自动执行 Awake/Update，直接调用 SetEnabled 驱动。
/// </summary>
public class DeveloperModeTests
{
    private readonly List<GameObject> created = new List<GameObject>();
    private readonly List<UnitHealth> healths = new List<UnitHealth>();
    private DeveloperModeController controller;

    [SetUp]
    public void SetUp()
    {
        DeveloperMode.SetEnabled(false);
        controller = CreateObject("DeveloperModeTest").AddComponent<DeveloperModeController>();
    }

    [TearDown]
    public void TearDown()
    {
        if (controller != null)
        {
            controller.SetEnabled(false);
        }

        DeveloperMode.SetEnabled(false);

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
    }

    [Test]
    public void StartEnabled_DefaultsToFalse()
    {
        var field = typeof(DeveloperModeController).GetField(
            "startEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "未找到 startEnabled 字段");
        Assert.AreEqual(false, field.GetValue(controller), "设计 §6.1 要求默认关闭");
    }

    [Test]
    public void Awake_AppliesStartEnabled_TrueOpensAndFalseStaysClosed()
    {
        CreateUnit("Mode_A", 3f, UnitTeam.Player);

        SetField(controller, "startEnabled", true);
        Invoke(controller, "Awake");

        Assert.IsTrue(DeveloperMode.Enabled, "startEnabled=true 时 Awake 应打开开发者模式");

        // 播放模式下由 Update 每帧同步；EditMode 里直接调用同一同步入口。
        Invoke(controller, "SyncSpheres");
        Assert.AreEqual(ExpectedSphereCount(), controller.SphereCount);
        AssertSphere("Perception_Mode_A", 3f);
    }

    [Test]
    public void Awake_WithDefaultStartEnabled_StaysClosed()
    {
        CreateUnit("Mode_A", 3f, UnitTeam.Player);

        Invoke(controller, "Awake");

        Assert.IsFalse(DeveloperMode.Enabled, "默认 startEnabled=false 时 Awake 不应打开");
        Assert.AreEqual(0, controller.SphereCount);
        Assert.IsNull(GameObject.Find("Perception_Mode_A"));
    }

    [Test]
    public void Toggle_SwitchesState_AndClearsOnOff()
    {
        CreateUnit("Mode_A", 3f, UnitTeam.Player);

        controller.Toggle();
        Assert.IsTrue(DeveloperMode.Enabled, "第一次 Toggle 应打开");
        Assert.IsNotNull(GameObject.Find("Perception_Mode_A"));

        controller.Toggle();
        Assert.IsFalse(DeveloperMode.Enabled, "第二次 Toggle 应关闭");
        Assert.AreEqual(0, controller.SphereCount);
        Assert.IsNull(GameObject.Find("Perception_Mode_A"));
    }

    [Test]
    public void Enabled_CreatesSpherePerPerceivingUnit_WithRadiusAndNoCollider()
    {
        CreateUnit("Mode_A", 3f, UnitTeam.Player);
        CreateUnit("Mode_B", 7f, UnitTeam.Enemy);
        CreateUnit("Mode_Zero", 0f, UnitTeam.Neutral);

        controller.SetEnabled(true);

        Assert.IsTrue(DeveloperMode.Enabled, "开关应变为开启");
        Assert.AreEqual(ExpectedSphereCount(), controller.SphereCount, "每个范围 > 0 的单位一个球");
        AssertSphere("Perception_Mode_A", 3f);
        AssertSphere("Perception_Mode_B", 7f);
        Assert.IsNull(GameObject.Find("Perception_Mode_Zero"), "范围为 0 的单位不应建球");
    }

    [Test]
    public void Disabled_ClearsAllSpheres()
    {
        CreateUnit("Mode_A", 3f, UnitTeam.Player);
        controller.SetEnabled(true);
        Assert.IsNotNull(GameObject.Find("Perception_Mode_A"));

        controller.SetEnabled(false);

        Assert.IsFalse(DeveloperMode.Enabled);
        Assert.AreEqual(0, controller.SphereCount, "关闭后字典应清空");
        Assert.IsNull(GameObject.Find("Perception_Mode_A"), "关闭后球体对象应销毁");
    }

    [Test]
    public void RepeatedToggle_DoesNotLeak()
    {
        CreateUnit("Mode_A", 3f, UnitTeam.Player);

        for (int i = 0; i < 3; i++)
        {
            controller.SetEnabled(true);
            Assert.AreEqual(ExpectedSphereCount(), controller.SphereCount, "第 " + i + " 次开启数量应稳定");
            Assert.AreEqual(
                controller.SphereCount,
                CountPerceptionObjects(),
                "第 " + i + " 次开启后场景中不应有重复球体");

            controller.SetEnabled(false);
            Assert.AreEqual(0, controller.SphereCount);
            Assert.AreEqual(0, CountPerceptionObjects());
        }
    }

    [Test]
    public void DeadUnit_IsRemovedOnNextSync()
    {
        var health = CreateUnit("Mode_A", 3f, UnitTeam.Player);
        controller.SetEnabled(true);
        Assert.IsNotNull(GameObject.Find("Perception_Mode_A"));

        SetField(health, "destroyOnDeath", false);
        health.TakeDamage(999f);
        Assert.IsFalse(health.IsAlive);

        controller.SetEnabled(true); // 已开启时再次调用仍会重新同步

        Assert.IsNull(GameObject.Find("Perception_Mode_A"), "死亡单位的球体应被清理");
        Assert.AreEqual(ExpectedSphereCount(), controller.SphereCount);
    }

    private UnitHealth CreateUnit(string name, float range, UnitTeam team)
    {
        var go = CreateObject(name);
        var health = go.AddComponent<UnitHealth>();
        health.Initialize(100f, team);
        UnitRegistry.Register(health);
        healths.Add(health);

        var stub = go.AddComponent<PerceptionSourceStub>();
        stub.Range = range;
        return health;
    }

    private GameObject CreateObject(string name)
    {
        var go = new GameObject(name);
        created.Add(go);
        return go;
    }

    private static void AssertSphere(string name, float range)
    {
        var sphere = GameObject.Find(name);
        Assert.IsNotNull(sphere, "缺少球体: " + name);
        Assert.AreEqual(Vector3.one * (range * 2f), sphere.transform.localScale, "球体半径应为范围 × 2");
        Assert.IsNull(sphere.GetComponent<Collider>(), "球体不得带 Collider");
        Assert.AreEqual(LayerMask.NameToLayer("Ignore Raycast"), sphere.layer, "球体应在 Ignore Raycast 层");
    }

    /// <summary>按注册表统计当前应显示球体的单位数，避免受测试顺序影响。</summary>
    private static int ExpectedSphereCount()
    {
        int count = 0;
        var all = UnitRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var health = all[i];
            if (health == null || !health.IsAlive)
            {
                continue;
            }

            var source = health.GetComponent<IPerceptionSource>();
            if (source != null && source.PerceptionRange > 0f)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountPerceptionObjects()
    {
        int count = 0;
        var renderers = Object.FindObjectsByType<MeshRenderer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].name.StartsWith("Perception_"))
            {
                count++;
            }
        }

        return count;
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
