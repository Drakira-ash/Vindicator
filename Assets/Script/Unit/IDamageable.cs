using UnityEngine;

/// <summary>
/// 可被攻击的目标。塔、敌人、Home 等一切有血量的对象都实现该接口。
/// </summary>
public interface IDamageable
{
    /// <summary>是否仍存活。</summary>
    bool IsAlive { get; }

    /// <summary>所属阵营。</summary>
    UnitTeam Team { get; }

    /// <summary>用于表现与定位的 Transform。</summary>
    Transform DamageTransform { get; }

    /// <summary>
    /// 计算指定世界坐标到本目标碰撞体表面的距离；没有碰撞体时退化为到原点的距离。
    /// </summary>
    float DistanceTo(Vector3 worldPosition);

    /// <summary>
    /// 承受伤害。
    /// </summary>
    /// <param name="amount">伤害值。</param>
    /// <param name="source">伤害来源，可为空。</param>
    void TakeDamage(float amount, Unit source = null);
}
