using UnityEngine;

/// <summary>
/// 固定炮台行为：不移动，在射程内自动攻击最近的敌对目标。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Unit))]
public class UnitTurretBrain : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float searchInterval = 0.25f;

    private Unit unit;
    private IDamageable target;
    private float searchTimer;

    /// <summary>当前目标。</summary>
    public IDamageable Target => target;

    private void Awake()
    {
        unit = GetComponent<Unit>();
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
                target = UnitRegistry.FindNearestHostile(transform.position, unit.Team, unit.GetAttackRange());
            }
        }

        if (target != null && target.IsAlive)
        {
            unit.ActivateAbilities(target);
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
