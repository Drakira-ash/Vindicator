using System.Collections.Generic;
using UnityEngine;

/// <summary>小队行为树输出的状态。</summary>
public enum SquadState
{
    Idle,
    Moving,
    Engaging
}

/// <summary>
/// 一个玩家小队：保存成员、队形、锚点与当前命令，并把槽位下发给成员。
/// 不直接驱动移动/攻击——成员 Brain 读取 <see cref="UnitCommander"/> 后执行，
/// 这样小队逻辑可以在 EditMode 中直接测试。
/// </summary>
[DisallowMultipleComponent]
public class Squad : MonoBehaviour
{
    [SerializeField] private int squadId;
    [SerializeField] private string squadName = "小队";
    [SerializeField] private UnitTeam team = UnitTeam.Player;
    [SerializeField] private FormationType formation = FormationType.Grid;
    [SerializeField, Min(0.1f)] private float spacing = FormationLayout.DefaultSpacing;
    [SerializeField, Min(0.1f)] private float arriveRadius = 0.6f;

    private readonly List<UnitCommander> members = new List<UnitCommander>();
    private readonly List<Vector3> slots = new List<Vector3>();
    private Vector3 anchor;
    private Vector3 facing = Vector3.forward;
    private UnitHealth attackTarget;
    private bool moveOrderActive;
    private bool manualControl;
    private SquadState state = SquadState.Idle;

    public int SquadId => squadId;
    public string SquadName => squadName;
    public UnitTeam Team => team;
    public FormationType Formation => formation;
    public float Spacing => spacing;
    public float ArriveRadius => arriveRadius;
    public IReadOnlyList<UnitCommander> Members => members;
    public IReadOnlyList<Vector3> Slots => slots;
    public Vector3 Anchor => anchor;
    public Vector3 Facing => facing;
    public bool MoveOrderActive => moveOrderActive;
    public SquadState State => state;

    /// <summary>
    /// 手动接管中：玩家给部分成员下过个体命令，行为树暂停自动接敌，
    /// 直到收到新的小队级命令（<see cref="MoveTo"/> / <see cref="Attack"/>）为止。
    /// </summary>
    public bool ManualControl => manualControl;

    /// <summary>当前集火目标；目标死亡后返回 null。</summary>
    public UnitHealth AttackTarget
    {
        get { return attackTarget != null && attackTarget.IsAlive ? attackTarget : null; }
    }

    /// <summary>初始化编号与显示名（由 <see cref="SquadRegistry"/> 调用）。</summary>
    public void Initialize(int id, string displayName, UnitTeam squadTeam = UnitTeam.Player)
    {
        squadId = id;
        squadName = string.IsNullOrEmpty(displayName) ? "小队 " + id : displayName;
        team = squadTeam;
    }

    /// <summary>切换队形；移动中会立刻重排槽位。</summary>
    public void SetFormation(FormationType value)
    {
        formation = value;
        if (moveOrderActive)
        {
            RepositionSlots(true);
        }
    }

    /// <summary>加入一个成员；不同阵营、已死亡或已在本队时返回 false。</summary>
    public bool AddMember(Unit unit)
    {
        if (unit == null || unit.Health == null || !unit.Health.IsAlive || unit.Team != team)
        {
            return false;
        }

        var commander = UnitCommander.Ensure(unit);
        if (commander == null || members.Contains(commander))
        {
            return false;
        }

        if (commander.SquadId >= 0 && commander.SquadId != squadId)
        {
            var previous = SquadRegistry.Find(commander.SquadId);
            if (previous != null && previous != this)
            {
                previous.RemoveMember(commander);

                // 整队改编后旧小队已无成员，直接散掉，避免面板里留下 0 人空壳。
                if (previous.Members.Count == 0)
                {
                    previous.Disband();
                }
            }
        }

        commander.SquadId = squadId;
        members.Add(commander);

        if (moveOrderActive)
        {
            RepositionSlots(true);
        }

        return true;
    }

    /// <summary>批量加入成员，返回实际加入数量。</summary>
    public int AddMembers(IEnumerable<Unit> units)
    {
        if (units == null)
        {
            return 0;
        }

        int added = 0;
        foreach (var unit in units)
        {
            if (AddMember(unit))
            {
                added++;
            }
        }

        return added;
    }

    /// <summary>移除成员并清空其命令与小队归属。</summary>
    public bool RemoveMember(UnitCommander member)
    {
        if (member == null || !members.Remove(member))
        {
            return false;
        }

        member.ClearSquad();

        if (moveOrderActive)
        {
            RepositionSlots(true);
        }

        return true;
    }

    /// <summary>成员单位列表（跳过已销毁对象）。</summary>
    public List<Unit> MemberUnits()
    {
        var result = new List<Unit>(members.Count);
        for (int i = 0; i < members.Count; i++)
        {
            var unit = members[i] != null ? members[i].GetComponent<Unit>() : null;
            if (unit != null)
            {
                result.Add(unit);
            }
        }

        return result;
    }

