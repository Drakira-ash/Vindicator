using System.Collections.Generic;
using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// 选中高亮的施加与撤销。核心约束：**不修改单位本体材质**。
    /// 单位材质是多个单位共用的 .mat 文件，而替换 shader 又很难与 URP Lit 做到
    /// 像素级一致（实测换过去会变暗）。因此这里不碰本体材质，而是：
    ///
    /// 选中时 → 在原材质数组末尾追加一个"描边专用材质槽"
    ///          （<c>Vindicator/SelectionOutline</c>，只画外扩背面）；
    /// 取消时 → 把材质数组原样还原，单位立即回到默认外观。
    ///
    /// 一个单子网格的 MeshRenderer 有 N 个材质时会用每个材质各画一遍同一子网格，
    /// 所以追加一个材质槽等价于给单位加了一个描边 pass，且本体材质完全不变。
    /// 每个渲染器的原始材质数组在第一次选中时保存，取消时恢复。
    /// </summary>
    public static class SelectionHighlighter
    {
        private const string OutlineShaderName = "Vindicator/SelectionOutline";
        private const string OutlineColorProperty = "_OutlineColor";
        private const string OutlineWidthProperty = "_OutlineWidth";
        private const float OutlineWidth = 0.035f;

        /// <summary>友军选中描边色（青蓝）。</summary>
        public static readonly Color PlayerColor = new Color(0.25f, 0.85f, 1f, 1f);

        /// <summary>敌军选中描边色（橙红）。</summary>
        public static readonly Color EnemyColor = new Color(1f, 0.35f, 0.15f, 1f);

        /// <summary>中立选中描边色（灰白）。</summary>
        public static readonly Color NeutralColor = new Color(0.9f, 0.9f, 0.9f, 1f);

        private static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        /// <summary>共享描边材质。所有单位共用，颜色/宽度通过 MaterialPropertyBlock 覆盖。</summary>
        private static Material outlineMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // 关闭 Domain Reload 时清空缓存，避免跨会话持有已销毁的渲染器。
            block.Clear();
            outlineMaterial = null;
        }

        /// <summary>按阵营取描边色。</summary>
        public static Color GetColor(UnitTeam team)
        {
            switch (team)
            {
                case UnitTeam.Player:
                    return PlayerColor;
                case UnitTeam.Enemy:
                    return EnemyColor;
                default:
                    return NeutralColor;
            }
        }

        /// <summary>
        /// 对对象施加或撤销选中高亮。<paramref name="selectable"/> 为 null 时安全返回。
        /// </summary>
        public static void Apply(ISelectable selectable, bool selected)
        {
            if (selectable == null)
            {
                return;
            }

            // 选择集合跨帧持有接口引用：单位被销毁后接口引用不是 C# null，
            // 直接访问 Transform/Team 会抛 MissingReferenceException。
            // 用 UnityEngine.Object 的“假空”判断提前退出，保证取消高亮不会
            // 中断整个选择更新（否则下一次点击/框选也会一起失败）。
            var unityObject = selectable as Object;
            if (unityObject == null)
            {
                return;
            }

            var target = selectable.Transform;
            if (target == null)
            {
                return;
            }

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            Color color = GetColor(selectable.Team);
            var outline = selected ? ResolveOutlineMaterial() : null;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                // 状态挂在渲染器所在物体上：单位被销毁时随物体一起回收，
                // 不会像静态字典那样留下"已销毁渲染器"的悬空缓存。
                var state = renderer.GetComponent<SelectionOutlineState>();

                if (selected && outline != null)
                {
                    if (state == null)
                    {
                        state = renderer.gameObject.AddComponent<SelectionOutlineState>();
                        state.originals = renderer.sharedMaterials;
                    }

                    if (!state.applied)
                    {
                        renderer.sharedMaterials = Append(state.originals, outline);
                        state.applied = true;
                    }

                    renderer.GetPropertyBlock(block);
                    block.SetColor(OutlineColorProperty, color);
                    block.SetFloat(OutlineWidthProperty, OutlineWidth);
                    renderer.SetPropertyBlock(block);
                }
                else if (state != null)
                {
                    // 还原为默认材质数组 —— 单位立即回到原 shader / 原亮度。
                    if (state.applied && state.originals != null)
                    {
                        renderer.sharedMaterials = state.originals;
                    }

                    state.applied = false;
                    Object.Destroy(state);
                }
            }
        }

        /// <summary>
        /// 批量施加选择状态：先撤销 <paramref name="previous"/> 中不再选中的，再对
        /// <paramref name="current"/> 应用。避免全量刷新造成的属性块抖动。
        /// </summary>
        public static void ApplyDiff(
            IReadOnlyList<ISelectable> previous,
            IReadOnlyList<ISelectable> current)
        {
            if (previous != null)
            {
                for (int i = 0; i < previous.Count; i++)
                {
                    var candidate = previous[i];
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (current != null && Contains(current, candidate))
                    {
                        continue;
                    }

                    Apply(candidate, false);
                }
            }

            if (current != null)
            {
                for (int i = 0; i < current.Count; i++)
                {
                    Apply(current[i], true);
                }
            }
        }

        private static Material ResolveOutlineMaterial()
        {
            if (outlineMaterial != null)
            {
                return outlineMaterial;
            }

            var shader = Shader.Find(OutlineShaderName);
            if (shader == null)
            {
                Debug.LogWarning("[SelectionHighlighter] 找不到 shader " + OutlineShaderName
                    + "，选中描边将被跳过。");
                return null;
            }

            outlineMaterial = new Material(shader)
            {
                name = "SelectionOutline (Runtime)",
                hideFlags = HideFlags.DontSave
            };
            outlineMaterial.SetFloat(OutlineWidthProperty, OutlineWidth);
            return outlineMaterial;
        }

        private static Material[] Append(Material[] source, Material extra)
        {
            if (source == null || source.Length == 0)
            {
                return new[] { extra };
            }

            var result = new Material[source.Length + 1];
            source.CopyTo(result, 0);
            result[source.Length] = extra;
            return result;
        }

        private static bool Contains(IReadOnlyList<ISelectable> list, ISelectable target)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], target))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
