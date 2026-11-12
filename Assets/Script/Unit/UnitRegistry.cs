using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局可攻击目标注册表。单位生成/启用时注册，死亡/禁用时注销。
/// 用于替代每帧 FindGameObjectsWithTag 的全局搜索。
/// </summary>
public static class UnitRegistry
{
    private static readonly List<UnitHealth> units = new List<UnitHealth>();

    /// <summary>当前所有已注册目标（只读）。</summary>
    public static IReadOnlyList<UnitHealth> All => units;

    /// <summary>
    /// 注册表内容发生变化时触发（注册或注销各触发一次）。
    /// UI 面板据此在单位生成/死亡时及时刷新，而不必每帧轮询。
    /// 订阅方必须在 OnDisable 中成对注销，避免残留静态引用。
    /// </summary>
    public static event Action Changed;

    public static void Register(UnitHealth health)
    {
        if (health != null && !units.Contains(health))
        {
            units.Add(health);
            Changed?.Invoke();
        }
    }

    public static void Unregister(UnitHealth health)
    {
        if (units.Remove(health))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// 查找距离指定位置最近的敌对存活目标。
    /// </summary>
    /// <param name="position">查询位置（世界坐标）。</param>
    /// <param name="selfTeam">自身阵营。</param>
    /// <param name="maxDistance">最大搜索距离，超出不算命中。</param>
    /// <returns>最近的目标；没有则返回 null。</returns>
    public static UnitHealth FindNearestHostile(Vector3 position, UnitTeam selfTeam, float maxDistance)
    {
        UnitHealth best = null;
        float bestDistance = maxDistance;

        for (int i = 0; i < units.Count; i++)
        {
            var health = units[i];
            if (health == null || !health.IsAlive || !UnitTeamUtility.IsHostile(selfTeam, health.Team))
            {
                continue;
            }

            float distance = health.DistanceTo(position);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = health;
            }
        }

        return best;
    }
}
