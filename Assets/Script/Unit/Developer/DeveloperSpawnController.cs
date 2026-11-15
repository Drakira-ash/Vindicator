using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// <summary>
/// 开发者模式的单位生成/销毁：
/// <c>K</c> 销毁当前选择，<c>S</c> 在鼠标位置生成民兵，<c>D</c> 在鼠标位置生成普通敌人；
/// 面板通过 <see cref="ArmEntry"/> / <see cref="TryPlaceAt"/> 在任意位置放置单位。
/// 生成点优先吸附 NavMesh，失败时使用鼠标射线命中点。
/// </summary>
[DisallowMultipleComponent]
public class DeveloperSpawnController : MonoBehaviour
{
    [SerializeField] private KeyCode destroyKey = KeyCode.K;
    [SerializeField] private KeyCode spawnMilitiaKey = KeyCode.S;
    [SerializeField] private KeyCode spawnEnemyKey = KeyCode.D;
    [SerializeField, Min(1f)] private float rayDistance = 500f;
    [SerializeField, Min(0f)] private float navMeshSnapRadius = 2f;
    [SerializeField] private bool logActions = true;
    [SerializeField] private Color playerPreviewColor = new Color(0.2f, 0.85f, 0.4f);
    [SerializeField] private Color enemyPreviewColor = new Color(1f, 0.3f, 0.25f);

    private static DeveloperSpawnController instance;

    private DeveloperSpawnCatalog catalog;
    private GameObject ghost;
    private DeveloperSpawnCatalog.Entry ghostEntry;
    private int spawnCounter;
    private bool warnedMissingCatalog;
    private Transform cachedEnemyPatrolGoal;

    /// <summary>当前控制器（运行期唯一）。</summary>
    public static DeveloperSpawnController Instance => instance;

    /// <summary>生成目录；未显式设置时从 Resources 加载。</summary>
    public DeveloperSpawnCatalog Catalog
    {
        get
        {
            if (catalog == null)
            {
                catalog = DeveloperSpawnCatalog.Load();
            }

            return catalog;
        }
        set { catalog = value; }
    }

    /// <summary>是否处于“选中类型、等待左键放置”模式。</summary>
    public bool IsPlacing { get; private set; }

    /// <summary>放置模式下待生成的条目。</summary>
    public DeveloperSpawnCatalog.Entry ArmedEntry { get; private set; }

    /// <summary>鼠标当前指向的放置点（已吸附 NavMesh）。</summary>
    public Vector3 PreviewPoint { get; private set; }

    /// <summary>本帧是否有可用的放置点。</summary>
    public bool HasPreviewPoint { get; private set; }

    /// <summary>本次会话生成过的单位数量。</summary>
    public int SpawnedCount { get; private set; }

    /// <summary>最近一次生成的单位。</summary>
    public GameObject LastSpawned { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<DeveloperSpawnController>() != null)
        {
            return;
        }

        var go = new GameObject("DeveloperSpawn");
        go.AddComponent<DeveloperSpawnController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            DestroyNow(gameObject);
            return;
        }

        instance = this;

        if (Application.isPlaying)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (instance != this)
        {
            return;
        }

        instance = null;
        DestroyGhost();
    }

    private void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!DeveloperMode.Enabled)
        {
            if (IsPlacing)
            {
                CancelPlacement();
            }

            return;
        }

        HandleHotkeys();
        HandlePlacementInput();
