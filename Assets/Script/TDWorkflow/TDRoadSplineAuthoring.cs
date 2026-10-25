using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

[ExecuteAlways]
public class TDRoadSplineAuthoring : MonoBehaviour
{
    [SerializeField] private SplineContainer splineSource;
    [SerializeField] private MeshFilter roadVisual;
    [SerializeField] private MeshRenderer roadRenderer;
    [SerializeField] private MeshCollider roadCollider;
    [SerializeField, Min(1f)] private float roadWidth = 3f;
    [SerializeField, Min(0.25f)] private float samplesPerMeter = 1f;

    /// <summary>
    /// 获取用于道路网格采样的样条容器。
    /// </summary>
    public SplineContainer SplineSource => splineSource;

    /// <summary>
    /// 获取道路宽度（米），用于左右偏移生成带状道路。
    /// </summary>
    public float RoadWidth => roadWidth;

    /// <summary>
    /// 获取每米采样次数，用于控制转弯平滑度与网格密度。
    /// </summary>
    public float SamplesPerMeter => samplesPerMeter;

    /// <summary>
    /// 由编辑器工具调用，基于当前样条重新生成道路网格并同步碰撞体。
    /// </summary>
    public void RebuildRoadMesh()
    {
        if (splineSource == null || roadVisual == null || roadRenderer == null || roadCollider == null)
        {
            Debug.LogWarning("TDRoadSplineAuthoring 引用未配置完整，已跳过道路更新。", this);
            return;
        }

        var spline = splineSource.Spline;
        if (spline == null || spline.Count < 2)
        {
            Debug.LogWarning("SplineSource 至少需要 2 个控制点。", this);
            return;
        }

        var mesh = EnsureMeshInstance();
        BuildRoadStripMesh(splineSource, spline, roadWidth, samplesPerMeter, mesh, out bool hasUnsupportedTurn);
        roadVisual.sharedMesh = mesh;
        roadCollider.sharedMesh = null;
        roadCollider.sharedMesh = mesh;

        if (hasUnsupportedTurn)
        {
            Debug.LogWarning("检测到极端急转弯或切线异常，局部宽度可能失真。请在 Scene 中调整控制点切线后重建。", this);
        }
    }

    /// <summary>
    /// 在缺少道路网格实例时创建一个独立 Mesh，避免误改其他对象资源。
    /// </summary>
    /// <returns>RoadVisual 使用的网格实例。</returns>
    private Mesh EnsureMeshInstance()
    {
        var mesh = roadVisual.sharedMesh;
        if (mesh != null)
        {
            return mesh;
        }

        mesh = new Mesh
        {
            name = "RoadVisualMesh"
        };
        roadVisual.sharedMesh = mesh;
        return mesh;
    }

    /// <summary>
    /// 使用样条中心线生成平坦道路带状网格：每个采样点生成左右两个顶点并拼接三角形。
    /// </summary>
    /// <param name="container">用于世界坐标求值的样条容器。</param>
    /// <param name="spline">输入样条曲线。</param>
    /// <param name="width">道路宽度（米）。</param>
    /// <param name="density">每米采样数。</param>
    /// <param name="mesh">输出网格。</param>
    /// <param name="hasUnsupportedTurn">是否检测到无法稳定求法线的急转情况。</param>
    private static void BuildRoadStripMesh(
        SplineContainer container,
        Spline spline,
        float width,
        float density,
        Mesh mesh,
        out bool hasUnsupportedTurn)
    {
        hasUnsupportedTurn = false;
        mesh.Clear();

        float length = Mathf.Max(1f, spline.GetLength());
        int segmentCount = Mathf.Max(2, Mathf.CeilToInt(length * Mathf.Max(0.25f, density)));

        var vertices = new List<Vector3>((segmentCount + 1) * 2);
        var normals = new List<Vector3>((segmentCount + 1) * 2);
        var uv = new List<Vector2>((segmentCount + 1) * 2);
        var triangles = new List<int>(segmentCount * 6);

        for (int i = 0; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            float3 position = container.EvaluatePosition(t);
            float3 tangent = container.EvaluateTangent(t);

            var tangent3 = ((Vector3)tangent).normalized;
            if (tangent3.sqrMagnitude < 0.0001f)
            {
                tangent3 = Vector3.forward;
                hasUnsupportedTurn = true;
            }

            var right = Vector3.Cross(Vector3.up, tangent3).normalized;
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.right;
                hasUnsupportedTurn = true;
            }

            var center = (Vector3)position;
            var halfWidth = width * 0.5f;
            vertices.Add(center - right * halfWidth);
            vertices.Add(center + right * halfWidth);
            normals.Add(Vector3.up);
            normals.Add(Vector3.up);
            uv.Add(new Vector2(0f, t));
            uv.Add(new Vector2(1f, t));

            if (i == segmentCount)
            {
                continue;
            }

            int root = i * 2;
            triangles.Add(root);
            triangles.Add(root + 2);
            triangles.Add(root + 1);
            triangles.Add(root + 1);
            triangles.Add(root + 2);
            triangles.Add(root + 3);
        }

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// 由初始化工具调用，批量设置道路引用与初始参数。
    /// </summary>
    /// <param name="source">样条源对象。</param>
    /// <param name="visual">道路网格过滤器。</param>
    /// <param name="renderer">道路渲染器。</param>
    /// <param name="collider">道路碰撞体。</param>
    /// <param name="width">初始宽度（米）。</param>
    public void Configure(SplineContainer source, MeshFilter visual, MeshRenderer renderer, MeshCollider collider, float width)
    {
        splineSource = source;
        roadVisual = visual;
        roadRenderer = renderer;
        roadCollider = collider;
        roadWidth = Mathf.Max(1f, width);
    }
}
