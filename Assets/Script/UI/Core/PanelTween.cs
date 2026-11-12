using System;
using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// 面板展开 / 收回的插值动画。只处理“进度 0→1 的数值与回调”，
    /// 不直接操作控件，因此可以复用到高度、透明度、位移等任何被插值的属性上。
    /// 用 unscaledDeltaTime 更新，保证暂停（timeScale = 0）时 UI 仍然可动。
    /// </summary>
    [Serializable]
    public class PanelTween
    {
        [Tooltip("动画时长（秒）。")]
        [SerializeField, Min(0.01f)] private float duration = 0.22f;

        private float progress;      // 0 = 完全收回，1 = 完全展开
        private float target;
        private float velocity;

        /// <summary>当前进度（0 收回，1 展开）。</summary>
        public float Progress => progress;

        /// <summary>是否正在播放动画。</summary>
        public bool IsAnimating => Mathf.Abs(progress - target) > 0.0005f;

        /// <summary>进度或目标发生变化时触发，参数为当前进度。</summary>
        public event Action<float> ProgressChanged;

        /// <summary>动画播放结束时触发。</summary>
        public event Action Completed;

        public float Duration
        {
            get => duration;
            set => duration = Mathf.Max(0.01f, value);
        }

        /// <summary>立即设置进度，不播放动画。</summary>
        public void Snap(float value)
        {
            progress = Mathf.Clamp01(value);
            target = progress;
            velocity = 0f;
            ProgressChanged?.Invoke(progress);
            Completed?.Invoke();
        }

        /// <summary>开始向目标进度过渡。</summary>
        public void Play(float value)
        {
            target = Mathf.Clamp01(value);
        }

        /// <summary>每帧推进；宿主在 Update 中调用。</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (!IsAnimating)
            {
                return;
            }

            // SmoothDamp 到目标，天然带缓出效果，且在目标附近平滑收敛。
            progress = Mathf.SmoothDamp(
                progress,
                target,
                ref velocity,
                Mathf.Max(0.0001f, duration * 0.35f),
                Mathf.Infinity,
                Mathf.Max(0f, unscaledDeltaTime));

            if (Mathf.Abs(progress - target) <= 0.0005f)
            {
                progress = target;
                velocity = 0f;
                ProgressChanged?.Invoke(progress);
                Completed?.Invoke();
                return;
            }

            ProgressChanged?.Invoke(progress);
        }
    }
}
