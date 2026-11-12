using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Vindicator.UI.EditorTools
{
    /// <summary>
    /// 验收辅助：把当前场景里的可选单位投影到屏幕，报告它们是否落在相机视野内
    /// 以及是否会被底部面板遮挡。用于在无法进入 PlayMode 时（批处理模式）
    /// 静态确认 UI 验收场景的布局正确。
    /// 菜单：Tools/UI/Verify Selection Test Scene
    /// </summary>
    public static class SelectionTestSceneVerifier
    {
        private const string TargetScenePath = "Assets/Scenes/UI_Test.unity";

        // 面板高度取自 RosterLayout，与面板实际尺寸同源，避免两处数字漂移。
        private static float PanelHeight => RosterLayout.PanelHeight;
        private const float ReferenceHeight = 1080f;
        private const float ReferenceWidth = 1920f;

        [MenuItem("Tools/UI/Verify Selection Test Scene", false, 22)]
        public static void Verify()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != TargetScenePath)
            {
                Debug.LogWarning("[UI-Verify] 当前场景不是 " + TargetScenePath + "，请先打开验收场景。");
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("[UI-Verify] 场景没有 Main Camera。");
                return;
            }

            var selectables = Object.FindObjectsByType<UnitSelectable>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            var report = new StringBuilder();
            report.AppendLine("[UI-Verify] 场景=" + scene.path);
            report.AppendLine("  相机=" + camera.name
                + " 正交=" + camera.orthographic
                + " size=" + camera.orthographicSize);
            report.AppendLine("  使用参考分辨率 " + ReferenceWidth + "x" + ReferenceHeight
                + "（批处理下 Screen 尺寸为 0，无法用真实像素）");

            // 面板在参考分辨率下的遮挡边界（像素，原点左下）。
            // 画布为 ScaleWithScreenSize + 参考分辨率 1920x1080 + match 0.5，
            // 因此在参考分辨率下 1 画布单位 = 1 像素，面板高度即为遮挡高度。
            float panelPixels = PanelHeight;
            report.AppendLine("  面板高度 " + panelPixels.ToString("F0") + "px，遮挡线 y < "
                + panelPixels.ToString("F0") + "px");

            int visible = 0;
            int occluded = 0;
            int offscreen = 0;

            for (int i = 0; i < selectables.Length; i++)
            {
                var item = selectables[i];
                if (item == null)
                {
                    continue;
                }

                // 注意：这里**不能**用 item.IsSelectable 过滤。编辑器非播放状态下
                // UnitHealth.Awake 不执行，血量仍为 0，IsSelectable 一律为 false。
                // 本工具是布局检查，枚举所有挂了 UnitSelectable 的对象即可。

                if (!TryProjectAtReference(
                        camera, item.WorldPosition, item.WorldRadius,
                        out Vector2 center, out float radius))
                {
                    report.AppendLine("  [相机后方] " + item.name);
                    offscreen++;
                    continue;
                }

                bool inViewport = center.x >= 0f && center.x <= ReferenceWidth
                                  && center.y >= 0f && center.y <= ReferenceHeight;

                if (!inViewport)
                {
                    offscreen++;
                    report.AppendLine(string.Format(
                        "  [视野外] {0,-16} team={1,-7} 屏幕=({2:F0},{3:F0})",
                        item.name, item.Team, center.x, center.y));
                    continue;
                }

                visible++;
                bool behindPanel = center.y - radius < panelPixels;
                if (behindPanel)
                {
                    occluded++;
                }

                report.AppendLine(string.Format(
                    "  [可见]   {0,-16} team={1,-7} 屏幕=({2:F0},{3:F0}) r={4:F1}{5}",
                    item.name, item.Team, center.x, center.y, radius,
                    behindPanel ? "  <-- 被面板遮挡" : string.Empty));
            }

            report.AppendLine(string.Format(
                "  小结：{0} 个可选单位，{1} 个在视野内，{2} 个被面板遮挡，{3} 个在视野外。",
                selectables.Length, visible, occluded, offscreen));
            report.AppendLine("  说明：本工具只统计场景中已存在的单位；"
                + "刷怪器在运行时生成的怪物不在场景里，需进 PlayMode 后观察。"
                + "被面板遮挡的单位数取决于它们当帧的位置，动态变化属正常。");

            if (occluded > 0)
            {
                Debug.LogWarning(report.ToString());
            }
            else
            {
                Debug.Log(report.ToString());
            }
        }

        /// <summary>
        /// 按参考分辨率手工投影：把世界点转到相机空间，再用正交尺寸推算像素坐标。
        /// 不依赖 Screen 尺寸，因此在 -nographics 批处理下同样可用。
        /// </summary>
        private static bool TryProjectAtReference(
            Camera camera,
            Vector3 worldPosition,
            float worldRadius,
            out Vector2 screenCenter,
            out float pixelRadius)
        {
            screenCenter = Vector2.zero;
            pixelRadius = 0f;

            Vector3 viewPos = camera.worldToCameraMatrix.MultiplyPoint(worldPosition);

            // 相机空间中 z 为负表示在相机前方。
            if (viewPos.z >= 0f)
            {
                return false;
            }

            if (!camera.orthographic)
            {
                return false;
            }

            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * (ReferenceWidth / ReferenceHeight);

            float u = viewPos.x / (halfWidth * 2f);   // -0.5 .. 0.5
            float v = viewPos.y / (halfHeight * 2f);

            screenCenter = new Vector2(
                (u + 0.5f) * ReferenceWidth,
                (v + 0.5f) * ReferenceHeight);

            // 正交相机：像素/世界单位在两个轴上比例相同。
            float pixelsPerUnit = ReferenceHeight / (halfHeight * 2f);
            pixelRadius = Mathf.Abs(worldRadius) * pixelsPerUnit;
            return true;
        }
    }
}
