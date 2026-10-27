using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单位行进路线：按顺序排列的子物体即为路点。
/// 只提供 XZ 位置，Y 由 UnitMover 射线贴地修正。
/// </summary>
[DisallowMultipleComponent]
public class UnitRoute : MonoBehaviour
{
    [SerializeField] private bool useChildTransforms = true;
    [SerializeField] private List<Transform> waypoints = new List<Transform>();

    /// <summary>路点数量。</summary>
    public int Count => waypoints.Count;

    private void Awake()
    {
        if (useChildTransforms)
        {
            RebuildFromChildren();
        }
    }

    /// <summary>
    /// 重新按子物体顺序收集路点。
    /// </summary>
    public void RebuildFromChildren()
    {
        waypoints.Clear();
        for (int i = 0; i < transform.childCount; i++)
        {
            waypoints.Add(transform.GetChild(i));
        }
    }

    /// <summary>
    /// 获取指定索引的路点世界坐标。
    /// </summary>
    public Vector3 GetPoint(int index)
    {
        if (waypoints.Count == 0)
        {
            return transform.position;
        }

        return waypoints[Mathf.Clamp(index, 0, waypoints.Count - 1)].position;
    }

    private void OnDrawGizmos()
    {
        if (!useChildTransforms)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
        for (int i = 0; i < transform.childCount; i++)
        {
            Vector3 point = transform.GetChild(i).position;
            Gizmos.DrawSphere(point, 0.25f);

            if (i > 0)
            {
                Gizmos.DrawLine(transform.GetChild(i - 1).position, point);
            }
        }
    }
}
