using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// 面板分栏的计数规则。故意只依赖数值、不依赖 ISelectable，
    /// 这样它属于无项目依赖的 <c>Vindicator.UI.Logic</c> 程序集，可以被 EditMode 测试直接覆盖。
    /// 视图层拿到单位列表后，只需把「总数 / 容量」交给这里算出可见与溢出数量。
    /// </summary>
    public static class RosterCounter
    {
        /// <summary>指定总数下，面板实际能展示的卡片数。</summary>
        public static int VisibleCount(int total, int capacity)
        {
            if (total <= 0)
            {
                return 0;
            }

            return Mathf.Min(total, Mathf.Max(0, capacity));
        }

        /// <summary>折叠进 +X 角标的数量。</summary>
        public static int OverflowCount(int total, int capacity)
        {
            if (total <= 0)
            {
                return 0;
            }

            return Mathf.Max(0, total - Mathf.Max(0, capacity));
        }

        /// <summary>是否需要显示 +X 角标。</summary>
        public static bool HasOverflow(int total, int capacity)
        {
            return OverflowCount(total, capacity) > 0;
        }

        /// <summary>角标文案；无溢出时返回空字符串。</summary>
        public static string BadgeText(int total, int capacity)
        {
            int overflow = OverflowCount(total, capacity);
            return overflow > 0 ? "+" + overflow : string.Empty;
        }
    }
}
