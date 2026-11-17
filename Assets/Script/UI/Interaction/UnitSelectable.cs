using UnityEngine;

namespace Vindicator.UI
{
    /// <summary>
    /// 把 <see cref="UnitHealth"/> 桥接成 <see cref="ISelectable"/>。
    /// 这是单位系统与 UI 系统之间唯一的连接点：单位侧不需要知道 UI 的存在，
    /// 移除本组件即可让某个单位不参与选择。
    /// 不新增任何 Collider，也不改变单位自身的渲染材质（见 <see cref="SelectionHighlighter"/>）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnitHealth))]
    public class UnitSelectable : MonoBehaviour, ISelectable
    {
        [Tooltip("世界空间包围球半径，用于屏幕投影时的点击容差。留 0 则按碰撞体自动估算。")]
        [SerializeField, Min(0f)] private float worldRadiusOverride = 0f;

        private UnitHealth health;
        private Unit unit;
        private UnitCommander commander;
        private float cachedRadius;
        private bool radiusResolved;

        /// <summary>
        /// 单位根节点。对象已被销毁时返回 null —— 选择集合可能跨帧持有本组件，
        /// 直接读 transform 会抛 MissingReferenceException。
        /// </summary>
        public Transform Transform => this != null ? transform : null;

        /// <summary>
        /// 阵营。按优先级取权威来源，避免时序窗口读到过期数据：
        ///
        /// 1. <see cref="UnitDefinition.team">——权威模板值。预制体上序列化的
        ///    <c>UnitHealth.team</c> 全都是 Enemy（包括战士和塔），真实阵营要等
        ///    <c>Unit.Awake</c> 用模板覆盖后才正确。直接读模板可绕开这个窗口。
        /// 2. <c>UnitHealth.team</c>——没有 Unit 组件时的回退（例如只挂血条的 Home）。
        /// 3. Neutral。
        ///
        /// 注意不要直接读缓存字段：它在 Awake 之前为 null，会错误返回 Neutral，
        /// 面板会因此把该单位从敌人/友军两栏都丢掉。
        /// </summary>
        public UnitTeam Team
        {
            get
            {
                EnsureReferences();

                if (unit != null && unit.Definition != null)
                {
                    return unit.Definition.team;
                }

                return health != null ? health.Team : UnitTeam.Neutral;
            }
        }

        public bool IsSelectable =>
            this != null && health != null && health.IsAlive && isActiveAndEnabled;

        public string DisplayName
        {
            get
            {
                if (unit != null && unit.Definition != null && !string.IsNullOrEmpty(unit.Definition.displayName))
                {
                    return unit.Definition.displayName;
                }

                return name;
            }
        }

        public Vector3 WorldPosition => health != null ? health.DamageTransform.position : transform.position;

        public float WorldRadius
        {
            get
            {
                if (!radiusResolved)
                {
                    cachedRadius = ResolveRadius();
                    radiusResolved = true;
                }

                return cachedRadius;
            }
        }

        /// <summary>血量组件，面板订阅血量变化时使用。</summary>
        public UnitHealth Health
        {
            get
            {
                EnsureReferences();
                return health;
            }
        }

        /// <summary>底层单位，选择桥把 UI 选择同步给 UnitSelection 时使用。</summary>
        public Unit Unit
        {
            get
            {
                EnsureReferences();
                return unit;
            }
        }

        /// <summary>所属小队编号；未编队返回 -1。懒查找，不在 getter 里创建组件。</summary>
        public int SquadId
        {
            get
            {
                if (commander == null)
                {
                    commander = GetComponent<UnitCommander>();
                }

                return commander != null ? commander.SquadId : -1;
            }
        }

        /// <summary>所属小队；未编队或小队已解散返回 null。</summary>
        public Squad Squad
        {
            get
            {
                int id = SquadId;
                return id >= 0 ? SquadRegistry.Find(id) : null;
            }
        }

        private void Awake()
        {
            EnsureReferences();
        }

        private void OnValidate()
        {
            radiusResolved = false;
        }

        public void SetSelected(bool selected)
        {
            SelectionHighlighter.Apply(this, selected);
        }

        private void EnsureReferences()
        {
            if (health == null)
            {
                health = GetComponent<UnitHealth>();
            }

            if (unit == null)
            {
                unit = GetComponent<Unit>();
            }
        }

        /// <summary>
        /// 半径优先取显式配置，其次按碰撞体估算，最后退化到固定值。
        /// 用碰撞体包围盒而不是单位根 Transform，避免模型缩放影响判定。
        /// </summary>
        private float ResolveRadius()
        {
            if (worldRadiusOverride > 0f)
            {
                return worldRadiusOverride;
            }

            var colliders = GetComponentsInChildren<Collider>(true);
            float best = 0f;
            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null)
                {
                    continue;
                }

                var extents = collider.bounds.extents;
                float radius = Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));
                if (radius > best)
                {
                    best = radius;
                }
            }

            return best > 0f ? best : 0.5f;
        }
    }
}
