using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// 溢出角标：「+X」。草图中两栏右下角各一个，显示“除了界面展示出来的以外还剩几个”。
    /// 点击后由 <see cref="RosterPanelView"/> 打开完整列表。
    /// 本组件只负责显示数量，点击行为在面板里统一接管。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnityEngine.UI.Button))]
    public class OverflowBadgeView : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Text label;

        /// <summary>角标显示的溢出数量；0 时自动隐藏。</summary>
        public int Count { get; private set; }

        /// <summary>刷新角标。数量为 0 时隐藏整个角标。</summary>
        public void SetCount(int count)
        {
            Count = Mathf.Max(0, count);

            if (label != null)
            {
                label.text = Count > 0 ? "+" + Count : string.Empty;
            }

            gameObject.SetActive(Count > 0);
        }
    }
}
