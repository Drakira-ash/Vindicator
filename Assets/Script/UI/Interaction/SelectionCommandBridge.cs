using System.Collections.Generic;
using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// 把新 UI 的 <see cref="SelectionService"/> 与旧命令层的 <see cref="UnitSelection"/>
    /// 双向同步。新 UI 点选卡片后，右键移动/攻击才能看到同一批选中单位；
    /// 反之 F1、旧 SquadPanel、开发者模式等通过 UnitSelection 做的选择也会反映到卡片。
    /// 同步过程用 <see cref="syncing"/> 防止两个事件互相递归。
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectionCommandBridge : MonoBehaviour
    {
        private SelectionService selection;
        private bool syncing;
        private bool subscribed;
        private bool legacyInputDisabled;

        private void Start()
        {
            selection = SelectionService.Instance;
            if (selection == null)
            {
                Debug.LogWarning("[SelectionBridge] 没找到 SelectionService，UI 选择不会同步到命令层。", this);
                return;
            }

            selection.SelectionChanged += OnSelectionChanged;
            UnitSelection.Changed += OnUnitSelectionChanged;
            subscribed = true;

            // 新 UI 已经有 SelectionInputReader 处理左键框选；关闭旧控制器的重复选择，
            // 但保留右键命令/热键。
            if (PlayerCommandController.Instance != null)
            {
                PlayerCommandController.Instance.LegacySelectionEnabled = false;
                legacyInputDisabled = true;
            }
        }

        private void Update()
        {
            // PlayerCommandController 的 bootstrap 顺序不保证一定早于本组件的 Start；
            // 这里补一次，确保旧左键选择/IMGUI 标记在 UI_Test 里关闭。
            if (!legacyInputDisabled && PlayerCommandController.Instance != null)
            {
                PlayerCommandController.Instance.LegacySelectionEnabled = false;
                legacyInputDisabled = true;
            }
        }

        private void OnDestroy()
        {
            if (!subscribed)
            {
                return;
            }

            if (selection != null)
            {
                selection.SelectionChanged -= OnSelectionChanged;
            }

            UnitSelection.Changed -= OnUnitSelectionChanged;
        }

        private void OnSelectionChanged(IReadOnlyList<ISelectable> current)
        {
            if (syncing)
            {
                return;
            }

            var units = new List<Unit>();
            if (current != null)
            {
                for (int i = 0; i < current.Count; i++)
                {
                    var selectable = current[i] as UnitSelectable;
                    if (selectable == null)
                    {
                        continue;
                    }

                    // 已销毁组件的接口引用不是 C# null，取 Unit 前先做 Unity 假空判断。
                    if ((Object)selectable == null)
                    {
                        continue;
                    }

                    var unit = selectable.Unit;
                    if (unit != null && !units.Contains(unit))
                    {
                        units.Add(unit);
                    }
                }
            }

            syncing = true;
            UnitSelection.Set(units);
            syncing = false;
        }

        private void OnUnitSelectionChanged()
        {
            if (syncing || selection == null)
            {
                return;
            }

            var selectables = new List<ISelectable>();
            var units = UnitSelection.All;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null)
                {
                    continue;
                }

                var selectable = unit.GetComponent<UnitSelectable>();
                if (selectable != null && selectable.IsSelectable && !selectables.Contains(selectable))
                {
                    selectables.Add(selectable);
                }
            }

            syncing = true;
            selection.SelectMany(selectables, false);
            syncing = false;
        }
    }
}
