using System;
using UnityEngine;

/// <summary>
/// 血量组件，同时是可攻击目标。可独立挂到塔、敌人、Home 等任意对象上。
/// </summary>
[DisallowMultipleComponent]
public class UnitHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private UnitTeam team = UnitTeam.Enemy;
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [SerializeField] private bool destroyOnDeath = true;
    [SerializeField, Min(0f)] private float destroyDelay = 0.3f;
    [SerializeField] private bool logDamage = false;

    private float current;
    private bool initialized;
    private Collider[] colliders;

    /// <summary>所属阵营。</summary>
    public UnitTeam Team => team;

    /// <summary>最大血量。</summary>
    public float Max => maxHealth;

    /// <summary>当前血量。</summary>
    public float Current => current;

    /// <summary>是否存活。</summary>
    public bool IsAlive => current > 0f;

    /// <summary>用于定位的 Transform。</summary>
    public Transform DamageTransform => transform;

    /// <summary>受到伤害（目标，伤害值）。</summary>
    public event Action<UnitHealth, float> Damaged;

    /// <summary>死亡事件。</summary>
    public event Action<UnitHealth> Died;

    /// <summary>血量变化事件。</summary>
    public event Action<UnitHealth> HealthChanged;

    private void Awake()
    {
        colliders = GetComponentsInChildren<Collider>(true);
        if (!initialized)
        {
            Initialize(maxHealth, team);
        }
    }

    private void OnEnable()
    {
        UnitRegistry.Register(this);
    }

    private void OnDisable()
    {
        UnitRegistry.Unregister(this);
    }

    /// <summary>
    /// 由 Unit 或场景初始化时调用，设置最大血量与阵营。
    /// </summary>
    public void Initialize(float max, UnitTeam newTeam)
    {
        maxHealth = Mathf.Max(1f, max);
        team = newTeam;
        current = maxHealth;
        initialized = true;
        HealthChanged?.Invoke(this);
    }

    /// <summary>
    /// 受到伤害。
    /// </summary>
    public void TakeDamage(float amount, Unit source = null)
    {
        if (!IsAlive || amount <= 0f)
        {
            return;
        }

        float before = current;
        current = Mathf.Max(0f, current - amount);

        Damaged?.Invoke(this, amount);
        HealthChanged?.Invoke(this);

        if (logDamage)
        {
            Debug.Log(string.Format("[Unit] {0} 受到 {1:F1} 伤害，剩余 {2:F1}/{3:F1}", name, amount, current, maxHealth), this);
        }

        if (before > 0f && current <= 0f)
        {
            Die();
        }
    }

    /// <summary>
    /// 治疗。
    /// </summary>
    public void Heal(float amount)
    {
        if (!IsAlive || amount <= 0f)
        {
            return;
        }

        current = Mathf.Min(maxHealth, current + amount);
        HealthChanged?.Invoke(this);
    }

    /// <summary>
    /// 计算世界坐标到碰撞体表面的距离；无碰撞体时退化为到原点的距离。
    /// </summary>
    public float DistanceTo(Vector3 worldPosition)
    {
        if (colliders == null || colliders.Length == 0)
        {
            return Vector3.Distance(worldPosition, DamageTransform.position);
        }

        float best = float.MaxValue;
        for (int i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            // 非凸 MeshCollider 的 ClosestPoint 不可靠，统一用包围盒近似。
            Vector3 closest = collider.bounds.ClosestPoint(worldPosition);
            float distance = Vector3.Distance(worldPosition, closest);
            if (distance < best)
            {
                best = distance;
            }
        }

        return best < float.MaxValue ? best : Vector3.Distance(worldPosition, DamageTransform.position);
    }

    private void Die()
    {
        if (logDamage)
        {
            Debug.Log(string.Format("[Unit] {0} 已死亡", name), this);
        }

        Died?.Invoke(this);

        if (destroyOnDeath)
        {
            Destroy(gameObject, destroyDelay);
        }
    }
}
