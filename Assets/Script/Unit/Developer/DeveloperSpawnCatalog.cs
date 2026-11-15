using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 开发者模式可生成单位的目录：按阵营列出显示名与预制体，供生成面板读取。
/// 资产放在 <c>Resources/Developer/SpawnCatalog.asset</c>，运行时自动加载，
/// 不参与任何玩法逻辑；缺失时面板会提示目录未配置。
/// </summary>
[CreateAssetMenu(fileName = "SpawnCatalog", menuName = "Vindicator/Developer Spawn Catalog")]
public class DeveloperSpawnCatalog : ScriptableObject
{
    /// <summary>Resources 下的资产名（不带扩展名）。</summary>
    public const string ResourcePath = "Developer/SpawnCatalog";

    /// <summary>民兵条目的固定 id（`S` 热键使用）。</summary>
    public const string MilitiaId = "militia";

    /// <summary>普通敌人/劫匪条目的固定 id（`D` 热键使用）。</summary>
    public const string EnemyId = "enemy";

    /// <summary>一条可生成单位：显示名、阵营和预制体。</summary>
    [Serializable]
    public class Entry
    {
        [SerializeField] private string id = "unit";
        [SerializeField] private string displayName = "单位";
        [SerializeField] private UnitTeam team = UnitTeam.Player;
        [SerializeField] private GameObject prefab;

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public UnitTeam Team => team;
        public GameObject Prefab => prefab;

        /// <summary>预制体已配置时才可生成。</summary>
        public bool IsValid => prefab != null;

        /// <summary>配置条目（资产创建与测试用）。</summary>
        public void Configure(string newId, string newDisplayName, UnitTeam newTeam, GameObject newPrefab)
        {
            id = newId;
            displayName = newDisplayName;
            team = newTeam;
            prefab = newPrefab;
        }
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    /// <summary>全部条目（只读）。</summary>
    public IReadOnlyList<Entry> Entries => entries;

    /// <summary>按 id 查找；没有时返回 null。</summary>
    public Entry Find(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] != null && entries[i].Id == id)
            {
                return entries[i];
            }
        }

        return null;
    }

    /// <summary>把某一阵营的条目追加到 results。</summary>
    public void CollectEntries(UnitTeam team, List<Entry> results)
    {
        if (results == null)
        {
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] != null && entries[i].Team == team)
            {
                results.Add(entries[i]);
            }
        }
    }

    /// <summary>某一阵营的第一个有效条目；没有时返回 null。</summary>
    public Entry FirstOfTeam(UnitTeam team)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] != null && entries[i].Team == team)
            {
                return entries[i];
            }
        }

        return null;
    }

    /// <summary>添加条目（资产创建与测试用）。</summary>
    public Entry AddEntry(string id, string displayName, UnitTeam team, GameObject prefab)
    {
        var entry = new Entry();
        entry.Configure(id, displayName, team, prefab);
        entries.Add(entry);
        return entry;
    }

    /// <summary>从 Resources 加载目录；没有资产时返回 null。</summary>
    public static DeveloperSpawnCatalog Load()
    {
        return Resources.Load<DeveloperSpawnCatalog>(ResourcePath);
    }
}
