using UnityEngine;

/// <summary>
/// 追踪投射物：每帧朝目标当前位置重算速度矢量，进入触发范围后结算一次伤害并销毁。
/// 不使用 Rigidbody/Collider，位移完全由本组件驱动，避免物理层与穿模问题。
/// </summary>
[DisallowMultipleComponent]
public class Projectile : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float speed = 20f;
    [SerializeField, Min(0.01f)] private float triggerRange = 1f;
    [SerializeField, Min(0.1f)] private float maxLifetime = 5f;
    [SerializeField] private bool logHits = true;
    [SerializeField] private bool logFlight = false;
    [SerializeField, Min(0.05f)] private float flightLogInterval = 0.25f;

    private IDamageable target;
    private Unit source;
    private UnitTeam sourceTeam = UnitTeam.Neutral;
    private float damage;
    private float splashRadius;
    private float age;
    private float flightLogTimer;
    private bool launched;
    private bool resolved;

    /// <summary>当前速度矢量（标量恒为 <see cref="speed"/>，方向随目标移动更新）。</summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>是否已经结算或销毁。</summary>
    public bool Resolved => resolved;

    /// <summary>当前追踪目标。</summary>
    public IDamageable Target => launched && !resolved ? target : null;

    /// <summary>速度标量。</summary>
    public float Speed => speed;

    /// <summary>触发范围。</summary>
    public float TriggerRange => triggerRange;

    /// <summary>
    /// 由发射方调用。阵营在发射时快照，飞行途中不再读取来源（来源可能已死亡）。
    /// </summary>
    public void Launch(IDamageable newTarget, float newDamage, float newSplashRadius, Unit newSource)
    {
        target = newTarget;
        damage = newDamage;
        splashRadius = newSplashRadius;
        source = newSource;
        sourceTeam = newSource != null ? newSource.Team : UnitTeam.Neutral;

        age = 0f;
        flightLogTimer = 0f;
        Velocity = Vector3.zero;
        resolved = false;
        launched = true;

        FaceTarget();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>推进一帧：检查存活 → 寿命 → 触发 → 扫掠 → 重算速度 → 位移。</summary>
    public void Tick(float deltaTime)
    {
        if (!launched || resolved)
        {
            return;
        }

        age += deltaTime;
        if (age >= maxLifetime)
        {
            Expire("超过存活时间");
            return;
        }

        if (!IsAliveTarget(target))
        {
            Expire("目标丢失");
            return;
        }

        float distance = target.DistanceTo(transform.position);
        if (distance <= triggerRange)
        {
            Resolve();
            return;
        }

        Vector3 targetPoint = target.DamageTransform.position;
        Vector3 toTarget = targetPoint - transform.position;
        if (toTarget.sqrMagnitude > 0.000001f)
        {
            Velocity = toTarget.normalized * speed;
        }

        if (Velocity.sqrMagnitude <= 0.000001f)
        {
            Resolve();
            return;
        }

        // 扫掠保护：本帧步进会跨过触发边界时，直接停在边界并结算，避免低帧率穿透目标。
        float remaining = distance - triggerRange;
        float step = speed * deltaTime;
        if (step >= remaining)
        {
            transform.position += Velocity.normalized * remaining;
            Resolve();
            return;
        }

        transform.rotation = Quaternion.LookRotation(Velocity);
        transform.position += Velocity * deltaTime;

        if (logFlight)
        {
            flightLogTimer -= deltaTime;
            if (flightLogTimer <= 0f)
            {
                flightLogTimer = flightLogInterval;
                Debug.Log(string.Format(
                    "[Projectile] t={0:F2} {1} 速度 ({2:F2},{3:F2},{4:F2}) 距目标 {5:F2}m",
                    Time.time, name, Velocity.x, Velocity.y, Velocity.z,
                    target.DistanceTo(transform.position)), this);
            }
        }
    }

    private void Resolve()
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        Velocity = Vector3.zero;

        if (IsAliveTarget(target))
        {
            var health = target as UnitHealth;
            string targetName = target.DamageTransform.name;

            target.TakeDamage(damage, source);

            if (splashRadius > 0f)
            {
                ApplySplash(health);
            }

            if (logHits)
            {
                string remaining = health != null
                    ? string.Format("，剩余 {0:F1}/{1:F1}", health.Current, health.Max)
                    : string.Empty;
                Debug.Log(string.Format("[Projectile] t={0:F2} {1} 命中 {2} 造成 {3:F1} 伤害{4}",
                    Time.time, name, targetName, damage, remaining), this);
            }
        }

        Destroy(gameObject);
    }

    private void Expire(string reason)
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        Velocity = Vector3.zero;

        if (logHits)
        {
            Debug.Log(string.Format("[Projectile] t={0:F2} {1} {2}，销毁", Time.time, name, reason), this);
        }

        Destroy(gameObject);
    }

    private void ApplySplash(UnitHealth primary)
    {
        var all = UnitRegistry.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var health = all[i];
            if (health == null || ReferenceEquals(health, primary) || !health.IsAlive)
            {
                continue;
            }

            if (!UnitTeamUtility.IsHostile(sourceTeam, health.Team))
            {
                continue;
            }

            if (health.DistanceTo(transform.position) <= splashRadius)
            {
                health.TakeDamage(damage, source);
            }
        }
    }

    private void FaceTarget()
    {
        if (!IsAliveTarget(target))
        {
            return;
        }

        Vector3 toTarget = target.DamageTransform.position - transform.position;
        if (toTarget.sqrMagnitude > 0.000001f)
        {
            transform.rotation = Quaternion.LookRotation(toTarget);
        }
    }

    private static bool IsAliveTarget(IDamageable candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        var unityObject = candidate as Object;
        if (unityObject == null)
        {
            return false;
        }

        return candidate.IsAlive;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, triggerRange);

        if (Velocity.sqrMagnitude > 0.000001f)
        {
            Gizmos.DrawLine(transform.position, transform.position + Velocity);
        }

        if (IsAliveTarget(target))
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f);
            Gizmos.DrawLine(transform.position, target.DamageTransform.position);
        }
    }
}
