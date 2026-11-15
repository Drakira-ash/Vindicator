using UnityEngine;

/// <summary>
/// 测试用感知来源：返回固定范围，用于开发者模式球体生成测试。
/// </summary>
public class PerceptionSourceStub : MonoBehaviour, IPerceptionSource
{
    /// <summary>返回的感知范围，由测试用例设置。</summary>
    public float Range = 1f;

    public float PerceptionRange
    {
        get { return Range; }
    }
}
