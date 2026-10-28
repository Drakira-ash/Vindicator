using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 项目资产冒烟测试：只依赖 AssetDatabase，不引用 Assembly-CSharp，
/// 用来验证载入的工程结构与关键资产没有断链。
/// </summary>
public class ProjectSmokeTests
{
    [TestCase("Assets/UnitData/Enemy_Normal.asset")]
    [TestCase("Assets/UnitData/Tower_Arrow.asset")]
    public void UnitDefinitionAssetsExist(string path)
    {
        Assert.IsNotNull(AssetDatabase.LoadMainAssetAtPath(path), "缺少单位模板: " + path);
    }

    [TestCase("Assets/Prefabs/Unit/Enemy_Normal.prefab")]
    [TestCase("Assets/Prefabs/Unit/Tower_Arrow.prefab")]
    public void UnitPrefabsExist(string path)
    {
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(path), "缺少单位预制体: " + path);
    }

    [Test]
    public void AcceptanceSceneExists()
    {
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MVP.unity"), "缺少验收场景 MVP.unity");
    }
}
