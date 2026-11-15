using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行期小队注册表。小队对象在 PlayMode 中创建，不写入场景；
/// 关闭 PlayMode 后自然消失，测试中通过 <see cref="Clear"/> 重置。
/// </summary>
public static class SquadRegistry
{
    private static readonly List<Squad> squads = new List<Squad>();
    private static int nextId = 1;

    /// <summary>小队增删时触发，面板据此刷新。</summary>
    public static event Action Changed;

    /// <summary>当前所有小队（可能包含已销毁对象，读取方需判空）。</summary>
    public static IReadOnlyList<Squad> All => squads;

    public static void Register(Squad squad)
    {
        if (squad != null && !squads.Contains(squad))
        {
            squads.Add(squad);
            Changed?.Invoke();
        }
    }

    public static void Unregister(Squad squad)
    {
        if (squad != null && squads.Remove(squad))
        {
            Changed?.Invoke();
        }
    }

    public static Squad Find(int id)
    {
        for (int i = 0; i < squads.Count; i++)
        {
            if (squads[i] != null && squads[i].SquadId == id)
            {
                return squads[i];
            }
        }

        return null;
    }

    /// <summary>用一组单位创建新小队，编号自动递增。</summary>
    public static Squad Create(IEnumerable<Unit> units, string name = null)
    {
        return Create(units, NextId(), name);
    }

    /// <summary>用指定编号创建小队；同编号的旧小队会先解散。</summary>
    public static Squad Create(IEnumerable<Unit> units, int preferredId, string name = null)
    {
        Prune();

        var existing = Find(preferredId);
        if (existing != null)
        {
            existing.Disband();
        }

        var go = new GameObject("Squad_" + preferredId);
        var squad = go.AddComponent<Squad>();
        squad.Initialize(preferredId, name ?? ("小队 " + preferredId));
        go.AddComponent<SquadBrain>();
        Register(squad);
        squad.AddMembers(units);

        if (preferredId >= nextId)
        {
            nextId = preferredId + 1;
        }

        Changed?.Invoke();
        return squad;
    }

    /// <summary>下一个可用编号。</summary>
    public static int NextId()
    {
        Prune();

        int max = 0;
        for (int i = 0; i < squads.Count; i++)
        {
            if (squads[i] != null && squads[i].SquadId > max)
            {
                max = squads[i].SquadId;
            }
        }

        return Mathf.Max(nextId, max + 1);
    }

    /// <summary>清空所有小队（测试收尾与场景重置）。</summary>
    public static void Clear()
    {
        var copy = new List<Squad>(squads);
        squads.Clear();
        nextId = 1;

        for (int i = 0; i < copy.Count; i++)
        {
            if (copy[i] != null)
            {
                copy[i].Disband();
            }
        }

        Changed?.Invoke();
    }

    private static void Prune()
    {
        squads.RemoveAll(squad => squad == null);
    }
}
