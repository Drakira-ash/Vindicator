using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键搭建 Unit 验收场景：单位模板、材质、预制体、路线、斜坡上的塔与敌人、可破坏的 Home。
/// 菜单：Tools/Unit/Create Unit Test Scene
/// </summary>
public static partial class UnitDemoSetup
{
    private const string DataRoot = "Assets/UnitData";
    private const string MaterialRoot = "Assets/UnitData/Materials";
    private const string PrefabRoot = "Assets/Prefabs/Unit";

    private const string EnemyDefinitionPath = DataRoot + "/Enemy_Normal.asset";
    private const string TowerDefinitionPath = DataRoot + "/Tower_Arrow.asset";
    private const string EnemyPrefabPath = PrefabRoot + "/Enemy_Normal.prefab";
    private const string TowerPrefabPath = PrefabRoot + "/Tower_Arrow.prefab";

    private const string RouteRootName = "UnitRoute";
    private const string EnemyInstanceName = "UnitDemo_Enemy";
    private const string TowerInstanceName = "UnitDemo_Tower";

    private static readonly Vector2[] RouteXZ =
    {
        new Vector2(-22.0f, -9.2f),
        new Vector2(-16.0f, -9.1f),
        new Vector2(-10.0f, -9.0f),
        new Vector2(-6.0f, -9.2f),
        new Vector2(-4.8f, -9.4f),
        new Vector2(-3.4f, -9.3f),
        new Vector2(-3.4f, -8.4f),
        new Vector2(-3.4f, -7.4f),
        new Vector2(-3.4f, -6.4f),
        new Vector2(-3.4f, -5.4f),
        new Vector2(-3.4f, -4.8f),
        new Vector2(-3.0f, -4.1f),
        new Vector2(-3.6f, -4.8f),
        new Vector2(-4.3f, -5.6f),
        new Vector2(-4.3f, -6.6f),
        new Vector2(-4.3f, -7.6f),
        new Vector2(-4.3f, -8.6f),
        new Vector2(-3.9f, -9.4f),
        new Vector2(-2.6f, -9.4f),
        new Vector2(2.0f, -7.6f),
        new Vector2(8.5f, -5.6f),
        new Vector2(14.5f, -4.1f)
    };

    private static readonly Vector3 TowerPosition = new Vector3(-1.7f, 0f, -2.2f);

    [MenuItem("Tools/Unit/Create Unit Test Scene", false, 10)]
    public static void CreateScene()
    {
        EnsureFolder(DataRoot);
        EnsureFolder(MaterialRoot);
        EnsureFolder(PrefabRoot);

        var enemyDefinition = CreateOrUpdateDefinition(
            EnemyDefinitionPath, "普通敌人", UnitTeam.Enemy,
            maxHealth: 100f, attack: 10f, moveSpeed: 2f, attackInterval: 2f,
            range: 1.5f, splashRadius: 0f, canMove: true, canAttack: true);

        var towerDefinition = CreateOrUpdateDefinition(
            TowerDefinitionPath, "箭塔", UnitTeam.Player,
            maxHealth: 500f, attack: 5f, moveSpeed: 0f, attackInterval: 1f,
            range: 20f, splashRadius: 0f, canMove: false, canAttack: true);

        var enemyMaterial = CreateOrUpdateMaterial(MaterialRoot + "/Enemy_Mat.mat", new Color(0.80f, 0.15f, 0.15f));
        var towerMaterial = CreateOrUpdateMaterial(MaterialRoot + "/Tower_Mat.mat", new Color(0.15f, 0.35f, 0.85f));

        var enemyPrefab = CreateEnemyPrefab(enemyDefinition, enemyMaterial);
        var towerPrefab = CreateTowerPrefab(towerDefinition, towerMaterial);

        var route = CreateRoute();

        RemoveIfExists(EnemyInstanceName);
        RemoveIfExists(TowerInstanceName);

        var towerGround = SampleGround(TowerPosition.x, TowerPosition.z);
        var tower = (GameObject)PrefabUtility.InstantiatePrefab(towerPrefab);
        tower.name = TowerInstanceName;
        tower.transform.position = new Vector3(TowerPosition.x, towerGround + 1.5f, TowerPosition.z);
        tower.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
        SetLayerRecursively(tower, LayerMask.NameToLayer("Ignore Raycast"));

        var enemy = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab);
        enemy.name = EnemyInstanceName;
        Vector3 start = route.GetPoint(0);
        enemy.transform.position = new Vector3(start.x, start.y + 1f, start.z);
        SetLayerRecursively(enemy, LayerMask.NameToLayer("Ignore Raycast"));
        SetSerializedValue(enemy.GetComponent<UnitMover>(), "route", route);

        ConfigureHome();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = enemy;
        EditorGUIUtility.PingObject(enemy);

