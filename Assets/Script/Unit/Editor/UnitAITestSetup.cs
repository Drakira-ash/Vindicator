using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键搭建 NavMesh AI 验收场景（RoadWorkflow_Test）：两处坡顶各两名战士 + 一座塔（攻击关闭），
/// 西侧生成 30 名敌人沿路网走向东侧巡逻点。
/// 菜单：Tools/Unit/Create AI Test Scene
/// </summary>
public static partial class UnitDemoSetup
{
    private const string AITestScenePath = "Assets/Scenes/RoadWorkflow_Test.unity";
    private const string WarriorDefinitionPath = DataRoot + "/Warrior_Guard.asset";
    private const string EnemyNavPrefabPath = PrefabRoot + "/Enemy_Normal_Nav.prefab";
    private const string WarriorPrefabPath = PrefabRoot + "/Warrior_Guard.prefab";

    private static readonly Vector3[] WarriorSpots =
    {
        new Vector3(-2.9f, 3.5f, -4.1f),
        new Vector3(-5.3f, 3.5f, -4.1f),
        new Vector3(-14.2f, 3.5f, 2.0f),
        new Vector3(-12.7f, 3.5f, 2.0f)
    };

    private static readonly Vector3[] TowerSpots =
    {
        new Vector3(-3.8f, 3.5f, -4.1f),
        new Vector3(-13.7f, 3.5f, 3.0f)
    };

    private static readonly Vector3 SpawnerPosition = new Vector3(-23.89f, 0f, -2.03f);
    private static readonly Vector3 PatrolGoalPosition = new Vector3(15.89f, 0.08f, -2.39f);

    [MenuItem("Tools/Unit/Create AI Test Scene", false, 11)]
    public static void CreateAITestScene()
    {
        if (!EnsureAITestSceneOpen())
        {
            return;
        }

        EnsureFolder(DataRoot);
        EnsureFolder(MaterialRoot);
        EnsureFolder(PrefabRoot);

        var enemyDefinition = CreateOrUpdateDefinition(
            EnemyDefinitionPath, "普通敌人", UnitTeam.Enemy,
            maxHealth: 100f, attack: 10f, moveSpeed: 2f, attackInterval: 2f,
            range: 1.5f, splashRadius: 0f, canMove: true, canAttack: true);

        var warriorDefinition = CreateOrUpdateDefinition(
            WarriorDefinitionPath, "战士", UnitTeam.Player,
            maxHealth: 50f, attack: 5f, moveSpeed: 4f, attackInterval: 2f,
            range: 2f, splashRadius: 0f, canMove: true, canAttack: true);

        var towerDefinition = CreateOrUpdateDefinition(
            TowerDefinitionPath, "箭塔", UnitTeam.Player,
            maxHealth: 500f, attack: 5f, moveSpeed: 0f, attackInterval: 1f,
            range: 20f, splashRadius: 0f, canMove: false, canAttack: true);

        var enemyMaterial = CreateOrUpdateMaterial(MaterialRoot + "/Enemy_Mat.mat", new Color(0.80f, 0.15f, 0.15f));
        var warriorMaterial = CreateOrUpdateMaterial(MaterialRoot + "/Warrior_Mat.mat", new Color(0.20f, 0.70f, 0.30f));
        var towerMaterial = CreateOrUpdateMaterial(MaterialRoot + "/Tower_Mat.mat", new Color(0.15f, 0.35f, 0.85f));

        var enemyPrefab = CreateNavEnemyPrefab(enemyDefinition, enemyMaterial);
        var warriorPrefab = CreateWarriorPrefab(warriorDefinition, warriorMaterial);
        var towerPrefab = CreateTowerPrefab(towerDefinition, towerMaterial);

        RemoveAITestObjects();

        int layer = LayerMask.NameToLayer("Ignore Raycast");
        for (int i = 0; i < WarriorSpots.Length; i++)
        {
            var spot = SnapToNavMesh(WarriorSpots[i], 2f);
            var warrior = (GameObject)PrefabUtility.InstantiatePrefab(warriorPrefab);
            warrior.name = "AI_Warrior_" + i;
            warrior.transform.position = spot + Vector3.up;
            SetLayerRecursively(warrior, layer);
        }

        for (int i = 0; i < TowerSpots.Length; i++)
        {
            var tower = (GameObject)PrefabUtility.InstantiatePrefab(towerPrefab);
            tower.name = "AI_Tower_" + (i == 0 ? "West" : "East");
            float ground = SampleGround(TowerSpots[i].x, TowerSpots[i].z);
            tower.transform.position = new Vector3(TowerSpots[i].x, ground + 1.5f, TowerSpots[i].z);
            SetLayerRecursively(tower, layer);
        }

        var patrolGoal = new GameObject("AI_PatrolGoal");
        patrolGoal.transform.position = new Vector3(
            PatrolGoalPosition.x, SampleGround(PatrolGoalPosition.x, PatrolGoalPosition.z), PatrolGoalPosition.z);

        var spawnerObject = new GameObject("AI_Spawner");
        spawnerObject.transform.position = new Vector3(
            SpawnerPosition.x, SampleGround(SpawnerPosition.x, SpawnerPosition.z), SpawnerPosition.z);

        var spawner = spawnerObject.AddComponent<UnitSpawner>();
        SetSerializedValue(spawner, "unitPrefab", enemyPrefab.GetComponent<Unit>());
        SetSerializedValue(spawner, "count", 30);
        SetSerializedValue(spawner, "startDelay", 0.5f);
        SetSerializedValue(spawner, "interval", 1f);
        SetSerializedValue(spawner, "scatterRadius", 0.5f);
        SetSerializedValue(spawner, "patrolGoal", patrolGoal.transform);

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = spawnerObject;
        EditorGUIUtility.PingObject(spawnerObject);

        Debug.Log("[Unit] AI 验收场景已就绪：4 名战士 + 2 座塔（攻击关闭）+ 30 名敌人，巡逻点 " + patrolGoal.transform.position);
    }

