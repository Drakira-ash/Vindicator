using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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
///
/// 移动采用“锚点跟随”：MoveTo 只记录目标点，Advance 每帧推进锚点、转向并重发槽位；
/// 成员始终只执行“去自己的槽位”，行进中因此能保持队形。
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

    [Header("跟随移动")]
    [SerializeField, Min(0.05f)] private float settleRadius = 0.3f;
    [SerializeField, Min(0.1f)] private float followSpeedFactor = 0.9f;
    [SerializeField, Min(0.1f)] private float fallbackMemberSpeed = 4f;
    [SerializeField, Min(0f)] private float startFormationError = 1f;
    [SerializeField, Min(0f)] private float slowDownError = 2f;
    [SerializeField, Min(0f)] private float stopFormationError = 3.5f;
    [SerializeField, Min(1f)] private float facingTurnSpeed = 180f;
    [SerializeField, Min(0.02f)] private float slotSnapInterval = 0.2f;
    [SerializeField, Min(0.01f)] private float arrivalTolerance = 0.15f;

    [Tooltip("锚点到达后等待成员落位的最长时间（秒），0 表示不超时。"
           + "槽位吸附到够不到的 NavMesh 边缘时，避免小队永远卡在 Moving。")]
    [SerializeField, Min(0f)] private float settleTimeout = 4f;

    [Header("姿态与集结点")]
    [SerializeField] private SquadPosture posture = SquadPosture.Garrison;
    [SerializeField] private Vector3 rallyPoint;
    [SerializeField] private bool hasRallyPoint;
    [SerializeField] private bool rallyAuto = true;
    [SerializeField, Min(0.1f)] private float garrisonLeashRange = 5f;

    private readonly List<UnitCommander> members = new List<UnitCommander>();
    private readonly List<UnitCommander> orderedMembers = new List<UnitCommander>();
    private readonly List<UnitCommander> pendingMembers = new List<UnitCommander>();
    private readonly List<Vector3> slots = new List<Vector3>();
    private readonly List<Vector3> rallySlots = new List<Vector3>();

    private Vector3 anchor;
    private Vector3 anchorTarget;
    private Vector3 facing = Vector3.forward;
    private Vector3 finalFacing = Vector3.forward;
    private UnitHealth attackTarget;
    private bool moveOrderActive;
    private bool manualControl;
    private bool waitingForFormation;
    private bool settleSnapDone;
    private float settleTimer;
    private bool slotAssignmentDirty = true;
    private bool navMeshSnapping;
    private float slotTimer;
    private SquadState state = SquadState.Idle;

    private NavMeshPath anchorPath;
    private Vector3[] anchorCorners;
    private int anchorCornerIndex;
    private bool anchorPathValid;

    public int SquadId => squadId;
    public string SquadName => squadName;
    public UnitTeam Team => team;
    public FormationType Formation => formation;
    public float Spacing => spacing;
    public float ArriveRadius => arriveRadius;
    public float SettleRadius => settleRadius;
    public IReadOnlyList<UnitCommander> Members => members;
    public IReadOnlyList<Vector3> Slots => slots;
    public Vector3 Anchor => anchor;
    public Vector3 AnchorTarget => anchorTarget;
    public Vector3 Facing => facing;
    public bool MoveOrderActive => moveOrderActive;
    public bool WaitingForFormation => waitingForFormation;
    public SquadState State => state;
    public SquadPosture Posture => posture;
    public Vector3 RallyPoint => rallyPoint;
    public bool HasRallyPoint => hasRallyPoint;
    public float GarrisonLeashRange => garrisonLeashRange;

    /// <summary>当前队形误差（成员到各自槽位的最大水平距离）。</summary>
    public float FormationError => MaxFormationError();

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
        posture = SquadPostureRules.DefaultFor(team);
        hasRallyPoint = false;
        rallyAuto = true;
    }

    /// <summary>切换队形；移动中立刻重排槽位，空闲时原地按新队形整队。</summary>
    public void SetFormation(FormationType value)
    {
        bool changed = formation != value;
        formation = value;
        if (moveOrderActive)
        {
            RepositionSlots(navMeshSnapping, true);
        }

        PushPostureToMembers();

        // 空闲且未交战时立即按新队形整队。这里直接给每名成员下发普通移动命令，
        // 不进入小队 Moving 状态：戒备姿态不推驻守槽位，若不做这一步阵型按钮没有可见效果；
        // 同时避免某个够不到的槽位让小队一直卡在 Moving（那会压掉自动接敌）。
        if (changed && !moveOrderActive && AttackTarget == null && members.Count > 0)
        {
            anchor = posture == SquadPosture.Garrison && hasRallyPoint
                ? rallyPoint
                : Centroid();
            RepositionSlots(navMeshSnapping, false);
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
        commander.SlotIndex = -1;
        members.Add(commander);
        slotAssignmentDirty = true;

        if (rallyAuto)
        {
            rallyPoint = Centroid();
            hasRallyPoint = true;
        }

        if (moveOrderActive)
        {
            RepositionSlots(navMeshSnapping, true);
        }

        PushPostureToMembers();
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

        ClearPostureOnMember(member);
        member.ClearSquad();
        slotAssignmentDirty = true;

        if (moveOrderActive)
        {
            RepositionSlots(navMeshSnapping, true);
        }

        PushPostureToMembers();
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

    /// <summary>取离参考点最近、且能吸附到 NavMesh 的成员位置；全部失败时返回参考点。</summary>
    private Vector3 NearestMemberNavMeshPoint(Vector3 reference)
    {
        Vector3 best = reference;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] == null)
            {
                continue;
            }

            Vector3 position = members[i].transform.position;
            if (!FormationLayout.TrySnapToNavMesh(position, 1.5f, out var snapped))
            {
                continue;
            }

            float distance = UnitCommander.HorizontalDistance(reference, snapped);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = snapped;
            }
        }

        return best;
    }

    /// <summary>
    /// 整队移动到目标点。锚点从当前质心出发，按最慢成员速度跟随；
    /// 到达后等待全部成员进入 <see cref="settleRadius"/> 再结束。
    /// </summary>
    public void MoveTo(Vector3 worldAnchor, Vector3 worldFacing, bool snapToNavMesh = true)
    {
        if (members.Count == 0)
        {
            return;
        }

        navMeshSnapping = snapToNavMesh;
        Vector3 centroid = Centroid();
        anchor = centroid;
        if (snapToNavMesh)
        {
            if (FormationLayout.TrySnapToNavMesh(centroid, spacing * 2f, out var startSnap))
            {
                anchor = startSnap;
            }
            else
            {
                // 成员分散在不同平台时，质心可能落在 NavMesh 外（空中/缝隙）。
                // 退而取“离质心最近的、落在 NavMesh 上的成员位置”作为起始锚点。
                anchor = NearestMemberNavMeshPoint(centroid);
            }
        }

        anchorTarget = worldAnchor;
        if (snapToNavMesh &&
            (FormationLayout.TrySnapToNavMesh(worldAnchor, spacing * 2f, out var targetSnap) ||
             FormationLayout.TrySnapToNavMesh(worldAnchor, spacing * 4f, out targetSnap)))
        {
            anchorTarget = targetSnap;
        }

        Vector3 toTarget = HorizontalDelta(anchor, anchorTarget);
        Vector3 flatFacing = worldFacing;
        flatFacing.y = 0f;
        finalFacing = flatFacing.sqrMagnitude > 0.0001f
            ? flatFacing.normalized
            : (toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : facing);
        facing = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : finalFacing;

        attackTarget = null;
        moveOrderActive = true;
        manualControl = false;
        waitingForFormation = true;
        settleSnapDone = false;
        settleTimer = 0f;
        slotTimer = 0f;
        state = SquadState.Moving;

        if (snapToNavMesh)
        {
            BuildAnchorPath();
        }
        else
        {
            // 测试/特殊场景明确要求不走 NavMesh：锚点走直线。
            anchorPathValid = false;
            anchorCorners = null;
        }

        RepositionSlots(snapToNavMesh, true);
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
        waitingForFormation = false;
        anchorPathValid = false;
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

    /// <summary>所有成员是否已进入各自槽位的指定半径（按槽位而不是命令判定）。</summary>
    public bool AllMembersSettled(float radius)
    {
        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member == null || !IsMemberAlive(member))
            {
                continue;
            }

            Vector3 slot = SlotFor(member);
            if (UnitCommander.HorizontalDistance(member.transform.position, slot) > radius)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>移动完成：清空成员命令、整队转向最终朝向并回到 Idle。</summary>
    public void CompleteMoveOrder()
    {
        moveOrderActive = false;
        waitingForFormation = false;
        anchorPathValid = false;
        facing = finalFacing;
        state = SquadState.Idle;
        ClearMemberOrders();

        // 集结点自动跟随落点：驻守单位以此为中心回位。
        rallyPoint = anchor;
        hasRallyPoint = true;
        rallyAuto = true;
        PushPostureToMembers();

        FaceMembers(finalFacing);
    }

    /// <summary>清空所有命令（移动/攻击）并回到 Idle。</summary>
    public void CompleteOrders()
    {
        moveOrderActive = false;
        waitingForFormation = false;
        anchorPathValid = false;
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

    /// <summary>切换小队姿态，并把新的感知/拴绳/驻守点同步给成员。</summary>
    public void SetPosture(SquadPosture value)
    {
        bool changed = posture != value;
        posture = value;
        PushPostureToMembers();

        // 空闲时切回驻守：在集结点重新整队（走跟随移动，不瞬移）。
        // 战斗中或已有命令时不打断，驻守回位点已在 PushPostureToMembers 中更新。
        if (changed && posture == SquadPosture.Garrison &&
            !moveOrderActive && AttackTarget == null && hasRallyPoint)
        {
            MoveTo(rallyPoint, facing);
        }
    }

    /// <summary>手动设置集结点（本轮自动跟随落点，此 API 预留给后续操作）。</summary>
    public void SetRallyPoint(Vector3 point)
    {
        if (FormationLayout.TrySnapToNavMesh(point, spacing * 2f, out var snapped) ||
            FormationLayout.TrySnapToNavMesh(point, spacing * 4f, out snapped))
        {
            point = snapped;
        }

        rallyPoint = point;
        hasRallyPoint = true;
        rallyAuto = false;
        PushPostureToMembers();

        if (posture == SquadPosture.Garrison && !moveOrderActive && AttackTarget == null)
        {
            MoveTo(rallyPoint, facing);
        }
    }

    /// <summary>每帧维护：剔除死亡成员、清理死亡目标；移动完成由 Advance 判定。</summary>
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

        if (attackTarget != null && !attackTarget.IsAlive)
        {
            CompleteOrders();
        }
    }

    /// <summary>
    /// 推进跟随移动。由 <see cref="SquadBrain.Tick"/> 每帧调用；
    /// EditMode 可直接调用并传入固定 dt。
    /// </summary>
    public void Advance(float deltaTime)
    {
        if (!moveOrderActive || members.Count == 0)
        {
            return;
        }

        if (deltaTime < 0f)
        {
            deltaTime = 0f;
        }

        float error = MaxFormationError();
        bool anchorArrived =
            UnitCommander.HorizontalDistance(anchor, anchorTarget) <= arrivalTolerance;

        // 起步：先等队形基本成型，避免拖着一字长蛇出发。
        if (waitingForFormation)
        {
            if (error > startFormationError)
            {
                // 锚点已到落点却长期等不齐（槽位够不到）时按超时收尾，
                // 否则小队会永远停在 Moving，自动接敌与后续命令都被压掉。
                if (anchorArrived)
                {
                    settleTimer += deltaTime;
                    if (settleTimeout > 0f && settleTimer >= settleTimeout)
                    {
                        waitingForFormation = false;
                        CompleteMoveOrder();
                    }
                }

                return;
            }

            waitingForFormation = false;
        }

        if (!anchorArrived)
        {
            settleTimer = 0f;

            float speed = EffectiveAnchorSpeed(error);
            if (speed > 0.001f && deltaTime > 0f)
            {
                StepAnchor(speed * deltaTime);
            }

            UpdateTravelFacing(deltaTime);

            slotTimer -= deltaTime;
            bool snap = navMeshSnapping && slotTimer <= 0f;
            if (snap)
            {
                slotTimer = Mathf.Max(0.02f, slotSnapInterval);
            }

            RepositionSlots(snap, true);

            if (UnitCommander.HorizontalDistance(anchor, anchorTarget) > arrivalTolerance)
            {
                return;
            }

            settleSnapDone = false;
        }

        // 到达落点：吸附一次最终槽位，转向最终朝向后等待成员落位。
        if (!settleSnapDone)
        {
            RepositionSlots(navMeshSnapping, true);
            settleSnapDone = true;
        }

        UpdateFinalFacing(deltaTime);
        if (Vector3.Angle(facing, finalFacing) > 1f)
        {
            RepositionSlots(false, true);
            return;
        }

        // 个别槽位可能吸附到够不到的 NavMesh 边缘：超时后照常结束，
        // 避免小队永久停在 Moving（那会压掉自动接敌与后续命令）。
        settleTimer += deltaTime;
        if (AllMembersSettled(settleRadius) ||
            (settleTimeout > 0f && settleTimer >= settleTimeout))
        {
            CompleteMoveOrder();
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
                ClearPostureOnMember(members[i]);
                members[i].ClearSquad();
            }
        }

        members.Clear();
        slots.Clear();
        SquadRegistry.Unregister(this);
        DestroyNow(gameObject);
    }

    private void RepositionSlots(bool snapToNavMesh, bool follow)
    {
        int count = members.Count;
        FormationLayout.FillSlots(anchor, facing, count, formation, spacing, slots);
        if (count == 0)
        {
            return;
        }

        EnsureSlotIndices(count);

        orderedMembers.Clear();
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null)
            {
                orderedMembers.Add(members[i]);
            }
        }

        orderedMembers.Sort(CompareSlotIndex);

        for (int i = 0; i < orderedMembers.Count && i < slots.Count; i++)
        {
            var member = orderedMembers[i];
            Vector3 slot = slots[i];
            if (snapToNavMesh)
            {
                if (FormationLayout.TrySnapToNavMesh(slot, spacing * 2f, out var snapped) ||
                    FormationLayout.TrySnapToNavMesh(slot, spacing * 4f, out snapped))
                {
                    slot = snapped;
                }
                else if (FormationLayout.TrySnapToNavMesh(anchor, 0.5f, out _))
                {
                    // 槽位在平台边缘/缝隙上：退到锚点，避免成员收到 NavMesh 外目标后原地卡死。
                    // 锚点本身也不在 NavMesh 时（例如没有烘焙数据的 EditMode）保留原始槽位。
                    slot = anchor;
                }
            }

            // 槽位记录实际下发的目标：FormationError / AllMembersSettled 都按它判定，
            // 否则 NavMesh 吸附后的目标和原始槽位会不一致，起步永远等不齐。
            slots[i] = slot;
            member.IssueMove(slot, squadId, follow);
        }
    }

    /// <summary>
    /// 稳定槽位分配：已有唯一有效下标的成员不动；
    /// 新加入/下标越界的成员按“离自己当前位置最近且未占用”补位。
    /// </summary>
    private void EnsureSlotIndices(int count)
    {
        if (!slotAssignmentDirty || count <= 0)
        {
            return;
        }

        var used = new bool[count];
        pendingMembers.Clear();

        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member == null)
            {
                continue;
            }

            int index = member.SlotIndex;
            if (index >= 0 && index < count && !used[index])
            {
                used[index] = true;
            }
            else
            {
                pendingMembers.Add(member);
            }
        }

        for (int i = 0; i < pendingMembers.Count; i++)
        {
            var member = pendingMembers[i];
            int bestIndex = -1;
            float bestDistance = float.MaxValue;

            for (int slotIndex = 0; slotIndex < count; slotIndex++)
            {
                if (used[slotIndex] || slotIndex >= slots.Count)
                {
                    continue;
                }

                float distance = UnitCommander.HorizontalDistance(
                    member.transform.position, slots[slotIndex]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = slotIndex;
                }
            }

            if (bestIndex < 0)
            {
                // 理论上不会发生；保底避免索引越界。
                bestIndex = Mathf.Clamp(member.SlotIndex, 0, count - 1);
            }

            member.SlotIndex = bestIndex;
            used[bestIndex] = true;
        }

        slotAssignmentDirty = false;
    }

    private float MaxFormationError()
    {
        float max = 0f;
        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member == null || !IsMemberAlive(member))
            {
                continue;
            }

            float distance = UnitCommander.HorizontalDistance(
                member.transform.position, SlotFor(member));
            if (distance > max)
            {
                max = distance;
            }
        }

        return max;
    }

    private Vector3 SlotFor(UnitCommander member)
    {
        int index = member.SlotIndex;
        return index >= 0 && index < slots.Count ? slots[index] : member.transform.position;
    }

    private float EffectiveAnchorSpeed(float error)
    {
        float slowest = SlowestMemberSpeed();
        float baseSpeed = slowest > 0.01f ? slowest : fallbackMemberSpeed;
        float speed = Mathf.Max(0f, baseSpeed * followSpeedFactor);

        if (error > slowDownError)
        {
            float upper = Mathf.Max(slowDownError + 0.01f, stopFormationError);
            float t = Mathf.InverseLerp(upper, slowDownError, error);
            speed *= Mathf.Clamp01(t);
        }

        return Mathf.Max(0f, speed);
    }

    private float SlowestMemberSpeed()
    {
        float slowest = float.MaxValue;
        for (int i = 0; i < members.Count; i++)
        {
            var unit = members[i] != null ? members[i].GetComponent<Unit>() : null;
            if (unit == null || unit.Stats == null)
            {
                continue;
            }

            float speed = unit.Stats.Get(StatType.MoveSpeed);
            if (speed > 0.01f && speed < slowest)
            {
                slowest = speed;
            }
        }

        return slowest == float.MaxValue ? 0f : slowest;
    }

    private void BuildAnchorPath()
    {
        anchorCorners = null;
        anchorCornerIndex = 1;
        anchorPathValid = false;

        if (anchorPath == null)
        {
            anchorPath = new NavMeshPath();
        }

        if (NavMesh.CalculatePath(anchor, anchorTarget, NavMesh.AllAreas, anchorPath) &&
            anchorPath.status == NavMeshPathStatus.PathComplete &&
            anchorPath.corners.Length >= 2)
        {
            anchorCorners = anchorPath.corners;
            anchorPathValid = true;
        }
    }

    private void StepAnchor(float distance)
    {
        if (!anchorPathValid)
        {
            Vector3 delta = HorizontalDelta(anchor, anchorTarget);
            float remaining = delta.magnitude;
            if (remaining <= 0.0001f)
            {
                anchor = anchorTarget;
                return;
            }

            float step = Mathf.Min(distance, remaining);
            anchor += delta / remaining * step;
            if (UnitCommander.HorizontalDistance(anchor, anchorTarget) <= arrivalTolerance)
            {
                anchor = anchorTarget;
            }

            return;
        }

        float left = distance;
        int guard = 0;
        while (left > 0.0001f && guard++ < 64)
        {
            if (anchorCorners == null || anchorCornerIndex >= anchorCorners.Length)
            {
                anchor = anchorTarget;
                break;
            }

            Vector3 delta = HorizontalDelta(anchor, anchorCorners[anchorCornerIndex]);
            float remaining = delta.magnitude;
            if (remaining <= 0.001f)
            {
                anchorCornerIndex++;
                continue;
            }

            float step = Mathf.Min(left, remaining);
            anchor += delta / remaining * step;
            left -= step;

            if (step >= remaining - 0.001f)
            {
                anchorCornerIndex++;
            }
        }

        if (UnitCommander.HorizontalDistance(anchor, anchorTarget) <= arrivalTolerance)
        {
            anchor = anchorTarget;
        }
    }

    private void UpdateTravelFacing(float deltaTime)
    {
        Vector3 desired;
        if (anchorPathValid && anchorCorners != null && anchorCornerIndex < anchorCorners.Length)
        {
            desired = HorizontalDelta(anchor, anchorCorners[anchorCornerIndex]);
        }
        else
        {
            desired = HorizontalDelta(anchor, anchorTarget);
        }

        if (desired.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float maxRadians = Mathf.Max(0f, facingTurnSpeed) * Mathf.Deg2Rad * deltaTime;
        Vector3 next = Vector3.RotateTowards(facing, desired.normalized, maxRadians, 0f);
        next.y = 0f;
        if (next.sqrMagnitude > 0.0001f)
        {
            facing = next.normalized;
        }
    }

    private void UpdateFinalFacing(float deltaTime)
    {
        if (Vector3.Angle(facing, finalFacing) <= 1f)
        {
            facing = finalFacing;
            return;
        }

        float maxRadians = Mathf.Max(0f, facingTurnSpeed) * Mathf.Deg2Rad * deltaTime;
        Vector3 next = Vector3.RotateTowards(facing, finalFacing, maxRadians, 0f);
        next.y = 0f;
        if (next.sqrMagnitude > 0.0001f)
        {
            facing = next.normalized;
        }

        if (Vector3.Angle(facing, finalFacing) <= 1f)
        {
            facing = finalFacing;
        }
    }

    private void FaceMembers(Vector3 direction)
    {
        Vector3 flat = direction;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f)
        {
            return;
        }

        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member == null || !IsMemberAlive(member))
            {
                continue;
            }

            var locomotion = member.GetComponent<UnitLocomotion>();
            if (locomotion != null)
            {
                locomotion.FaceDirection(flat);
            }
            else
            {
                member.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
        }
    }

    /// <summary>把姿态、集结点与驻守回位点推送给成员 Brain。</summary>
    private void PushPostureToMembers()
    {
        if (members.Count == 0)
        {
            return;
        }

        bool hasGuardSlots = false;
        if (posture == SquadPosture.Garrison && hasRallyPoint)
        {
            FormationLayout.FillSlots(
                rallyPoint, facing, members.Count, formation, spacing, rallySlots);
            hasGuardSlots = rallySlots.Count == members.Count;
        }

        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member == null)
            {
                continue;
            }

            var brain = member.GetComponent<WarriorBrain>();
            if (brain == null)
            {
                continue;
            }

            Vector3 guardPoint = member.transform.position;
            bool hasGuardPoint = false;

            if (hasGuardSlots)
            {
                int index = member.SlotIndex;
                if (index < 0 || index >= rallySlots.Count)
                {
                    index = members.IndexOf(member);
                }

                guardPoint = rallySlots[Mathf.Clamp(index, 0, rallySlots.Count - 1)];
                if (FormationLayout.TrySnapToNavMesh(guardPoint, spacing * 2f, out var snappedGuard) ||
                    FormationLayout.TrySnapToNavMesh(guardPoint, spacing * 4f, out snappedGuard))
                {
                    guardPoint = snappedGuard;
                }
                else if (FormationLayout.TrySnapToNavMesh(rallyPoint, 0.5f, out var snappedRally))
                {
                    guardPoint = snappedRally;
                }

                hasGuardPoint = true;
            }

            brain.ApplySquadPosture(
                posture, rallyPoint, hasRallyPoint, garrisonLeashRange,
                guardPoint, hasGuardPoint);
        }
    }

    private static void ClearPostureOnMember(UnitCommander member)
    {
        if (member == null)
        {
            return;
        }

        var brain = member.GetComponent<WarriorBrain>();
        if (brain != null)
        {
            brain.ClearSquadPosture();
        }
    }

    private bool PruneDeadMembers()
    {
        bool removed = false;

        for (int i = members.Count - 1; i >= 0; i--)
        {
            var member = members[i];
            if (member == null || !IsMemberAlive(member))
            {
                if (member != null)
                {
                    member.ClearSquad();
                }

                members.RemoveAt(i);
                removed = true;
                slotAssignmentDirty = true;
            }
        }

        if (removed && moveOrderActive)
        {
            RepositionSlots(false, true);
        }

        if (removed)
        {
            PushPostureToMembers();
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

    private static bool IsMemberAlive(UnitCommander member)
    {
        var unit = member != null ? member.GetComponent<Unit>() : null;
        return unit != null && unit.Health != null && unit.Health.IsAlive;
    }

    private static Vector3 HorizontalDelta(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        delta.y = 0f;
        return delta;
    }

    private static int CompareSlotIndex(UnitCommander a, UnitCommander b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a == null)
        {
            return 1;
        }

        if (b == null)
        {
            return -1;
        }

        return a.SlotIndex.CompareTo(b.SlotIndex);
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
