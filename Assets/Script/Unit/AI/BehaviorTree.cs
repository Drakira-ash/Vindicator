using System;

/// <summary>
/// 行为树节点返回值。Running 预留给需要跨帧的节点，当前小队行为树只使用 Success/Failure。
/// </summary>
public enum BehaviorStatus
{
    Running,
    Success,
    Failure
}

/// <summary>
/// 最小行为树节点基类。节点无状态，每次 Tick 重新求值，便于 EditMode 直接驱动。
/// </summary>
public abstract class BehaviorNode
{
    public abstract BehaviorStatus Tick();
}

/// <summary>顺序节点：所有子节点成功才成功，遇到失败立即失败。</summary>
public sealed class BehaviorSequence : BehaviorNode
{
    private readonly BehaviorNode[] children;

    public BehaviorSequence(params BehaviorNode[] children)
    {
        this.children = children ?? Array.Empty<BehaviorNode>();
    }

    public override BehaviorStatus Tick()
    {
        for (int i = 0; i < children.Length; i++)
        {
            var status = children[i].Tick();
            if (status != BehaviorStatus.Success)
            {
                return status;
            }
        }

        return BehaviorStatus.Success;
    }
}

/// <summary>选择节点：返回第一个非失败子节点的结果。</summary>
public sealed class BehaviorSelector : BehaviorNode
{
    private readonly BehaviorNode[] children;

    public BehaviorSelector(params BehaviorNode[] children)
    {
        this.children = children ?? Array.Empty<BehaviorNode>();
    }

    public override BehaviorStatus Tick()
    {
        for (int i = 0; i < children.Length; i++)
        {
            var status = children[i].Tick();
            if (status != BehaviorStatus.Failure)
            {
                return status;
            }
        }

        return BehaviorStatus.Failure;
    }
}

/// <summary>条件节点：谓词为真返回 Success，否则 Failure。</summary>
public sealed class BehaviorCondition : BehaviorNode
{
    private readonly Func<bool> predicate;

    public BehaviorCondition(Func<bool> predicate)
    {
        this.predicate = predicate;
    }

    public override BehaviorStatus Tick()
    {
        return predicate != null && predicate() ? BehaviorStatus.Success : BehaviorStatus.Failure;
    }
}

/// <summary>动作节点：直接执行委托并返回其结果。</summary>
public sealed class BehaviorAction : BehaviorNode
{
    private readonly Func<BehaviorStatus> action;

    public BehaviorAction(Func<BehaviorStatus> action)
    {
        this.action = action;
    }

    public override BehaviorStatus Tick()
    {
        return action != null ? action() : BehaviorStatus.Failure;
    }
}
