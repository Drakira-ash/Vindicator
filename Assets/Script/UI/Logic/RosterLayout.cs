using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// 面板布局常量与几何计算。集中一处，保证「卡片排布」「栏位尺寸」「面板高度」
    /// 三处算的是同一套数字 —— 之前这三者是各自硬编码的，结果栏位比卡片小、
    /// 卡片溢出容器外。
    /// 只做算术、不碰 UI 组件，因此属于无项目依赖的 <c>Vindicator.UI.Logic</c> 程序集，可被测试覆盖。
    /// </summary>
    public static class RosterLayout
    {
        /// <summary>每栏列数（草图：3 列）。</summary>
        public const int Columns = 3;

        /// <summary>每栏行数（草图：2 行）。</summary>
        public const int Rows = 2;

        /// <summary>单张卡片尺寸。</summary>
        public const float CardWidth = 76f;
        public const float CardHeight = 104f;

        /// <summary>卡片之间的间距。</summary>
        public const float CardSpacing = 8f;

        /// <summary>栏位标题高度。</summary>
        public const float SectionHeaderHeight = 26f;

        /// <summary>面板内边距（左右 / 上下）。</summary>
        public const float PaddingX = 12f;
        public const float PaddingY = 10f;

        /// <summary>面板顶边到第一行卡片的距离。</summary>
        public const float ContentTopOffset = 8f;

        /// <summary>
        /// 网格下方为 +X 角标单独预留的一行高度。
        /// 角标不能盖在最后一张卡片上 —— 那会挡掉卡片名称，所以面板高度
        /// 必须把这一行算进去，而不是让角标浮在卡片上方。
        /// </summary>
        public const float BadgeHeight = 24f;

        /// <summary>每栏最多显示的卡片数。</summary>
        public static int VisibleCapacity => Columns * Rows;

        /// <summary>n 列（或 n 行）内容 + 间距占用的总长度。</summary>
        public static float LineExtent(int count, float itemSize, float spacing)
        {
            if (count <= 0)
            {
                return 0f;
            }

            return count * itemSize + (count - 1) * spacing;
        }

        /// <summary>单栏网格的宽度（3 列卡片 + 2 个间距）。</summary>
        public static float GridWidth => LineExtent(Columns, CardWidth, CardSpacing);

        /// <summary>单栏网格的高度（2 行卡片 + 1 个间距）。</summary>
        public static float GridHeight => LineExtent(Rows, CardHeight, CardSpacing);

        /// <summary>
        /// 栏位内容高度：标题 + 顶留白 + 网格 + 角标预留行。
        /// 前四项之外剩下的 <see cref="BadgeHeight"/> 就是 +X 角标的位置，
        /// 因此角标与卡片互不遮挡。
        /// </summary>
        public static float SectionHeight =>
            SectionHeaderHeight + ContentTopOffset + GridHeight + BadgeHeight;

        /// <summary>面板总高度：栏位内容 + 上下内边距。</summary>
        public static float PanelHeight => SectionHeight + PaddingY * 2f;

        /// <summary>面板总宽度：左右两栏网格 + 中缝 + 两侧内边距。</summary>
        public static float PanelWidth => GridWidth * 2f + 24f + PaddingX * 2f;

        /// <summary>
        /// 计算第 <paramref name="index"/> 张卡片在栏位内的位置。
        /// 坐标系：栏位原点在中上（anchor/pivot = 0.5, 1），x 向右为正，y 向下为负。
        /// </summary>
        public static Vector2 CardPosition(int index)
        {
            int column = index % Columns;
            int row = index / Columns;

            float x = -GridWidth * 0.5f + CardWidth * 0.5f + column * (CardWidth + CardSpacing);
            float y = -row * (CardHeight + CardSpacing);
            return new Vector2(x, y);
        }
    }
}
