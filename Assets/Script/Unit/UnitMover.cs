using UnityEngine;

/// <summary>
/// 沿 <see cref="UnitRoute"/> 做贴地移动：路点只提供水平走向，高度完全由脚下的球体投射决定。
/// 每帧在候选位置向下投射，只接受坡度不超过 <see cref="maxSlopeAngle"/> 的表面，
/// 可跨越不高于 <see cref="stepHeight"/> 的台阶，并向下吸附不超过 <see cref="snapDistance"/> 的落差；
/// 找不到可站立表面（陡坡、悬空）时原地不动。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Unit))]
public class UnitMover : MonoBehaviour
{
    [SerializeField] private UnitRoute route;
    [SerializeField, Min(0.05f)] private float arriveThreshold = 0.25f;
    [SerializeField] private LayerMask groundMask = ~(1 << 2);
    [SerializeField, Min(0.05f)] private float sphereRadius = 0.35f;
    [SerializeField, Min(0f)] private float groundOffset = 0f;
    [SerializeField, Range(0f, 89f)] private float maxSlopeAngle = 45f;
    [SerializeField, Min(0f)] private float stepHeight = 0.3f;
    [SerializeField, Min(0f)] private float snapDistance = 0.5f;
    [SerializeField, Min(0f)] private float turnSpeed = 540f;

    private const int HitBufferSize = 16;
    private const float ProbeHeight = 2f;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[HitBufferSize];

    private Unit unit;
    private int waypointIndex;
    private bool paused;
    private bool finished;
    private bool warned;

    /// <summary>当前路线。</summary>
    public UnitRoute Route
    {
        get => route;
        set => route = value;
    }

    /// <summary>是否已走完全部路点。</summary>
    public bool Finished => finished;

    /// <summary>暂停/恢复移动。</summary>
    public void SetPaused(bool value)
    {
        paused = value;
    }

    /// <summary>回到路线起点重新开始。</summary>
    public void ResetRoute()
    {
        waypointIndex = 0;
        finished = false;
        paused = false;
    }

    private void Awake()
    {
        unit = GetComponent<Unit>();
    }

    private void Update()
    {
        if (route == null || route.Count == 0)
        {
            WarnOnce();
            return;
        }

        if (paused || finished)
        {
            return;
        }

        if (waypointIndex >= route.Count)
        {
            finished = true;
            return;
        }

        Vector3 feet = transform.position - Vector3.up * groundOffset;
        if (TryProbeGround(feet, out var ground))
        {
            feet.y = ground.point.y;
        }

        Vector3 target = route.GetPoint(waypointIndex);
        Vector3 toTarget = target - feet;
        toTarget.y = 0f;

        float horizontalDistance = toTarget.magnitude;
        if (horizontalDistance <= arriveThreshold)
        {
            waypointIndex++;
            ApplyFeetPosition(feet);
            return;
        }

        float speed = unit != null ? unit.Stats.Get(StatType.MoveSpeed) : 0f;
        if (speed <= 0f)
        {
            ApplyFeetPosition(feet);
            return;
        }

        Vector3 direction = toTarget / Mathf.Max(horizontalDistance, 0.0001f);
        float step = Mathf.Min(speed * Time.deltaTime, horizontalDistance);
        Vector3 candidate = feet + direction * step;

        if (TryProbeGround(candidate, out var candidateGround))
        {
            feet = new Vector3(candidate.x, candidateGround.point.y, candidate.z);
            ApplyFeetPosition(feet);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                turnSpeed * Time.deltaTime);
            return;
        }

        ApplyFeetPosition(feet);
    }

    private void ApplyFeetPosition(Vector3 feet)
    {
        transform.position = feet + Vector3.up * groundOffset;
    }

    /// <summary>
    /// 从脚底上方 <see cref="ProbeHeight"/> 处向下做球体投射，返回可站立且落在
    /// [脚底 - <see cref="snapDistance"/>, 脚底 + <see cref="stepHeight"/>] 区间内的最高表面。
    /// 起点抬高可避免球体与斜坡重叠，否则 SphereCast 会直接忽略该碰撞体；
    /// 坡面超过 <see cref="maxSlopeAngle"/> 的命中会被忽略。
    /// </summary>
    private bool TryProbeGround(Vector3 feet, out RaycastHit ground)
    {
        float radius = Mathf.Max(0.05f, sphereRadius);
        Vector3 origin = feet + Vector3.up * ProbeHeight;
        float distance = ProbeHeight + snapDistance;

        int count = Physics.SphereCastNonAlloc(
            origin, radius, Vector3.down, hitBuffer, distance, groundMask, QueryTriggerInteraction.Ignore);

        float minY = feet.y - snapDistance;
        float maxY = feet.y + stepHeight;

        bool found = false;
        ground = default;
        for (int i = 0; i < count; i++)
        {
            var hit = hitBuffer[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (Vector3.Angle(hit.normal, Vector3.up) > maxSlopeAngle)
            {
                continue;
            }

            float y = hit.point.y;
            if (y < minY || y > maxY)
            {
                continue;
            }

            if (!found || y > ground.point.y)
            {
                ground = hit;
                found = true;
            }
        }

        return found;
    }

    private void WarnOnce()
    {
        if (!warned)
        {
            warned = true;
            Debug.LogWarning("UnitMover 未配置 UnitRoute，单位不会移动。", this);
        }
    }
}
