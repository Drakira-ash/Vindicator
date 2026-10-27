using UnityEngine;

/// <summary>
/// 单位模板：策划配置的基础数值与行为开关。运行时只读，不保存任何可变状态。
/// </summary>
[CreateAssetMenu(fileName = "UnitDefinition", menuName = "Vindicator/Unit Definition")]
public class UnitDefinition : ScriptableObject
{
    [Header("身份")]
    public string displayName = "Unit";
    public UnitTeam team = UnitTeam.Enemy;

    [Header("属性")]
    [Min(1f)] public float maxHealth = 100f;
    [Min(0f)] public float attack = 10f;
    [Min(0f)] public float moveSpeed = 2f;
    [Min(0.05f)] public float attackInterval = 2f;
    [Min(0f)] public float range = 1.5f;
    [Min(0f)] public float splashRadius = 0f;

    [Header("行为开关")]
    public bool canMove = true;
    public bool canAttack = true;
}
