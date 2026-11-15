/// <summary>
/// 单位 Brain 的状态名接口，供开发者数据表统一读取。
/// 实现方：EnemyBrain、WarriorBrain、CitizenBrain、UnitTurretBrain。
/// </summary>
public interface IUnitAgentState
{
    /// <summary>当前状态名（Idle / Chase / Attack / Patrol / …）。</summary>
    string StateName { get; }
}
