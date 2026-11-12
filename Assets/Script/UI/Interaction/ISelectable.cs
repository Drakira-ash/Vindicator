using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// 可被选择系统识别的对象。交互层只依赖这个接口，不直接依赖 Unit / UnitHealth，
    /// 因此将来把选择能力挂到建筑、资源点等非单位对象上时无需改动选择逻辑。
    /// </summary>
    public interface ISelectable
    {
        /// <summary>用于定位的 Transform。</summary>
        Transform Transform { get; }

        /// <summary>阵营，决定分栏与高亮配色。</summary>
        UnitTeam Team { get; }

        /// <summary>是否仍可被选中（死亡/销毁后为 false）。</summary>
        bool IsSelectable { get; }

        /// <summary>显示名称，用于面板卡片标题。</summary>
        string DisplayName { get; }

        /// <summary>世界空间包围球半径，用于屏幕投影时的像素半径换算。</summary>
        float WorldRadius { get; }

        /// <summary>投影与包围盒判定的参考点（世界坐标）。</summary>
        Vector3 WorldPosition { get; }

        /// <summary>设置选中表现（高亮等），由 <see cref="SelectionHighlighter"/> 调用。</summary>
        void SetSelected(bool selected);
    }
}
