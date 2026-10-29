using UnityEngine;

/// <summary>
/// 单位移动后端抽象。<see cref="UnitMover"/>（贴地路点）与 <see cref="UnitMotor"/>（NavMeshAgent）都实现它，
/// AI 只依赖这层接口，不直接绑定具体移动组件。
/// </summary>
public abstract class UnitLocomotion : MonoBehaviour
{
    /// <summary>暂停/恢复移动，保留当前目标以便恢复。</summary>
    public abstract void SetPaused(bool paused);

    /// <summary>移动到世界坐标（追击目标、返回驻守点）。</summary>
    public abstract void MoveTo(Vector3 worldPosition);

    /// <summary>停止并清空当前移动目标。</summary>
    public abstract void Stop();
}
