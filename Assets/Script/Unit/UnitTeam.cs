/// <summary>
/// 单位阵营。中立单位与任何一方都不敌对。
/// </summary>
public enum UnitTeam
{
    Player,
    Enemy,
    Neutral
}

/// <summary>
/// 阵营关系判定工具。
/// </summary>
public static class UnitTeamUtility
{
    /// <summary>
    /// 判断两个阵营是否敌对：不同阵营且都不是中立。
    /// </summary>
    public static bool IsHostile(UnitTeam self, UnitTeam other)
    {
        return self != other && self != UnitTeam.Neutral && other != UnitTeam.Neutral;
    }
}
