using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局建筑注册表。公民通过它查找感知范围内的友方建筑。
/// </summary>
public static class UnitBuildingRegistry
{
    private static readonly List<UnitBuilding> buildings = new List<UnitBuilding>();

    /// <summary>当前所有已注册建筑（只读）。</summary>
    public static IReadOnlyList<UnitBuilding> All => buildings;

    public static void Register(UnitBuilding building)
    {
        if (building != null && !buildings.Contains(building))
        {
            buildings.Add(building);
        }
    }

    public static void Unregister(UnitBuilding building)
    {
        buildings.Remove(building);
    }

    /// <summary>
    /// 查找最近的同队建筑。<paramref name="preferRuined"/> 为 true 时先找废墟，
    /// 范围内没有废墟才退回已建成建筑。
    /// </summary>
    public static UnitBuilding FindNearestFriendly(
        Vector3 position, UnitTeam team, float maxDistance, bool preferRuined)
    {
        if (maxDistance <= 0f)
        {
            return null;
        }

        if (preferRuined)
        {
            var ruined = FindNearest(position, team, maxDistance, true);
            if (ruined != null)
            {
                return ruined;
            }
        }

        return FindNearest(position, team, maxDistance, false);
    }

    private static UnitBuilding FindNearest(
        Vector3 position, UnitTeam team, float maxDistance, bool ruinedOnly)
    {
        UnitBuilding best = null;
        float bestDistance = maxDistance;

        for (int i = 0; i < buildings.Count; i++)
        {
            var building = buildings[i];
            if (building == null || building.Team != team)
            {
                continue;
            }

            if (ruinedOnly && !building.IsRuined)
            {
                continue;
            }

            float distance = building.DistanceTo(position);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = building;
            }
        }

        return best;
    }
}
