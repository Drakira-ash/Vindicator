using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 基于 NavMeshAgent 的移动后端。速度每帧从 <see cref="StatBlock"/> 读取，
/// 攻击时通过 <c>isStopped</c> 立即停下，恢复后继续寻路。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Unit), typeof(NavMeshAgent))]
public class UnitMotor : UnitLocomotion
{
    private const float RepathThreshold = 0.2f;

    private Unit unit;
    private NavMeshAgent agent;

    /// <summary>底层 NavMeshAgent，便于调试与编辑器工具配置。</summary>
    public NavMeshAgent Agent => agent;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        agent = GetComponent<NavMeshAgent>();
        agent.updatePosition = true;
        agent.updateRotation = true;
        agent.autoBraking = true;
    }

    private void Start()
    {
        if (agent == null || agent.isOnNavMesh)
        {
            return;
        }

        NavMeshHit hit;
        if (NavMesh.SamplePosition(transform.position, out hit, 3f, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
        }
        else
        {
            Debug.LogWarning("UnitMotor 未落在 NavMesh 上，单位将无法移动。", this);
        }
    }

    private void Update()
    {
        if (unit == null || agent == null)
        {
            return;
        }

        float speed = unit.Stats.Get(StatType.MoveSpeed);
        if (speed > 0f)
        {
            agent.speed = speed;
        }
    }

    public override void SetPaused(bool paused)
    {
        if (agent == null)
        {
            return;
        }

        agent.isStopped = paused;
    }

    public override void MoveTo(Vector3 worldPosition)
    {
        if (agent == null)
        {
            return;
        }

        // FaceDirection 会临时关闭自动旋转，恢复移动时必须打开。
        agent.updateRotation = true;

        if (agent.isStopped)
        {
            agent.isStopped = false;
        }

        // 同一目标反复设置会重置路径，节流到明显位移才重新寻路。
        if (agent.hasPath && (agent.destination - worldPosition).sqrMagnitude < RepathThreshold * RepathThreshold)
        {
            return;
        }

        agent.SetDestination(worldPosition);
    }

    public override void Stop()
    {
        if (agent == null)
        {
            return;
        }

        agent.isStopped = true;
        agent.ResetPath();
    }

    /// <summary>停下并临时关闭 NavMeshAgent 自动旋转，再转向指定方向。</summary>
    public override void FaceDirection(Vector3 worldDirection)
    {
        if (agent == null)
        {
            base.FaceDirection(worldDirection);
            return;
        }

        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        agent.isStopped = true;
        agent.updateRotation = false;
        transform.rotation = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
    }
}