    /// <summary>成员质心（水平面）；无成员时返回自身位置。</summary>
    public Vector3 Centroid()
    {
        Vector3 sum = Vector3.zero;
        int count = 0;

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null)
            {
                sum += members[i].transform.position;
                count++;
            }
        }

        return count > 0 ? sum / count : transform.position;
    }

    /// <summary>按当前队形向锚点移动；每个成员获得一个独立槽位。</summary>
    public void MoveTo(Vector3 worldAnchor, Vector3 worldFacing, bool snapToNavMesh = true)
    {
        if (members.Count == 0)
        {
            return;
        }

        anchor = worldAnchor;
        facing = FormationLayout.Flatten(worldFacing);
        attackTarget = null;
        moveOrderActive = true;
        manualControl = false;
        state = SquadState.Moving;
        RepositionSlots(snapToNavMesh);
    }

    /// <summary>全队集火目标；目标无效时清空命令。</summary>
    public void Attack(UnitHealth target)
    {
        if (target == null || !target.IsAlive || members.Count == 0)
        {
            CompleteOrders();
            return;
        }

        attackTarget = target;
        moveOrderActive = false;
        manualControl = false;
        state = SquadState.Engaging;

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null)
            {
                members[i].IssueAttack(target, squadId);
            }
        }
    }

    /// <summary>所有成员是否已到达各自槽位（命令已清空视为到达）。</summary>
    public bool AllMembersArrived()
    {
        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member == null || member.OrderType != UnitOrderType.Move)
            {
                continue;
            }

            if (UnitCommander.HorizontalDistance(member.transform.position, member.Destination) > arriveRadius)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>移动完成：清空成员命令并回到 Idle。</summary>
    public void CompleteMoveOrder()
    {
        moveOrderActive = false;
        state = SquadState.Idle;
        ClearMemberOrders();
    }

    /// <summary>清空所有命令（移动/攻击）并回到 Idle。</summary>
    public void CompleteOrders()
    {
        moveOrderActive = false;
        attackTarget = null;
        state = SquadState.Idle;
        ClearMemberOrders();
    }

    /// <summary>行为树写状态；保留给面板与开发者表展示。</summary>
    public void SetState(SquadState next)
    {
        state = next;
    }

    /// <summary>
    /// 由指挥层设置“手动接管”。接管期间行为树不自动接敌，
    /// 避免把玩家刚下的个体移动/攻击命令改写成小队集火。
    /// </summary>
    public void SetManualControl(bool value)
    {
        manualControl = value;
    }

    /// <summary>每帧维护：剔除死亡成员、完成到达判定、清理死亡目标。</summary>
    public void Tick()
    {
        bool removed = PruneDeadMembers();

        if (members.Count == 0)
        {
            if (moveOrderActive || attackTarget != null || state != SquadState.Idle)
            {
                CompleteOrders();
            }

            return;
        }

        if (moveOrderActive && AllMembersArrived())
        {
            CompleteMoveOrder();
            return;
        }

        if (attackTarget != null && !attackTarget.IsAlive)
        {
            CompleteOrders();
        }
    }

    /// <summary>解散：清空成员命令与归属，销毁小队对象。</summary>
    public void Disband()
    {
        CompleteOrders();
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null)
            {
                members[i].ClearSquad();
            }
        }

        members.Clear();
        slots.Clear();
        SquadRegistry.Unregister(this);
        DestroyNow(gameObject);
    }

    private void RepositionSlots(bool snapToNavMesh)
    {
        FormationLayout.FillSlots(anchor, facing, members.Count, formation, spacing, slots);

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] == null)
            {
                continue;
            }

            Vector3 slot = slots[i];
            if (snapToNavMesh &&
                FormationLayout.TrySnapToNavMesh(slot, spacing * 2f, out var snapped))
            {
                slot = snapped;
            }

            members[i].IssueMove(slot, squadId);
        }
    }

    private bool PruneDeadMembers()
    {
        bool removed = false;

        for (int i = members.Count - 1; i >= 0; i--)
        {
            var member = members[i];
            var unit = member != null ? member.GetComponent<Unit>() : null;
            if (member == null || unit == null || unit.Health == null || !unit.Health.IsAlive)
            {
                if (member != null)
                {
                    member.ClearSquad();
                }

                members.RemoveAt(i);
                removed = true;
            }
        }

        if (removed && moveOrderActive)
        {
            RepositionSlots(true);
        }

        return removed;
    }

    private void ClearMemberOrders()
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null)
            {
                members[i].ClearOrder();
            }
        }
    }

    private static void DestroyNow(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(target);
        }
        else
        {
            Object.DestroyImmediate(target);
        }
    }
}
