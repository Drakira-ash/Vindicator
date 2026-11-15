using System;
using UnityEngine;

/// <summary>
/// 建筑状态：废墟可被公民修建；建成后恢复为正常建筑（箭塔）。
/// </summary>
public enum BuildingStatus
{
    Ruined,
    Operational
}

/// <summary>
/// 可修建建筑：废墟阶段不可被攻击，公民按生产力推进建造条；
/// 建成后启用血量、能力与炮台，并允许公民进驻（进驻者被销毁）。
/// </summary>
[DisallowMultipleComponent]
public class UnitBuilding : MonoBehaviour
{
    [SerializeField] private UnitTeam team = UnitTeam.Player;
    [SerializeField] private BuildingStatus initialStatus = BuildingStatus.Ruined;
    [SerializeField, Min(1f)] private float buildRequired = 500f;
    [SerializeField, Min(0.1f)] private float interactRange = 1.5f;
    [SerializeField] private Material ruinedMaterial;
    [SerializeField] private Material operationalMaterial;
    [SerializeField] private UnitHealth health;
    [SerializeField] private Unit unit;
    [SerializeField] private UnitTurretBrain turret;
    [SerializeField] private HealthBar healthBar;
    [SerializeField] private BuildProgressBar buildBar;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private bool logChanges = true;

    private BuildingStatus status;
    private float buildProgress;

    /// <summary>当前状态。</summary>
    public BuildingStatus Status => status;

    /// <summary>是否仍是废墟。</summary>
    public bool IsRuined => status == BuildingStatus.Ruined;

    /// <summary>是否已建成。</summary>
    public bool IsOperational => status == BuildingStatus.Operational;

    /// <summary>所属阵营。</summary>
    public UnitTeam Team => team;

    /// <summary>建成所需建造点。</summary>
    public float BuildRequired => buildRequired;

    /// <summary>当前建造进度（建造点）。</summary>
    public float BuildProgress => buildProgress;

    /// <summary>建造进度比例（0..1）。</summary>
    public float Progress01 => buildRequired > 0f ? Mathf.Clamp01(buildProgress / buildRequired) : 1f;

    /// <summary>公民可开始修建/进驻的距离（水平中心距）。</summary>
    public float InteractRange => interactRange;

    /// <summary>建造进度变化。</summary>
    public event Action<UnitBuilding> ProgressChanged;

    /// <summary>建成事件。</summary>
    public event Action<UnitBuilding> Completed;

    /// <summary>有公民进驻（进驻者随后被销毁）。</summary>
    public event Action<UnitBuilding, Unit> Garrisoned;

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponent<UnitHealth>();
        }

        if (unit == null)
        {
            unit = GetComponent<Unit>();
        }

        if (turret == null)
        {
            turret = GetComponent<UnitTurretBrain>();
        }

        if (healthBar == null)
        {
            healthBar = GetComponent<HealthBar>();
        }

        if (buildBar == null)
        {
            buildBar = GetComponent<BuildProgressBar>();
        }

        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }

        status = initialStatus;
        buildProgress = status == BuildingStatus.Operational ? buildRequired : 0f;
    }

    private void OnEnable()
    {
        UnitBuildingRegistry.Register(this);
    }

    private void OnDisable()
    {
        UnitBuildingRegistry.Unregister(this);
    }

    private void Start()
    {
        ApplyStatusVisibility();
    }

    /// <summary>到建筑中心的水平距离。</summary>
    public float DistanceTo(Vector3 worldPosition)
    {
        Vector3 to = worldPosition - transform.position;
        to.y = 0f;
        return to.magnitude;
    }

    /// <summary>是否允许该单位继续修建。</summary>
    public bool CanBeWorkedBy(Unit worker)
    {
        if (worker == null || worker.Health == null || !worker.Health.IsAlive)
        {
            return false;
        }

        return worker.Team == team && IsRuined && buildProgress < buildRequired;
    }

    /// <summary>
    /// 增加建造进度；本次达到上限时完成并返回 true。
    /// </summary>
    public bool AddBuildProgress(float amount)
    {
        if (!IsRuined || amount <= 0f)
        {
            return false;
        }

        buildProgress = Mathf.Clamp(buildProgress + amount, 0f, buildRequired);
        ProgressChanged?.Invoke(this);

        if (buildProgress < buildRequired)
        {
            return false;
        }

        Complete();
        return true;
    }

    /// <summary>
    /// 公民进驻已建成建筑并销毁自身。仅同队且已建成时成功。
    /// </summary>
    public bool Garrison(Unit occupant)
    {
        if (!IsOperational || occupant == null || occupant.Health == null || !occupant.Health.IsAlive)
        {
            return false;
        }

        if (occupant.Team != team)
        {
            return false;
        }

        Log(string.Format("[Building] {0} 进驻 {1}，销毁自身", occupant.name, name));
        Garrisoned?.Invoke(this, occupant);

        // EditMode 测试里 Destroy 不会立即执行且会报错；非运行时改为停用。
        if (Application.isPlaying)
        {
            Destroy(occupant.gameObject);
        }
        else
        {
            occupant.gameObject.SetActive(false);
        }

        return true;
    }

    private void Complete()
    {
        if (status == BuildingStatus.Operational)
        {
            return;
        }

        status = BuildingStatus.Operational;
        buildProgress = buildRequired;
        ApplyStatusVisibility();
        Log(string.Format("[Building] {0} 修建完成", name));
        Completed?.Invoke(this);
    }

    /// <summary>
    /// 统一应用废弃/建成的可见性与组件开关。
    /// 废弃阶段禁用 UnitHealth，使其不进入 UnitRegistry，敌人无法选中。
    /// </summary>
    private void ApplyStatusVisibility()
    {
        bool ruined = IsRuined;

        if (health != null)
        {
            health.enabled = !ruined;
        }

        if (unit != null)
        {
            unit.AbilitiesEnabled = !ruined;
        }

        if (turret != null)
        {
            turret.enabled = !ruined;
        }

        if (healthBar != null)
        {
            healthBar.SetVisible(!ruined);
        }

        if (buildBar != null)
        {
            buildBar.SetVisible(ruined);
        }

        if (targetRenderer != null)
        {
            var material = ruined ? ruinedMaterial : operationalMaterial;
            if (material != null)
            {
                targetRenderer.sharedMaterial = material;
            }
        }
    }

    private void Log(string message)
    {
        if (logChanges)
        {
            Debug.Log(message, this);
        }
    }
}
