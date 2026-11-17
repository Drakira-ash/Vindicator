using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Vindicator.UI
{
    /// <summary>
    /// 把鼠标输入翻译成“单击选择 / 拖动框选 / 取消选择”三种意图，交给
    /// <see cref="SelectionService"/> 执行。本组件只负责判定，不持有选择状态。
    /// 指针落在 UI 上时不触发场景选择，避免点面板按钮时误清空选择。
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectionInputReader : MonoBehaviour
    {
        [Tooltip("场景相机。留空则自动取 Camera.main。")]
        [SerializeField] private Camera targetCamera;

        [Tooltip("拖动超过该像素距离才算框选，否则视为单击。")]
        [SerializeField, Min(1f)] private float dragThresholdPixels = 6f;

        [Tooltip("是否允许右键 / Esc 取消选择。")]
        [SerializeField] private bool enableCancelInput = true;

        [Tooltip("拖动时的框选矩形绘制。留空则自动查找。")]
        [SerializeField] private DragBoxView dragBox;

        private SelectionService selection;
        private bool isDragging;
        private Vector2 dragStart;
        private Vector2 dragCurrent;

        /// <summary>当前是否正在拖拽框选。</summary>
        public bool IsDragging => isDragging;

        private void Awake()
        {
            selection = GetComponent<SelectionService>();
            if (selection == null)
            {
                selection = gameObject.AddComponent<SelectionService>();
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (dragBox == null)
            {
                dragBox = GetComponentInChildren<DragBoxView>(true);
            }
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 pointer = mouse.position.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame)
            {
                // 按下时落在 UI 上：这一整套操作都不属于场景。
                if (IsPointerOverUI(pointer))
                {
                    return;
                }

                isDragging = true;
                dragStart = pointer;
                dragCurrent = pointer;
                dragBox?.Begin(dragStart);
                return;
            }

            if (isDragging && mouse.leftButton.isPressed)
            {
                dragCurrent = pointer;
                dragBox?.UpdateDrag(dragStart, dragCurrent);
                return;
            }

            if (isDragging && mouse.leftButton.wasReleasedThisFrame)
            {
                isDragging = false;
                dragBox?.End();

                // 抬起时若指针在 UI 上（例如从场景拖到面板上松开），按取消处理。
                if (IsPointerOverUI(pointer))
                {
                    return;
                }

                bool additive = selection != null
                    && selection.AllowsAdditive(IsControlHeld());

                var rect = ScreenProjector.RectFromPoints(dragStart, pointer);
                bool wasDrag = Vector2.Distance(dragStart, pointer) >= dragThresholdPixels;

                if (wasDrag)
                {
                    selection?.HandleDragSelect(targetCamera, rect, additive);
                }
                else
                {
                    selection?.HandleClick(targetCamera, pointer, additive);
                }

                return;
            }

            if (enableCancelInput)
            {
                // 右键留给 PlayerCommandController 下移动/攻击命令，不再清空选择；
                // 只有 Esc 取消当前选择。
                bool cancelPressed = Keyboard.current != null
                    && Keyboard.current.escapeKey.wasPressedThisFrame;

                if (cancelPressed)
                {
                    isDragging = false;
                    dragBox?.End();
                    selection?.Clear();
                }
            }
        }

        /// <summary>指针是否落在任意 UI 元素上（含面板、按钮与开发者 IMGUI 面板）。</summary>
        public static bool IsPointerOverUI(Vector2 screenPoint)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null && eventSystem.IsPointerOverGameObject())
            {
                return true;
            }

            // 开发者生成面板是 IMGUI，不在 EventSystem 里；否则点它会误触发场景框选。
            return DeveloperSpawnController.BlocksWorldInput(screenPoint);
        }

        private static bool IsControlHeld()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            return keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        }

        private void OnDisable()
        {
            // 组件被关闭时结束拖拽，避免框选矩形残留在屏幕上。
            if (isDragging)
            {
                isDragging = false;
                dragBox?.End();
            }
        }
    }
}
