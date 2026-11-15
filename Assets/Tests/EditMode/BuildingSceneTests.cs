using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 验收场景结构测试：只读检查 RoadWorkflow_Test 中的废墟塔、公民、战士、
/// 生成器与开发者模式，防止误跑旧菜单覆盖布置后测试仍然全绿；
/// 另验证菜单清理逻辑在显式根对象集合上可重复执行且不影响已载入的验收场景。
/// 场景已打开时直接复用；未打开则以 additive 方式载入，结束后关闭，不保存。
/// </summary>
public class BuildingSceneTests
{
    private const string ScenePath = "Assets/Scenes/RoadWorkflow_Test.unity";

    private Scene scene;
    private bool openedHere;

    [SetUp]
    public void SetUp()
    {
        scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            openedHere = true;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (openedHere && scene.IsValid() && scene.isLoaded)
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void Scene_HasTwoRuinedTowersAndTwoCitizens()
    {
        var objects = SceneObjects();

        var towers = FindByPrefix(objects, "AI_Tower_");
        Assert.AreEqual(2, towers.Count, "应有两座塔位");
        for (int i = 0; i < towers.Count; i++)
        {
            var building = towers[i].GetComponent<UnitBuilding>();
            Assert.IsNotNull(building, towers[i].name + " 缺少 UnitBuilding");
            Assert.AreEqual(
                (int)BuildingStatus.Ruined,
                SerializedInt(building, "initialStatus"),
                towers[i].name + " 应为废墟");
            Assert.AreEqual(500f, SerializedFloat(building, "buildRequired"), 0.001f,
                towers[i].name + " 建造条上限应为 500");
        }

        Assert.AreEqual(2, FindByPrefix(objects, "AI_Citizen_").Count, "应有两名公民");
    }

    [Test]
    public void Scene_CitizensAreWithinRepairRangeOfRuinTowerPrefabInstances()
    {
        var objects = SceneObjects();
        var towers = FindByPrefix(objects, "AI_Tower_");
        var citizens = FindByPrefix(objects, "AI_Citizen_");
        Assert.AreEqual(2, towers.Count, "应有两座塔位");
        Assert.AreEqual(2, citizens.Count, "应有两名公民");

        var buildings = new UnitBuilding[towers.Count];
        for (int i = 0; i < towers.Count; i++)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(towers[i]);
            Assert.IsNotNull(source, towers[i].name + " 不是预制体实例");
            Assert.AreEqual(
                "Assets/Prefabs/Unit/Ruin_Tower.prefab",
                AssetDatabase.GetAssetPath(source),
                towers[i].name + " 应来自废墟塔预制体");

            buildings[i] = towers[i].GetComponent<UnitBuilding>();
            Assert.IsNotNull(buildings[i], towers[i].name + " 缺少 UnitBuilding");
        }

        for (int c = 0; c < citizens.Count; c++)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(citizens[c]);
            Assert.IsNotNull(source, citizens[c].name + " 不是预制体实例");
            Assert.AreEqual(
                "Assets/Prefabs/Unit/Citizen.prefab",
                AssetDatabase.GetAssetPath(source),
                citizens[c].name + " 应来自公民预制体");

            float nearest = float.MaxValue;
            UnitBuilding nearestBuilding = null;
            for (int t = 0; t < buildings.Length; t++)
            {
                float distance = buildings[t].DistanceTo(citizens[c].transform.position);
                if (distance < nearest)
                {
                    nearest = distance;
                    nearestBuilding = buildings[t];
                }
            }

            Assert.IsNotNull(nearestBuilding, citizens[c].name + " 找不到可修建的塔");
            Assert.LessOrEqual(
                nearest, nearestBuilding.InteractRange + 0.01f,
                citizens[c].name + " 应位于塔的交互距离内，否则永远修不好废墟");
        }

