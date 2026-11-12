using NUnit.Framework;
using UnityEngine;
using Vindicator.UI;

/// <summary>
/// UI 交互层纯逻辑的 EditMode 测试：屏幕投影几何与面板计数规则。
/// 只依赖 <c>Vindicator.UI.Logic</c> 程序集（无项目依赖），
/// 因此可以绕开"测试程序集不能引用 Assembly-CSharp"的限制。
/// 覆盖的正是"决定交互结果对不对"的算法：框选矩形构造、命中判定、
/// 拖动与点击的区分、+X 溢出计数与角标文案。
/// </summary>
public class UISelectionLogicTests
{
    // -----------------------------------------------------------------
    // 屏幕坐标：拖动矩形的构造
    // -----------------------------------------------------------------

    [Test]
    public void RectFromPoints_NormalizesAllFourDragDirections()
    {
        var start = new Vector2(100f, 200f);

        // 用户可能从任意方向拖动。矩形必须始终是"正尺寸"（xMin/yMin 为左下角），
        // 因此每种拖法的 min/max 都应与起点、终点的分量取 min/max 一致。
        var cases = new[]
        {
            // end            期望 xMin  yMin  宽    高
            (new Vector2(300f, 400f), 100f, 200f, 200f, 200f),  // 右上
            (new Vector2(300f, 50f),  100f,  50f, 200f, 150f),  // 右下
            (new Vector2(20f, 400f),   20f, 200f,  80f, 200f),  // 左上
            (new Vector2(20f, 50f),    20f,  50f,  80f, 150f)   // 左下
        };

        for (int i = 0; i < cases.Length; i++)
        {
            var (end, xMin, yMin, width, height) = cases[i];
            var rect = ScreenProjector.RectFromPoints(start, end);

            Assert.AreEqual(xMin, rect.xMin, 0.01f, "第 " + i + " 种拖法的 xMin 错误");
            Assert.AreEqual(yMin, rect.yMin, 0.01f, "第 " + i + " 种拖法的 yMin 错误");
            Assert.AreEqual(width, rect.width, 0.01f, "第 " + i + " 种拖法的宽度错误");
            Assert.AreEqual(height, rect.height, 0.01f, "第 " + i + " 种拖法的高度错误");

            // 关键性质：无论朝哪个方向拖，宽高都不得为负。
            Assert.GreaterOrEqual(rect.width, 0f);
            Assert.GreaterOrEqual(rect.height, 0f);
        }
    }

    [Test]
    public void IsDegenerate_SeparatesClickFromDrag()
    {
        // 原地点击：宽高为 0，应走单击路径。
        var click = ScreenProjector.RectFromPoints(new Vector2(10f, 10f), new Vector2(10f, 10f));
        Assert.IsTrue(ScreenProjector.IsDegenerate(click), "零面积矩形应判为退化");

        // 发生位移：应走框选路径。
        var drag = ScreenProjector.RectFromPoints(new Vector2(10f, 10f), new Vector2(80f, 90f));
        Assert.IsFalse(ScreenProjector.IsDegenerate(drag), "有位移的矩形不应判为退化");
    }

    [Test]
    public void IsDegenerate_RespectsCustomThreshold()
    {
        // 1 像素的抖动应被视为点击，而不是框选。
        var tiny = ScreenProjector.RectFromPoints(new Vector2(10f, 10f), new Vector2(11f, 11f));
        Assert.IsTrue(ScreenProjector.IsDegenerate(tiny, 2f), "阈值 2px 时 1px 抖动应算点击");
        Assert.IsFalse(ScreenProjector.IsDegenerate(tiny, 0.5f), "阈值 0.5px 时 1px 位移应算拖动");
    }

    // -----------------------------------------------------------------
    // 屏幕坐标：命中判定
    // -----------------------------------------------------------------

    [Test]
    public void RectHitTests_DistinguishContainmentFromOverlap()
    {
        var rect = new Rect(0f, 0f, 100f, 100f);
        var center = new Vector2(50f, 50f);

        // 完全在内部：两种判定都命中。
        Assert.IsTrue(ScreenProjector.IsCircleInsideRect(center, 10f, rect));
        Assert.IsTrue(ScreenProjector.IsCircleOverlappingRect(center, 10f, rect));

        // 中心在矩形外但圆搭到边缘：宽松判定命中，严格判定不命中。
        var onEdge = new Vector2(105f, 50f);
        Assert.IsFalse(ScreenProjector.IsCircleInsideRect(onEdge, 10f, rect), "搭边不算完全包含");
        Assert.IsTrue(ScreenProjector.IsCircleOverlappingRect(onEdge, 10f, rect), "搭边算相交");

        // 完全脱离：两种判定都不命中。
        var outside = new Vector2(200f, 200f);
        Assert.IsFalse(ScreenProjector.IsCircleInsideRect(outside, 10f, rect));
        Assert.IsFalse(ScreenProjector.IsCircleOverlappingRect(outside, 10f, rect));
    }

    [Test]
    public void IsPointInCircle_RespectsRadiusBoundary()
    {
        var center = new Vector2(50f, 50f);
        Assert.IsTrue(ScreenProjector.IsPointInCircle(new Vector2(59f, 50f), center, 10f), "半径内应命中");
        Assert.IsTrue(ScreenProjector.IsPointInCircle(new Vector2(60f, 50f), center, 10f), "正好在半径上应命中");
        Assert.IsFalse(ScreenProjector.IsPointInCircle(new Vector2(61f, 50f), center, 10f), "半径外不应命中");
    }

