/// <summary>
/// 单位检测范围来源。Brain 与炮台各自实现，开发者模式的球体可视化统一读取该接口。
/// </summary>
public interface IPerceptionSource
{
    /// <summary>当前检测范围（米）；小于等于 0 表示当前不显示。</summary>
    float PerceptionRange { get; }
}
