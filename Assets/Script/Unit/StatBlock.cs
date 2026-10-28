using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单位属性类型。新增属性时在这里扩展即可。
/// </summary>
public enum StatType
{
    MaxHealth,
    Attack,
    MoveSpeed,
    AttackInterval,
    Range,
    SplashRadius
}

/// <summary>
/// 属性修正类型，按固定管线顺序参与计算。
/// </summary>
public enum StatModifierKind
{
    /// <summary>加法固定值。</summary>
    Flat,

    /// <summary>加法百分比（100 表示 +100%）。</summary>
    AddPercent,

    /// <summary>乘法百分比（多个修正相乘）。</summary>
    Multiply,

    /// <summary>直接覆盖最终值，最后生效。</summary>
    Override
}

/// <summary>
/// 一条属性修正记录。source 用于按来源整体移除。
/// </summary>
[Serializable]
public struct StatModifier
{
    public StatType stat;
    public StatModifierKind kind;
    public float value;
    public string source;

    public StatModifier(StatType stat, StatModifierKind kind, float value, string source = null)
    {
        this.stat = stat;
        this.kind = kind;
        this.value = value;
        this.source = source;
    }
}

/// <summary>
/// 运行时属性容器：基础值 + 修正器列表，惰性重算并广播变更。
/// 计算管线：base → +Flat → +AddPercent → ×Multiply → Override → clamp。
/// </summary>
public class StatBlock
{
    private readonly Dictionary<StatType, float> baseValues = new Dictionary<StatType, float>();
    private readonly List<StatModifier> modifiers = new List<StatModifier>();
    private readonly Dictionary<StatType, float> cached = new Dictionary<StatType, float>();
    private readonly HashSet<StatType> dirty = new HashSet<StatType>();

    /// <summary>属性值变更事件（属性，新值）。</summary>
    public event Action<StatType, float> Changed;

    /// <summary>
    /// 设置基础值。
    /// </summary>
    public void SetBase(StatType stat, float value)
    {
        baseValues[stat] = value;
        MarkDirty(stat);
    }

    /// <summary>
    /// 读取最终值；如有脏标记则先重算。
    /// </summary>
    public float Get(StatType stat)
    {
        if (dirty.Contains(stat))
        {
            Refresh(stat);
        }

        float value;
        return cached.TryGetValue(stat, out value) ? value : 0f;
    }

    /// <summary>
    /// 添加一条修正。
    /// </summary>
    public void AddModifier(StatModifier modifier)
    {
        modifiers.Add(modifier);
        MarkDirty(modifier.stat);
    }

    /// <summary>
    /// 移除指定来源的全部修正。
    /// </summary>
    public void RemoveModifiersFrom(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return;
        }

        bool removed = false;
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            if (modifiers[i].source == source)
            {
                MarkDirty(modifiers[i].stat);
                modifiers.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            MarkAllDirty();
        }
    }

    /// <summary>
    /// 清空所有修正。
    /// </summary>
    public void ClearModifiers()
    {
        modifiers.Clear();
        MarkAllDirty();
    }

    private void MarkDirty(StatType stat)
    {
        dirty.Add(stat);
        Refresh(stat);
    }

    private void MarkAllDirty()
    {
        foreach (var stat in baseValues.Keys)
        {
            MarkDirty(stat);
        }
    }

    private void Refresh(StatType stat)
    {
        dirty.Remove(stat);

        float value = Evaluate(stat);
        float previous;
        bool hadValue = cached.TryGetValue(stat, out previous);
        cached[stat] = value;

        if (Changed != null && (!hadValue || !Mathf.Approximately(previous, value)))
        {
            Changed(stat, value);
        }
    }

    private float Evaluate(StatType stat)
    {
        float value;
        baseValues.TryGetValue(stat, out value);

        float flat = 0f;
        float addPercent = 0f;
        float multiply = 1f;
        float overrideValue = float.NaN;

        for (int i = 0; i < modifiers.Count; i++)
        {
            var modifier = modifiers[i];
            if (modifier.stat != stat)
            {
                continue;
            }

            switch (modifier.kind)
            {
                case StatModifierKind.Flat:
                    flat += modifier.value;
                    break;
                case StatModifierKind.AddPercent:
                    addPercent += modifier.value;
                    break;
                case StatModifierKind.Multiply:
                    multiply *= modifier.value;
                    break;
                case StatModifierKind.Override:
                    overrideValue = modifier.value;
                    break;
            }
        }

        value += flat;
        value *= 1f + addPercent / 100f;
        value *= multiply;

        if (!float.IsNaN(overrideValue))
        {
            value = overrideValue;
        }

        if (stat == StatType.MaxHealth)
        {
            return Mathf.Max(1f, value);
        }

        return Mathf.Max(0f, value);
    }
}
