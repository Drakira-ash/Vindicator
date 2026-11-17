using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;

namespace Vindicator.UI.EditorTools
{
    /// <summary>
    /// 一键搭建 UI 与交互验收场景：把 <c>RoadWorkflow_Test.unity</c> 复制为 <c>UI_Test.unity</c>，
    /// 在副本上追加 Canvas / EventSystem / 选择交互组件 / 部队预览面板。
    /// 单位本体材质保持不变；选中描边由 <see cref="SelectionHighlighter"/> 在运行时
    /// 追加材质槽实现。
    /// **源场景不会被修改**，用户在其中未提交的改动也不受影响。
    /// 菜单：Tools/UI/Create Selection Test Scene
    /// </summary>
    public static class SelectionTestSceneSetup
    {
        private const string SourceScenePath = "Assets/Scenes/RoadWorkflow_Test.unity";
        private const string TargetScenePath = "Assets/Scenes/UI_Test.unity";
        private const string LegacyTargetScenePath = "Assets/Scenes/MVP_UI_Test.unity";

        private const string UIRootName = "UI";
        private const string CanvasName = "Canvas";
        private const string EventSystemName = "EventSystem";
        private const string InteractionName = "Interaction";
        private const string PanelName = "RosterPanel";

        private static readonly Color PlayerAccent = new Color(0.25f, 0.85f, 1f, 1f);
        private static readonly Color EnemyAccent = new Color(1f, 0.35f, 0.15f, 1f);

        [MenuItem("Tools/UI/Create Selection Test Scene", false, 20)]
        public static void CreateSelectionTestScene()
        {
            if (!File.Exists(SourceScenePath))
            {
                Debug.LogError("[UI] 找不到源场景 " + SourceScenePath);
                return;
            }

            if (!EnsureSaveCurrentScene())
            {
                return;
            }

            // 清理上一轮的产物：旧命名（MVP 副本）与新命名。只删本工具生成的这两个路径。
            DeleteIfExists(LegacyTargetScenePath);
            DeleteIfExists(TargetScenePath);

            // 1. 复制场景。副本独立存在，源场景保持原样。
            if (!AssetDatabase.CopyAsset(SourceScenePath, TargetScenePath))
            {
                Debug.LogError("[UI] 复制场景失败：" + TargetScenePath);
                return;
            }

            AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);

            // 2. 给场景里已有的单位补 UnitSelectable。运行时刷出来的单位由
            //    UnitSelectableBootstrap 在注册表事件里补齐（同样不动预制体）。
            int selectableCount = AttachSelectables(scene);

            // 3. UI：Canvas / EventSystem / 交互组件 / 面板 / 运行时补充组件。
            //    高亮不需要编辑器预置：SelectionHighlighter 在选中时给渲染器追加
            //    一个描边材质槽，本体材质（URP Lit）始终不动。
            BuildUserInterface(scene, camera: Camera.main);

            // 开发者模式与编队体系：开发者生成面板/数据表/目录是运行时自举的，
            // 但 DeveloperMode 开关需要场景里有一个序列化对象才会默认启用。
            UnitDemoSetup.EnsureDeveloperModeObject();

            // 注意：这里**不冻结**战斗。UI_Test 必须是 RoadWorkflow_Test 的真实场景
            // （怪物逐波刷出、塔防交战），否则验收的不是实际玩法。战斗会改变面板内容，
            // 这是预期行为。

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(string.Format(
                "[UI] {0} 构建完成：{1} 个场景单位挂上 UnitSelectable。",
                TargetScenePath, selectableCount));
        }

