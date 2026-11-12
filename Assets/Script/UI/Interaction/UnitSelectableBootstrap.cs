using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// 为运行时生成的单位补挂 <see cref="UnitSelectable"/>。
    ///
    /// 场景里预先摆好的单位由编辑器工具（Tools/UI/Create Selection Test Scene）挂好；
    /// 而刷怪器（<see cref="UnitSpawner"/>）生成的怪物不在场景里，只能等它们注册到
    /// <see cref="UnitRegistry"/> 时补上。这样**不需要修改预制体资产**，
    /// 同一份预制体在别的场景里的行为也不受影响。
    /// </summary>
    [DisallowMultipleComponent]
    public class UnitSelectableBootstrap : MonoBehaviour
    {
        private void OnEnable()
        {
            UnitRegistry.Changed += Sync;
            Sync();
        }

        private void OnDisable()
        {
            UnitRegistry.Changed -= Sync;
        }

        private static void Sync()
        {
            var all = UnitRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var health = all[i];

                // 单位销毁后注册表里的引用会短暂留成"假空"，必须判掉。
                if (health == null)
                {
                    continue;
                }

                if (health.GetComponent<UnitSelectable>() == null)
                {
                    health.gameObject.AddComponent<UnitSelectable>();
                }
            }
        }
    }
}
