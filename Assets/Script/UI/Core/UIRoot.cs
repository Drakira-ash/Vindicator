using UnityEngine;
using UnityEngine.UI;

namespace Vindicator.UI
{
    /// <summary>
    /// UI 根节点引用容器：把画布、缩放器、场景相机集中在一个地方，
    /// 让面板与交互组件可以互相解耦地取到自己需要的东西。
    /// 由场景搭建工具（Tools/UI/Create Selection Test Scene）自动挂载并填好引用。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIRoot : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private CanvasScaler canvasScaler;
        [SerializeField] private Camera sceneCamera;

        /// <summary>UI 画布。</summary>
        public Canvas Canvas => canvas != null ? canvas : (canvas = GetComponentInParent<Canvas>());

        /// <summary>画布缩放器。</summary>
        public CanvasScaler Scaler => canvasScaler != null
            ? canvasScaler
            : (canvasScaler = GetComponentInParent<CanvasScaler>());

        /// <summary>主场景相机；未显式配置时回退到 Camera.main。</summary>
        public Camera SceneCamera
        {
            get
            {
                if (sceneCamera == null)
                {
                    sceneCamera = Camera.main;
                }

                return sceneCamera;
            }
            set => sceneCamera = value;
        }

        /// <summary>画布的根 RectTransform，面板挂载的父级。</summary>
        public RectTransform RootRect => Canvas != null
            ? Canvas.transform as RectTransform
            : null;
    }
}