        // 每座塔都要有公民负责，否则两名人手挤在同一塔旁时另一座永远修不好。
        for (int t = 0; t < buildings.Length; t++)
        {
            bool covered = false;
            for (int c = 0; c < citizens.Count; c++)
            {
                if (buildings[t].DistanceTo(citizens[c].transform.position) <=
                    buildings[t].InteractRange + 0.01f)
                {
                    covered = true;
                    break;
                }
            }

            Assert.IsTrue(covered, towers[t].name + " 附近应至少有一名公民");
        }
    }

    [Test]
    public void TestSceneCleanup_RemovesDuplicatesAndIsIdempotent_WithoutTouchingAcceptanceScene()
    {
        int acceptanceCount = SceneObjects().Count;

        string[] names =
        {
            "AI_Warrior_0", "AI_Tower_West", "AI_Citizen_0",
            "AI_Spawner", "AI_PatrolGoal", "DeveloperMode"
        };

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Unit/Citizen.prefab");
        Assert.IsNotNull(prefab, "缺少公民预制体，无法在预览场景中模拟生成对象");

        var generated = new List<GameObject>();
        GameObject sentinel = null;
        var previewScene = EditorSceneManager.NewPreviewScene();
        try
        {
            // 用预制体实例直接建到预览场景，避免触碰当前活动场景（可能是验收场景本身）。
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, previewScene);
                    go.name = names[i];
                    generated.Add(go);
                }
            }

            sentinel = (GameObject)PrefabUtility.InstantiatePrefab(prefab, previewScene);
            sentinel.name = "NotGenerated";

            Assert.AreEqual(12, generated.Count, "应模拟菜单被重复执行两次后的 12 个生成对象");
            Assert.AreEqual(13, previewScene.GetRootGameObjects().Length, "预览场景应有 12 个生成对象 + 1 个哨兵");

            UnitDemoSetup.RemoveAITestObjects(previewScene.GetRootGameObjects());

            for (int i = 0; i < generated.Count; i++)
            {
                Assert.IsTrue(generated[i] == null, "清理后不应残留生成对象");
            }

            Assert.IsTrue(sentinel != null, "非生成对象不应被清理");
            Assert.AreEqual(1, previewScene.GetRootGameObjects().Length, "预览场景只应剩哨兵");
            Assert.AreEqual(acceptanceCount, SceneObjects().Count, "清理不应影响已载入的验收场景");

            // 第二次清理应保持幂等：引用已销毁也不应抛错或产生副作用。
            UnitDemoSetup.RemoveAITestObjects(previewScene.GetRootGameObjects());
            Assert.IsTrue(sentinel != null, "重复清理不应影响非生成对象");
            Assert.AreEqual(acceptanceCount, SceneObjects().Count, "重复清理不应影响验收场景");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    [Test]
    public void Scene_KeepsWarriorsSpawnerPatrolAndDeveloperMode()
    {
        var objects = SceneObjects();

        Assert.AreEqual(4, FindByPrefix(objects, "AI_Warrior_").Count, "应有四名战士");

        var spawner = FindByName(objects, "AI_Spawner");
        Assert.IsNotNull(spawner, "缺少 AI_Spawner");
        var spawnerComponent = spawner.GetComponent<UnitSpawner>();
        Assert.IsNotNull(spawnerComponent, "AI_Spawner 缺少 UnitSpawner");
        Assert.AreEqual(30, SerializedInt(spawnerComponent, "count"), "敌人数量应为 30");

        Assert.IsNotNull(FindByName(objects, "AI_PatrolGoal"), "缺少 AI_PatrolGoal");

        var developerMode = FindByName(objects, "DeveloperMode");
        Assert.IsNotNull(developerMode, "缺少 DeveloperMode");
        var controller = developerMode.GetComponent<DeveloperModeController>();
        Assert.IsNotNull(controller, "DeveloperMode 缺少 DeveloperModeController");
        Assert.AreEqual(true, SerializedBool(controller, "startEnabled"),
            "验收场景应默认开启开发者模式");
    }

    private List<GameObject> SceneObjects()
    {
        var result = new List<GameObject>();
        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            var transforms = roots[i].GetComponentsInChildren<Transform>(true);
            for (int j = 0; j < transforms.Length; j++)
            {
                result.Add(transforms[j].gameObject);
            }
        }

        return result;
    }

    private static List<GameObject> FindByPrefix(List<GameObject> objects, string prefix)
    {
        var result = new List<GameObject>();
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i].name.StartsWith(prefix))
            {
                result.Add(objects[i]);
            }
        }

        return result;
    }

    private static GameObject FindByName(List<GameObject> objects, string name)
    {
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i].name == name)
            {
                return objects[i];
            }
        }

        return null;
    }

    private static int SerializedInt(Object target, string propertyName)
    {
        var property = FindProperty(target, propertyName);
        return property.intValue;
    }

    private static float SerializedFloat(Object target, string propertyName)
    {
        var property = FindProperty(target, propertyName);
        return property.floatValue;
    }

    private static bool SerializedBool(Object target, string propertyName)
    {
        var property = FindProperty(target, propertyName);
        return property.boolValue;
    }

    private static SerializedProperty FindProperty(Object target, string propertyName)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(propertyName);
        Assert.IsNotNull(property, "未找到序列化字段: " + propertyName);
        return property;
    }
}
