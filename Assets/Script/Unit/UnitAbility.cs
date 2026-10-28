using UnityEngine;

/// <summary>
/// 能力基类。能力是可组合的 MonoBehaviour 组件，由 <see cref="Unit"/> 统一驱动。
/// </summary>
public abstract class UnitAbility : MonoBehaviour
{
    [SerializeField] private bool abilityEnabled = true;

    /// <summary>能力拥有者。</summary>
    protected Unit Owner { get; private set; }

    /// <summary>拥有者的属性容器。</summary>
    protected StatBlock Stats => Owner != null ? Owner.Stats : null;

    /// <summary>能力自身开关。</summary>
    public bool AbilityEnabled => abilityEnabled;

    /// <summary>
    /// 由 Unit 在 Awake 时调用。
    /// </summary>
    public virtual void Initialize(Unit owner)
    {
        Owner = owner;
    }

    /// <summary>有效射程（含命中容差）。</summary>
    public abstract float EffectiveRange { get; }

    /// <summary>当前是否可以对目标激活。</summary>
    public abstract bool CanActivate(IDamageable target);

    /// <summary>执行能力。</summary>
    public abstract void Execute(IDamageable target);

    /// <summary>每帧驱动，用于冷却等计时。</summary>
    public virtual void Tick(float deltaTime)
    {
    }

    /// <summary>开关能力。</summary>
    public void SetEnabled(bool value)
    {
        abilityEnabled = value;
    }
}
