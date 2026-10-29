using UnityEngine;

/// <summary>
/// 敌人行为：无目标时前往巡逻点，发现附近敌对目标后追击，进入射程后停下攻击；
/// 目标死亡后重新搜索最近的敌人。被友军拦截时（<see cref="UnitEvents.InterceptRequested"/>）
/// 把攻击目标切换为拦截者，拦截者死亡或距离过远后解除。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Unit))]
public class EnemyBrain : MonoBehaviour
{
    private enum State
    {
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
    [SerializeField] private bool logState = true;

    private Unit unit;
    private UnitLocomotion locomotion;
    private IDamageable target;
    private Unit interceptedBy;
    private float searchTimer;
    private State state = (State)(-1);

    /// <summary>当前目标，便于调试与测试断言。</summary>
    public IDamageable Target => target;

    /// <summary>当前状态名，便于调试与测试断言。</summary>
    public string StateName => state.ToString();

    private void Awake()
    {
        unit = GetComponent<Unit>();
        locomotion = GetComponent<UnitLocomotion>();

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
    }

    private void OnDisable()
    {
        if (unit != null && unit.Health != null)
        {
            unit.Health.Died -= OnSelfDied;
        }

        UnitEvents.InterceptRequested -= OnInterceptRequested;
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

        if (!HasLiveTarget())
        {
            target = null;
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
            Patrol();
            return;
        }

        if (!ValidateIntercept())
        {
            target = null;
            return;
        }

        if (CheckAttack())
        {
            return;
        }

        Chase();
    }

    private void Patrol()
    {
        SetState(State.Patrol);

        if (patrolGoal == null)
        {
            locomotion.Stop();
            return;
        }

        Vector3 toGoal = patrolGoal.position - transform.position;
        toGoal.y = 0f;

        if (toGoal.magnitude > patrolArriveThreshold)
        {
            locomotion.SetPaused(false);
            locomotion.MoveTo(patrolGoal.position);
        }
        else
        {
            locomotion.Stop();
        }
    }

    private void Chase()
    {
        SetState(State.Chase);
        locomotion.SetPaused(false);
        locomotion.MoveTo(target.DamageTransform.position);
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
        unit.ActivateAbilities(target);
        return true;
    }

    private void AcquireTarget()
    {
        var candidate = UnitRegistry.FindNearestHostile(transform.position, unit.Team, acquireRange);
        if (candidate == null)
        {
            return;
        }

        target = candidate;
        Log(string.Format("[AI] {0} 锁定目标 {1}", name, candidate.name));
    }

    private void OnInterceptRequested(InterceptRequest request)
    {
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

        if (locomotion != null)
        {
            locomotion.Stop();
        }
    }
}