    [Test]
    public void TryProject_ReturnsFalseWithoutCamera()
    {
        // 相机缺失时不应抛出异常，而应安静地返回 false。
        Assert.IsFalse(ScreenProjector.TryProject(null, Vector3.zero, out _));
        Assert.IsFalse(
            ScreenProjector.TryProjectCircle(null, Vector3.zero, 0.5f, out _, out _));
    }

    // -----------------------------------------------------------------
    // 面板计数：+X 溢出角标
    // -----------------------------------------------------------------

    [Test]
    public void RosterCounter_ComputesOverflowAgainstCapacity()
    {
        // 容量 6（3 列 × 2 行），9 个敌人 => 6 可见 + 3 折叠。
        Assert.AreEqual(6, RosterCounter.VisibleCount(9, 6));
        Assert.AreEqual(3, RosterCounter.OverflowCount(9, 6));
        Assert.IsTrue(RosterCounter.HasOverflow(9, 6));
        Assert.AreEqual("+3", RosterCounter.BadgeText(9, 6));
    }

    [Test]
    public void RosterCounter_NoOverflowWithinCapacity()
    {
        Assert.AreEqual(4, RosterCounter.VisibleCount(4, 6));
        Assert.AreEqual(0, RosterCounter.OverflowCount(4, 6));
        Assert.IsFalse(RosterCounter.HasOverflow(4, 6));
        Assert.AreEqual(string.Empty, RosterCounter.BadgeText(4, 6), "无溢出时角标文案应为空");
    }

    [Test]
    public void RosterCounter_ExactlyFullHasNoOverflow()
    {
        // 边界：正好填满不应显示角标。
        Assert.AreEqual(6, RosterCounter.VisibleCount(6, 6));
        Assert.AreEqual(0, RosterCounter.OverflowCount(6, 6));
        Assert.IsFalse(RosterCounter.HasOverflow(6, 6));
    }

    [Test]
    public void RosterCounter_HandlesEmptyAndInvalidInput()
    {
        Assert.AreEqual(0, RosterCounter.VisibleCount(0, 6));
        Assert.AreEqual(0, RosterCounter.OverflowCount(0, 6));
        Assert.AreEqual(string.Empty, RosterCounter.BadgeText(0, 6));

        // 负数（异常输入）不应产生负计数。
        Assert.AreEqual(0, RosterCounter.VisibleCount(-3, 6));
        Assert.AreEqual(0, RosterCounter.OverflowCount(-3, 6));

        // 容量为 0 时全部折叠。
        Assert.AreEqual(5, RosterCounter.OverflowCount(5, 0));
        Assert.AreEqual(0, RosterCounter.VisibleCount(5, 0));
    }

    // -----------------------------------------------------------------
    // 面板布局几何
    // -----------------------------------------------------------------

    [Test]
    public void RosterLayout_GridAndBadgeRowFitInsideSectionWithoutOverlap()
    {
        // 栏位内容 = 标题 + 顶留白 + 网格 + 角标预留行，四者必须严丝合缝。
        // 曾经角标直接压在最后一张卡片上，根因就是面板高度没给角标留位置。
        Assert.AreEqual(
            RosterLayout.SectionHeight,
            RosterLayout.SectionHeaderHeight + RosterLayout.ContentTopOffset
                + RosterLayout.GridHeight + RosterLayout.BadgeHeight,
            0.01f);

        // 角标行必须为正，否则角标无处可放。
        Assert.Greater(RosterLayout.BadgeHeight, 0f);

        // 网格下边缘到栏位底部的距离应恰好等于角标行高 —— 也就是说
        // 角标占据的正是卡片下方的空白，不会与卡片重叠。
        float gapBelowGrid = RosterLayout.SectionHeight
                             - RosterLayout.SectionHeaderHeight
                             - RosterLayout.ContentTopOffset
                             - RosterLayout.GridHeight;
        Assert.AreEqual(RosterLayout.BadgeHeight, gapBelowGrid, 0.01f);
    }

    [Test]
    public void RosterLayout_PanelWrapsTwoGridsAndPadding()
    {
        float expectedWidth = RosterLayout.GridWidth * 2f + 24f + RosterLayout.PaddingX * 2f;
        Assert.AreEqual(expectedWidth, RosterLayout.PanelWidth, 0.01f);

        float expectedHeight = RosterLayout.SectionHeight + RosterLayout.PaddingY * 2f;
        Assert.AreEqual(expectedHeight, RosterLayout.PanelHeight, 0.01f);
    }

    [Test]
    public void RosterLayout_CardPositionsStayInsideGrid()
    {
        int capacity = RosterLayout.Columns * RosterLayout.Rows;

        for (int i = 0; i < capacity; i++)
        {
            Vector2 position = RosterLayout.CardPosition(i);

            // 卡片以中上为锚点，x 是中心偏移，y 向下为负。
            float left = position.x - RosterLayout.CardWidth * 0.5f;
            float right = position.x + RosterLayout.CardWidth * 0.5f;
            float top = position.y;
            float bottom = position.y - RosterLayout.CardHeight;

            Assert.GreaterOrEqual(left + RosterLayout.GridWidth * 0.5f, -0.01f,
                "第 " + i + " 张卡片越过网格左边");
            Assert.LessOrEqual(right + RosterLayout.GridWidth * 0.5f, RosterLayout.GridWidth + 0.01f,
                "第 " + i + " 张卡片越过网格右边");
            Assert.LessOrEqual(top, 0.01f, "第 " + i + " 张卡片越过网格上边");
            Assert.GreaterOrEqual(bottom + RosterLayout.GridHeight, -0.01f,
                "第 " + i + " 张卡片越过网格下边");
        }
    }
}