        /// <summary>
        /// 原地更新 UI_Test：保留场景现有 AI/塔/刷怪布置，只补 UnitSelectable、
        /// 重建生成 UI/EventSystem，并确保 DeveloperMode 默认开启。幂等，可重复执行。
        /// </summary>
        [MenuItem("Tools/UI/Update Selection Test Scene In Place", false, 21)]
        public static void UpdateSelectionTestSceneInPlace()
        {
            if (!EnsureSaveCurrentScene())
            {
                return;
            }

            if (!File.Exists(TargetScenePath))
            {
                Debug.LogError(
                    "[UI] 找不到 " + TargetScenePath +
                    "，请先执行 Tools/UI/Create Selection Test Scene。");
                return;
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != TargetScenePath)
            {
                scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            }

            int selectableCount = AttachSelectables(scene);
            BuildUserInterface(scene, camera: Camera.main);
            UnitDemoSetup.EnsureDeveloperModeObject();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(string.Format(
                "[UI] {0} 原地更新完成：{1} 个场景单位带 UnitSelectable，DeveloperMode 已开启。",
                TargetScenePath, selectableCount));
        }

        private static void DeleteIfExists(string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        // -----------------------------------------------------------------
        // 单位选择支持
        // -----------------------------------------------------------------

        /// <summary>
        /// 给场景中每个带 UnitHealth 的对象补上 UnitSelectable。
        /// 幂等：已有组件的对象会跳过。
        /// </summary>
        private static int AttachSelectables(Scene scene)
        {
            int count = 0;
            var roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                var healths = roots[i].GetComponentsInChildren<UnitHealth>(true);
                for (int j = 0; j < healths.Length; j++)
                {
                    var health = healths[j];
                    if (health == null)
                    {
                        continue;
                    }

                    if (health.GetComponent<UnitSelectable>() == null)
                    {
                        health.gameObject.AddComponent<UnitSelectable>();
                    }

                    count++;
                }
            }

            return count;
        }

        // -----------------------------------------------------------------
        // UI 搭建
        // -----------------------------------------------------------------

        private static void BuildUserInterface(Scene scene, Camera camera)
        {
            // 幂等：重复执行先清掉旧的 UI 层级。
            DestroyIfPresent(UIRootName);
            DestroyIfPresent(EventSystemName);

            if (camera == null)
            {
                Debug.LogWarning("[UI] 场景没有 Main Camera，面板的相机引用需要手工指定。");
            }

            EnsureEventSystem();
            BuildCanvas(camera);
        }

