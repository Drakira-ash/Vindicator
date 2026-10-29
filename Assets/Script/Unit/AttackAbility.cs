using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 攻击能力：按攻击间隔对射程内目标造成伤害，支持可选的范围溅射。
/// 伤害默认有 <see cref="damageDelay"/> 秒起手延迟，冷却从起手时开始计算；
/// 起手期间目标死亡则本次伤害落空。
/// </summary>
public class AttackAbility : UnitAbility
{
    [SerializeField, Min(0f)] private float hitPadding = 0.6f;
    [SerializeField, Min(0f)] private float splashPadding = 0f;
    [SerializeField, Min(0f)] private float damageDelay = 0.3f;
    [SerializeField] private bool logAttacks = false;

    private struct PendingHit
    {
        public IDamageable target;
        public float remaining;
        public float damage;
        public float splash;
    }

    private readonly List<PendingHit> pendingHits = new List<PendingHit>();

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

        float splash = Stats != null ? Stats.Get(StatType.SplashRadius) : 0f;

        if (damageDelay <= 0f)
        {
            ApplyHit(target, damage, splash);
            return;
        }

        pendingHits.Add(new PendingHit
        {
            target = target,
            remaining = damageDelay,
            damage = damage,
            splash = splash
        });

        if (logAttacks)
        {
            Debug.Log(string.Format("[Attack] t={3:F2} {0} 起手 -> {1}（{2:F2}s 后结算）",
                Owner.name, target.DamageTransform.name, damageDelay, Time.time), Owner);
        }
    }

    public override void Tick(float deltaTime)
    {
        if (cooldown > 0f)
        {
            cooldown -= deltaTime;
        }

        for (int i = pendingHits.Count - 1; i >= 0; i--)
        {
            var hit = pendingHits[i];
            hit.remaining -= deltaTime;
            if (hit.remaining > 0f)
            {
                pendingHits[i] = hit;
                continue;
            }

            pendingHits.RemoveAt(i);
            ApplyHit(hit.target, hit.damage, hit.splash);
        }
    }

    private void ApplyHit(IDamageable target, float damage, float splash)
    {
        if (Owner == null || !IsAliveTarget(target))
        {
            return;
        }

        target.TakeDamage(damage, Owner);

        if (splash > 0f)
        {
            ApplySplash(target, damage, splash);
        }

        if (logAttacks)
        {
            Debug.Log(string.Format("[Attack] t={3:F2} {0} -> {1} 造成 {2:F1} 伤害",
                Owner.name, target.DamageTransform.name, damage, Time.time), Owner);
        }
    }

    private static bool IsAliveTarget(IDamageable target)
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
