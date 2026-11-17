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

    /// <summary>
    /// 停止并清空当前移动目标。注意 <see cref="UnitMover"/> 的清空目标会让单位回到路线跟随，
    /// 因此需要真正静止时请用 <see cref="Halt"/>。
    /// </summary>
    public abstract void Stop();

    /// <summary>停止并保持停止：清空移动目标并暂停，用于 Idle/死亡等需要真正静止的状态。</summary>
    public void Halt()
    {
        Stop();
        SetPaused(true);
    }

    /// <summary>
    /// 原地转向指定方向（水平面）。默认直接旋转 Transform；
    /// NavMeshAgent 后端可覆写为暂停自动旋转后再转。
    /// </summary>
    public virtual void FaceDirection(Vector3 worldDirection)
    {
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
    }
}
