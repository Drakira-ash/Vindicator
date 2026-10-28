using UnityEngine;

/// <summary>
/// 攻击能力：按攻击间隔对射程内目标造成伤害，支持可选的范围溅射。
/// </summary>
public class AttackAbility : UnitAbility
{
    [SerializeField, Min(0f)] private float hitPadding = 0.6f;
    [SerializeField, Min(0f)] private float splashPadding = 0f;
    [SerializeField] private bool logAttacks = false;

    private float cooldown;

    /// <summary>射程 + 命中容差。</summary>
    public override float EffectiveRange
    {
        get
        {
            if (Stats == null)
            {
                return 0f;
            }

            return Stats.Get(StatType.Range) + hitPadding;
        }
    }

    public override bool CanActivate(IDamageable target)
    {
        if (!AbilityEnabled || Owner == null || target == null || !target.IsAlive)
        {
            return false;
        }

        if (cooldown > 0f)
        {
            return false;
        }

        return target.DistanceTo(transform.position) <= EffectiveRange;
    }

    public override void Execute(IDamageable target)
    {
        float interval = Stats != null ? Stats.Get(StatType.AttackInterval) : 1f;
        cooldown = Mathf.Max(0.05f, interval);

        float damage = Stats != null ? Stats.Get(StatType.Attack) : 0f;
        if (damage <= 0f || target == null || !target.IsAlive)
        {
            return;
        }

        target.TakeDamage(damage, Owner);

        float splash = Stats != null ? Stats.Get(StatType.SplashRadius) : 0f;
        if (splash > 0f)
        {
            ApplySplash(target, damage, splash);
        }

        if (logAttacks)
        {
            Debug.Log(string.Format("[Attack] {0} -> {1} 造成 {2:F1} 伤害", Owner.name, target.DamageTransform.name, damage), Owner);
        }
    }

    public override void Tick(float deltaTime)
    {
        if (cooldown > 0f)
        {
            cooldown -= deltaTime;
        }
    }

    private void ApplySplash(IDamageable primary, float damage, float radius)
    {
        var all = UnitRegistry.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var health = all[i];
            if (health == null || ReferenceEquals(health, primary) || !health.IsAlive)
            {
                continue;
            }

            if (!UnitTeamUtility.IsHostile(Owner.Team, health.Team))
            {
                continue;
            }

            if (health.DistanceTo(transform.position) <= radius + splashPadding)
            {
                health.TakeDamage(damage, Owner);
            }
        }
    }
}
