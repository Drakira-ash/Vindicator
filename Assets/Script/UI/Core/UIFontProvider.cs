using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// UI 字体来源。项目未导入 TextMeshPro Essentials，因此统一使用 uGUI 的
    /// <see cref="UnityEngine.UI.Text"/>。中文字符依赖系统字体，这里按优先级尝试
    /// Windows 常见中文字体，最后回退到 Unity 内置字体。
    /// 字体对象会缓存，避免每个 Text 各建一份动态字体图集。
    /// </summary>
    public static class UIFontProvider
    {
        private static readonly string[] PreferredFonts =
        {
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "SimHei",
            "Noto Sans CJK SC",
            "Segoe UI",
            "Arial"
        };

        private static Font cachedFont;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            cachedFont = null;
        }

        /// <summary>获取共享的 UI 字体。</summary>
        public static Font Get()
        {
            if (cachedFont != null)
            {
                return cachedFont;
            }

            // CreateDynamicFontFromOSFont 会在缺字时回退系统字体，中文可直接显示。
            cachedFont = Font.CreateDynamicFontFromOSFont(PreferredFonts, 24);
            if (cachedFont != null)
            {
                return cachedFont;
            }

            // 极端情况下（无字体服务）退回内置字体。
#if UNITY_2022_2_OR_NEWER
            cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            return cachedFont;
        }

        /// <summary>
        /// 给 Text 组件补上字体（预制体里字体引用为空时调用）。
        /// </summary>
        public static void EnsureFont(UnityEngine.UI.Text text)
        {
            if (text != null && text.font == null)
            {
                text.font = Get();
            }
        }
    }
}
