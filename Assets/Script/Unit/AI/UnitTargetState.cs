using UnityEngine;

/// <summary>
/// 单位的目标指针数据表：追逐单位（正在靠近）与锁定单位（正在攻击）。
/// 两者互斥：追击时只写 ChaseTarget，攻击时只写 LockTarget，待命/死亡时都清空。
/// 由 Brain 维护，开发者数据表只读展示。
/// </summary>
[DisallowMultipleComponent]
public class UnitTargetState : MonoBehaviour
{
    [SerializeField] private Transform chaseTarget;
    [SerializeField] private Transform lockTarget;

    /// <summary>正在靠近的目标；对象销毁后自动视为空。</summary>
    public Transform ChaseTarget => IsValid(chaseTarget) ? chaseTarget : null;

    /// <summary>正在攻击的目标；对象销毁后自动视为空。</summary>
    public Transform LockTarget => IsValid(lockTarget) ? lockTarget : null;

    public void SetChase(Transform target)
    {
        chaseTarget = IsValid(target) ? target : null;
    }

    public void SetLock(Transform target)
    {
        lockTarget = IsValid(target) ? target : null;
    }

    public void ClearChase()
    {
        chaseTarget = null;
    }

    public void ClearLock()
    {
        lockTarget = null;
    }

    /// <summary>清空两个指针，进入 Idle/巡逻/死亡时调用。</summary>
    public void ClearAll()
    {
        chaseTarget = null;
        lockTarget = null;
    }

    /// <summary>取单位上的目标状态，没有就补一个（预制体未挂载时仍能工作）。</summary>
    public static UnitTargetState Ensure(GameObject host)
    {
        if (host == null)
        {
            return null;
        }

        var state = host.GetComponent<UnitTargetState>();
        return state != null ? state : host.AddComponent<UnitTargetState>();
    }

    private static bool IsValid(Transform target)
    {
        return target != null;
    }
}
