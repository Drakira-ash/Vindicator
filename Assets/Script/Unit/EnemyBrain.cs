using UnityEngine;

/// <summary>
/// 敌人行为：目标丢失时先回到 Idle，下一帧立即进入新一轮判定（无目标则朝终点/巡逻点移动）；
/// 发现附近敌对目标后追击，进入射程后停下攻击；目标死亡后重新搜索最近的敌人。被友军拦截时
/// （<see cref="UnitEvents.InterceptRequested"/>）把攻击目标切换为拦截者：
/// 拦截者进入攻击范围才攻击，否则原地待命（Idle）不再移动；
/// 拦截者死亡或距离过远后解除。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Unit))]
public class EnemyBrain : MonoBehaviour, IPerceptionSource, IUnitAgentState
{
    private enum State
    {
        Idle,
        Patrol,
        Chase,
        Attack
    }

    [SerializeField] private Transform patrolGoal;
    [SerializeField, Min(0f)] private float acquireRange = 7f;
    [SerializeField, Min(0.05f)] private float searchInterval = 0.25f;
    [SerializeField, Min(0.1f)] private float patrolArriveThreshold = 0.5f;
    [SerializeField, Min(0f)] private float interceptAcceptRange = 12f;
    [SerializeField, Min(0f)] private float interceptLeash = 20f;

    // 2026-11-08 计划暂时关闭拦截：默认不响应友军的拦截请求，按普通索敌运行。
    [SerializeField] private bool interceptEnabled = false;
    [SerializeField] private bool logState = true;

    private Unit unit;
    private UnitLocomotion locomotion;
    private UnitTargetState targets;
    private IDamageable target;
    private Unit interceptedBy;
    private float searchTimer;
    private State state = (State)(-1);
    private bool forceIdleFrame;
    private bool reevaluateNext;

    /// <summary>当前目标，便于调试与测试断言。</summary>
    public IDamageable Target => target;

    /// <summary>无索敌目标时的巡逻终点；开发者模式生成敌人时复用它。</summary>
    public Transform PatrolGoal => patrolGoal;

    /// <summary>当前状态名，便于调试与测试断言。</summary>
    public string StateName => state.ToString();

    /// <summary>感知范围：优先读模板属性，未配置时回退到序列化字段。</summary>
    public float PerceptionRange
    {
        get
        {
            float stat = unit != null ? unit.Stats.Get(StatType.PerceptionRange) : 0f;
            return stat > 0f ? stat : acquireRange;
        }
    }

    private void Awake()
    {
        unit = GetComponent<Unit>();
        locomotion = GetComponent<UnitLocomotion>();
        targets = UnitTargetState.Ensure(gameObject);

        if (locomotion == null)
        {
            Debug.LogError("EnemyBrain 需要同物体上的 UnitLocomotion（UnitMotor 或 UnitMover）。", this);
        }
    }

    private void OnEnable()
    {
        if (unit != null && unit.Health != null)
        {
            unit.Health.Died += OnSelfDied;
        }

        UnitEvents.InterceptRequested += OnInterceptRequested;
        UnitEvents.InterceptReleased += OnInterceptReleased;
    }

    private void OnDisable()
    {
        if (unit != null && unit.Health != null)
        {
            unit.Health.Died -= OnSelfDied;
        }

        UnitEvents.InterceptRequested -= OnInterceptRequested;
        UnitEvents.InterceptReleased -= OnInterceptReleased;
    }

    /// <summary>由生成器在实例化后设置，决定无目标时的去向。</summary>
    public void SetPatrolGoal(Transform goal)
    {
        patrolGoal = goal;
    }

    private void Update()
    {
        if (unit == null || unit.Health == null || !unit.Health.IsAlive || locomotion == null)
        {
            return;
        }

        bool hadTarget = target != null;

        if (!HasLiveTarget())
        {
            if (target != null)
            {
                targets.ClearAll();
            }

            target = null;
            // 目标（含拦截者）已死亡时一并清掉拦截关系，避免残留状态挡住后续索敌。
            interceptedBy = null;
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
            // 上一帧已回到 Idle：现在从 Idle 进入下一轮判定。
            if (reevaluateNext)
            {
                reevaluateNext = false;
                Patrol();
                return;
            }

            // 刚丢失目标或收到拦截解除：先回到 Idle 停留一帧，
            // 下一帧再从 Idle 进入新一轮判定（仍无目标则朝终点移动）。
            if (hadTarget || forceIdleFrame)
            {
                forceIdleFrame = false;
                Hold();
                reevaluateNext = true;
                return;
            }

            // 尚未接战：按配置朝终点（巡逻点）移动。
            Patrol();
            return;
        }

        forceIdleFrame = false;
        reevaluateNext = false;

        if (!ValidateIntercept())
        {
            // 超距解除拦截：本帧回到 Idle，下一帧从 Idle 进入新一轮判定。
            target = null;
            Hold();
            reevaluateNext = true;
            return;
        }

        if (interceptedBy != null)
        {
            // 被拦截：拦截者在攻击范围内才攻击；否则原地待命，不再朝拦截者移动。
            if (CheckAttack())
            {
                return;
            }

            Hold();
            return;
        }

        if (CheckAttack())
        {
            return;
        }

        Chase();
    }

