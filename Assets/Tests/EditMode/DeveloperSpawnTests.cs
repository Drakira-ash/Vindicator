using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 开发者模式生成/销毁测试：目录解析、生成阵营与组件、任意位置放置、
/// K 销毁选中、面板逐行销毁，以及面板/放置模式对世界输入的屏蔽。
/// EditMode 下不自动执行 Awake/OnEnable/Update，按需显式调用。
/// </summary>
public class DeveloperSpawnTests
{
    private readonly List<GameObject> created = new List<GameObject>();
    private readonly List<UnitHealth> healths = new List<UnitHealth>();

    private DeveloperSpawnController controller;
    private DeveloperSpawnCatalog catalog;

    [SetUp]
    public void SetUp()
    {
        DeveloperMode.SetEnabled(false);
        UnitSelection.Clear();
        SquadRegistry.Clear();

        catalog = DeveloperSpawnCatalog.Load();
        Assert.IsNotNull(catalog, "应能从 Resources/Developer/SpawnCatalog.asset 加载生成目录");
    }

    [TearDown]
    public void TearDown()
    {
        DeveloperMode.SetEnabled(false);
        UnitSelection.Clear();
        SquadRegistry.Clear();

        if (controller != null)
        {
            controller.CancelPlacement();
        }

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
    public void Catalog_HasMilitiaAndEnemyEntries()
    {
        var militia = catalog.Find(DeveloperSpawnCatalog.MilitiaId);
        Assert.IsNotNull(militia, "目录应包含民兵条目");
        Assert.IsNotNull(militia.Prefab, "民兵条目应引用 Warrior_Guard 预制体");
        Assert.AreEqual(UnitTeam.Player, militia.Team, "民兵属于玩家方");

        var enemy = catalog.Find(DeveloperSpawnCatalog.EnemyId);
        Assert.IsNotNull(enemy, "目录应包含普通敌人条目");
        Assert.IsNotNull(enemy.Prefab, "敌人条目应引用 Enemy_Normal_Nav 预制体");
        Assert.AreEqual(UnitTeam.Enemy, enemy.Team, "普通敌人属于敌方");
    }

    [Test]
    public void SpawnEntry_Militia_IsPlayerWarriorAtRequestedPosition()
    {
        var entry = catalog.Find(DeveloperSpawnCatalog.MilitiaId);
        var requested = new Vector3(12f, 0f, -7f);

        var spawn = Spawn(entry, requested);

        Assert.IsNotNull(spawn);
        Assert.AreEqual(0f, Vector3.Distance(requested, spawn.transform.position), 0.01f,
            "关闭吸附后应生成在指定的任意位置");
        Assert.IsNotNull(spawn.GetComponent<WarriorBrain>(), "民兵应带 WarriorBrain");
        Assert.AreEqual(UnitTeam.Player, spawn.GetComponent<UnitHealth>().Team, "生成的民兵必须站在玩家方");
        Assert.IsTrue(spawn.name.StartsWith(entry.Prefab.name + "_Dev_"), "生成物用 _Dev_ 前缀命名");
    }

    [Test]
    public void SpawnEntry_Enemy_IsEnemyWithEnemyBrain()
    {
        var entry = catalog.Find(DeveloperSpawnCatalog.EnemyId);

        var spawn = Spawn(entry, new Vector3(-9f, 0f, 4f));

        Assert.IsNotNull(spawn);
        Assert.AreEqual(UnitTeam.Enemy, spawn.GetComponent<UnitHealth>().Team);
        Assert.IsNotNull(spawn.GetComponent<EnemyBrain>(), "普通敌人应带 EnemyBrain");
    }

    [Test]
    public void SpawnEntry_RegistersUnitInRegistry()
    {
        var entry = catalog.Find(DeveloperSpawnCatalog.MilitiaId);

        var spawn = Spawn(entry, new Vector3(3f, 0f, 3f));
        var health = spawn.GetComponent<UnitHealth>();

        // EditMode 下 Instantiate 不会自动触发 OnEnable，手动补一次以验证注册路径。
        Invoke(health, "OnEnable");

        CollectionAssert.Contains(UnitRegistry.All, health, "生成的单位应注册到 UnitRegistry");
    }

    [Test]
    public void ArmThenPlace_SpawnsOnceAndLeavesPlacementMode()
    {
        var entry = catalog.Find(DeveloperSpawnCatalog.MilitiaId);
        var spawner = CreateController();

        Assert.IsTrue(spawner.ArmEntry(entry));
        Assert.IsTrue(spawner.IsPlacing);
        Assert.AreSame(entry, spawner.ArmedEntry);

        Assert.IsTrue(spawner.TryPlaceAt(new Vector3(20f, 0f, 20f)));
        Assert.IsFalse(spawner.IsPlacing, "放置成功后应退出放置模式");
        Assert.IsNull(spawner.ArmedEntry);
        Assert.AreEqual(1, spawner.SpawnedCount);

        TrackSpawn(spawner.LastSpawned);
    }

    [Test]
    public void ArmingSameEntryTwice_CancelsPlacement()
    {
        var entry = catalog.Find(DeveloperSpawnCatalog.MilitiaId);
        var spawner = CreateController();

        Assert.IsTrue(spawner.ArmEntry(entry));
        Assert.IsFalse(spawner.ArmEntry(entry), "再次点击同一条目应取消放置");
        Assert.IsFalse(spawner.IsPlacing);
        Assert.IsNull(spawner.ArmedEntry);
    }

    [Test]
    public void DestroySelectedUnits_KillsSelectionAndClearsIt()
    {
        var spawner = CreateController();
        var unitA = CreateUnit("SelectedA", UnitTeam.Player, new Vector3(0f, 0f, 0f));
        var unitB = CreateUnit("SelectedB", UnitTeam.Player, new Vector3(1f, 0f, 0f));
        UnitSelection.Set(new[] { unitA, unitB });

        int destroyed = spawner.DestroySelectedUnits();

        Assert.AreEqual(2, destroyed);
        Assert.IsFalse(unitA.Health.IsAlive);
        Assert.IsFalse(unitB.Health.IsAlive);
        Assert.AreEqual(0, UnitSelection.Count, "销毁后选择应清空");
    }

    [Test]
    public void DestroySelectedUnits_WithoutSelection_DoesNothing()
    {
        var spawner = CreateController();

        Assert.AreEqual(0, spawner.DestroySelectedUnits());
    }

    [Test]
    public void DestroyUnit_CanKillAnyEnemyFromPanel()
    {
        var spawner = CreateController();
        var enemy = CreateUnit("AnyEnemy", UnitTeam.Enemy, new Vector3(5f, 0f, 5f));

        Assert.IsTrue(spawner.DestroyUnit(enemy.Health));
        Assert.IsFalse(enemy.Health.IsAlive);
        Assert.IsFalse(spawner.DestroyUnit(enemy.Health), "已死亡单位不应重复销毁");
    }

    [Test]
    public void BlocksWorldInput_FollowsPlacementModeAndDeveloperMode()
    {
        var spawner = CreateController();
        Invoke(spawner, "Awake");
        var entry = catalog.Find(DeveloperSpawnCatalog.MilitiaId);

        DeveloperMode.SetEnabled(true);
        Assert.IsFalse(DeveloperSpawnController.BlocksWorldInput(new Vector2(10f, 10f)),
            "未进入放置模式且鼠标不在面板上时不应屏蔽世界输入");

        spawner.ArmEntry(entry);
        Assert.IsTrue(DeveloperSpawnController.BlocksWorldInput(new Vector2(10f, 10f)),
            "放置模式下左键应留给放置而不是框选");

        spawner.CancelPlacement();
        DeveloperMode.SetEnabled(false);
        Assert.IsFalse(DeveloperSpawnController.BlocksWorldInput(new Vector2(10f, 10f)),
            "关闭开发者模式后不应屏蔽世界输入");
    }

    [Test]
    public void Panel_ExpandsAndCollapses()
    {
        var panel = CreateObject("PanelTest").AddComponent<DeveloperSpawnPanel>();

        Assert.IsTrue(panel.IsExpanded, "面板默认展开");
        panel.ToggleExpanded();
        Assert.IsFalse(panel.IsExpanded);
        panel.SetExpanded(true);
        Assert.IsTrue(panel.IsExpanded);
    }

    [Test]
    public void IsSelectable_AllowsEnemyOnlyInDeveloperMode()
    {
        var enemy = CreateUnit("SelectableEnemy", UnitTeam.Enemy, Vector3.zero);

        DeveloperMode.SetEnabled(false);
        Assert.IsFalse(PlayerCommandController.IsSelectable(enemy),
            "普通模式下敌人不可选中");

        DeveloperMode.SetEnabled(true);
        Assert.IsTrue(PlayerCommandController.IsSelectable(enemy),
            "开发者模式下敌人应可选中以便编辑");
        Assert.IsFalse(PlayerCommandController.IsCommandable(enemy),
            "敌人可选中但不接受玩家命令");

        var warrior = CreateWarrior("SelectableWarrior", Vector3.zero);
        DeveloperMode.SetEnabled(true);
        Assert.IsTrue(PlayerCommandController.IsCommandable(warrior));
        Assert.IsTrue(PlayerCommandController.IsSelectable(warrior));
    }

    [Test]
    public void SelectAndDestroyEnemy_InDeveloperMode()
    {
        var spawner = CreateController();
        var enemy = CreateUnit("EnemyToDestroy", UnitTeam.Enemy, new Vector3(4f, 0f, 4f));
        var commands = CreateObject("PlayerCommandsTest").AddComponent<PlayerCommandController>();

        DeveloperMode.SetEnabled(true);
        commands.SelectUnits(new[] { enemy });
        Assert.AreEqual(1, UnitSelection.Count, "开发者模式下点选/框选敌人应进入选择");

        Assert.AreEqual(1, spawner.DestroySelectedUnits(), "按 K 应能销毁选中的敌人");
        Assert.IsFalse(enemy.Health.IsAlive);
        Assert.AreEqual(0, UnitSelection.Count, "销毁后选择应清空");
    }

    [Test]
    public void MoveCommand_DoesNotCommandSelectedEnemy()
    {
        var commands = CreateObject("PlayerCommandsTest").AddComponent<PlayerCommandController>();
        var warrior = CreateWarrior("CommandWarrior", Vector3.zero);
        var enemy = CreateUnit("CommandEnemy", UnitTeam.Enemy, new Vector3(2f, 0f, 2f));

        DeveloperMode.SetEnabled(true);
        commands.SelectUnits(new[] { warrior, enemy });
        Assert.AreEqual(2, UnitSelection.Count, "开发者模式下混编选择应包含敌人");

        Assert.IsTrue(commands.IssueMoveCommand(new Vector3(10f, 0f, 10f), Vector3.forward));

        var warriorCommander = warrior.GetComponent<UnitCommander>();
        Assert.IsNotNull(warriorCommander, "友军应收到移动命令");
        Assert.IsTrue(warriorCommander.HasOrder);
        Assert.IsNull(enemy.GetComponent<UnitCommander>(), "敌人不应收到玩家命令");
        Assert.IsNull(commands.SelectionSquad(), "敌人不应参与玩家小队");
    }

    [Test]
    public void DisablingDeveloperMode_DropsSelectedEnemies()
    {
        var commands = CreateObject("PlayerCommandsTest").AddComponent<PlayerCommandController>();
        Invoke(commands, "Awake");
        var warrior = CreateWarrior("KeptWarrior", Vector3.zero);
        var enemy = CreateUnit("DroppedEnemy", UnitTeam.Enemy, new Vector3(2f, 0f, 2f));

        DeveloperMode.SetEnabled(true);
        commands.SelectUnits(new[] { warrior, enemy });
        Assert.AreEqual(2, UnitSelection.Count, "开发者模式下混编选择应包含敌人");

        DeveloperMode.SetEnabled(false);
        Assert.AreEqual(1, UnitSelection.Count, "关闭开发者模式后应剔除敌人");
        Assert.AreSame(warrior, UnitSelection.All[0]);
        Assert.IsFalse(UnitSelection.Contains(enemy));
    }

    [Test]
    public void UnitsInScreenRect_IncludesEnemyOnlyInDeveloperMode()
    {
        var cameraObject = CreateObject("BoxSelectCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 10f, 0f);
        camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var enemy = CreateUnit("BoxSelectEnemy", UnitTeam.Enemy, Vector3.zero);
        UnitRegistry.Register(enemy.Health);

        // 覆盖整个屏幕，避免依赖 Game 视图的实际分辨率。
        var screen = new Rect(-10000f, -10000f, 20000f, 20000f);

        DeveloperMode.SetEnabled(false);
        CollectionAssert.DoesNotContain(PlayerCommandController.UnitsInScreenRect(screen, camera), enemy,
            "普通模式下框选不应包含敌人");

        DeveloperMode.SetEnabled(true);
        CollectionAssert.Contains(PlayerCommandController.UnitsInScreenRect(screen, camera), enemy,
            "开发者模式下框选应包含敌人");
    }

    [Test]
    public void FindPatrolGoal_PrefersSpawnerThenFallsBackToEnemy()
    {
        var spawnerGoal = CreateObject("SpawnerPatrolGoal").transform;
        var enemyGoal = CreateObject("EnemyPatrolGoal").transform;

        var spawner = CreateObject("GoalSpawner").AddComponent<UnitSpawner>();
        var enemy = CreateObject("GoalEnemy").AddComponent<EnemyBrain>();

        Assert.IsNull(DeveloperSpawnController.FindPatrolGoal(new[] { spawner }, new[] { enemy }),
            "两者都没有巡逻目标时应返回 null，敌人保持原地待命");

        SetField(enemy, "patrolGoal", enemyGoal);
        Assert.AreSame(enemyGoal,
            DeveloperSpawnController.FindPatrolGoal(new[] { spawner }, new[] { enemy }),
            "没有 spawner 配置时应退化为复用已有敌人的巡逻目标");

        SetField(spawner, "patrolGoal", spawnerGoal);
        Assert.AreSame(spawnerGoal,
            DeveloperSpawnController.FindPatrolGoal(new[] { spawner }, new[] { enemy }),
            "应优先使用 UnitSpawner 配置的巡逻目标");
    }

    [Test]
    public void SpawnedEnemy_GetsPatrolGoalFromSceneSpawner()
    {
        var goal = CreateObject("DevPatrolGoal").transform;
        var spawnerObject = CreateObject("DevPatrolSpawner");
        var unitSpawner = spawnerObject.AddComponent<UnitSpawner>();
        SetField(unitSpawner, "patrolGoal", goal);

        var entry = catalog.Find(DeveloperSpawnCatalog.EnemyId);
        var spawn = Spawn(entry, new Vector3(2f, 0f, 2f));
        var brain = spawn.GetComponent<EnemyBrain>();

        Assert.IsNotNull(brain);
        // 场景里还有原有 spawner，FindObjectsByType 顺序不保证；
        // 这里只验证“生成后确实接到了巡逻目标”，优先级与身份由 FindPatrolGoal_* 覆盖。
        Assert.IsNotNull(brain.PatrolGoal,
            "生成的敌人应继承场景中的巡逻终点，否则会一直原地待命");
    }

    private DeveloperSpawnController CreateController()
    {
        controller = CreateObject("DeveloperSpawnTest").AddComponent<DeveloperSpawnController>();
        controller.Catalog = catalog;
        SetField(controller, "navMeshSnapRadius", 0f);
        return controller;
    }

    private GameObject Spawn(DeveloperSpawnCatalog.Entry entry, Vector3 position)
    {
        var spawner = CreateController();
        var spawn = spawner.SpawnEntry(entry, position);
        TrackSpawn(spawn);
        return spawn;
    }

    private void TrackSpawn(GameObject spawn)
    {
        if (spawn == null || created.Contains(spawn))
        {
            return;
        }

        created.Add(spawn);
        var health = spawn.GetComponent<UnitHealth>();
        if (health != null)
        {
            healths.Add(health);
        }
    }

    private Unit CreateUnit(string name, UnitTeam team, Vector3 position)
    {
        var go = CreateObject(name);
        go.transform.position = position;

        var health = go.AddComponent<UnitHealth>();
        health.Initialize(100f, team);
        SetField(health, "destroyOnDeath", false);
        healths.Add(health);

        var unit = go.AddComponent<Unit>();
        Invoke(unit, "Awake");
        return unit;
    }

    private Unit CreateWarrior(string name, Vector3 position)
    {
        var unit = CreateUnit(name, UnitTeam.Player, position);
        // 补移动桩，避免 WarriorBrain.Awake 因缺少 UnitLocomotion 报错。
        unit.gameObject.AddComponent<StubLocomotion>();
        unit.gameObject.AddComponent<WarriorBrain>();
        return unit;
    }

    private GameObject CreateObject(string name)
    {
        var go = new GameObject(name);
        created.Add(go);
        return go;
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
