using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// 选择状态机：维护“当前选中的单位集合”，并在变化时通知高亮与面板。
    /// 场景里的点击、框选与面板卡片的点击最终都收敛到这里，保证只有一个真相源。
    /// 选择集合不可跨帧持有引用做缓存 —— 单位会死亡销毁，使用方请订阅事件或每次读 <see cref="Selected"/>。
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectionService : MonoBehaviour
    {
        [Tooltip("框选时是否要求单位完全落在矩形内。关闭则只要搭边就算命中。")]
        [SerializeField] private bool requireFullContainment = false;

        [Tooltip("单击命中的屏幕容差，按屏幕高度比例计算，避免分辨率变化影响手感。")]
        [SerializeField, Range(0f, 0.1f)] private float clickToleranceRatio = 0.02f;

        [Tooltip("经典模式：单击替换选择（不累加）。开启后按住 Ctrl 可累加选择。")]
        [SerializeField] private bool additiveWithModifier = true;

        private readonly List<ISelectable> selected = new List<ISelectable>();
        private readonly List<ISelectable> previousBuffer = new List<ISelectable>();
        private static readonly List<UnitSelectable> candidateBuffer = new List<UnitSelectable>();

        /// <summary>
        /// 场景内当前的选择服务。这是"被选中单位指针"的唯一全局入口：
        /// 任何系统（面板、指令系统、AI 调试面板）都从这里读选择集合，
        /// 不要各自缓存一份，否则单位死亡销毁后会留下悬空引用。
        /// </summary>
        public static SelectionService Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>选择集合发生变化时触发，参数为新的选择集合（只读视图，勿修改）。</summary>
        public event Action<IReadOnlyList<ISelectable>> SelectionChanged;

        /// <summary>当前选择集合的只读视图。</summary>
        public IReadOnlyList<ISelectable> Selected => selected;

        /// <summary>当前选中数量。</summary>
        public int Count => selected.Count;

        /// <summary>单击容差的像素值，随屏幕高度缩放。</summary>
        public float ClickTolerancePixels => clickToleranceRatio * Screen.height;

        /// <summary>清空选择。</summary>
        public void Clear()
        {
            SetSelection(null, false);
        }

        /// <summary>
        /// 选中单个对象。<paramref name="additive"/> 为 true 时追加而不替换。
        /// </summary>
        public void Select(ISelectable target, bool additive = false)
        {
            if (target == null || !target.IsSelectable)
            {
                if (!additive)
                {
                    Clear();
                }

                return;
            }

            SetSelection(new[] { target }, additive);
        }

        /// <summary>
        /// 用一批对象替换当前选择。<paramref name="additive"/> 为 true 时并入现有集合。
        /// </summary>
        public void SelectMany(IReadOnlyList<ISelectable> targets, bool additive = false)
        {
            SetSelection(targets, additive);
        }

        /// <summary>
        /// 屏幕单击选择：取点击点容差范围内距离最近的一个单位。
        /// 命中空白时不改变选择，由调用方决定是否清空（见 <see cref="HandleClick"/>）。
        /// </summary>
        public ISelectable PickAt(Camera camera, Vector2 screenPoint)
        {
            if (camera == null)
            {
                return null;
            }

            var candidates = GatherCandidates();

            ISelectable best = null;
            float bestDistanceSqr = float.MaxValue;
            float tolerance = ClickTolerancePixels;

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!ScreenProjector.TryProjectCircle(
                        camera,
                        candidate.WorldPosition,
                        candidate.WorldRadius,
                        out Vector2 center,
                        out float pixelRadius))
                {
                    continue;
                }

                // 视觉半径与点击容差取较大者：小单位也能被轻松点到。
                float hitRadius = Mathf.Max(pixelRadius, tolerance);
                float distanceSqr = (screenPoint - center).sqrMagnitude;
                if (distanceSqr > hitRadius * hitRadius)
                {
                    continue;
                }

                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// 屏幕框选：返回矩形内的所有单位。矩形退化（原地点击）时返回空集合，
        /// 由调用方改走 <see cref="PickAt"/>。
        /// </summary>
        public List<ISelectable> PickInRect(Camera camera, Rect screenRect)
        {
            var result = new List<ISelectable>();
            if (camera == null || ScreenProjector.IsDegenerate(screenRect))
            {
                return result;
            }

            var candidates = GatherCandidates();
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!ScreenProjector.TryProjectCircle(
                        camera,
                        candidate.WorldPosition,
                        candidate.WorldRadius,
                        out Vector2 center,
                        out float pixelRadius))
                {
                    continue;
                }

                bool hit = requireFullContainment
                    ? ScreenProjector.IsCircleInsideRect(center, pixelRadius, screenRect)
                    : ScreenProjector.IsCircleOverlappingRect(center, pixelRadius, screenRect);

                if (hit)
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

        /// <summary>
        /// 场景单击的统一入口：命中单位则选中，命中空白则清空（符合设计案
        /// “再次点击其他区域会取消上次的选择”）。
        /// </summary>
        public void HandleClick(Camera camera, Vector2 screenPoint, bool additive)
        {
            var hit = PickAt(camera, screenPoint);
            if (hit == null)
            {
                // 未按 Ctrl 时点到空白 = 取消当前所有选中。
                if (!additive)
                {
                    Clear();
                }

                return;
            }

            if (additive)
            {
                // Ctrl + 单击 = 多选：已选中的再点一次移出，未选中的加入。
                Toggle(hit);
                return;
            }

            // 未按 Ctrl 时，单击任何单位都会**替换**现有选择（等价于先全部取消再选中该单位）。
            Select(hit, false);
        }

        /// <summary>把对象从选择集合中移除；不在集合里则无副作用。</summary>
        public void Deselect(ISelectable target)
        {
            if (target == null || !ContainsSelected(target))
            {
                return;
            }

            var next = new List<ISelectable>(selected.Count);
            for (int i = 0; i < selected.Count; i++)
            {
                if (!ReferenceEquals(selected[i], target))
                {
                    next.Add(selected[i]);
                }
            }

            SetSelection(next, false);
        }

        private void Toggle(ISelectable target)
        {
            var next = new List<ISelectable>(selected.Count + 1);
            bool wasSelected = false;

            for (int i = 0; i < selected.Count; i++)
            {
                if (ReferenceEquals(selected[i], target))
                {
                    wasSelected = true;
                    continue;
                }

                next.Add(selected[i]);
            }

            if (!wasSelected)
            {
                next.Add(target);
            }

            SetSelection(next, false);
        }

        /// <summary>某对象当前是否被选中。</summary>
        public bool ContainsSelected(ISelectable target)
        {
            if (target == null)
            {
                return false;
            }

            for (int i = 0; i < selected.Count; i++)
            {
                if (ReferenceEquals(selected[i], target))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 场景框选的统一入口：以矩形结果替换选择；空矩形视为取消选择。
        /// </summary>
        public void HandleDragSelect(Camera camera, Rect screenRect, bool additive)
        {
            if (ScreenProjector.IsDegenerate(screenRect))
            {
                if (!additive)
                {
                    Clear();
                }

                return;
            }

            var hits = PickInRect(camera, screenRect);
            if (hits.Count == 0 && !additive)
            {
                Clear();
                return;
            }

            SetSelection(hits, additive);
        }

        /// <summary>是否应接受 Ctrl 累加（未开启累加模式时永远返回 false）。</summary>
        public bool AllowsAdditive(bool modifierHeld)
        {
            return additiveWithModifier && modifierHeld;
        }

        /// <summary>
        /// 收集当前所有可选对象。每帧重新收集而不缓存：
        /// 单位会持续生成与销毁，缓存清单需要额外的失效维护，收益不划算。
        /// </summary>
        public static List<UnitSelectable> GatherCandidates()
        {
            candidateBuffer.Clear();

            var all = UnitRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var health = all[i];
                // 缓存的接口/组件引用在对象销毁后仍非 null，必须先做假空判断，
                // 否则 GetComponent 会抛 MissingReferenceException。
                if (health == null)
                {
                    continue;
                }

                var selectable = health.GetComponent<UnitSelectable>();
                if (selectable == null || !selectable.IsSelectable)
                {
                    continue;
                }

                candidateBuffer.Add(selectable);
            }

            return candidateBuffer;
        }

        private void SetSelection(IReadOnlyList<ISelectable> next, bool additive)
        {
            previousBuffer.Clear();
            previousBuffer.AddRange(selected);

            // 先剔除死亡/已销毁的条目。单位死亡后 Unity 销毁对象时，
            // 选择集合里的接口引用不会变成 C# null；若继续留着，
            // 下一次高亮 diff 会抛 MissingReferenceException，把整次选择更新
            // （包括玩家新的点选/框选）一起中断，表现为“点不动、编不了队”。
            bool pruned = false;
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                var item = selected[i];
                if (item == null || !item.IsSelectable)
                {
                    selected.RemoveAt(i);
                    pruned = true;
                }
            }

            var resolved = new List<ISelectable>();

            if (additive)
            {
                resolved.AddRange(selected);
            }

            if (next != null)
            {
                for (int i = 0; i < next.Count; i++)
                {
                    var item = next[i];
                    if (item == null || !item.IsSelectable || resolved.Contains(item))
                    {
                        continue;
                    }

                    resolved.Add(item);
                }
            }

            // 即使集合内容没变，只要剔除了无效条目也要广播一次：
            // 旧命令层（UnitSelection）需要跟着清掉已经死亡的选中单位。
            if (!pruned && SameSet(selected, resolved))
            {
                return;
            }

            selected.Clear();
            selected.AddRange(resolved);

            SelectionHighlighter.ApplyDiff(previousBuffer, selected);
            SelectionChanged?.Invoke(selected);
        }

        private static bool SameSet(List<ISelectable> a, List<ISelectable> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!ReferenceEquals(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
