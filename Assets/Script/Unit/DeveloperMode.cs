/// <summary>
/// 开发者模式全局开关。开时显示所有单位的感知范围球体，关时全部隐藏。
/// </summary>
public static class DeveloperMode
{
    /// <summary>是否开启。</summary>
    public static bool Enabled { get; private set; }

    /// <summary>开关变化事件。</summary>
    public static event System.Action<bool> Changed;

    /// <summary>设置开关；值未变化时不触发事件。</summary>
    public static void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        Enabled = enabled;
        Changed?.Invoke(enabled);
    }

    /// <summary>反转开关。</summary>
    public static void Toggle()
    {
        SetEnabled(!Enabled);
    }
}
