using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// 屏幕空间投影与矩形判定工具。全部为纯静态函数，不依赖 MonoBehaviour，
    /// 便于在 EditMode 测试中直接验证几何逻辑。
    /// 屏幕坐标统一采用 Unity 惯例：原点在左下角，单位像素，与
    /// <c>Mouse.current.position</c> 及 uGUI 的 Screen Space Overlay 画布一致。
    /// </summary>
    public static class ScreenProjector
    {
        /// <summary>
        /// 把世界坐标投影到屏幕像素坐标。位于相机后方时返回 false。
        /// </summary>
        public static bool TryProject(Camera camera, Vector3 worldPosition, out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;
            if (camera == null)
            {
                return false;
            }

            Vector3 projected = camera.WorldToScreenPoint(worldPosition);
            if (projected.z <= 0f)
            {
                return false;
            }

            screenPoint = new Vector2(projected.x, projected.y);
            return true;
        }

        /// <summary>
        /// 把单位投影为屏幕上的一个近似圆（中心 + 像素半径）。
        /// 像素半径由世界半径换算，用于单击容差与框选包含判定。
        /// </summary>
        public static bool TryProjectCircle(
            Camera camera,
            Vector3 worldPosition,
            float worldRadius,
            out Vector2 screenCenter,
            out float pixelRadius)
        {
            screenCenter = Vector2.zero;
            pixelRadius = 0f;

            if (!TryProject(camera, worldPosition, out screenCenter))
            {
                return false;
            }

            // 正交相机：像素/世界比例恒定，直接取纵向缩放。
            // 透视相机：用该点处的视距换算，保证近大远小。
            float pixelsPerWorldUnit;
            if (camera.orthographic)
            {
                pixelsPerWorldUnit = camera.pixelHeight / (camera.orthographicSize * 2f);
            }
            else
            {
                Vector3 projected = camera.WorldToScreenPoint(worldPosition);
                float distance = Mathf.Max(0.01f, projected.z);
                float halfFovRad = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
                pixelsPerWorldUnit = camera.pixelHeight / (2f * distance * Mathf.Tan(halfFovRad));
            }

            pixelRadius = Mathf.Max(0f, worldRadius) * pixelsPerWorldUnit;
            return true;
        }

        /// <summary>点是否落在圆内。</summary>
        public static bool IsPointInCircle(Vector2 point, Vector2 center, float radius)
        {
            return (point - center).sqrMagnitude <= radius * radius;
        }

        /// <summary>圆是否整体包含在矩形内（单位完全在框选范围内才命中）。</summary>
        public static bool IsCircleInsideRect(Vector2 center, float radius, Rect rect)
        {
            return center.x - radius >= rect.xMin
                && center.x + radius <= rect.xMax
                && center.y - radius >= rect.yMin
                && center.y + radius <= rect.yMax;
        }

        /// <summary>圆是否与矩形相交（宽松判定，只要搭边就算命中）。</summary>
        public static bool IsCircleOverlappingRect(Vector2 center, float radius, Rect rect)
        {
            float closestX = Mathf.Clamp(center.x, rect.xMin, rect.xMax);
            float closestY = Mathf.Clamp(center.y, rect.yMin, rect.yMax);
            float dx = center.x - closestX;
            float dy = center.y - closestY;
            return dx * dx + dy * dy <= radius * radius;
        }

        /// <summary>
        /// 由拖拽的起点与终点构造矩形，自动处理方向（四个象限都能得到正尺寸矩形）。
        /// 这是框选的核心几何：拖动方向不影响结果。
        /// </summary>
        public static Rect RectFromPoints(Vector2 start, Vector2 end)
        {
            float x = Mathf.Min(start.x, end.x);
            float y = Mathf.Min(start.y, end.y);
            float width = Mathf.Abs(end.x - start.x);
            float height = Mathf.Abs(end.y - start.y);
            return new Rect(x, y, width, height);
        }

        /// <summary>矩形面积是否为 0（用于把“原地点击”与“真正拖动”区分开）。</summary>
        public static bool IsDegenerate(Rect rect, float threshold = 0.5f)
        {
            return rect.width < threshold || rect.height < threshold;
        }
    }
}
