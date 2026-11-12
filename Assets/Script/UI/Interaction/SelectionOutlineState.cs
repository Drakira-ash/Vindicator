using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// 记录某个渲染器在追加描边材质**之前**的原始材质数组。
    /// 由 <see cref="SelectionHighlighter"/> 在第一次选中时挂上，取消选中后销毁；
    /// 挂在渲染器所在物体上，单位被销毁时随之回收，不会留下悬空缓存。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SelectionOutlineState : MonoBehaviour
    {
        /// <summary>追加描边之前的材质数组（原样引用）。</summary>
        [HideInInspector] public Material[] originals;

        /// <summary>当前是否已把描边材质追加到渲染器上。</summary>
        [HideInInspector] public bool applied;
    }
}
