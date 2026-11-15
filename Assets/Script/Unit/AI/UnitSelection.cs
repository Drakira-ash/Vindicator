using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家当前选择。只保存存活的可指挥单位，变化时广播事件供面板刷新。
/// 与输入层解耦，EditMode 可直接设置选择后测试命令下发。
/// </summary>
public static class UnitSelection
{
    private static readonly List<Unit> selected = new List<Unit>();

    /// <summary>选择变化事件。</summary>
    public static event Action Changed;

    /// <summary>当前选中单位（只读）。</summary>
    public static IReadOnlyList<Unit> All => selected;

    public static int Count => selected.Count;

    /// <summary>替换选择。</summary>
    public static void Set(IEnumerable<Unit> units)
    {
        selected.Clear();
        AddInternal(units);
        Changed?.Invoke();
    }

    /// <summary>追加选择。</summary>
    public static void Add(IEnumerable<Unit> units)
    {
        AddInternal(units);
        Changed?.Invoke();
    }

    /// <summary>切换单个单位（Shift 点击）。</summary>
    public static void Toggle(Unit unit)
    {
        if (unit == null)
        {
            return;
        }

        if (selected.Contains(unit))
        {
            selected.Remove(unit);
        }
        else
        {
            selected.Add(unit);
        }

        Changed?.Invoke();
    }

    public static void Clear()
    {
        if (selected.Count == 0)
        {
            return;
        }

        selected.Clear();
        Changed?.Invoke();
    }

    public static bool Contains(Unit unit)
    {
        return unit != null && selected.Contains(unit);
    }

    /// <summary>剔除已死亡/已销毁的单位。</summary>
    public static void Prune()
    {
        int removed = selected.RemoveAll(unit =>
            unit == null || unit.Health == null || !unit.Health.IsAlive);

        if (removed > 0)
        {
            Changed?.Invoke();
        }
    }

    private static void AddInternal(IEnumerable<Unit> units)
    {
        if (units == null)
        {
            return;
        }

        foreach (var unit in units)
        {
            if (unit == null || unit.Health == null || !unit.Health.IsAlive)
            {
                continue;
            }

            if (!selected.Contains(unit))
            {
                selected.Add(unit);
            }
        }
    }
}