        private static void DestroyIfPresent(string name)
        {
            var found = GameObject.Find(name);
            if (found != null)
            {
                Object.DestroyImmediate(found);
            }
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject(EventSystemName, typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.position = Vector3.zero;
        }

        private static void BuildCanvas(Camera camera)
        {
            var uiRootGo = new GameObject(UIRootName, typeof(RectTransform));
            var canvasGo = new GameObject(
                CanvasName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            canvasGo.transform.SetParent(uiRootGo.transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // 交互根：选择服务 + 输入读取 + 框选绘制
            var interactionGo = new GameObject(InteractionName, typeof(RectTransform));
            interactionGo.transform.SetParent(canvasGo.transform, false);
            StretchFull(interactionGo.GetComponent<RectTransform>());

            var selection = interactionGo.AddComponent<SelectionService>();
            var inputReader = interactionGo.AddComponent<SelectionInputReader>();
            var dragBox = interactionGo.AddComponent<DragBoxView>();

            // 运行时刷出来的怪物不在场景里，靠这个组件在注册表事件里补 UnitSelectable。
            interactionGo.AddComponent<UnitSelectableBootstrap>();

            // 新 UI 选择与旧命令层 UnitSelection 的桥。没有它，点卡片后右键命令看不到选中单位。
            interactionGo.AddComponent<SelectionCommandBridge>();

            SetPrivateField(inputReader, "targetCamera", camera);
            SetPrivateField(inputReader, "dragBox", dragBox);

            var panel = BuildRosterPanel(canvasGo.transform, selection);

            var uiRoot = uiRootGo.AddComponent<UIRoot>();
            SetPrivateField(uiRoot, "canvas", canvas);
            SetPrivateField(uiRoot, "canvasScaler", scaler);
            SetPrivateField(uiRoot, "sceneCamera", camera);

            // 面板排在交互层之下，保证框选矩形画在面板之上时仍可点击面板按钮。
            panel.transform.SetAsFirstSibling();
        }

        private static RosterPanelView BuildRosterPanel(Transform canvasTransform, SelectionService selection)
        {
            // 面板尺寸由 RosterLayout 按内容算出，不再硬编码 —— 硬编码正是
            // 之前"栏位比卡片小、卡片溢出容器"的根因。
            float panelHeight = RosterLayout.PanelHeight;
            float panelWidth = RosterLayout.PanelWidth;

            var panelGo = new GameObject(PanelName, typeof(RectTransform));
            panelGo.transform.SetParent(canvasTransform, false);

            var panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);
            panelRect.anchoredPosition = Vector2.zero;

            var background = CreateImage(panelGo.transform, "Background", new Color(0.07f, 0.08f, 0.11f, 0.92f));
            StretchFull(background.rectTransform);

            // 两栏分别锚定面板左半 / 右半，并留出内边距。
            var enemyGrid = CreateSection(panelGo.transform, "EnemySection", isLeft: true, accent: EnemyAccent, label: "敌人");
            var allyGrid = CreateSection(panelGo.transform, "AllySection", isLeft: false, accent: PlayerAccent, label: "友军");

            // 中缝分隔线
            var divider = CreateImage(panelGo.transform, "Divider", new Color(1f, 1f, 1f, 0.12f));
            var dividerRect = divider.rectTransform;
            dividerRect.anchorMin = new Vector2(0.5f, 0f);
            dividerRect.anchorMax = new Vector2(0.5f, 1f);
            dividerRect.pivot = new Vector2(0.5f, 0.5f);
            dividerRect.sizeDelta = new Vector2(1f, -(RosterLayout.PaddingY * 2f));
            dividerRect.anchoredPosition = Vector2.zero;

            // 角标挂在栏位（Section）而不是网格（Grid）下：网格里已经排满 6 张卡，
            // 角标放进去必然压住最后一张卡。栏位底部有 RosterLayout.BadgeHeight 的
            // 专用预留行，角标落在这里才不会遮挡卡片。
            var enemyBadge = CreateOverflowBadge(enemyGrid.parent, "EnemyOverflow", EnemyAccent, out var enemyBadgeLabel);
            var allyBadge = CreateOverflowBadge(allyGrid.parent, "AllyOverflow", PlayerAccent, out var allyBadgeLabel);

            var fullListRoot = BuildFullList(panelGo.transform, out var fullListContent, out var closeButton);

            // 把手：面板的兄弟节点，收回时贴着面板顶边移动，不会被一起推出屏幕。
            var handleGo = new GameObject("ToggleHandle", typeof(RectTransform));
            handleGo.transform.SetParent(canvasTransform, false);
            var handleRect = handleGo.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0.5f, 0f);
            handleRect.anchorMax = new Vector2(0.5f, 0f);
            handleRect.pivot = new Vector2(0.5f, 0f);
            handleRect.sizeDelta = new Vector2(120f, 34f);
            handleRect.anchoredPosition = new Vector2(0f, panelHeight);

            var handleImage = handleGo.AddComponent<Image>();
            handleImage.color = new Color(0.16f, 0.18f, 0.24f, 0.98f);
            var handleButton = handleGo.AddComponent<Button>();
            handleButton.targetGraphic = handleImage;

            var handleLabel = CreateText(handleGo.transform, "Label", "展开", 16, TextAnchor.MiddleCenter);
            StretchFull(handleLabel.rectTransform);

            var view = panelGo.AddComponent<RosterPanelView>();
            SetPrivateField(view, "selection", selection);
            SetPrivateField(view, "panelRoot", panelRect);
            SetPrivateField(view, "contentRoot", background.rectTransform);
            SetPrivateField(view, "toggleButton", handleButton);
            SetPrivateField(view, "toggleLabel", handleLabel);
            SetPrivateField(view, "toggleHandle", handleRect);
            SetPrivateField(view, "enemyGrid", enemyGrid);
            SetPrivateField(view, "allyGrid", allyGrid);
            // 不能用 grid.GetComponentInChildren<Text>()：Grid 下的第一个 Text 是
            // 溢出角标的标签，会把「敌人 (9)」挤进 48px 的角标里，栏标题反而没有数量。
            SetPrivateField(view, "enemyCountLabel", FindSectionHeader(enemyGrid));
            SetPrivateField(view, "allyCountLabel", FindSectionHeader(allyGrid));
            SetPrivateField(view, "enemyOverflowButton", enemyBadge);
            SetPrivateField(view, "allyOverflowButton", allyBadge);
            SetPrivateField(view, "enemyOverflowLabel", enemyBadgeLabel);
            SetPrivateField(view, "allyOverflowLabel", allyBadgeLabel);
            SetPrivateField(view, "fullListRoot", fullListRoot);
            SetPrivateField(view, "fullListContent", fullListContent);
            SetPrivateField(view, "fullListCloseButton", closeButton);
            SetPrivateField(view, "collapsedOffset", -panelHeight);

            return view;
        }

        /// <summary>
        /// 建一栏：一个定位于面板左半/右半的容器，内部含标题与卡片网格。
        /// 容器的尺寸按 RosterLayout 的内容尺寸设置，保证卡片被完整容纳。
        /// </summary>
        private static RectTransform CreateSection(Transform panel, string name, bool isLeft, Color accent, string label)
        {
            var sectionGo = new GameObject(name, typeof(RectTransform));
            sectionGo.transform.SetParent(panel, false);

            var sectionRect = sectionGo.GetComponent<RectTransform>();
            // 左栏锚在面板左半边，右栏锚在右半边；再各留 PaddingX 的内边距。
            float anchorMin = isLeft ? 0f : 0.5f;
            float anchorMax = isLeft ? 0.5f : 1f;
            sectionRect.anchorMin = new Vector2(anchorMin, 0f);
            sectionRect.anchorMax = new Vector2(anchorMax, 1f);
            sectionRect.pivot = new Vector2(0.5f, 0.5f);
            sectionRect.offsetMin = new Vector2(RosterLayout.PaddingX, RosterLayout.PaddingY);
            sectionRect.offsetMax = new Vector2(-RosterLayout.PaddingX, -RosterLayout.PaddingY);

            // 标题
            var header = CreateText(sectionGo.transform, "Header", label, 16, TextAnchor.MiddleCenter);
            header.color = accent;
            var headerRect = header.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, RosterLayout.SectionHeaderHeight);
            headerRect.anchoredPosition = Vector2.zero;

            // 卡片网格：尺寸严格等于内容尺寸（3 列 × 2 行 + 间距），
            // 这样卡片恰好被容器装下。锚在标题下方居中。
            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(sectionGo.transform, false);

            var gridRect = gridGo.GetComponent<RectTransform>();
            gridRect.anchorMin = new Vector2(0.5f, 1f);
            gridRect.anchorMax = new Vector2(0.5f, 1f);
            gridRect.pivot = new Vector2(0.5f, 1f);
            gridRect.sizeDelta = new Vector2(RosterLayout.GridWidth, RosterLayout.GridHeight);
            gridRect.anchoredPosition = new Vector2(0f, -(RosterLayout.SectionHeaderHeight + RosterLayout.ContentTopOffset));

            return gridRect;
        }

