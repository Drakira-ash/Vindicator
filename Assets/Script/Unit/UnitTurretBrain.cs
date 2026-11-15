using UnityEngine;

/// <summary>
/// 固定炮台行为：不移动，在射程内自动攻击最近的敌对目标。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Unit))]
public class UnitTurretBrain : MonoBehaviour, IPerceptionSource, IUnitAgentState
{
    [SerializeField, Min(0.05f)] private float searchInterval = 0.25f;

    private Unit unit;
    private UnitTargetState targets;
    private IDamageable target;
    private float searchTimer;

    /// <summary>当前目标。</summary>
    public IDamageable Target => target;

    /// <summary>炮台状态：有目标即攻击，否则待机。</summary>
    public string StateName => target != null ? "Attack" : "Idle";

    /// <summary>炮台没有独立感知；开发者模式显示其攻击范围，禁用时返回 0。</summary>
    public float PerceptionRange
    {
        get
        {
            if (!isActiveAndEnabled || unit == null || unit.Health == null || !unit.Health.IsAlive)
            {
                return 0f;
            }

            return unit.GetAttackRange();
        }
    }

    private void Awake()
    {
        unit = GetComponent<Unit>();
        targets = UnitTargetState.Ensure(gameObject);
    }

    private void Update()
    {
        if (unit == null || !unit.Health.IsAlive)
        {
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
        else if (target.DistanceTo(transform.position) > unit.GetAttackRange())
        {
            // 目标走出射程后立即重搜，否则会一直锁定一个够不到的单位而停火。
            target = null;
            targets.ClearAll();
            searchTimer = 0f;
        }

        if (target == null)
        {
            searchTimer -= Time.deltaTime;
            if (searchTimer <= 0f)
            {
                searchTimer = searchInterval;
                target = UnitRegistry.FindNearestHostile(transform.position, unit.Team, unit.GetAttackRange());
            }
        }

        if (target != null && target.IsAlive)
        {
            targets.SetLock(target.DamageTransform);
            targets.ClearChase();
            unit.ActivateAbilities(target);
        }
        else
        {
            targets.ClearAll();
        }
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
}
