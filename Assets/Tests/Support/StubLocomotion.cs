using UnityEngine;

/// <summary>
/// 测试用移动后端：只记录调用，不产生实际位移。
/// </summary>
public class StubLocomotion : UnitLocomotion
{
    /// <summary>MoveTo 调用次数。</summary>
    public int MoveToCalls { get; private set; }

    /// <summary>Stop 调用次数。</summary>
    public int StopCalls { get; private set; }

    /// <summary>当前是否暂停。</summary>
    public bool Paused { get; private set; }

    /// <summary>最后一次移动目标。</summary>
    public Vector3 LastDestination { get; private set; }

    public override void SetPaused(bool paused)
    {
        Paused = paused;
    }

    public override void MoveTo(Vector3 worldPosition)
    {
        MoveToCalls++;
        LastDestination = worldPosition;
        Paused = false;
    }

    public override void Stop()
    {
        StopCalls++;
    }
}
