using UnityEngine;

/// <summary>
/// 战士行为：驻守原地，敌对单位进入检测范围且仍在拴绳内时发起拦截请求并追击，
/// 进入攻击范围后停下攻击；目标死亡或超出拴绳距离时返回驻守点。
/// 发请求前先判拴绳：超出拴绳的目标下一帧就会被释放，会造成反复拦截/释放的抖动。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Unit))]
public class WarriorBrain : MonoBehaviour, IPerceptionSource, IUnitAgentState
{
    private enum State
    {
        Idle,
        Move,
        Chase,
        Attack,
        Return
    }

    // 民兵是宝贵资源：基础只在 3m 内主动接敌；驻守姿态保持 3m，戒备姿态提升到 6m。
    [SerializeField, Min(0f)] private float detectRange = 3f;
    [SerializeField, Min(0f)] private float alertDetectRange = 6f;
    [SerializeField, Min(0f)] private float leashRange = 14f;
    [SerializeField, Min(0.1f)] private float guardArriveThreshold = 0.5f;
    [SerializeField, Min(0.05f)] private float searchInterval = 0.25f;
    [SerializeField, Min(0.1f)] private float commandArriveRadius = 0.6f;
    [SerializeField] private bool interceptEnabled = false;
    [SerializeField] private bool logState = true;

    private Unit unit;
    private UnitLocomotion locomotion;
    private UnitCommander commander;
    private UnitTargetState targets;
    private IDamageable target;
    private Vector3 guardPosition;
    private float searchTimer;
    private State state = (State)(-1);
    private bool hadOrder;

    private bool hasSquadPosture;
    private SquadPosture squadPosture;
    private Vector3 squadRallyPoint;
    private bool hasSquadRallyPoint;
    private float squadLeashRange;
    private Vector3 squadGuardPoint;
    private bool hasSquadGuardPoint;

    /// <summary>当前目标，便于调试与测试断言。</summary>
    public IDamageable Target => target;

    /// <summary>当前状态名，便于调试与测试断言。</summary>
    public string StateName => state.ToString();

    /// <summary>是否已被某个小队设置了姿态。</summary>
    public bool HasSquadPosture => hasSquadPosture;

    /// <summary>当前生效的小队姿态。</summary>
    public SquadPosture SquadPosture => squadPosture;

    /// <summary>当前驻守回位点，便于测试断言。</summary>
    public Vector3 GuardPosition => guardPosition;

    /// <summary>
    /// 有效感知范围：基础值优先读模板属性/序列化字段；
    /// 戒备姿态下至少为 <see cref="alertDetectRange"/>。
    /// </summary>
    public float PerceptionRange
    {
        get
        {
            float stat = unit != null ? unit.Stats.Get(StatType.PerceptionRange) : 0f;
            float baseRange = stat > 0f ? stat : detectRange;
            if (hasSquadPosture && squadPosture == SquadPosture.Alert)
            {
                return Mathf.Max(baseRange, alertDetectRange);
            }

            return baseRange;
        }
    }

    private void Awake()
    {
        unit = GetComponent<Unit>();
        locomotion = GetComponent<UnitLocomotion>();
        commander = GetComponent<UnitCommander>();
        targets = UnitTargetState.Ensure(gameObject);
        guardPosition = transform.position;

        if (locomotion == null)
        {
            Debug.LogError("WarriorBrain 需要同物体上的 UnitLocomotion（UnitMotor 或 UnitMover）。", this);
        }
    }

    /// <summary>
    /// 由小队推送姿态：戒备提高感知且战后不回位；驻守把拴绳锚点设为集结点、
    /// 距离 5m，并用 <paramref name="guardPoint"/> 指定集结点附近的回位槽位。
    /// </summary>
    public void ApplySquadPosture(
        SquadPosture posture,
        Vector3 rallyPoint,
        bool hasRallyPoint,
        float leashRangeForPosture,
        Vector3 guardPoint,
        bool hasGuardPoint)
    {
        hasSquadPosture = true;
        squadPosture = posture;
        squadRallyPoint = rallyPoint;
        hasSquadRallyPoint = hasRallyPoint;
        squadLeashRange = leashRangeForPosture;

        // 记住小队下发的驻守槽位：命令结束后要回到这里，而不是停在追击途中。
        hasSquadGuardPoint = hasGuardPoint;
        if (hasGuardPoint)
        {
            squadGuardPoint = guardPoint;
            guardPosition = guardPoint;
        }

        // 姿态可能改变感知/拴绳，立即重新评估目标。
        searchTimer = 0f;
    }

