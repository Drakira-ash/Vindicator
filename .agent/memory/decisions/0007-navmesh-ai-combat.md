# 0007 — NavMesh AI、移动抽象与事件式拦截

- 日期：2026-10-29
- 状态：接受
- 影响组件：`UnitLocomotion`、`UnitMotor`、`UnitMover`、`EnemyBrain`、`WarriorBrain`、`UnitEvents`、`AttackAbility`、`UnitSpawner`、`Editor/UnitAITestSetup.cs`

## 背景

`tmp.txt` 要求验证敌人/战士状态机、伤害与死亡、以及“友军拦截敌人”的完整链路。原实现只有沿手写 `UnitRoute` 的 `UnitMover`，敌人 AI 直接 `RequireComponent(UnitMover)`，无法在有烘焙 NavMesh 的 `RoadWorkflow_Test` 里自由追击与上坡；也没有拦截、伤害起手延迟。

MVP 场景没有 NavMesh，不能直接删掉 `UnitMover`；两套移动需要共存。

## 决定

1. **新增 `UnitLocomotion` 抽象**（`SetPaused` / `MoveTo` / `Stop`），AI 只依赖它。
   - `UnitMotor` 封装 `NavMeshAgent`（速度每帧取 `StatBlock`，攻击时 `isStopped`，恢复后继续寻路）。
   - `UnitMover` 继续实现贴地路点，并新增坐标模式，MVP 场景不用改。
2. **新 AI 场景 `RoadWorkflow_Test`**：敌人与战士使用 `UnitMotor` + 场景已烘焙的 `NavMeshSurface`；不新增运行时 NavMesh 烘焙。
3. **拦截用静态事件** `UnitEvents.InterceptRequested`：战士锁定敌人时发出请求；敌人自行校验（目标是自己、双方存活、敌对、距离 ≤ 12m、未被其他拦截者占用）。接受后转火拦截者，拦截者死亡或距离 > 20m 时释放。
4. **攻击起手延迟**：`AttackAbility.damageDelay = 0.3s`；冷却在起手时开始，延迟结束才结算；目标中途死亡则落空。
5. **测试布置编辑器化**：`Tools/Unit/Create AI Test Scene` 一键生成/重建，避免手改场景 YAML；生成器 `UnitSpawner` 按间隔生成 30 个敌人。

## 替代方案

1. **让 `EnemyBrain` 同时兼容两种移动组件**：用 `GetComponent<UnitMover>() ?? GetComponent<NavMeshAgent>()` 分支，逻辑会散落在每个 Brain 里；抽象接口更干净。
2. **全部迁到 NavMesh 并删 `UnitMover`**：MVP 没有 NavMesh，会破坏旧验收场景。
3. **拦截做成直接方法调用/中央管理器**：需要 Brain 互相持有引用；静态事件让敌我解耦，订阅/注销可控。
4. **伤害立即结算**：无法表现起手动作，也不符合 `tmp.txt` 的“加一个伤害延迟”要求。

## 后果

- 新增单位预制体必须挂 `NavMeshAgent + UnitMotor`（或在无 NavMesh 场景挂 `UnitMover`），Brain 会报错提示缺少 `UnitLocomotion`。
- 缓存目标必须做 Unity 假空判断；已修复 `EnemyBrain`/`WarriorBrain`/`AttackAbility`/`UnitTurretBrain` 中恒为 false 的旧写法。
- 30 敌人的数值下 4 名战士约 20s 全灭；这是 UnitDefinition 配置问题，观察长时间行为需调整数值。
- `TDRoadNetwork` 仍未与移动桥接（见 backlog `road-network-pathfinding`）。
