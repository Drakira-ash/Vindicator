using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 测试用生成器：按间隔在自身附近生成敌人并指定巡逻终点。
/// 生成点会先吸附到 NavMesh，避免单位出生在网格外。
/// </summary>
public class UnitSpawner : MonoBehaviour
{
    [SerializeField] private Unit unitPrefab;
    [SerializeField, Min(1)] private int count = 30;
    [SerializeField, Min(0f)] private float startDelay = 0.5f;
    [SerializeField, Min(0f)] private float interval = 1f;
    [SerializeField, Min(0f)] private float scatterRadius = 0.5f;
    [SerializeField] private Transform patrolGoal;
    [SerializeField] private bool logSpawns = true;

    private IEnumerator Start()
    {
        if (unitPrefab == null)
        {
            Debug.LogWarning("UnitSpawner 未配置单位预制体。", this);
            yield break;
        }

        if (startDelay > 0f)
        {
            yield return new WaitForSeconds(startDelay);
        }

        for (int i = 0; i < count; i++)
        {
            Spawn(i);

            if (interval > 0f && i < count - 1)
            {
                yield return new WaitForSeconds(interval);
            }
        }
    }

    private void Spawn(int index)
    {
        Vector2 offset = Random.insideUnitCircle * scatterRadius;
        Vector3 desired = transform.position + new Vector3(offset.x, 0f, offset.y);

        if (NavMesh.SamplePosition(desired, out var hit, 2f, NavMesh.AllAreas))
        {
            desired = hit.position;
        }

        var instance = Instantiate(unitPrefab, desired + Vector3.up, Quaternion.identity);
        instance.name = unitPrefab.name + "_" + index;

        var brain = instance.GetComponent<EnemyBrain>();
        if (brain != null && patrolGoal != null)
        {
            brain.SetPatrolGoal(patrolGoal);
        }

        if (logSpawns)
        {
            Debug.Log(string.Format("[Spawner] {0} 生成 {1}", name, instance.name), this);
        }
    }
}