        /// <summary>取分栏标题文字（Section/Header），用于显示「敌人 (n)」计数。</summary>
        private static Text FindSectionHeader(RectTransform grid)
        {
            var section = grid != null ? grid.parent : null;
            var header = section != null ? section.Find("Header") : null;
            return header != null ? header.GetComponent<Text>() : null;
        }

        private static Button CreateOverflowBadge(Transform section, string name, Color accent, out Text label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(section, false);

            var rect = go.GetComponent<RectTransform>();
            // 角标贴在栏位右下角的专用预留行里（草图的 +X 在右下角）。
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(52f, RosterLayout.BadgeHeight - 4f);
            rect.anchoredPosition = new Vector2(0f, 2f);

            var image = go.GetComponent<Image>();
            image.color = new Color(accent.r, accent.g, accent.b, 0.9f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            label = CreateText(go.transform, "Label", "+0", 15, TextAnchor.MiddleCenter);
            StretchFull(label.rectTransform);

            go.AddComponent<OverflowBadgeView>();
            return button;
        }

        private static RectTransform BuildFullList(Transform parent, out RectTransform content, out Button closeButton)
        {
            var rootGo = new GameObject("FullList", typeof(RectTransform));
            rootGo.transform.SetParent(parent, false);

            var rootRect = rootGo.GetComponent<RectTransform>();
            // 完整列表是覆盖层，锚到画布而非面板，避免被面板高度裁掉。
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(760f, 320f);
            rootRect.anchoredPosition = Vector2.zero;

            var bg = CreateImage(rootGo.transform, "Background", new Color(0.05f, 0.06f, 0.09f, 0.98f));
            StretchFull(bg.rectTransform);

            var title = CreateText(rootGo.transform, "Title", "全部单位", 16, TextAnchor.MiddleLeft);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(-24f, 28f);
            titleRect.anchoredPosition = new Vector2(-12f, -6f);

            var closeGo = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(rootGo.transform, false);
            var closeRect = closeGo.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(38f, 28f);
            closeRect.anchoredPosition = new Vector2(-8f, -6f);

            var closeImage = closeGo.GetComponent<Image>();
            closeImage.color = new Color(0.5f, 0.2f, 0.2f, 0.95f);
            closeButton = closeGo.GetComponent<Button>();
            closeButton.targetGraphic = closeImage;

            var closeLabel = CreateText(closeGo.transform, "Label", "×", 18, TextAnchor.MiddleCenter);
            StretchFull(closeLabel.rectTransform);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(rootGo.transform, false);
            content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(1f, 1f);
            content.offsetMin = new Vector2(10f, 10f);
            content.offsetMax = new Vector2(-10f, -38f);

            rootGo.SetActive(false);
            return rootRect;
        }

        // -----------------------------------------------------------------
        // 通用小工具
        // -----------------------------------------------------------------

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(Transform parent, string name, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = UIFontProvider.Get();
            text.fontSize = size;
            text.text = content;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf))
            {
                EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }

        private static bool EnsureSaveCurrentScene()
        {
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                bool saved = EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                if (!saved)
                {
                    Debug.Log("[UI] 用户取消了保存，操作中止。");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 通过 SerializedObject 写私有序列化字段，避免为搭建工具放宽字段可见性。
        /// </summary>
        private static void SetPrivateField(Object target, string fieldName, object value)
        {
            if (target == null)
            {
                return;
            }

            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogWarning(string.Format("[UI] {0} 上找不到字段 {1}", target.GetType().Name, fieldName));
                return;
            }

            switch (value)
            {
                case null:
                    property.objectReferenceValue = null;
                    break;
                case Object unityObject:
                    property.objectReferenceValue = unityObject;
                    break;
                case bool boolValue:
                    property.boolValue = boolValue;
                    break;
                case int intValue:
                    property.intValue = intValue;
                    break;
                case float floatValue:
                    property.floatValue = floatValue;
                    break;
                case string stringValue:
                    property.stringValue = stringValue;
                    break;
                default:
                    Debug.LogWarning(string.Format("[UI] 不支持的字段类型 {0}", value.GetType().Name));
                    return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