#endif
    }

    /// <summary>
    /// 世界输入是否应被开发者工具吃掉：放置模式下全部吃掉；
    /// 鼠标位于生成面板上时也吃掉，避免点按钮的同时在世界里框选/下命令。
    /// </summary>
    public static bool BlocksWorldInput(Vector2 screenPosition)
    {
        if (!DeveloperMode.Enabled)
        {
            return false;
        }

        var controller = instance;
        if (controller != null && controller.IsPlacing)
        {
            return true;
        }

        return DeveloperSpawnPanel.IsPointerOverPanel(screenPosition);
    }

    /// <summary>选中一个条目进入放置模式；重复选中同一条目会取消。</summary>
    public bool ArmEntry(DeveloperSpawnCatalog.Entry entry)
    {
        if (entry == null || entry.Prefab == null)
        {
            Log("[Dev] 条目没有配置预制体，无法放置");
            return false;
        }

        bool sameEntry = IsPlacing && ArmedEntry == entry;
        CancelPlacement();

        if (sameEntry)
        {
            Log("[Dev] 已取消放置：" + entry.DisplayName);
            return false;
        }

        ArmedEntry = entry;
        IsPlacing = true;
        Log("[Dev] 待放置：" + entry.DisplayName + "（左键放置 / 右键取消）");
        return true;
    }

    /// <summary>退出放置模式并销毁预览。</summary>
    public void CancelPlacement()
    {
        IsPlacing = false;
        ArmedEntry = null;
        HasPreviewPoint = false;
        DestroyGhost();
    }

    /// <summary>在指定世界坐标生成当前待放置的单位。</summary>
    public bool TryPlaceAt(Vector3 worldPosition)
    {
        if (!IsPlacing || ArmedEntry == null)
        {
            return false;
        }

        var entry = ArmedEntry;
        CancelPlacement();
        return SpawnEntry(entry, worldPosition) != null;
    }

    /// <summary>按条目 id 在鼠标位置立即生成（`S` / `D` 热键）。</summary>
    public bool SpawnAtMouse(string entryId)
    {
        var entry = ResolveEntry(entryId);
        if (entry == null)
        {
            return false;
        }

        if (!TryGetMouseGroundPoint(out var point))
        {
            Log("[Dev] 鼠标下没有可放置的地面");
            return false;
        }

        return SpawnEntry(entry, point) != null;
    }

    /// <summary>立即在指定位置生成条目对应的单位。</summary>
    public GameObject SpawnEntry(DeveloperSpawnCatalog.Entry entry, Vector3 worldPosition)
    {
        if (entry == null || entry.Prefab == null)
        {
            Log("[Dev] 生成失败：条目或预制体为空");
            return null;
        }

        Vector3 spawnPoint = ResolveSpawnPoint(worldPosition);
        var spawn = Instantiate(entry.Prefab, spawnPoint, Quaternion.identity);
        spawn.name = entry.Prefab.name + "_Dev_" + (++spawnCounter);

        // 预制体的序列化阵营可能和目录不一致（例如民兵预制体存的是 Enemy），
        // 这里按目录条目强制对齐，保证 S 生成的民兵站在玩家方。
        var health = spawn.GetComponent<UnitHealth>();
        if (health != null && health.Team != entry.Team)
        {
            health.Initialize(health.Max, entry.Team);
        }

        // 敌人预制体不序列化巡逻终点，不补这一项新敌人会一直原地待命；
        // 复用场景中原有敌人的巡逻配置，让它和原敌人一样朝目标前进。
        var brain = spawn.GetComponent<EnemyBrain>();
        if (brain != null)
        {
            var goal = ResolveEnemyPatrolGoal();
            if (goal != null)
            {
                brain.SetPatrolGoal(goal);
            }
        }

        SpawnedCount++;
        LastSpawned = spawn;
        Log(string.Format("[Dev] 生成 {0}（{1}）于 {2}", entry.DisplayName, entry.Team, spawnPoint));
        return spawn;
    }

    /// <summary>销毁当前选择中的全部存活单位；返回实际销毁数量。</summary>
    public int DestroySelectedUnits()
    {
        var selected = UnitSelection.All;
        if (selected.Count == 0)
        {
            Log("[Dev] 没有选中单位，按 K 无效");
            return 0;
        }

        var targets = new List<UnitHealth>(selected.Count);
        for (int i = 0; i < selected.Count; i++)
        {
            var unit = selected[i];
            if (unit != null && unit.Health != null && unit.Health.IsAlive)
            {
                targets.Add(unit.Health);
            }
        }

        int count = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            if (DestroyUnit(targets[i]))
            {
                count++;
            }
        }

        UnitSelection.Clear();
        Log(string.Format("[Dev] 销毁选中单位 {0} 个", count));
        return count;
    }

    /// <summary>销毁任意单位（面板逐行销毁也走这里）。</summary>
    public bool DestroyUnit(UnitHealth health)
    {
        if (health == null || !health.IsAlive)
        {
            return false;
        }

        string unitName = health.name;
        health.TakeDamage(health.Max + 1f);
        bool destroyed = !health.IsAlive;
        if (destroyed)
        {
            Log("[Dev] 销毁 " + unitName);
        }

        return destroyed;
    }

    private void HandleHotkeys()
    {
        if (Input.GetKeyDown(destroyKey))
        {
            DestroySelectedUnits();
            return;
        }

        if (Input.GetKeyDown(spawnMilitiaKey))
        {
            SpawnAtMouse(DeveloperSpawnCatalog.MilitiaId);
            return;
        }

        if (Input.GetKeyDown(spawnEnemyKey))
        {
            SpawnAtMouse(DeveloperSpawnCatalog.EnemyId);
        }
    }

    private void HandlePlacementInput()
    {
        if (!IsPlacing)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            Log("[Dev] 取消放置");
            CancelPlacement();
            return;
        }

        if (!TryGetMouseGroundPoint(out var point))
        {
            HasPreviewPoint = false;
            SetGhostActive(false);
            return;
        }

        PreviewPoint = point;
        HasPreviewPoint = true;
        UpdateGhost(point);

        if (Input.GetMouseButtonDown(0) && !DeveloperSpawnPanel.IsPointerOverPanel(Input.mousePosition))
        {
            TryPlaceAt(point);
        }
    }

    private DeveloperSpawnCatalog.Entry ResolveEntry(string entryId)
    {
        var source = Catalog;
        if (source == null)
        {
            if (!warnedMissingCatalog)
            {
                warnedMissingCatalog = true;
                Debug.LogWarning("开发者模式：未找到 " + DeveloperSpawnCatalog.ResourcePath + "，无法生成单位。", this);
            }

            return null;
        }

        var entry = source.Find(entryId);
        if (entry != null)
        {
            return entry;
        }

        // 目录被改名时退化到该阵营的第一个条目，热键仍可用。
        if (entryId == DeveloperSpawnCatalog.MilitiaId)
        {
            return source.FirstOfTeam(UnitTeam.Player);
        }

        if (entryId == DeveloperSpawnCatalog.EnemyId)
        {
            return source.FirstOfTeam(UnitTeam.Enemy);
        }

        return null;
    }

    /// <summary>
    /// 找到场景中原有敌人使用的巡逻终点，让开发者模式生成的敌人复用：
    /// 优先读 <see cref="UnitSpawner"/> 的配置，其次读已有 <see cref="EnemyBrain"/> 的巡逻点；
    /// 都没有时返回 null（与预制体默认行为一致，敌人原地待命）。
    /// </summary>
    private Transform ResolveEnemyPatrolGoal()
    {
        if (cachedEnemyPatrolGoal != null)
        {
            return cachedEnemyPatrolGoal;
        }

        var spawners = FindObjectsByType<UnitSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var enemies = FindObjectsByType<EnemyBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        cachedEnemyPatrolGoal = FindPatrolGoal(spawners, enemies);
        return cachedEnemyPatrolGoal;
    }

    /// <summary>
    /// 按优先级取巡逻终点：先看 <see cref="UnitSpawner"/> 的配置，再看已有敌人的巡逻点。
    /// 抽成静态方法，EditMode 可直接用构造数组覆盖两条回退路径。
    /// </summary>
    public static Transform FindPatrolGoal(UnitSpawner[] spawners, EnemyBrain[] enemies)
    {
        if (spawners != null)
        {
            for (int i = 0; i < spawners.Length; i++)
            {
                if (spawners[i] != null && spawners[i].PatrolGoal != null)
                {
                    return spawners[i].PatrolGoal;
                }
            }
        }

        if (enemies != null)
        {
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i] != null && enemies[i].PatrolGoal != null)
                {
                    return enemies[i].PatrolGoal;
                }
            }
        }

        return null;
    }

    /// <summary>射线命中点优先，其次与 y=0 平面求交；都失败时返回 false。</summary>
    private bool TryGetMouseGroundPoint(out Vector3 point)
    {
        return TryGetGroundPoint(Input.mousePosition, out point);
    }

    /// <summary>把屏幕坐标转成世界地面点，便于测试与复用。</summary>
    public bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 point)
    {
        point = Vector3.zero;
        var camera = Camera.main;
        if (camera == null)
        {
            return false;
        }

        Ray ray = camera.ScreenPointToRay(screenPosition);
        if (Physics.Raycast(ray, out var hit, rayDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }

        var groundPlane = new Plane(Vector3.up, Vector3.zero);
        if (groundPlane.Raycast(ray, out float distance) && distance > 0f && distance < rayDistance)
        {
            point = ray.GetPoint(distance);
            return true;
        }

        return false;
    }

    /// <summary>生成点吸附到 NavMesh；不在网格上时保持原坐标（“任意位置放置”）。</summary>
    private Vector3 ResolveSpawnPoint(Vector3 worldPosition)
    {
        if (navMeshSnapRadius > 0f &&
            NavMesh.SamplePosition(worldPosition, out var hit, navMeshSnapRadius, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return worldPosition;
    }

    private void UpdateGhost(Vector3 worldPosition)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (ghost == null || ghostEntry != ArmedEntry)
        {
            DestroyGhost();
            ghost = CreateGhost(ArmedEntry);
            ghostEntry = ArmedEntry;
        }

        if (ghost == null)
        {
            return;
        }

        ghost.transform.position = ResolveSpawnPoint(worldPosition);
        SetGhostActive(true);
    }

    private GameObject CreateGhost(DeveloperSpawnCatalog.Entry entry)
    {
        if (entry == null || entry.Prefab == null)
        {
            return null;
        }

        var preview = Instantiate(entry.Prefab);
        preview.name = "[Preview] " + entry.DisplayName;

        // 预览幽灵只用来“看”：不注册、不参战、不挡射线。
        var colliders = preview.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        var behaviours = preview.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            behaviours[i].enabled = false;
        }

        Color tint = entry.Team == UnitTeam.Enemy ? enemyPreviewColor : playerPreviewColor;
        var block = new MaterialPropertyBlock();
        var renderers = preview.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            renderers[i].receiveShadows = false;
            renderers[i].GetPropertyBlock(block);
            block.SetColor("_BaseColor", tint);
            block.SetColor("_Color", tint);
            renderers[i].SetPropertyBlock(block);
        }

        return preview;
    }

    private void SetGhostActive(bool active)
    {
        if (ghost != null && ghost.activeSelf != active)
        {
            ghost.SetActive(active);
        }
    }

    private void DestroyGhost()
    {
        if (ghost != null)
        {
            DestroyNow(ghost);
        }

        ghost = null;
        ghostEntry = null;
    }

    private void Log(string message)
    {
        if (logActions)
        {
            Debug.Log(message, this);
        }
    }

    private static void DestroyNow(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(target);
        }
        else
        {
            Object.DestroyImmediate(target);
        }
    }
}