    /// <summary>离开小队时清除姿态，恢复未编队时的个体行为。</summary>
    public void ClearSquadPosture()
    {
        hasSquadPosture = false;
        squadPosture = SquadPosture.Garrison;
        hasSquadRallyPoint = false;
        squadLeashRange = 0f;
        hasSquadGuardPoint = false;
        guardPosition = transform.position;
        searchTimer = 0f;
    }

    private void OnEnable()
    {
        if (unit != null && unit.Health != null)
        {
            unit.Health.Died += OnSelfDied;
        }
    }

    private void OnDisable()
    {
        if (unit != null && unit.Health != null)
        {
            unit.Health.Died -= OnSelfDied;
        }
    }

    private void Update()
    {
        if (unit == null || unit.Health == null || !unit.Health.IsAlive || locomotion == null)
        {
            return;
        }

        // UnitCommander 可能由 SquadRegistry 在运行期补挂（晚于 Awake），这里按需补取。
        if (commander == null)
        {
            commander = GetComponent<UnitCommander>();
        }

        // 玩家命令优先于个体 AI：先执行移动/集火，命令结束后再恢复驻守逻辑。
        bool hasOrder = commander != null && commander.HasOrder;
        if (hasOrder)
        {
            hadOrder = true;
            ExecuteOrder();
            return;
        }

        // 命令刚结束（尤其是小队统一清空跟随命令）：
        // - 驻守成员回到小队推下来的集结点/阵型槽位，而不是停在追击途中；
        // - 戒备或未编队成员仍把当前站位记为驻守点（战后不返回旧位置）。
        if (hadOrder)
        {
            hadOrder = false;
            guardPosition = hasSquadPosture &&
                            squadPosture == SquadPosture.Garrison &&
                            hasSquadGuardPoint
                ? squadGuardPoint
                : transform.position;
            SetState(State.Idle);
            locomotion.Halt();
        }

        if (!HasLiveTarget())
        {
            if (target != null)
            {
                targets.ClearAll();
            }

            target = null;
        }

        if (target != null && IsBeyondLeash())
        {
            Log(string.Format("[Warrior] {0} 目标 {1} 超出拴绳，放弃追击", name, target.DamageTransform.name));
            if (interceptEnabled)
            {
                var health = target as UnitHealth;
                if (health != null)
                {
                    UnitEvents.RaiseInterceptReleased(new InterceptRequest(unit, health));
                }
            }

            target = null;
            targets.ClearAll();
        }

        if (target == null)
        {
            searchTimer -= Time.deltaTime;
            if (searchTimer <= 0f)
            {
                searchTimer = searchInterval;
                AcquireTarget();
            }
        }

        if (target == null)
        {
            if (hasSquadPosture && squadPosture == SquadPosture.Alert)
            {
                StayInPlace();
                return;
            }

            Guard();
            return;
        }

        if (CheckAttack())
        {
            return;
        }

        Chase();
    }

    private void AcquireTarget()
    {
        var candidate = UnitRegistry.FindNearestHostile(transform.position, unit.Team, PerceptionRange);
        if (candidate == null)
        {
            return;
        }

        // 先判拴绳再发拦截请求，避免“请求 -> 下一帧超绳释放 -> 再请求”的抖动。
        // 驻守姿态的拴绳锚点是集结点，半径 5m；未编队时仍用 guardPosition / 14m。
        if (DistanceToLeashAnchor(candidate) > EffectiveLeashRange())
        {
            return;
        }

        target = candidate;

        // 拦截暂时关闭（2026-11-08 计划）：默认只锁定并追击，不再通知敌人转火。
        if (interceptEnabled)
        {
            UnitEvents.RaiseInterceptRequested(new InterceptRequest(unit, candidate));
            Log(string.Format("[Intercept] {0} 请求拦截 {1}", name, candidate.name));
        }
        else
        {
            Log(string.Format("[Warrior] {0} 锁定 {1}", name, candidate.name));
        }
    }

