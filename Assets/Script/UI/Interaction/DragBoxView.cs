using UnityEngine;
using UnityEngine.UI;

namespace Vindicator.UI
{
    /// <summary>
    /// 拖拽框选的视觉反馈：一个跟随指针的矩形描边。
    /// 运行时自建对象（不依赖预制体），保证任何场景下挂上就能用。
    ///
    /// 几何约定：起点（按下鼠标的位置）是**固定角**，对角线另一端跟随鼠标移动。
    /// 矩形始终以起点为锚、向鼠标方向张开，因此往任意方向拖拽都符合直觉。
    /// </summary>
    [DisallowMultipleComponent]
    public class DragBoxView : MonoBehaviour
    {
        [Tooltip("是否填充矩形内部。默认关闭，只画描边，避免遮挡场景内容。")]
        [SerializeField] private bool fillEnabled = false;

        [SerializeField] private Color fillColor = new Color(0.35f, 0.75f, 1f, 0.16f);
        [SerializeField] private Color borderColor = new Color(0.55f, 0.85f, 1f, 0.95f);

        [Tooltip("边框粗细（参考画布宽度的比例）。")]
        [SerializeField, Range(0.0005f, 0.01f)] private float borderThicknessRatio = 0.0025f;

        private RectTransform root;
        private RectTransform fillRect;
        private Image fillImage;
        private RectTransform borderRoot;
        private Image[] borderEdges;
        private Canvas canvas;

        /// <summary>
        /// 屏幕坐标转画布局部坐标所需的事件相机。
        /// Overlay 画布必须传 null；Camera/Screen Space - Camera 画布要传画布相机，
        /// 否则换算结果会整体偏移 —— 这正是框选矩形不跟手的原因。
        /// </summary>
        private Camera EventCamera =>
            canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

        private void Awake()
        {
            canvas = GetComponentInParent<Canvas>();
            Build();
            Hide();
        }

        /// <summary>开始一次框选，先把矩形收敛到起点。</summary>
        public void Begin(Vector2 screenPoint)
        {
            Show(ScreenProjector.RectFromPoints(screenPoint, screenPoint));
        }

        /// <summary>拖拽中刷新矩形：<paramref name="start"/> 固定，<paramref name="current"/> 跟随鼠标。</summary>
        public void UpdateDrag(Vector2 start, Vector2 current)
        {
            Show(ScreenProjector.RectFromPoints(start, current));
        }

        /// <summary>结束框选，隐藏反馈。</summary>
        public void End()
        {
            Hide();
        }

        private void Show(Rect screenRect)
        {
            if (root == null)
            {
                return;
            }

            root.gameObject.SetActive(true);

            var parentRect = root.parent as RectTransform;
            if (parentRect == null)
            {
                return;
            }

            // 注意：ScreenPointToLocalPointInRectangle 返回的局部坐标以**父级 pivot**
            // 为原点，而 root 的锚点收敛在父级左下角（左下角才是 anchoredPosition 的
            // 参考点）。两者相差父级矩形的左下角偏移，不修正就会整体偏移半个画布
            // —— 表现为"框选跟着一个固定位移走"。
            var eventCamera = EventCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, screenRect.min, eventCamera, out Vector2 localMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, screenRect.max, eventCamera, out Vector2 localMax);

            Vector2 parentMin = parentRect.rect.min;
            Vector2 min = localMin - parentMin;
            Vector2 max = localMax - parentMin;

            Vector2 size = max - min;
            float referenceWidth = parentRect.rect.width;
            float thickness = Mathf.Max(1f, borderThicknessRatio * referenceWidth);

            // root 的锚点收敛在父级左下角（见 Build），此时 anchoredPosition 就是
            // 相对父级左下角的偏移，sizeDelta 就是绝对尺寸。矩形因此与鼠标精确对齐。
            root.anchoredPosition = min;
            root.sizeDelta = size;

            if (fillImage != null)
            {
                fillImage.enabled = fillEnabled;
            }

            if (fillRect != null && fillEnabled)
            {
                fillRect.offsetMin = new Vector2(thickness, thickness);
                fillRect.offsetMax = new Vector2(-thickness, -thickness);
            }

            // 四个边用独立的 Image 拼出描边，避免依赖 9-slice 贴图资源。
            float innerHeight = Mathf.Max(0f, size.y - thickness * 2f);
            SetEdge(borderEdges[0], new Vector2(0f, 0f), new Vector2(size.x, thickness));
            SetEdge(borderEdges[1], new Vector2(0f, size.y - thickness), new Vector2(size.x, thickness));
            SetEdge(borderEdges[2], new Vector2(0f, thickness), new Vector2(thickness, innerHeight));
            SetEdge(borderEdges[3], new Vector2(size.x - thickness, thickness), new Vector2(thickness, innerHeight));
        }

        private void Hide()
        {
            if (root != null && root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(false);
            }
        }

        private static void SetEdge(Image edge, Vector2 position, Vector2 size)
        {
            if (edge == null)
            {
                return;
            }

            var rect = edge.rectTransform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void Build()
        {
            var host = new GameObject("DragBox", typeof(RectTransform));
            root = host.GetComponent<RectTransform>();
            root.SetParent(transform, false);

            // 锚点收敛到父级左下角：这样 anchoredPosition 是绝对偏移、sizeDelta 是绝对尺寸。
            // 若用拉伸锚点（0,0)-(1,1)，sizeDelta 会变成"相对父级尺寸的增量"，
            // 矩形就会和鼠标位置对不上 —— 这是本组件最初的一个真实 bug。
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillRect = fill.GetComponent<RectTransform>();
            fillRect.SetParent(root, false);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillImage = fill.GetComponent<Image>();
            fillImage.color = fillColor;
            fillImage.raycastTarget = false;
            fillImage.enabled = fillEnabled;

            borderRoot = new GameObject("Border", typeof(RectTransform)).GetComponent<RectTransform>();
            borderRoot.SetParent(root, false);
            borderRoot.anchorMin = Vector2.zero;
            borderRoot.anchorMax = Vector2.zero;
            borderRoot.pivot = Vector2.zero;
            borderRoot.offsetMin = Vector2.zero;
            borderRoot.offsetMax = Vector2.zero;

            borderEdges = new Image[4];
            for (int i = 0; i < borderEdges.Length; i++)
            {
                var edge = new GameObject("Edge" + i, typeof(RectTransform), typeof(Image));
                var rect = edge.GetComponent<RectTransform>();
                rect.SetParent(borderRoot, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;

                var image = edge.GetComponent<Image>();
                image.color = borderColor;
                image.raycastTarget = false;
                borderEdges[i] = image;
            }
        }
    }
}
