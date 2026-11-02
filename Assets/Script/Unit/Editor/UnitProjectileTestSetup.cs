using UnityEditor;

/// <summary>
/// 一键搭建投射物验收场景（RoadWorkflow_Test）：与 AI 验收场景相同，
/// 但两座箭塔开启攻击并发射追踪箭矢，单位与塔均带跟随血条。
/// 菜单：Tools/Unit/Create Projectile Test Scene
/// </summary>
public static partial class UnitDemoSetup
{
    [MenuItem("Tools/Unit/Create Projectile Test Scene", false, 12)]
    public static void CreateProjectileTestScene()
    {
        BuildAITestScene(true);
    }
}
