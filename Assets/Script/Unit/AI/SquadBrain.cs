using UnityEngine;

/// <summary>
/// 小队群体行为树：
/// 攻击命令 → 移动命令 → 自动接敌 → 待命。
/// 只负责发布/撤销命令，具体移动与攻击仍由成员 Brain 执行。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Squad))]
public class SquadBrain : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float scanInterval = 0.25f;
    [SerializeField] private bool autoEngage = true;
    [SerializeField, Min(0f)] private float fallbackEngageRange = 6f;
    [SerializeField] private bool logState = false;

    private Squad squad;
    private BehaviorNode root;
    private float scanTimer;
    private UnitHealth pendingEnemy;
    private BehaviorStatus lastStatus = BehaviorStatus.Failure;
    private SquadState lastState = SquadState.Idle;

    /// <summary>上一次 Tick 的行为树结果，便于测试。</summary>
    public BehaviorStatus LastStatus => lastStatus;

    /// <summary>当前状态名（Idle / Moving / Engaging），便于测试与面板。</summary>
    public string StateName => squad != null ? squad.State.ToString() : SquadState.Idle.ToString();

    /// <summary>本帧扫描到的候选敌人。</summary>
    public UnitHealth PendingEnemy => pendingEnemy != null && pendingEnemy.IsAlive ? pendingEnemy : null;

    private void Awake()
    {
        squad = GetComponent<Squad>();
        BuildTree();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>单帧驱动入口。公开以便 EditMode 直接调用，不必等待真实帧。</summary>
    public void Tick(float deltaTime)
    {
        if (squad == null)
        {
            return;
        }

        squad.Tick();
        squad.Advance(deltaTime);
        EnforceGarrisonLeash();

        scanTimer -= deltaTime;
        if (scanTimer <= 0f)
        {
            scanTimer = scanInterval;
            pendingEnemy = autoEngage ? FindEnemy() : null;
        }
        else if (pendingEnemy != null && !pendingEnemy.IsAlive)
        {
            pendingEnemy = null;
        }

        lastStatus = root.Tick();

        // 全员阵亡后回收小队对象，避免面板里留下“小队 N｜0 人”的空壳。
        // 放在行为树之后、Tick 之外执行：编辑模式下 Disband 会立刻销毁本组件，
        // 此时除了 return 不再访问任何成员。
        if (squad.MemberUnits().Count == 0)
        {
            squad.Disband();
            return;
        }

        if (logState && lastState != squad.State)
        {
            lastState = squad.State;
            Debug.Log(string.Format("[Squad] {0} -> {1}", squad.SquadName, lastState), this);
        }
    }

    private void BuildTree()
    {
        root = new BehaviorSelector(
            // 1. 已有集火目标：维持 Engaging，成员各自追击/攻击。
            new BehaviorSequence(
                new BehaviorCondition(() => squad.AttackTarget != null),
                new BehaviorAction(() =>
                {
                    squad.SetState(SquadState.Engaging);
                    return BehaviorStatus.Success;
                })),

            // 2. 移动命令：由 Squad.Advance 在“锚点到达 + 成员落位”后统一完成，
            //    行为树这里只维持 Moving 状态，避免锚点还在走时提前结束。
            new BehaviorSequence(
                new BehaviorCondition(() => squad.MoveOrderActive),
                new BehaviorAction(() =>
                {
                    squad.SetState(SquadState.Moving);
                    return BehaviorStatus.Success;
                })),

            // 3. 无命令时自动接敌：对最近敌人下达集火命令。
            //    手动接管期间跳过，否则会把玩家刚下的个体命令改写成全队集火。
            new BehaviorSequence(
                new BehaviorCondition(() => !squad.ManualControl && PendingEnemy != null),
                new BehaviorAction(() =>
                {
                    var target = PendingEnemy;
                    if (target == null)
                    {
                        return BehaviorStatus.Failure;
                    }

                    squad.Attack(target);
                    pendingEnemy = null;
                    return BehaviorStatus.Success;
                })),

            // 4. 待命：清理过期命令，保持队形。
            new BehaviorAction(() =>
            {
                squad.SetState(SquadState.Idle);
                return BehaviorStatus.Success;
            }));
    }

    private UnitHealth FindEnemy()
    {
        var units = squad.MemberUnits();
        if (units.Count == 0 || squad.Team == UnitTeam.Neutral)
        {
            return null;
        }

        float range = 0f;
        for (int i = 0; i < units.Count; i++)
        {
            var source = units[i].GetComponent<IPerceptionSource>();
            if (source != null && source.PerceptionRange > range)
            {
                range = source.PerceptionRange;
            }
        }

        if (range <= 0f)
        {
            range = fallbackEngageRange;
        }

        var enemy = UnitRegistry.FindNearestHostile(squad.Centroid(), squad.Team, range);

        // 驻守姿态只接集结点 5m 内的敌人；超出后由 EnforceGarrisonLeash 放弃追击并回位。
        if (enemy != null &&
            squad.Posture == SquadPosture.Garrison &&
            squad.HasRallyPoint &&
            enemy.DistanceTo(squad.RallyPoint) > squad.GarrisonLeashRange)
        {
            return null;
        }

        return enemy;
    }

    /// <summary>驻守：集火目标离开集结点 5m 后立即结束攻击，成员回驻守槽位。</summary>
    private void EnforceGarrisonLeash()
    {
        if (squad.Posture != SquadPosture.Garrison || !squad.HasRallyPoint)
        {
            return;
        }

        var target = squad.AttackTarget;
        if (target == null)
        {
            return;
        }

        if (target.DistanceTo(squad.RallyPoint) <= squad.GarrisonLeashRange)
        {
            return;
        }

        squad.CompleteOrders();
        pendingEnemy = null;
    }
}
