using UnityEngine;

/// <summary>
/// 战士行为：驻守原地，敌对单位进入检测范围后发起拦截请求并追击，
/// 进入攻击范围后停下攻击；目标死亡或超出拴绳距离时返回驻守点。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Unit))]
public class WarriorBrain : MonoBehaviour
{
    private enum State
    {
        Idle,
        Chase,
        Attack,
        Return
    }

    [SerializeField, Min(0f)] private float detectRange = 10f;
    [SerializeField, Min(0f)] private float leashRange = 14f;
    [SerializeField, Min(0.1f)] private float guardArriveThreshold = 0.5f;
    [SerializeField, Min(0.05f)] private float searchInterval = 0.25f;
    [SerializeField] private bool logState = true;

    private Unit unit;
    private UnitLocomotion locomotion;
    private IDamageable target;
    private Vector3 guardPosition;
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

        if (!HasLiveTarget())
        {
            target = null;
        }

        if (target != null && IsBeyondLeash())
        {
            Log(string.Format("[Warrior] {0} 目标 {1} 超出拴绳，放弃追击", name, target.DamageTransform.name));
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
        var candidate = UnitRegistry.FindNearestHostile(transform.position, unit.Team, detectRange);
        if (candidate == null)
        {
            return;
        }

        target = candidate;
        UnitEvents.RaiseInterceptRequested(new InterceptRequest(unit, candidate));
        Log(string.Format("[Intercept] {0} 请求拦截 {1}", name, candidate.name));
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

    private void Guard()
    {
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
            locomotion.Stop();
        }
    }

    private bool IsBeyondLeash()
    {
        float distance = target.DistanceTo(guardPosition);
        return distance > leashRange;
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

        if (locomotion != null)
        {
            locomotion.Stop();
        }
    }
}
