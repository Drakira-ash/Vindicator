using UnityEngine;

/// <summary>玩家命令类型。命令存在期间优先于单位个体 AI。</summary>
public enum UnitOrderType
{
    None,
    Move,
    Attack
}

/// <summary>
/// 玩家命令接收器：保存移动/攻击命令与所属小队编号。
/// 只做数据记录，具体执行由对应 Brain 读取；便于 EditMode 断言命令是否下发与清空。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Unit))]
public class UnitCommander : MonoBehaviour
{
    [SerializeField] private int squadId = -1;

    private UnitOrderType orderType = UnitOrderType.None;
    private Vector3 destination;
    private UnitHealth attackTarget;

    /// <summary>所属小队编号；-1 表示未编队。</summary>
    public int SquadId
    {
        get { return squadId; }
        set { squadId = value; }
    }

    /// <summary>当前命令类型。</summary>
    public UnitOrderType OrderType => orderType;

    /// <summary>是否有未完成的命令。</summary>
    public bool HasOrder => orderType != UnitOrderType.None;

    /// <summary>移动命令的目标坐标（槽位）。</summary>
    public Vector3 Destination => destination;

    /// <summary>攻击命令的目标；目标死亡后返回 null。</summary>
    public UnitHealth AttackTarget
    {
        get { return attackTarget != null && attackTarget.IsAlive ? attackTarget : null; }
    }

    /// <summary>取当前攻击目标；没有攻击命令或目标已死亡时返回 false。</summary>
    public bool TryGetAttackTarget(out UnitHealth target)
    {
        target = AttackTarget;
        return orderType == UnitOrderType.Attack && target != null;
    }

    /// <summary>下发移动命令。</summary>
    public void IssueMove(Vector3 worldPosition, int squad = -1)
    {
        orderType = UnitOrderType.Move;
        destination = worldPosition;
        attackTarget = null;

        if (squad >= 0)
        {
            squadId = squad;
        }
    }

    /// <summary>下发攻击命令；空目标或已死亡目标会被忽略。</summary>
    public void IssueAttack(UnitHealth target, int squad = -1)
    {
        if (target == null || !target.IsAlive)
        {
            return;
        }

        orderType = UnitOrderType.Attack;
        attackTarget = target;

        if (squad >= 0)
        {
            squadId = squad;
        }
    }

    /// <summary>清空命令，保留小队归属。</summary>
    public void ClearOrder()
    {
        orderType = UnitOrderType.None;
        attackTarget = null;
    }

    /// <summary>清空命令并退出小队（解散编队时使用）。</summary>
    public void ClearSquad()
    {
        ClearOrder();
        squadId = -1;
    }

    /// <summary>水平距离，编队到达判定统一用这个口径。</summary>
    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    /// <summary>确保单位身上有命令接收器。</summary>
    public static UnitCommander Ensure(Unit unit)
    {
        if (unit == null)
        {
            return null;
        }

        var commander = unit.GetComponent<UnitCommander>();
        return commander != null ? commander : unit.gameObject.AddComponent<UnitCommander>();
    }
}
