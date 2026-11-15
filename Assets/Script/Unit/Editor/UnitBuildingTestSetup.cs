using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 一键搭建建筑验收场景（RoadWorkflow_Test）：原有两处塔位改为废墟塔，
/// 每座废墟旁放一名公民；公民在 5 秒内修好废墟并进驻（销毁自身）。
/// 菜单：Tools/Unit/Create Building Test Scene
/// </summary>
public static partial class UnitDemoSetup
{
    private const string CitizenDefinitionPath = DataRoot + "/Citizen.asset";
    private const string CitizenPrefabPath = PrefabRoot + "/Citizen.prefab";
    private const string RuinTowerPrefabPath = PrefabRoot + "/Ruin_Tower.prefab";

    // 默认相机从 -Z 方向俯视，塔身（3m 高）会完全挡住正后方 +Z 处的公民；
    // 侧向偏移保证开局就能看见公民，同时仍在 interactRange（1.5）内。
    // 两处塔位的可行 NavMesh 落点不同（东侧平台更窄），所以按塔位分别给偏移。
    private static readonly Vector3[] CitizenSpotOffsets =
    {
        new Vector3(-0.8f, 0f, 0.8f),
        new Vector3(-1.2f, 0f, 0f),
    };

    [MenuItem("Tools/Unit/Create Building Test Scene", false, 13)]
    public static void CreateBuildingTestScene()
    {
        BuildAITestScene(TowerSetup.RuinedWithCitizens);
    }

    /// <summary>
    /// 废墟塔：外观先使用废墟材质，禁用血量/能力/炮台，公民修满 500 建造点后恢复为箭塔。
    /// </summary>
    private static GameObject CreateRuinTowerPrefab(
        UnitDefinition definition, Material operationalMaterial, GameObject arrowPrefab)
    {
        var ruinMaterial = CreateOrUpdateMaterial(
            MaterialRoot + "/Ruin_Mat.mat", new Color(0.38f, 0.33f, 0.27f));

        var temp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        temp.name = "Ruin_Tower";
        temp.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f);
        ApplyMaterial(temp, ruinMaterial);

        var health = temp.AddComponent<UnitHealth>();
        var unit = temp.AddComponent<Unit>();
        var attack = temp.AddComponent<AttackAbility>();
        var turret = temp.AddComponent<UnitTurretBrain>();
        var building = temp.AddComponent<UnitBuilding>();
        var buildBar = temp.AddComponent<BuildProgressBar>();
        EnsureHealthBar(temp, 2.1f);
        var healthBar = temp.GetComponent<HealthBar>();

        SetSerializedValue(unit, "definition", definition);
        SetSerializedValue(unit, "abilitiesEnabled", true);
        SetSerializedValue(health, "logDamage", true);
        SetSerializedValue(attack, "projectilePrefab", arrowPrefab);
        SetSerializedValue(attack, "logAttacks", true);
        SetSerializedValue(attack, "muzzleHeight", 1.6f);

        SetSerializedValue(building, "team", UnitTeam.Player);
        SetSerializedValue(building, "initialStatus", BuildingStatus.Ruined);
        SetSerializedValue(building, "buildRequired", 500f);
        SetSerializedValue(building, "interactRange", 1.5f);
        SetSerializedValue(building, "ruinedMaterial", ruinMaterial);
        SetSerializedValue(building, "operationalMaterial", operationalMaterial);
        SetSerializedValue(building, "health", health);
        SetSerializedValue(building, "unit", unit);
        SetSerializedValue(building, "turret", turret);
        SetSerializedValue(building, "healthBar", healthBar);
        SetSerializedValue(building, "buildBar", buildBar);
        SetSerializedValue(building, "targetRenderer", temp.GetComponent<Renderer>());

        return SavePrefab(temp, RuinTowerPrefabPath);
    }

    /// <summary>
    /// 公民：血量 30 / 攻击 2 / 移速 2 / 攻击间隔 2 / 射程 1.5 / 感知 2 / 生产力 100。
    /// </summary>
    private static GameObject CreateCitizenPrefab()
    {
        var definition = CreateOrUpdateDefinition(
            CitizenDefinitionPath, "公民", UnitTeam.Player,
            maxHealth: 30f, attack: 2f, moveSpeed: 2f, attackInterval: 2f,
            range: 1.5f, splashRadius: 0f, canMove: true, canAttack: true,
            perceptionRange: 2f, productivity: 100f);

        var material = CreateOrUpdateMaterial(
            MaterialRoot + "/Citizen_Mat.mat", new Color(0.85f, 0.80f, 0.30f));

        var temp = CreateUnitBody("Citizen", new Vector3(0.7f, 0.9f, 0.7f), material);
        temp.AddComponent<UnitHealth>();
        var unit = temp.AddComponent<Unit>();
        temp.AddComponent<AttackAbility>();
        var agent = temp.AddComponent<NavMeshAgent>();
        ConfigureAgent(agent, definition.moveSpeed);
        temp.AddComponent<UnitMotor>();
        temp.AddComponent<CitizenBrain>();
        EnsureHealthBar(temp, 1.3f);

        SetSerializedValue(unit, "definition", definition);
        SetSerializedValue(temp.GetComponent<UnitHealth>(), "logDamage", true);
        SetSerializedValue(temp.GetComponent<AttackAbility>(), "logAttacks", true);

        return SavePrefab(temp, CitizenPrefabPath);
    }

    private static void CreateCitizenInstances(GameObject citizenPrefab)
    {
        int layer = LayerMask.NameToLayer("Ignore Raycast");

        for (int i = 0; i < TowerSpots.Length; i++)
        {
            var offset = CitizenSpotOffsets[i % CitizenSpotOffsets.Length];
            var spot = SnapToNavMesh(TowerSpots[i] + offset, 3f);
            var citizen = (GameObject)PrefabUtility.InstantiatePrefab(citizenPrefab);
            citizen.name = "AI_Citizen_" + i;
            citizen.transform.position = spot + Vector3.up;
            SetLayerRecursively(citizen, layer);
        }
    }

    private static void EnsureDeveloperModeObject()
    {
        var existing = GameObject.Find("DeveloperMode");
        if (existing != null)
        {
            return;
        }

        var go = new GameObject("DeveloperMode");
        var controller = go.AddComponent<DeveloperModeController>();
        SetSerializedValue(controller, "startEnabled", true);
    }
}
