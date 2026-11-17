using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 开发者模式两张全局数据表的行数据。纯静态逻辑，EditMode 可直接断言；
/// 所有引用先做 Unity 对象有效性判断，单位/小队销毁后显示 "-"。
/// </summary>
public static class DeveloperTableData
{
    /// <summary>小队表列名。</summary>
    public static readonly string[] SquadHeaders =
    {
        "ID", "名称", "阵营", "队形", "人数", "状态", "姿态", "目标"
    };

    /// <summary>单位表列名。</summary>
    public static readonly string[] UnitHeaders =
    {
        "名称", "阵营", "状态", "追逐", "锁定", "小队"
    };

    /// <summary>小队表数据：编号 / 名称 / 阵营 / 队形 / 人数 / 状态 / 集火目标。</summary>
    public static List<string[]> BuildSquadRows()
    {
        var rows = new List<string[]>();
        var all = SquadRegistry.All;

        for (int i = 0; i < all.Count; i++)
        {
            var squad = all[i];
            if (squad == null)
            {
                continue;
            }

            var target = squad.AttackTarget;
            rows.Add(new[]
            {
                squad.SquadId.ToString(),
                squad.SquadName,
                squad.Team.ToString(),
                squad.Formation.ToString(),
                squad.MemberUnits().Count.ToString(),
                squad.State.ToString(),
                squad.Posture.ToString(),
                target != null ? target.name : "-"
            });
        }

        return rows;
    }

    /// <summary>单位表数据：名称 / 阵营 / 状态 / 追逐单位 / 锁定单位 / 小队编号。</summary>
    public static List<string[]> BuildUnitRows()
    {
        var rows = new List<string[]>();
        var all = UnitRegistry.All;

        for (int i = 0; i < all.Count; i++)
        {
            var health = all[i];
            if (health == null || !health.IsAlive)
            {
                continue;
            }

            var unit = health.GetComponent<Unit>();
            var targetState = health.GetComponent<UnitTargetState>();

            rows.Add(new[]
            {
                health.name,
                health.Team.ToString(),
                StateOf(unit),
                targetState != null ? NameOf(targetState.ChaseTarget) : "-",
                targetState != null ? NameOf(targetState.LockTarget) : "-",
                SquadIdOf(unit)
            });
        }

        return rows;
    }

    private static string StateOf(Unit unit)
    {
        if (unit == null)
        {
            return "-";
        }

        var reporter = unit.GetComponent<IUnitAgentState>();
        return reporter != null && !string.IsNullOrEmpty(reporter.StateName) ? reporter.StateName : "-";
    }

    private static string SquadIdOf(Unit unit)
    {
        if (unit == null)
        {
            return "-";
        }

        var commander = unit.GetComponent<UnitCommander>();
        return commander != null && commander.SquadId >= 0 ? commander.SquadId.ToString() : "-";
    }

    private static string NameOf(Transform target)
    {
        return target != null ? target.name : "-";
    }
}