    /// <summary>原地待命（被拦截但拦截者未进入射程，或目标丢失当帧回到 Idle）。</summary>
    private void Hold()
    {
        targets.ClearAll();

        if (state != State.Idle)
        {
            SetState(State.Idle);
            locomotion.Halt();
        }
        else
        {
            // UnitMover.Stop() 只清目标、不暂停路线跟随，必须同时暂停移动器。
            locomotion.SetPaused(true);
        }
    }

    private void Patrol()
    {
        targets.ClearAll();

        if (patrolGoal == null)
        {
            // 没有 AI 巡逻点：退出坐标模式，把移动交还给 locomotion（UnitMover 的路线跟随）。
            if (state != State.Idle)
            {
                SetState(State.Idle);
                locomotion.Stop();
            }

            locomotion.SetPaused(false);
            return;
        }

        if (DistanceToPatrolGoal() <= patrolArriveThreshold)
        {
            // 已到达终点：真正停下（UnitMover 的 Stop 会退回路线，必须同时暂停）。
            if (state != State.Idle)
            {
                SetState(State.Idle);
                locomotion.Halt();
            }
            else
            {
                locomotion.SetPaused(true);
            }

            return;
        }

        SetState(State.Patrol);
        locomotion.SetPaused(false);
        locomotion.MoveTo(patrolGoal.position);
    }

    private float DistanceToPatrolGoal()
    {
        if (patrolGoal == null)
        {
            return 0f;
        }

        Vector3 toGoal = patrolGoal.position - transform.position;
        toGoal.y = 0f;
        return toGoal.magnitude;
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
        float range = unit.GetAttackRange();
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

    private void AcquireTarget()
    {
        var candidate = UnitRegistry.FindNearestHostile(transform.position, unit.Team, PerceptionRange);
        if (candidate == null)
        {
            return;
        }

        target = candidate;
        Log(string.Format("[AI] {0} 锁定目标 {1}", name, candidate.name));
    }

    private void OnInterceptRequested(InterceptRequest request)
    {
        // 2026-11-08 计划暂时关闭敌军的“被拦截”：不转火、不待命，按普通索敌继续。
        if (!interceptEnabled)
        {
            return;
        }

        if (unit == null || unit.Health == null || !unit.Health.IsAlive)
        {
            return;
        }

        if (!ReferenceEquals(request.Target, unit.Health))
        {
            return;
        }

        var interceptor = request.Interceptor;
        if (interceptor == null || interceptor.Health == null || !interceptor.Health.IsAlive)
        {
            return;
        }

        if (!UnitTeamUtility.IsHostile(unit.Team, interceptor.Team))
        {
            return;
        }

        if (interceptedBy != null && interceptedBy.Health != null && interceptedBy.Health.IsAlive)
        {
            return;
        }

        if (interceptor.Health.DistanceTo(transform.position) > interceptAcceptRange)
        {
            Log(string.Format("[Intercept] {0} 拒绝 {1} 的拦截请求：距离过远", name, interceptor.name));
            return;
        }

        interceptedBy = interceptor;
        target = interceptor.Health;
        searchTimer = searchInterval;
        Log(string.Format("[Intercept] {0} 接受 {1} 的拦截，改为攻击 {1}", name, interceptor.name));
    }

    /// <summary>拦截者主动放弃追击时解除拦截，避免敌人被留在原地永久待命。</summary>
    private void OnInterceptReleased(InterceptRequest request)
    {
        if (unit == null || unit.Health == null || interceptedBy == null)
        {
            return;
        }

        if (!ReferenceEquals(request.Interceptor, interceptedBy) ||
            !ReferenceEquals(request.Target, unit.Health))
        {
            return;
        }

        Log(string.Format("[Intercept] {0} 因 {1} 放弃追击而解除拦截", name, interceptedBy.name));
        interceptedBy = null;
        target = null;
        searchTimer = searchInterval;
        // 下一次 Update 先回到 Idle，再进入新一轮判定。
        forceIdleFrame = true;
    }

    /// <summary>拦截后持续校验拦截者；死亡或拉出过远距离时解除，交回常规索敌。</summary>
    private bool ValidateIntercept()
    {
        if (interceptedBy == null)
        {
            return true;
        }

        bool gone = interceptedBy.Health == null || !interceptedBy.Health.IsAlive;
        bool tooFar = !gone && interceptedBy.Health.DistanceTo(transform.position) > interceptLeash;

        if (!gone && !tooFar)
        {
            return true;
        }

        Log(string.Format("[Intercept] {0} 解除对 {1} 的拦截", name, interceptedBy.name));
        interceptedBy = null;
        target = null;
        searchTimer = searchInterval;
        return false;
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
        if (logState)
        {
            Debug.Log(string.Format("[AI] {0} -> {1}", name, state), this);
        }
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
        interceptedBy = null;

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