        Debug.Log("[Unit] 验收场景已就绪：路线 " + route.Count + " 个路点，塔位于斜坡顶部，Home 已挂血量组件。");
    }

    private static UnitDefinition CreateOrUpdateDefinition(
        string path, string displayName, UnitTeam team,
        float maxHealth, float attack, float moveSpeed, float attackInterval,
        float range, float splashRadius, bool canMove, bool canAttack)
    {
        var definition = AssetDatabase.LoadAssetAtPath<UnitDefinition>(path);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<UnitDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }

        definition.displayName = displayName;
        definition.team = team;
        definition.maxHealth = maxHealth;
        definition.attack = attack;
        definition.moveSpeed = moveSpeed;
        definition.attackInterval = attackInterval;
        definition.range = range;
        definition.splashRadius = splashRadius;
        definition.canMove = canMove;
        definition.canAttack = canAttack;

        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();
        return definition;
    }

    private static Material CreateOrUpdateMaterial(string path, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static GameObject CreateEnemyPrefab(UnitDefinition definition, Material material)
    {
        var temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        temp.name = "Enemy_Normal";
        temp.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        ApplyMaterial(temp, material);

        temp.AddComponent<UnitHealth>();
        var unit = temp.AddComponent<Unit>();
        temp.AddComponent<AttackAbility>();
        var mover = temp.AddComponent<UnitMover>();
        temp.AddComponent<EnemyBrain>();

        SetSerializedValue(unit, "definition", definition);
        SetSerializedValue(mover, "groundOffset", 1f);
        SetSerializedValue(temp.GetComponent<UnitHealth>(), "logDamage", true);

        var prefab = PrefabUtility.SaveAsPrefabAsset(temp, EnemyPrefabPath);
        UnityEngine.Object.DestroyImmediate(temp);
        return prefab;
    }

    private static GameObject CreateTowerPrefab(UnitDefinition definition, Material material)
    {
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        temp.name = "Tower_Arrow";
        temp.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f);
        ApplyMaterial(temp, material);

        temp.AddComponent<UnitHealth>();
        var unit = temp.AddComponent<Unit>();
        temp.AddComponent<AttackAbility>();
        temp.AddComponent<UnitTurretBrain>();

        SetSerializedValue(unit, "definition", definition);
        SetSerializedValue(unit, "abilitiesEnabled", false);
        SetSerializedValue(temp.GetComponent<UnitHealth>(), "logDamage", true);

        var prefab = PrefabUtility.SaveAsPrefabAsset(temp, TowerPrefabPath);
        UnityEngine.Object.DestroyImmediate(temp);
        return prefab;
    }

    private static UnitRoute CreateRoute()
    {
        RemoveIfExists(RouteRootName);

        var root = new GameObject(RouteRootName);
        for (int i = 0; i < RouteXZ.Length; i++)
        {
            var waypoint = new GameObject(string.Format("Waypoint_{0:D2}", i));
            waypoint.transform.SetParent(root.transform, false);
            float y = SampleGround(RouteXZ[i].x, RouteXZ[i].y);
            waypoint.transform.position = new Vector3(RouteXZ[i].x, y, RouteXZ[i].y);
        }

        var route = root.AddComponent<UnitRoute>();
        route.RebuildFromChildren();
        return route;
    }

    private static void ConfigureHome()
    {
        var home = GameObject.Find("TD_Whitebox/Home/Home");
        if (home == null)
        {
            Debug.LogWarning("[Unit] 未找到 TD_Whitebox/Home/Home，跳过 Home 配置。");
            return;
        }

        var health = home.GetComponent<UnitHealth>();
        if (health == null)
        {
            health = home.AddComponent<UnitHealth>();
        }

        SetSerializedValue(health, "team", UnitTeam.Player);
        SetSerializedValue(health, "maxHealth", 300f);
        SetSerializedValue(health, "destroyOnDeath", true);
        SetSerializedValue(health, "logDamage", true);
    }

    private static float SampleGround(float x, float z)
    {
        int mask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
        if (Physics.Raycast(new Vector3(x, 30f, z), Vector3.down, out RaycastHit hit, 100f, mask, QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }

        return 0f;
    }

    private static void ApplyMaterial(GameObject target, Material material)
    {
        var renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private static void RemoveIfExists(string name)
    {
        var existing = GameObject.Find(name);
        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(existing);
        }
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (layer < 0)
        {
            return;
        }

        target.layer = layer;
        for (int i = 0; i < target.transform.childCount; i++)
        {
            SetLayerRecursively(target.transform.GetChild(i).gameObject, layer);
        }
    }

    private static void SetSerializedValue(UnityEngine.Object target, string fieldName, object value)
    {
        if (target == null)
        {
            return;
        }

        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogWarning("[Unit] 找不到字段 " + fieldName + " on " + target.GetType().Name);
            return;
        }

        switch (property.propertyType)
        {
            case SerializedPropertyType.ObjectReference:
                property.objectReferenceValue = value as UnityEngine.Object;
                break;
            case SerializedPropertyType.Float:
                property.floatValue = Convert.ToSingle(value);
                break;
            case SerializedPropertyType.Integer:
                property.intValue = Convert.ToInt32(value);
                break;
            case SerializedPropertyType.Boolean:
                property.boolValue = Convert.ToBoolean(value);
                break;
            case SerializedPropertyType.Enum:
                property.enumValueIndex = Convert.ToInt32(value);
                break;
            case SerializedPropertyType.LayerMask:
                property.intValue = Convert.ToInt32(value);
                break;
            default:
                Debug.LogWarning("[Unit] 不支持的字段类型 " + property.propertyType + " (" + fieldName + ")");
                break;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(path);
        string name = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            EnsureFolder(parent.Replace('\\', '/'));
        }

        AssetDatabase.CreateFolder(parent, name);
    }
}
