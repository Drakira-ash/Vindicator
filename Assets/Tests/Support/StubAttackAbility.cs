using UnityEngine;

/// <summary>
/// 测试用攻击能力：固定射程，记录执行次数。
/// </summary>
public class StubAttackAbility : UnitAbility
{
    /// <summary>有效射程，由测试用例设置。</summary>
    public float range = 3f;

    /// <summary>Execute 调用次数。</summary>
    public int Executions { get; private set; }

    public override float EffectiveRange
    {
        get { return range; }
    }

    public override bool CanActivate(IDamageable target)
    {
        return target != null && target.IsAlive;
    }

    public override void Execute(IDamageable target)
    {
        Executions++;
    }
}
