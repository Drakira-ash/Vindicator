using UnityEngine;

/// <summary>
/// 敌人行为：沿路线前进，发现附近敌对目标后停下攻击；目标死亡后重新搜索，
/// 没有目标则继续沿路线行走。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Unit), typeof(UnitMover))]
public class EnemyBrain : MonoBehaviour
{
    [SerializeField, Min(0f)] private float acquireRange = 7f;
    [SerializeField, Min(0.05f)] private float searchInterval = 0.25f;

    private Unit unit;
    private UnitMover mover;
    private IDamageable target;
    private float searchTimer;

    /// <summary>当前目标，便于调试与测试断言。</summary>
    public IDamageable Target => target;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        mover = GetComponent<UnitMover>();
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
        if (unit == null || !unit.Health.IsAlive)
        {
            return;
        }

        if (!HasLiveTarget())
        {
            target = null;

            searchTimer -= Time.deltaTime;
            if (searchTimer <= 0f)
            {
                searchTimer = searchInterval;
                AcquireTarget();
            }
        }

        if (target == null)
        {
            mover.SetPaused(false);
            return;
        }

        float range = unit.GetAttackRange();
        float distance = target.DistanceTo(transform.position);
        bool inRange = range > 0f && distance <= range;

        mover.SetPaused(inRange);

        if (inRange)
        {
            unit.ActivateAbilities(target);
        }
    }

    private void AcquireTarget()
    {
        var candidate = UnitRegistry.FindNearestHostile(transform.position, unit.Team, acquireRange);
        if (candidate == null || ReferenceEquals(candidate, target))
        {
            return;
        }

        target = candidate;
        Debug.Log(string.Format("[AI] {0} 锁定目标 {1}", name, candidate.name), this);
    }

    private bool HasLiveTarget()
    {
        if (target == null)
        {
            return false;
        }

        var unityObject = target as Object;
        if (unityObject != null && unityObject == null)
        {
            return false;
        }

        return target.IsAlive;
    }

    private void OnSelfDied(UnitHealth health)
    {
        enabled = false;
        if (mover != null)
        {
            mover.SetPaused(true);
        }
    }
}