    private static bool EnsureAITestSceneOpen()
    {
        if (SceneManager.GetActiveScene().path == AITestScenePath)
        {
            return true;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return false;
        }

        EditorSceneManager.OpenScene(AITestScenePath, OpenSceneMode.Single);
        return true;
    }

    private static GameObject CreateNavEnemyPrefab(UnitDefinition definition, Material material)
    {
        var temp = CreateUnitBody("Enemy_Normal_Nav", new Vector3(0.8f, 1f, 0.8f), material);
        temp.AddComponent<UnitHealth>();
        var unit = temp.AddComponent<Unit>();
        temp.AddComponent<AttackAbility>();
        var agent = temp.AddComponent<NavMeshAgent>();
        ConfigureAgent(agent, definition.moveSpeed);
        temp.AddComponent<UnitMotor>();
        temp.AddComponent<EnemyBrain>();

        SetSerializedValue(unit, "definition", definition);
        SetSerializedValue(temp.GetComponent<UnitHealth>(), "logDamage", true);
        SetSerializedValue(temp.GetComponent<AttackAbility>(), "logAttacks", true);

        return SavePrefab(temp, EnemyNavPrefabPath);
    }

    private static GameObject CreateWarriorPrefab(UnitDefinition definition, Material material)
    {
        var temp = CreateUnitBody("Warrior_Guard", new Vector3(0.8f, 1f, 0.8f), material);
        temp.AddComponent<UnitHealth>();
        var unit = temp.AddComponent<Unit>();
        temp.AddComponent<AttackAbility>();
        var agent = temp.AddComponent<NavMeshAgent>();
        ConfigureAgent(agent, definition.moveSpeed);
        temp.AddComponent<UnitMotor>();
        temp.AddComponent<WarriorBrain>();

        SetSerializedValue(unit, "definition", definition);
        SetSerializedValue(temp.GetComponent<UnitHealth>(), "logDamage", true);
        SetSerializedValue(temp.GetComponent<AttackAbility>(), "logAttacks", true);

        return SavePrefab(temp, WarriorPrefabPath);
    }

    private static GameObject CreateUnitBody(string name, Vector3 scale, Material material)
    {
        var temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        temp.name = name;
        temp.transform.localScale = scale;
        ApplyMaterial(temp, material);
        return temp;
    }

    private static void ConfigureAgent(NavMeshAgent agent, float speed)
    {
        agent.radius = 0.4f;
        agent.height = 2f;
        agent.baseOffset = 1f;
        agent.speed = speed;
        agent.angularSpeed = 540f;
        agent.acceleration = 16f;
        agent.stoppingDistance = 0.1f;
        agent.updateRotation = true;
        agent.updatePosition = true;
        agent.autoBraking = true;
    }

    private static GameObject SavePrefab(GameObject temp, string path)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
        Object.DestroyImmediate(temp);
        return prefab;
    }

    private static Vector3 SnapToNavMesh(Vector3 spot, float maxDistance)
    {
        if (NavMesh.SamplePosition(spot, out var hit, maxDistance, NavMesh.AllAreas))
        {
            return hit.position;
        }

        Debug.LogWarning("[Unit] " + spot + " 附近没有 NavMesh，单位将无法移动。");
        return spot;
    }

    private static void RemoveAITestObjects()
    {
        var existing = GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        for (int i = existing.Length - 1; i >= 0; i--)
        {
            var candidate = existing[i];
            if (candidate == null)
            {
                continue;
            }

            string name = candidate.name;
            if (name.StartsWith("AI_Warrior_") || name.StartsWith("AI_Tower_") ||
                name == "AI_Spawner" || name == "AI_PatrolGoal")
            {
                Object.DestroyImmediate(candidate.gameObject);
            }
        }
    }
}
