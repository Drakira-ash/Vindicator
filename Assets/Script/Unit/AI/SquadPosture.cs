/// <summary>小队姿态。</summary>
public enum SquadPosture
{
    /// <summary>戒备：感知提高（3→6），战斗结束后不返回原位置。</summary>
    Alert,

    /// <summary>驻守：待在集结点附近，敌人离开集结点 5m 即停止追击并返回。</summary>
    Garrison
}

/// <summary>姿态默认规则：友军驻守，敌方戒备。</summary>
public static class SquadPostureRules
{
    public static SquadPosture DefaultFor(UnitTeam team)
    {
        return team == UnitTeam.Enemy ? SquadPosture.Alert : SquadPosture.Garrison;
    }
}
