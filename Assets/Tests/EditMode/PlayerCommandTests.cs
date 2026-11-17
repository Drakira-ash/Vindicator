using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 玩家指挥层测试：选择过滤、屏幕框选、编队复用/新建、移动与集火命令、数字编队。
/// 不触发真实输入。
/// </summary>
public class PlayerCommandTests
{
    private readonly List<GameObject> created = new List<GameObject>();
    private readonly List<UnitHealth> healths = new List<UnitHealth>();
    private PlayerCommandController controller;

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

        if (controller != null)
        {
            Object.DestroyImmediate(controller.gameObject);
            controller = null;
        }

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
    public void SelectUnits_FiltersHostileAndNonCommandableUnits()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        var enemy = CreateUnit("Enemy", UnitTeam.Enemy, new Vector3(1f, 0f, 1f), false);
        var plain = CreateUnit("Plain", UnitTeam.Player, new Vector3(3f, 0f, 0f), false);

        Command.SelectUnits(new[] { a, enemy, plain, b });

        Assert.AreEqual(2, UnitSelection.Count);
        Assert.IsTrue(UnitSelection.Contains(a));
        Assert.IsTrue(UnitSelection.Contains(b));
    }

    [Test]
    public void IssueMoveCommand_WithoutSquad_MovesUnitsAndDoesNotCreateSquad()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        Command.SelectUnits(new[] { a, b });

        var target = new Vector3(10f, 0f, 10f);
        Assert.IsTrue(Command.IssueMoveCommand(target, Vector3.forward));

        Assert.AreEqual(0, CountSquads(), "右键移动不应自动编队");

        var commanderA = a.GetComponent<UnitCommander>();
        var commanderB = b.GetComponent<UnitCommander>();
        Assert.AreEqual(UnitOrderType.Move, commanderA.OrderType);
        Assert.AreEqual(UnitOrderType.Move, commanderB.OrderType);
        Assert.AreEqual(-1, commanderA.SquadId, "未编队时单位不应被写入小队编号");

        Assert.Less(Vector3.Distance(commanderA.Destination, target), 3f);
        Assert.Less(Vector3.Distance(commanderB.Destination, target), 3f);
        Assert.Greater(
            Vector3.Distance(commanderA.Destination, commanderB.Destination),
            1f,
            "群体移动应保持两人的相对位置，不能挤到同一点");
    }

    [Test]
    public void IssueMoveCommand_WithWholeSquadSelected_UsesFormationMove()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        Command.SelectUnits(new[] { a, b });
        Assert.IsTrue(Command.IssueMoveCommand(new Vector3(5f, 0f, 0f), Vector3.forward));

        Assert.AreSame(squad, Command.SelectionSquad(), "完整小队不应被重建");
        Assert.AreEqual(1, CountSquads());
        Assert.IsTrue(squad.MoveOrderActive, "整队选中时应按队形移动");
    }

    [Test]
    public void IssueAttackCommand_WithWholeSquadSelected_OrdersFocusFire()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        var enemy = CreateUnit("Enemy", UnitTeam.Enemy, new Vector3(8f, 0f, 8f), false);
        var squad = SquadRegistry.Create(new[] { a, b });
        Command.SelectUnits(new[] { a, b });

        Assert.IsTrue(Command.IssueAttackCommand(enemy.Health));

        Assert.AreSame(enemy.Health, squad.AttackTarget);
        Assert.AreEqual(UnitOrderType.Attack, a.GetComponent<UnitCommander>().OrderType);
        Assert.AreEqual(UnitOrderType.Attack, b.GetComponent<UnitCommander>().OrderType);
        Assert.AreEqual(1, CountSquads(), "整队攻击不应新建小队");
    }

    [Test]
    public void IssueAttackCommand_WithoutSquad_OrdersEachUnitIndividually()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        var enemy = CreateUnit("Enemy", UnitTeam.Enemy, new Vector3(8f, 0f, 8f), false);
        Command.SelectUnits(new[] { a, b });

        Assert.IsTrue(Command.IssueAttackCommand(enemy.Health));

        Assert.AreEqual(0, CountSquads(), "右键攻击不应自动编队");
        Assert.AreEqual(UnitOrderType.Attack, a.GetComponent<UnitCommander>().OrderType);
        Assert.AreEqual(UnitOrderType.Attack, b.GetComponent<UnitCommander>().OrderType);
        Assert.AreSame(enemy.Health, a.GetComponent<UnitCommander>().AttackTarget);
    }

    [Test]
    public void IssueAttackCommand_RejectsDeadTarget()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var enemy = CreateUnit("Enemy", UnitTeam.Enemy, new Vector3(8f, 0f, 8f), false);
        Command.SelectUnits(new[] { a });
        SetField(enemy.Health, "destroyOnDeath", false);
        enemy.Health.TakeDamage(999f);

        Assert.IsFalse(Command.IssueAttackCommand(enemy.Health));
        Assert.AreEqual(0, CountSquads());
    }

    [Test]
    public void UnitsInScreenRect_PicksOnlyLivingWarriorsInsideRect()
    {
        var cameraGo = new GameObject("Camera");
        created.Add(cameraGo);
        var camera = cameraGo.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 10f, 0f);
        camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var inside = CreateWarrior("Inside", Vector3.zero);
        var outside = CreateWarrior("Outside", new Vector3(30f, 0f, 30f));

        Vector3 screen = camera.WorldToScreenPoint(inside.transform.position);
        var rect = new Rect(screen.x - 20f, screen.y - 20f, 40f, 40f);

        var picked = PlayerCommandController.UnitsInScreenRect(rect, camera);

        CollectionAssert.Contains(picked, inside);
        CollectionAssert.DoesNotContain(picked, outside);
    }

    [Test]
    public void UnitsInScreenRect_UsesRendererBoundsNotOnlyPivot()
    {
        var cameraGo = new GameObject("Camera");
        created.Add(cameraGo);
        var camera = cameraGo.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 10f, -8f);
        camera.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

        var warrior = CreateWarrior("Body", Vector3.zero);
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        created.Add(body);
        body.transform.SetParent(warrior.transform, false);
        body.transform.localPosition = new Vector3(0f, 3f, 0f);

        Vector3 bodyScreen = camera.WorldToScreenPoint(body.transform.position);
        Vector3 pivotScreen = camera.WorldToScreenPoint(warrior.transform.position);
        Assert.Greater(Mathf.Abs(bodyScreen.y - pivotScreen.y), 10f, "测试相机需要让身体与枢轴在屏幕上有明显落差");

        var rect = new Rect(bodyScreen.x - 5f, bodyScreen.y - 5f, 10f, 10f);
        Assert.IsFalse(rect.Contains(new Vector2(pivotScreen.x, pivotScreen.y)), "选择框不应覆盖枢轴");

        var picked = PlayerCommandController.UnitsInScreenRect(rect, camera);

        CollectionAssert.Contains(picked, warrior, "框住身体（而非枢轴）也应选中单位");
    }

    [Test]
    public void SelectAllPlayerWarriors_PicksOnlyCommandableWarriors()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        CreateUnit("Enemy", UnitTeam.Enemy, new Vector3(1f, 0f, 1f), false);
        CreateUnit("Plain", UnitTeam.Player, new Vector3(3f, 0f, 0f), false);

        Assert.AreEqual(2, Command.SelectAllPlayerWarriors());
        Assert.IsTrue(UnitSelection.Contains(a));
        Assert.IsTrue(UnitSelection.Contains(b));
    }

    [Test]
    public void AssignSquad_ThenSelectSquad_RestoresSelection()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        Command.SelectUnits(new[] { a, b });

        Assert.IsTrue(Command.AssignSquad(3));
        Assert.AreEqual(3, a.GetComponent<UnitCommander>().SquadId);

        UnitSelection.Clear();
        Assert.IsTrue(Command.SelectSquad(3));
        Assert.AreEqual(2, UnitSelection.Count);
    }

    [Test]
    public void SelectionSquad_ReturnsNullForEmptySelection()
    {
        Assert.IsNull(Command.SelectionSquad());
    }

    [Test]
    public void SelectionSquad_RequiresWholeSquadSelection()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        Command.SelectUnits(new[] { a, b });
        Assert.AreSame(squad, Command.SelectionSquad());

        Command.SelectUnits(new[] { a });
        Assert.IsNull(Command.SelectionSquad(), "只选中部分成员时不算整队");
    }

    [Test]
    public void CreateSquadFromSelection_AutoIncrementsIds()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));

        Command.SelectUnits(new[] { a });
        Assert.IsTrue(Command.CreateSquadFromSelection());
        Command.SelectUnits(new[] { b });
        Assert.IsTrue(Command.CreateSquadFromSelection());

        Assert.IsNotNull(SquadRegistry.Find(1), "第一个小队编号应为 1");
        Assert.IsNotNull(SquadRegistry.Find(2), "第二个小队编号应自增为 2");
        Assert.AreEqual(1, a.GetComponent<UnitCommander>().SquadId);
        Assert.AreEqual(2, b.GetComponent<UnitCommander>().SquadId);
    }

    [Test]
    public void CreateSquadFromSelection_WithEmptySelection_ReturnsFalse()
    {
        Assert.IsFalse(Command.CreateSquadFromSelection());
        Assert.AreEqual(0, CountSquads());
    }

    [Test]
    public void MoveCommand_OnPartialSquadSelection_DropsSquadMoveOrder()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });
        squad.MoveTo(new Vector3(4f, 0f, 4f), Vector3.forward, false);
        Assert.IsTrue(squad.MoveOrderActive);

        Command.SelectUnits(new[] { a });
        Assert.IsTrue(Command.IssueMoveCommand(new Vector3(9f, 0f, 0f), Vector3.forward));

        Assert.IsFalse(squad.MoveOrderActive, "给部分成员下个体命令时应让小队放弃编队移动");
        Assert.IsTrue(squad.ManualControl, "个体命令应让小队进入手动接管，暂停自动接敌");
        Assert.AreEqual(UnitOrderType.Move, a.GetComponent<UnitCommander>().OrderType);
        Assert.AreEqual(UnitOrderType.None, b.GetComponent<UnitCommander>().OrderType, "未选中的成员应停止移动");
    }

    [Test]
    public void PartialSquadMove_IsNotOverwrittenBySquadBrainAutoEngage()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        var c = CreateWarrior("C", new Vector3(0f, 0f, 2f));
        var squad = SquadRegistry.Create(new[] { a, b, c });
        var brain = squad.GetComponent<SquadBrain>();
        Invoke(brain, "Awake");
        CreateUnit("Enemy", UnitTeam.Enemy, new Vector3(1f, 0f, 1f), false);

        Command.SelectUnits(new[] { a, b });
        Assert.IsTrue(Command.IssueMoveCommand(new Vector3(9f, 0f, 0f), Vector3.forward));

        // 推进一帧小队行为树：感知范围内有敌人，但手动接管期间不得自动接敌。
        brain.Tick(0.3f);

        Assert.IsTrue(squad.ManualControl);
        Assert.IsNull(squad.AttackTarget, "部分选中下个体命令后，小队不应自动接敌覆盖命令");
        Assert.AreEqual(UnitOrderType.Move, a.GetComponent<UnitCommander>().OrderType, "个体移动命令被小队集火覆盖了");
        Assert.AreEqual(UnitOrderType.Move, b.GetComponent<UnitCommander>().OrderType, "个体移动命令被小队集火覆盖了");
        Assert.AreEqual(UnitOrderType.None, c.GetComponent<UnitCommander>().OrderType, "未选中的成员不应被拉去集火");
    }

    [Test]
    public void WholeSquadCommand_ClearsManualControl()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        Command.SelectUnits(new[] { a });
        Assert.IsTrue(Command.IssueMoveCommand(new Vector3(9f, 0f, 0f), Vector3.forward));
        Assert.IsTrue(squad.ManualControl);

        Command.SelectUnits(new[] { a, b });
        Assert.IsTrue(Command.IssueMoveCommand(new Vector3(5f, 0f, 0f), Vector3.forward));

        Assert.IsFalse(squad.ManualControl, "整队命令应解除手动接管，恢复小队行为");
        Assert.IsTrue(squad.MoveOrderActive);
    }

    [Test]
    public void SelectionSquad_IgnoresDeadMembersNotYetPruned()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        SetField(b.GetComponent<UnitHealth>(), "destroyOnDeath", false);
        b.GetComponent<UnitHealth>().TakeDamage(999f);

        // 尸体尚未被 Squad.Tick 剔除，但选择“全部存活成员”仍应识别为整队。
        Command.SelectUnits(new[] { a });
        Assert.AreSame(squad, Command.SelectionSquad());
    }

    [Test]
    public void RegroupingWholeSquad_DisbandsEmptiedPreviousSquad()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        var first = SquadRegistry.Create(new[] { a, b });
        int firstId = first.SquadId;

        Command.SelectUnits(new[] { a, b });
        Assert.IsTrue(Command.CreateSquadFromSelection());

        Assert.AreEqual(1, CountSquads(), "旧小队成员被全部移走后应自动解散，不留 0 人空壳");
        Assert.IsNull(SquadRegistry.Find(firstId));
    }

    [Test]
    public void CycleSelectedSquadFormation_RequiresWholeSquad()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        Command.SelectUnits(new[] { a, b });
        Assert.IsTrue(Command.CycleSelectedSquadFormation());
        Assert.AreNotEqual(FormationType.Grid, squad.Formation, "默认方阵应切换到下一种");

        Command.SelectUnits(new[] { a });
        Assert.IsFalse(Command.CycleSelectedSquadFormation(), "部分选中不应改阵型");
    }

    [Test]
    public void ToggleSelectedSquadPosture_RequiresWholeSquad()
    {
        var a = CreateWarrior("A", Vector3.zero);
        var b = CreateWarrior("B", new Vector3(2f, 0f, 0f));
        var squad = SquadRegistry.Create(new[] { a, b });

        Assert.AreEqual(SquadPosture.Garrison, squad.Posture);

        Command.SelectUnits(new[] { a, b });
        Assert.IsTrue(Command.ToggleSelectedSquadPosture());
        Assert.AreEqual(SquadPosture.Alert, squad.Posture);

        Command.SelectUnits(new[] { a });
        Assert.IsFalse(Command.ToggleSelectedSquadPosture(), "部分选中不应改姿态");
        Assert.AreEqual(SquadPosture.Alert, squad.Posture, "失败的命令不应改变姿态");
    }

    private PlayerCommandController Command
    {
        get
        {
            if (controller == null)
            {
                var go = new GameObject("PlayerCommands");
                created.Add(go);
                controller = go.AddComponent<PlayerCommandController>();
            }

            return controller;
        }
    }

    private Unit CreateWarrior(string name, Vector3 position)
    {
        var unit = CreateUnit(name, UnitTeam.Player, position, true);
        return unit;
    }

    private Unit CreateUnit(string name, UnitTeam team, Vector3 position, bool warrior)
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

        if (warrior)
        {
            // 先补移动桩，避免 WarriorBrain.Awake 因缺少 UnitLocomotion 报错。
            go.AddComponent<StubLocomotion>();
            go.AddComponent<WarriorBrain>();
        }

        return unit;
    }

    private static int CountSquads()
    {
        int count = 0;
        var all = SquadRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] != null)
            {
                count++;
            }
        }

        return count;
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
