using UnityEngine;

/// <summary>
/// 单位根组件：持有单位模板、运行时属性与能力组件集合。
/// 能力行为由挂载的 <see cref="UnitAbility"/> 组件实现，本组件只负责调度。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitHealth))]
public class Unit : MonoBehaviour
{
    [SerializeField] private UnitDefinition definition;
    [SerializeField] private bool abilitiesEnabled = true;

    private UnitHealth health;
    private UnitAbility[] abilities;
    private readonly StatBlock stats = new StatBlock();

    /// <summary>单位模板。</summary>
    public UnitDefinition Definition => definition;

    /// <summary>运行时属性容器。</summary>
    public StatBlock Stats => stats;

    /// <summary>血量组件。</summary>
    public UnitHealth Health => health;

    /// <summary>阵营。</summary>
    public UnitTeam Team => health != null
        ? health.Team
        : (definition != null ? definition.team : UnitTeam.Neutral);

    /// <summary>能力总开关，例如验收时需要临时关闭塔的攻击。</summary>
    public bool AbilitiesEnabled
    {
        get => abilitiesEnabled;
        set => abilitiesEnabled = value;
    }

    private void Awake()
    {
        health = GetComponent<UnitHealth>();
        abilities = GetComponents<UnitAbility>();

        if (definition != null)
        {
            health.Initialize(definition.maxHealth, definition.team);

            stats.SetBase(StatType.MaxHealth, definition.maxHealth);
            stats.SetBase(StatType.Attack, definition.attack);
            stats.SetBase(StatType.MoveSpeed, definition.moveSpeed);
            stats.SetBase(StatType.AttackInterval, definition.attackInterval);
            stats.SetBase(StatType.Range, definition.range);
            stats.SetBase(StatType.SplashRadius, definition.splashRadius);
        }

        for (int i = 0; i < abilities.Length; i++)
        {
            abilities[i].Initialize(this);
        }
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        for (int i = 0; i < abilities.Length; i++)
        {
            abilities[i].Tick(deltaTime);
        }
    }

    /// <summary>
    /// 尝试对所有能力执行一次激活检查；满足条件的能力立即执行。
    /// </summary>
    public void ActivateAbilities(IDamageable target)
    {
        if (!abilitiesEnabled || target == null || !target.IsAlive)
        {
            return;
        }

        for (int i = 0; i < abilities.Length; i++)
        {
            var ability = abilities[i];
            if (ability.CanActivate(target))
            {
                ability.Execute(target);
            }
        }
    }

    /// <summary>
    /// 返回所有能力中的最大有效射程；没有能力时返回 0。
    /// </summary>
    public float GetAttackRange()
    {
        float best = 0f;
        for (int i = 0; i < abilities.Length; i++)
        {
            float range = abilities[i].EffectiveRange;
            if (range > best)
            {
                best = range;
            }
        }

        return best;
    }
}
