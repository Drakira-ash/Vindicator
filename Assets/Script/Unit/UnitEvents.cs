using System;

/// <summary>
/// 拦截请求：友军（战士）请求把某个敌人转为自己作为攻击目标。
/// </summary>
public readonly struct InterceptRequest
{
    /// <summary>发起拦截的友军单位。</summary>
    public readonly Unit Interceptor;

    /// <summary>被请求拦截的敌方目标。</summary>
    public readonly UnitHealth Target;

    public InterceptRequest(Unit interceptor, UnitHealth target)
    {
        Interceptor = interceptor;
        Target = target;
    }
}

/// <summary>
/// 全局战斗事件。订阅方必须在 OnEnable/OnDisable 成对注册与注销，避免残留引用。
/// </summary>
public static class UnitEvents
{
    /// <summary>友军尝试拦截敌人时触发。</summary>
    public static event Action<InterceptRequest> InterceptRequested;

    /// <summary>友军放弃拦截时触发（目标超出拴绳等），避免被拦截方永久待命。</summary>
    public static event Action<InterceptRequest> InterceptReleased;

    public static void RaiseInterceptRequested(InterceptRequest request)
    {
        InterceptRequested?.Invoke(request);
    }

    public static void RaiseInterceptReleased(InterceptRequest request)
    {
        InterceptReleased?.Invoke(request);
    }
}