    private void ExecuteOrder()
    {
        if (commander.OrderType == UnitOrderType.Move)
        {
            target = null;
            targets.ClearAll();

            Vector3 destination = commander.Destination;
            if (!commander.FollowOrder &&
                UnitCommander.HorizontalDistance(transform.position, destination) <= commandArriveRadius)
            {
                commander.ClearOrder();
                guardPosition = transform.position;
                SetState(State.Idle);
                locomotion.Halt();
                return;
            }

            // 跟随命令到点也不清空：锚点还在移动，下一帧会给出新槽位；
            // 由小队 CompleteMoveOrder 统一清命令，避免“到点→清命令→回驻守”抖动。
            SetState(State.Move);
            locomotion.SetPaused(false);
            locomotion.MoveTo(destination);
            return;
        }

        if (commander.OrderType == UnitOrderType.Attack)
        {
            if (!commander.TryGetAttackTarget(out var attackTarget))
            {
                commander.ClearOrder();
                target = null;
                targets.ClearAll();
                Guard();
                return;
            }

            target = attackTarget;
            if (CheckAttack())
            {
                return;
            }

            Chase();
            return;
        }

        commander.ClearOrder();
    }

    private void Chase()
    {
        SetState(State.Chase);
        locomotion.SetPaused(false);
        locomotion.MoveTo(target.DamageTransform.position);
        targets.SetChase(target.DamageTransform);
        targets.ClearLock();
    }

    private bool CheckAttack()
    {
        float range = EngagedAttackRange();
        if (range <= 0f || target.DistanceTo(transform.position) > range)
        {
            return false;
        }

        SetState(State.Attack);
        locomotion.SetPaused(true);
        targets.SetLock(target.DamageTransform);
        targets.ClearChase();
        unit.ActivateAbilities(target);
        return true;
    }

    /// <summary>
    /// 接战距离取双方射程的较小者：目标射程更短时继续逼近，否则会停在目标打不到的位置。
    /// 被拦截的敌人会原地待命，若战士只按自己的射程停下，敌人将永远够不到战士。
    /// 目标射程更长或目标不是 Unit 时按自身射程处理。
    /// </summary>
    private float EngagedAttackRange()
    {
        float range = unit.GetAttackRange();
        if (range <= 0f || target == null)
        {
            return range;
        }

        var targetUnit = target.DamageTransform.GetComponentInParent<Unit>();
        if (targetUnit == null)
        {
            return range;
        }

        float targetRange = targetUnit.GetAttackRange();
        return targetRange > 0f ? Mathf.Min(range, targetRange) : range;
    }

    private void Guard()
    {
        targets.ClearAll();

        Vector3 toGuard = guardPosition - transform.position;
        toGuard.y = 0f;

        if (toGuard.magnitude > guardArriveThreshold)
        {
            SetState(State.Return);
            locomotion.SetPaused(false);
            locomotion.MoveTo(guardPosition);
            return;
        }

        if (state != State.Idle)
        {
            SetState(State.Idle);
            locomotion.Halt();
        }
        else
        {
            locomotion.SetPaused(true);
        }
    }

    /// <summary>戒备姿态战后原地停留：不返回旧驻守点。</summary>
    private void StayInPlace()
    {
        targets.ClearAll();
        guardPosition = transform.position;

        if (state != State.Idle)
        {
            SetState(State.Idle);
            locomotion.Halt();
        }
        else
        {
            locomotion.SetPaused(true);
        }
    }

    private bool IsBeyondLeash()
    {
        return DistanceToLeashAnchor(target) > EffectiveLeashRange();
    }

    private float EffectiveLeashRange()
    {
        return hasSquadPosture && squadPosture == SquadPosture.Garrison && hasSquadRallyPoint
            ? Mathf.Max(0.1f, squadLeashRange)
            : leashRange;
    }

    private Vector3 LeashAnchor()
    {
        return hasSquadPosture && squadPosture == SquadPosture.Garrison && hasSquadRallyPoint
            ? squadRallyPoint
            : guardPosition;
    }

    private float DistanceToLeashAnchor(IDamageable candidate)
    {
        return candidate.DistanceTo(LeashAnchor());
    }

    private float DistanceToGuard(IDamageable candidate)
    {
        return candidate.DistanceTo(guardPosition);
    }

    private bool HasLiveTarget()
    {
        if (target == null)
        {
            return false;
        }

        var unityObject = target as Object;
        if (unityObject == null)
        {
            return false;
        }

        return target.IsAlive;
    }

    private void SetState(State next)
    {
        if (state == next)
        {
            return;
        }

        state = next;
        Log(string.Format("[Warrior] {0} -> {1}", name, state));
    }

    private void Log(string message)
    {
        if (logState)
        {
            Debug.Log(message, this);
        }
    }

    private void OnSelfDied(UnitHealth health)
    {
        enabled = false;
        target = null;

        if (targets != null)
        {
            targets.ClearAll();
        }

        if (locomotion != null)
        {
            locomotion.Halt();
        }
    }
}
