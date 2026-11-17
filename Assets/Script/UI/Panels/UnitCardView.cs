using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Vindicator.UI
{
    /// <summary>
    /// 单张单位卡片：图标 + 名称 + 血量条 + 选中态描边。
    /// 视觉结构在运行时构建，避免依赖手工维护的预制体层级；
    /// 卡片只负责显示与转发点击，选择状态由 <see cref="RosterPanelView"/> 统一驱动。
    /// </summary>
    [DisallowMultipleComponent]
    public class UnitCardView : MonoBehaviour, IPointerClickHandler
    {
        private const float IconSize = 54f;
        private const float BarHeight = 6f;

        [SerializeField] private Color selectedOutlineColor = new Color(1f, 0.78f, 0.2f, 1f);

        private Image background;
        private Image icon;
        private Image healthFill;
        private Image selectionOutline;
        private Text nameLabel;
        private Text healthLabel;

        private UnitSelectable bound;
        private UnitHealth boundHealth;

        /// <summary>卡片被点击时触发（参数为被点击的单位）。</summary>
        public event Action<UnitSelectable> Clicked;

        /// <summary>当前绑定的单位，未绑定时为 null。</summary>
        public UnitSelectable Bound => bound;

        /// <summary>构建内部控件。由 <see cref="RosterPanelView"/> 在实例化后调用一次。</summary>
        public void Build()
        {
            if (background != null)
            {
                return;
            }

            var rect = (RectTransform)transform;

            selectionOutline = CreateImage("SelectionOutline", rect, selectedOutlineColor);
            Stretch(selectionOutline.rectTransform, 0f);
            selectionOutline.enabled = false;

            background = CreateImage("Background", rect, new Color(0.12f, 0.13f, 0.16f, 0.85f));
            Stretch(background.rectTransform, 2f);

            // 卡片必须有一个可命中的 Graphic，否则 uGUI 射线找不到本卡片的
            // IPointerClickHandler，点击会穿透到场景。
            background.raycastTarget = true;

            icon = CreateImage("Icon", background.rectTransform, Color.gray);
            var iconRect = icon.rectTransform;
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.sizeDelta = new Vector2(IconSize, IconSize);
            iconRect.anchoredPosition = new Vector2(0f, -6f);

            // 血条底槽 + 填充
            var barBg = CreateImage("HealthBarBg", background.rectTransform, new Color(0.3f, 0.06f, 0.06f, 0.95f));
            var barBgRect = barBg.rectTransform;
            barBgRect.anchorMin = new Vector2(0.08f, 0f);
            barBgRect.anchorMax = new Vector2(0.92f, 0f);
            barBgRect.pivot = new Vector2(0.5f, 0f);
            barBgRect.anchoredPosition = new Vector2(0f, 22f);
            barBgRect.sizeDelta = new Vector2(0f, BarHeight);

            healthFill = CreateImage("HealthBarFill", barBgRect, new Color(0.25f, 0.85f, 0.35f, 1f));
            var fillRect = healthFill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            // 血量数字压在血条上，字号小，避免遮挡
            healthLabel = CreateText("HealthLabel", barBgRect, 10, TextAnchor.MiddleCenter);
            Stretch(healthLabel.rectTransform, 0f);

            nameLabel = CreateText("NameLabel", background.rectTransform, 11, TextAnchor.MiddleCenter);
            var nameRect = nameLabel.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 0f);
            nameRect.pivot = new Vector2(0.5f, 0f);
            nameRect.anchoredPosition = new Vector2(0f, 4f);
            nameRect.sizeDelta = new Vector2(-6f, 16f);
            nameLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            nameLabel.verticalOverflow = VerticalWrapMode.Truncate;
        }

        /// <summary>绑定到一个单位并刷新显示。</summary>
        public void Bind(UnitSelectable target)
        {
            Build();

            if (boundHealth != null)
            {
                boundHealth.HealthChanged -= OnHealthChanged;
            }

            bound = target;
            boundHealth = target != null ? target.Health : null;

            if (boundHealth != null)
            {
                boundHealth.HealthChanged += OnHealthChanged;
            }

            Refresh();
        }

        /// <summary>设置选中态描边。</summary>
        public void SetSelected(bool selected)
        {
            Build();
            if (selectionOutline != null)
            {
                selectionOutline.enabled = selected;
            }
        }

        /// <summary>按当前绑定刷新名称、血量与阵营配色。</summary>
        public void Refresh()
        {
            if (background == null)
            {
                return;
            }

            if (bound == null)
            {
                nameLabel.text = "--";
                healthLabel.text = string.Empty;
                icon.color = new Color(0.3f, 0.3f, 0.3f, 1f);
                healthFill.fillAmount = 0f;
                return;
            }

            nameLabel.text = bound.DisplayName;
            icon.color = SelectionHighlighter.GetColor(bound.Team) * 0.75f;
            icon.color = new Color(icon.color.r, icon.color.g, icon.color.b, 1f);

            float ratio = boundHealth != null && boundHealth.Max > 0f
                ? Mathf.Clamp01(boundHealth.Current / boundHealth.Max)
                : 0f;

            healthFill.fillAmount = ratio;
            healthFill.color = Color.Lerp(new Color(0.9f, 0.25f, 0.2f), new Color(0.25f, 0.85f, 0.35f), ratio);

            healthLabel.text = boundHealth != null
                ? string.Format("{0:F0}/{1:F0}", boundHealth.Current, boundHealth.Max)
                : string.Empty;
        }

        /// <summary>解绑并退订血量事件。</summary>
        public void Unbind()
        {
            if (boundHealth != null)
            {
                boundHealth.HealthChanged -= OnHealthChanged;
            }

            bound = null;
            boundHealth = null;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (bound != null)
            {
                Clicked?.Invoke(bound);
            }
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void OnHealthChanged(UnitHealth health)
        {
            Refresh();
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(string name, Transform parent, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = UIFontProvider.Get();
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }
    }
}
