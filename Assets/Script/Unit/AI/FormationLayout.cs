using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>小队队形。</summary>
public enum FormationType
{
    /// <summary>横队：所有成员排成一排，面向 forward。</summary>
    Line,

    /// <summary>纵队：所有成员排成一列，沿 forward 前后排列。</summary>
    Column,

    /// <summary>方阵：接近正方形的网格，默认队形。</summary>
    Grid
}

/// <summary>
/// 纯计算的编队站位算法：给定锚点、朝向、成员数与间距，生成每个成员的槽位。
/// 不依赖场景对象，EditMode 可直接断言；是否吸附 NavMesh 由调用方决定。
/// </summary>
public static class FormationLayout
{
    /// <summary>默认成员间距（米）。</summary>
    public const float DefaultSpacing = 1.6f;

    /// <summary>把朝向压到水平面；零向量回退到世界 +Z。</summary>
    public static Vector3 Flatten(Vector3 forward)
    {
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    /// <summary>按队形计算列数。</summary>
    public static int ColumnsFor(int count, FormationType formation)
    {
        if (count <= 0)
        {
            return 1;
        }

        switch (formation)
        {
            case FormationType.Line:
                return count;
            case FormationType.Column:
                return 1;
            default:
                return Mathf.CeilToInt(Mathf.Sqrt(count));
        }
    }

    /// <summary>
    /// 生成 count 个槽位。锚点是队形中心，第一排位于朝向前方。
    /// 结果写入 slots（先清空），调用方可复用列表。
    /// </summary>
    public static void FillSlots(
        Vector3 anchor,
        Vector3 forward,
        int count,
        FormationType formation,
        float spacing,
        List<Vector3> slots)
    {
        slots.Clear();
        if (count <= 0)
        {
            return;
        }

        float step = Mathf.Max(0.05f, spacing);
        forward = Flatten(forward);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        int columns = Mathf.Max(1, ColumnsFor(count, formation));
        int rows = Mathf.CeilToInt((float)count / columns);

        for (int i = 0; i < count; i++)
        {
            int column = i % columns;
            int row = i / columns;
            float lateral = (column - (columns - 1) * 0.5f) * step;
            float depth = ((rows - 1) * 0.5f - row) * step;
            slots.Add(anchor + right * lateral + forward * depth);
        }
    }

    /// <summary>取单个槽位，便于测试与零散查询。</summary>
    public static Vector3 GetSlot(
        Vector3 anchor,
        Vector3 forward,
        int count,
        FormationType formation,
        float spacing,
        int index)
    {
        var slots = new List<Vector3>(count);
        FillSlots(anchor, forward, count, formation, spacing, slots);
        return index >= 0 && index < slots.Count ? slots[index] : anchor;
    }

    /// <summary>
    /// 把槽位吸附到 NavMesh。EditMode 没有烘焙 NavMesh 时返回 false，调用方保留原槽位。
    /// </summary>
    public static bool TrySnapToNavMesh(Vector3 position, float radius, out Vector3 snapped)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(position, out hit, Mathf.Max(0.1f, radius), NavMesh.AllAreas))
        {
            snapped = hit.position;
            return true;
        }

        snapped = position;
        return false;
    }
}
