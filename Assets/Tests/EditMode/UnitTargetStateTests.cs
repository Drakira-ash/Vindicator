using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 目标指针数据表测试：追逐/锁定互斥、销毁对象自动视为空、Ensure 自动补组件。
/// </summary>
public class UnitTargetStateTests
{
    private readonly List<GameObject> created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < created.Count; i++)
        {
            if (created[i] != null)
            {
                Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();
    }

    [Test]
    public void Ensure_AddsComponentOnlyOnce()
    {
        var host = Create("Host");
        var first = UnitTargetState.Ensure(host);
        var second = UnitTargetState.Ensure(host);

        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
        Assert.AreEqual(1, host.GetComponents<UnitTargetState>().Length);
    }

    [Test]
    public void SetChase_ThenSetLock_KeepsPointersIndependent()
    {
        var host = Create("Host");
        var chase = Create("Chase").transform;
        var locked = Create("Lock").transform;
        var state = UnitTargetState.Ensure(host);

        state.SetChase(chase);
        Assert.AreSame(chase, state.ChaseTarget);
        Assert.IsNull(state.LockTarget);

        state.SetLock(locked);
        state.ClearChase();
        Assert.AreSame(locked, state.LockTarget);
        Assert.IsNull(state.ChaseTarget);
    }

    [Test]
    public void ClearAll_ClearsBothPointers()
    {
        var host = Create("Host");
        var state = UnitTargetState.Ensure(host);
        state.SetChase(Create("Chase").transform);
        state.SetLock(Create("Lock").transform);

        state.ClearAll();

        Assert.IsNull(state.ChaseTarget);
        Assert.IsNull(state.LockTarget);
    }

    [Test]
    public void DestroyedTarget_ReadsAsNull()
    {
        var host = Create("Host");
        var target = Create("Target");
        var state = UnitTargetState.Ensure(host);
        state.SetChase(target.transform);

        Object.DestroyImmediate(target);

        Assert.IsNull(state.ChaseTarget, "已销毁的目标应视为空指针");
        Assert.IsNull(state.LockTarget);
    }

    private GameObject Create(string name)
    {
        var go = new GameObject(name);
        created.Add(go);
        return go;
    }
}
