using UnityEngine;

/// <summary>
/// 公民行为：
/// Idle 时先找感知范围内的友方建筑——废墟优先，有废墟就去修建；
/// 没有废墟但有已建成建筑就走去进驻（进驻后销毁自身）；
/// 范围内没有建筑时才索敌攻击，没有敌人则继续 Idle。
/// 修建途中被打断或死亡时，建造进度保留在建筑上，后续公民继续修。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Unit))]
public class CitizenBrain : MonoBehaviour, IPerceptionSource
{
    private enum State
    {
        Idle,
        MoveToRepair,
        Repair,
        MoveToGarrison,
        Chase,
        Attack
    }

    // 回退值：手工放置或未绑定 UnitDefinition 的公民仍能感知/索敌（与 EnemyBrain/WarriorBrain 一致）。
    [SerializeField, Min(0f)] private float perceptionRange = 2f;
    [SerializeField, Min(0.05f)] private float searchInterval = 0.25f;
    [SerializeField] private bool logState = true;

    private Unit unit;
    private UnitLocomotion locomotion;
    private UnitBuilding buildingTarget;
    private IDamageable target;
    private float searchTimer;
    private State state = (State)(-1);
    private bool warnedNoProductivity;

    /// <summary>当前状态名，便于调试与测试断言。</summary>
    public string StateName => state.ToString();

    /// <summary>感知范围：优先 StatBlock，未配置时回退到序列化字段。</summary>
    public float PerceptionRange
    {
        get
        {
            float stat = unit != null ? unit.Stats.Get(StatType.PerceptionRange) : 0f;
            return stat > 0f ? stat : perceptionRange;
        }
    }

    /// <summary>当前建筑目标，便于调试与测试断言。</summary>
    public UnitBuilding BuildingTarget => buildingTarget;

    /// <summary>当前敌方目标，便于调试与测试断言。</summary>
    public IDamageable Target => target;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        locomotion = GetComponent<UnitLocomotion>();

        if (locomotion == null)
        {
            Debug.LogError("CitizenBrain 需要同物体上的 UnitLocomotion（UnitMotor 或 UnitMover）。", this);
        }
    }

    private void OnEnable()
    {
        if (unit != null && unit.Health != null)
        {
            unit.Health.Died += OnSelfDied;
        }
    }

    private void OnDisable()
    {
        if (unit != null && unit.Health != null)
        {
            unit.Health.Died -= OnSelfDied;
        }
    }

    private void Update()
    {
        if (unit == null || unit.Health == null || !unit.Health.IsAlive || locomotion == null)
        {
            return;
        }

        Tick(Time.deltaTime);
    }

    /// <summary>
    /// 单帧决策入口。公开以便 EditMode 测试直接驱动，不必等待真实帧。
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (unit == null || unit.Health == null || !unit.Health.IsAlive || locomotion == null)
        {
            return;
        }

        if (buildingTarget != null && !IsValidBuilding(buildingTarget))
        {
            buildingTarget = null;
        }

        if (buildingTarget != null)
        {
            HandleBuilding(deltaTime);
            return;
        }

        if (target != null && !HasLiveTarget())
        {
            target = null;
        }

        if (target != null)
        {
            HandleEnemy();
            return;
        }

        searchTimer -= deltaTime;
        if (searchTimer <= 0f)
        {
            searchTimer = searchInterval;
            Search();
        }

        if (buildingTarget != null)
        {
            HandleBuilding(deltaTime);
            return;
        }

        if (target != null)
        {
            HandleEnemy();
            return;
        }

        Idle();
    }

    private void Search()
    {
        var building = UnitBuildingRegistry.FindNearestFriendly(
            transform.position, unit.Team, PerceptionRange, true);

        if (building != null)
        {
            buildingTarget = building;
            target = null;
            Log(string.Format("[Citizen] {0} 发现建筑 {1}（{2}）",
                name, building.name, building.IsRuined ? "废墟" : "已建成"));
            return;
        }

        var enemy = UnitRegistry.FindNearestHostile(transform.position, unit.Team, PerceptionRange);
        if (enemy != null)
        {
            target = enemy;
            Log(string.Format("[Citizen] {0} 发现敌人 {1}", name, enemy.name));
        }
    }

    private void HandleBuilding(float deltaTime)
    {
        if (buildingTarget == null)
        {
            return;
        }

        float distance = buildingTarget.DistanceTo(transform.position);

        if (buildingTarget.IsRuined)
        {
            if (distance > buildingTarget.InteractRange)
            {
                MoveToBuilding(State.MoveToRepair);
                return;
            }

            Repair(deltaTime);
            return;
        }

        if (distance > buildingTarget.InteractRange)
        {
            MoveToBuilding(State.MoveToGarrison);
            return;
        }

        if (buildingTarget.Garrison(unit))
        {
            // 进驻后自身被销毁（EditMode 下被停用），不再需要行为。
            enabled = false;
            return;
        }

        buildingTarget = null;
        Idle();
    }

    private void MoveToBuilding(State moveState)
    {
        SetState(moveState);
        locomotion.SetPaused(false);
        locomotion.MoveTo(buildingTarget.transform.position);
    }

    private void Repair(float deltaTime)
    {
        if (state != State.Repair)
        {
            SetState(State.Repair);
            locomotion.Halt();
        }
        else
        {
            locomotion.SetPaused(true);
        }

        if (!buildingTarget.CanBeWorkedBy(unit))
        {
            buildingTarget = null;
            Idle();
            return;
        }

        float productivity = unit.Stats.Get(StatType.Productivity);
        if (productivity <= 0f)
        {
            if (!warnedNoProductivity)
            {
                warnedNoProductivity = true;
                Debug.LogWarning("[Citizen] 生产力为 0，无法推进建造。", this);
            }

            return;
        }

        buildingTarget.AddBuildProgress(productivity * deltaTime);
    }

    private void HandleEnemy()
    {
        float range = unit.GetAttackRange();
        if (range > 0f && target.DistanceTo(transform.position) <= range)
        {
            if (state != State.Attack)
            {
                SetState(State.Attack);
                locomotion.SetPaused(true);
            }

            unit.ActivateAbilities(target);
            return;
        }

        SetState(State.Chase);
        locomotion.SetPaused(false);
        locomotion.MoveTo(target.DamageTransform.position);
    }

    private void Idle()
    {
        if (state != State.Idle)
        {
            SetState(State.Idle);
            locomotion.Halt();
        }
        else
        {
            locomotion.SetPaused(true);
        }
    }

    private static bool IsValidBuilding(UnitBuilding building)
    {
        return building != null && (building.IsRuined || building.IsOperational);
    }

    private bool HasLiveTarget()
    {
        if (target == null)
        {
            return false;
        }

        var unityObject = target as Object;
        if (unityObject == null)
        {
            return false;
        }

        return target.IsAlive;
    }

    private void SetState(State next)
    {
        if (state == next)
        {
            return;
        }

        state = next;
        Log(string.Format("[Citizen] {0} -> {1}", name, state));
    }

    private void OnSelfDied(UnitHealth health)
    {
        enabled = false;
        buildingTarget = null;
        target = null;

        if (locomotion != null)
        {
            locomotion.Halt();
        }
    }

    private void Log(string message)
    {
        if (logState)
        {
            Debug.Log(message, this);
        }
    }
}
