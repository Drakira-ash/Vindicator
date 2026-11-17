using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Vindicator.UI
{
    /// <summary>
    /// 队伍兵牌：把一个小队的成员合成一张卡。显示队名、人数、阵型/状态、
    /// 聚合血条与姿态图标；点击卡体选中全队并展开成员浮层，点击剑/盾切换姿态。
    /// </summary>
    [DisallowMultipleComponent]
    public class SquadCardView : MonoBehaviour, IPointerClickHandler
    {
        private const float IconSize = 36f;
        private const float BarHeight = 5f;

        [SerializeField] private Color selectedOutlineColor = new Color(1f, 0.78f, 0.2f, 1f);
        [SerializeField] private Color partialOutlineColor = new Color(0.25f, 0.85f, 1f, 1f);

        private Image background;
        private Image icon;
        private Image healthFill;
        private Image selectionOutline;
        private Image postureBackground;
        private Text nameLabel;
        private Text infoLabel;
        private Text countLabel;
        private Text healthLabel;
        private Text postureLabel;
        private Button postureButton;

        private RosterGroupData bound;

        /// <summary>卡体被点击（选中全队并展开成员浮层）。</summary>
        public event Action<RosterGroupData> Clicked;

        /// <summary>剑/盾姿态按钮被点击。</summary>
        public event Action<RosterGroupData> PostureClicked;

        public RosterGroupData Bound => bound;

        public void Build()
        {
            if (background != null)
            {
                return;
            }

            var rect = (RectTransform)transform;

            selectionOutline = CreateImage("SelectionOutline", rect, selectedOutlineColor, false);
            Stretch(selectionOutline.rectTransform, 0f);
            selectionOutline.enabled = false;

            background = CreateImage("Background", rect, new Color(0.12f, 0.13f, 0.16f, 0.9f), true);
            Stretch(background.rectTransform, 2f);

            icon = CreateImage("SquadIcon", background.rectTransform, Color.gray, false);
            var iconRect = icon.rectTransform;
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.sizeDelta = new Vector2(IconSize, IconSize);
            iconRect.anchoredPosition = new Vector2(0f, -4f);

            countLabel = CreateText("Count", iconRect, 15, TextAnchor.MiddleCenter);
            Stretch(countLabel.rectTransform, 0f);
            countLabel.fontStyle = FontStyle.Bold;

            nameLabel = CreateText("NameLabel", background.rectTransform, 10, TextAnchor.MiddleCenter);
            var nameRect = nameLabel.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0f, -42f);
            nameRect.sizeDelta = new Vector2(-4f, 14f);
            nameLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            nameLabel.verticalOverflow = VerticalWrapMode.Truncate;

            infoLabel = CreateText("InfoLabel", background.rectTransform, 8, TextAnchor.MiddleCenter);
            var infoRect = infoLabel.rectTransform;
            infoRect.anchorMin = new Vector2(0f, 1f);
            infoRect.anchorMax = new Vector2(1f, 1f);
            infoRect.pivot = new Vector2(0.5f, 1f);
            infoRect.anchoredPosition = new Vector2(0f, -56f);
            infoRect.sizeDelta = new Vector2(-4f, 12f);
            infoLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            infoLabel.verticalOverflow = VerticalWrapMode.Truncate;
            infoLabel.color = new Color(0.75f, 0.8f, 0.85f, 1f);

            var barBg = CreateImage(
                "HealthBarBg", background.rectTransform,
                new Color(0.3f, 0.06f, 0.06f, 0.95f), false);
            var barBgRect = barBg.rectTransform;
            barBgRect.anchorMin = new Vector2(0.1f, 0f);
            barBgRect.anchorMax = new Vector2(0.9f, 0f);
            barBgRect.pivot = new Vector2(0.5f, 0f);
            barBgRect.anchoredPosition = new Vector2(0f, 18f);
            barBgRect.sizeDelta = new Vector2(0f, BarHeight);

            healthFill = CreateImage(
                "HealthBarFill", barBgRect, new Color(0.25f, 0.85f, 0.35f, 1f), false);
            var fillRect = healthFill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            healthLabel = CreateText("HealthLabel", barBgRect, 8, TextAnchor.MiddleCenter);
            Stretch(healthLabel.rectTransform, 0f);

            // 剑/盾姿态按钮（右上角）
            postureButton = CreateButton(
                "PostureButton", background.rectTransform,
                new Color(0.18f, 0.2f, 0.24f, 0.95f));
            var postureRect = (RectTransform)postureButton.transform;
            postureRect.anchorMin = new Vector2(1f, 1f);
            postureRect.anchorMax = new Vector2(1f, 1f);
            postureRect.pivot = new Vector2(1f, 1f);
            postureRect.sizeDelta = new Vector2(22f, 22f);
            postureRect.anchoredPosition = new Vector2(-1f, -1f);
            postureBackground = postureButton.GetComponent<Image>();

            postureLabel = CreateText("Glyph", postureRect, 12, TextAnchor.MiddleCenter);
            Stretch(postureLabel.rectTransform, 0f);
            postureLabel.fontStyle = FontStyle.Bold;

            postureButton.onClick.AddListener(() => PostureClicked?.Invoke(bound));

            var expandHint = CreateText("ExpandHint", background.rectTransform, 11, TextAnchor.MiddleCenter);
            var hintRect = expandHint.rectTransform;
            hintRect.anchorMin = new Vector2(1f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(1f, 0f);
            hintRect.sizeDelta = new Vector2(14f, 14f);
            hintRect.anchoredPosition = new Vector2(-2f, 2f);
            expandHint.text = "▾";
            expandHint.color = new Color(0.8f, 0.85f, 0.9f, 1f);
        }

        public void Bind(RosterGroupData group)
        {
            Build();
            bound = group;
            Refresh();
        }

        public void SetSelected(RosterSelectionState state)
        {
            Build();
            if (selectionOutline == null)
            {
                return;
            }

            selectionOutline.enabled = state != RosterSelectionState.None;
            selectionOutline.color = state == RosterSelectionState.Partial
                ? partialOutlineColor
                : selectedOutlineColor;
        }

        public void Refresh()
        {
            if (background == null)
            {
                return;
            }

            if (bound == null)
            {
                nameLabel.text = "--";
                infoLabel.text = string.Empty;
                countLabel.text = string.Empty;
                healthLabel.text = string.Empty;
                healthFill.fillAmount = 0f;
                postureLabel.text = "—";
                postureButton.interactable = false;
                return;
            }

            nameLabel.text = bound.DisplayName;
            countLabel.text = bound.MemberCount.ToString();

            var squad = bound.Squad;
            if (squad != null)
            {
                infoLabel.text = string.Format("{0} · {1}", squad.Formation, squad.State);
            }
            else
            {
                infoLabel.text = "单兵";
            }

            icon.color = WithAlpha(SelectionHighlighter.GetColor(bound.Team) * 0.75f, 1f);

            float ratio = bound.HealthMax > 0f
                ? Mathf.Clamp01(bound.HealthCurrent / bound.HealthMax)
                : 0f;
            healthFill.fillAmount = ratio;
            healthFill.color = Color.Lerp(
                new Color(0.9f, 0.25f, 0.2f),
                new Color(0.25f, 0.85f, 0.35f),
                ratio);
            healthLabel.text = string.Format("{0:F0}/{1:F0}", bound.HealthCurrent, bound.HealthMax);

            bool alert = squad != null && squad.Posture == SquadPosture.Alert;
            postureLabel.text = squad == null ? "—" : (alert ? "剑" : "盾");
            postureBackground.color = alert
                ? new Color(0.55f, 0.28f, 0.12f, 0.97f)
                : new Color(0.16f, 0.38f, 0.58f, 0.97f);
            postureButton.interactable = squad != null;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (bound != null)
            {
                Clicked?.Invoke(bound);
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Image CreateImage(
            string name, Transform parent, Color color, bool raycastTarget)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
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

        private static Button CreateButton(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            return button;
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
