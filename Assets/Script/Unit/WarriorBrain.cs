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

    // 民兵是宝贵资源：默认只在 4m 内主动接敌（2026-11-08 计划，由 10 下调）。
    [SerializeField, Min(0f)] private float detectRange = 4f;
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

    /// <summary>当前目标，便于调试与测试断言。</summary>
    public IDamageable Target => target;

    /// <summary>当前状态名，便于调试与测试断言。</summary>
    public string StateName => state.ToString();

    /// <summary>感知范围：优先读模板属性，未配置时回退到序列化字段。</summary>
    public float PerceptionRange
    {
        get
        {
            float stat = unit != null ? unit.Stats.Get(StatType.PerceptionRange) : 0f;
            return stat > 0f ? stat : detectRange;
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
        if (commander != null && commander.HasOrder)
        {
            ExecuteOrder();
            return;
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
        if (DistanceToGuard(candidate) > leashRange)
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
            if (UnitCommander.HorizontalDistance(transform.position, destination) <= commandArriveRadius)
            {
                commander.ClearOrder();
                guardPosition = transform.position;
                SetState(State.Idle);
                locomotion.Halt();
                return;
            }

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

    private bool IsBeyondLeash()
    {
        return DistanceToGuard(target) > leashRange;
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
