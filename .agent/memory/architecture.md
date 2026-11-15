# 架构与不变量

> 依据：项目文档 + 代码符号核对，2026-11-10。实现细节以源码与 `YC/`、`YXZ/` 文档为准；两者冲突时先核对代码。

## 1. 单位系统（`Assets/Script/Unit/`）

| 模块 | 职责 |
|---|---|
| `Unit` | 单位根组件（`RequireComponent(UnitHealth)`）；`Awake` 初始化属性/能力，`Update` 调度能力，`ActivateAbilities` 统一授权。 |
| `UnitDefinition` | ScriptableObject 模板：名称、阵营、血量、攻击、移速、攻击间隔、射程、溅射、**感知范围**、**生产力**、能否移动/攻击。 |
| `StatBlock` | 运行时可变属性容器，按 `StatType` 存取，支持修正值；**运行时不改 SO 资产**。 |
| `UnitHealth` | `IDamageable`；受伤/死亡事件、延迟销毁、注册进 `UnitRegistry`。 |
| `UnitAbility` / `AttackAbility` | 能力基类与攻击能力；冷却、有效射程、可选溅射；`damageDelay`（默认 0.3s）起手后才结算伤害，起手期间目标死亡则落空。配置 `projectilePrefab` 时改为在 `transform.position + up * muzzleHeight` 生成 `Projectile` 并发射；预制体缺组件时记 error 并回退直接伤害。 |
| `Projectile` | 追踪投射物：`Launch(target, damage, splash, source)` 后每 tick 检查寿命/目标存活 → 进入 `triggerRange`（按碰撞体表面距离）结算 → 否则按目标当前位置重算速度矢量（标量恒定）→ 扫掠保护防穿模 → 位移并朝向；`resolved` 保证单次结算；发射时快照来源阵营。 |
| `UnitLocomotion` | 移动后端抽象：`SetPaused` / `MoveTo(worldPos)` / `Stop` / `Halt()`；AI 只依赖该接口。`Stop` 只清坐标目标（`UnitMover` 会退回路线），`Halt` = `Stop` + `SetPaused(true)` 表示真正静止。 |
| `UnitMotor` | NavMeshAgent 后端：速度每帧取 `StatBlock.MoveSpeed`；攻击时 `isStopped`，恢复后继续寻路；未落在 NavMesh 时 `Warp` 到最近点。 |
| `UnitMover` | 贴地路点/坐标后端（实现 `UnitLocomotion`）：路点只给水平走向，高度由脚下 `SphereCast` 探测决定；无 route 时可用 `MoveTo` 追坐标。 |
| `EnemyBrain` | Idle → Patrol → Chase → Attack；每 0.25s 索敌；到达巡逻点 0.5m 内 `Halt` 转 Idle，未配巡逻点则 `Stop` + `SetPaused(false)` 把移动交还路线；目标进入射程则暂停并攻击；接受 `UnitEvents.InterceptRequested` 转火拦截者：拦截者在攻击范围外时进入 Idle 原地待命（`Halt`），进入范围才 Attack。目标丢失/解除拦截当帧先 `Halt` 回 Idle，下一帧再判定。状态机见 `YC/Unit状态机_2026_11_02.md`。 |
| `WarriorBrain` | Idle → Chase → Attack → Return；检测 10m、拴绳 14m；锁定敌人时发出拦截请求；接战距离取双方射程较小值，超出拴绳放弃目标时发 `InterceptReleased`。 |
| `UnitEvents` | 全局战斗事件；`InterceptRequested` / `InterceptReleased` 携带拦截者 `Unit` 与被拦截的 `UnitHealth`。 |
| `UnitSpawner` | 测试用生成器：按间隔生成敌人并设置巡逻终点，出生点吸附 NavMesh。 |
| `UnitTurretBrain` | 塔 AI：原地索敌与开火；目标走出射程立即清空并重索敌；是否可攻击由 `Unit.abilitiesEnabled` 控制。 |
| `HealthBar` | 世界空间跟随血条：`Awake` 在单位外单独建 Background/Fill 双 Quad（无 Collider、Ignore Raycast 层），`LateUpdate` 跟随 `DamageTransform + up * heightOffset` 并 billboard 朝向主相机；订阅 `HealthChanged` 刷新宽度；`SetVisible(bool)` 供废墟隐藏/建成恢复；销毁时清理血条对象。 |
| `UnitBuilding` | 可修建建筑：`BuildingStatus.Ruined/Operational`；进度 0..`buildRequired`（默认 500）存在运行时实例上；废墟阶段禁用 `UnitHealth`/炮台/能力并隐藏血条，建成后恢复；`Garrison(Unit)` 仅同队且已建成时成功，进驻者运行时销毁（EditMode 下停用）。 |
| `UnitBuildingRegistry` | 静态建筑注册表；`FindNearestFriendly(..., preferRuined)` 先找废墟、再退回最近建筑。 |
| `BuildProgressBar` | 废墟建造条：独立跟随对象、双 Quad、无 Collider；订阅 `ProgressChanged`/`Completed`，建成隐藏。 |
| `CitizenBrain` | Idle → MoveToRepair/Repair / MoveToGarrison / Chase/Attack；Idle 时建筑优先（废墟优先于已建成），无建筑才索敌；按 `Productivity` 每秒推进建造点；建成后立即进驻并销毁自身；死亡停止行为。`Tick(deltaTime)` 公开便于 EditMode 驱动。 |
| `IPerceptionSource` | 统一感知范围来源：`EnemyBrain`/`WarriorBrain`/`CitizenBrain` 优先读 `StatBlock.PerceptionRange`（为 0 时回退各自的序列化字段），`UnitTurretBrain` 返回攻击范围且禁用时为 0。 |
| `DeveloperMode` / `DeveloperModeController` | F2 开关 + `startEnabled` 初始值（默认 `false`，需在 Inspector 或场景里显式打开）；为所有 `IPerceptionSource` 单位画半透明球（独立对象、无 Collider、Ignore Raycast、按阵营着色）；关闭时清空，无单位的球会被回收。`RuntimeInitializeOnLoadMethod` 自动创建，无需手动放置。编辑模式销毁走 `DestroyImmediate`（`DestroyNow`），运行时走 `Destroy`；`DontDestroyOnLoad` 只在 `Application.isPlaying` 时调用，EditMode 测试可直接驱动 `Awake`。 |
| `UnitRegistry` | 静态注册表；`FindNearestHostile` 线性搜索。 |
| `UnitRoute` | 路点序列（Transform 列表）。 |
| `UnitTeam`, `IDamageable` | 阵营枚举/工具与伤害接口。 |
| `Editor/UnitDemoSetup.cs` | 菜单 `Tools ▸ Unit ▸ Create Unit Test Scene`，重建模板/材质/预制体/22 点路线/塔/敌人/Home；共享助手 `EnsureArrowProjectilePrefab`、`EnsureHealthBar`、`CreateProjectileTowerPrefab`。 |
| `Editor/UnitAITestSetup.cs` | 菜单 `Tools ▸ Unit ▸ Create AI Test Scene`，在 `RoadWorkflow_Test` 生成 4 战士 + 2 塔 + 30 敌人生成器 + 巡逻点，可重复执行；`BuildAITestScene(TowerSetup)` 由三个菜单共用（`Disabled` / `Projectile` / `RuinedWithCitizens`）；`RemoveAITestObjects(GameObject[] roots)` 只清理当前活动场景的根集合，重复执行菜单不会产生重复布置，也不会误删 additive 载入的场景。 |
| `Editor/UnitProjectileTestSetup.cs` | 菜单 `Tools ▸ Unit ▸ Create Projectile Test Scene`：同一场景，但塔使用 `Tower_Arrow_Projectile.prefab`（攻击开启 + 发射箭矢），单位与塔带血条。 |
| `Editor/UnitBuildingTestSetup.cs` | 菜单 `Tools ▸ Unit ▸ Create Building Test Scene`：两处塔位换成 `Ruin_Tower.prefab`，旁边各生成一名 `AI_Citizen`，并放置 `DeveloperMode`；用 `TowerSetup.RuinedWithCitizens` 复用同一场景构建。 |

数据流：`UnitDefinition` → `Unit.Awake` 写入 `StatBlock` 与血量 → Brain 索敌（`UnitRegistry`）→ `Unit.ActivateAbilities` → `AttackAbility` 起手/延迟结算 → `UnitHealth` 事件/死亡 → 注册表注销 → Brain 重新索敌、返回驻守点或解除拦截。

投射物路径：`AttackAbility` 起手结束（`pendingHits` 到期）→ 若配置 `projectilePrefab` 则在枪口生成 `Projectile.Launch` → 每 tick 追踪/扫掠 → 进入触发范围调用一次 `TakeDamage`（可选溅射）→ `Destroy`；目标中途死亡或超时则只销毁不结算。`HealthBar` 通过 `HealthChanged` 与 `DamageTransform` 与上述链路解耦。

建筑/公民路径：`CitizenBrain.Search` → `UnitBuildingRegistry`（同队、`PerceptionRange` 内，废墟优先）→ 走到 `interactRange` 内 → `UnitBuilding.AddBuildProgress(productivity * dt)` → 累计 500 → `Complete`（恢复 `UnitHealth`/炮台/能力、隐藏建造条、切材质）→ 下一帧公民 `Garrison` → 销毁自身。无建筑时才走 `UnitRegistry.FindNearestHostile` 追击/攻击。感知范围经 `IPerceptionSource` 统一暴露，`DeveloperModeController` 只读取它生成可视化球体。

详细实现记录：[YC/Unit实现记录_2026_10_28.md](../../YC/Unit实现记录_2026_10_28.md)、[YC/ToDo_2026_10_29.md](../../YC/ToDo_2026_10_29.md)。设计依据：[YC/Research/README.md](../../YC/Research/README.md)。

## 2. 路网系统（`Assets/Script/TDWorkflow/`）

| 模块 | 职责 |
|---|---|
| `TDRoadNetwork` | 路网数据（节点 + 无向边）；BFS `TryGetNodePath`、`FindNearestNode`、端点查询。 |
| `TDRoadMeshBuilder` | 纯几何：边 → 贝塞尔中心线 → 带宽度网格；处理弯角与路口补片。与路面使用同一条中心线。 |
| `TDRoadSplineAuthoring` | 样条创作辅助。 |
| `Editor/*` | 场景视图画路/拖拽/吸附/删除、参数面板、旧 PolyShape 路导入。 |

生成结果：`Assets/TDWorkflow/Generated/RoadWorkflow_Test_Road.asset`（网格资产）及若干材质。完整操作说明：[YXZ/道路编辑工作流.md](../../YXZ/道路编辑工作流.md)。

**尚未桥接**：`TDRoadNetwork` 的 BFS 路径还不是单位的移动来源。`RoadWorkflow_Test` 中的新 AI 单位走场景里已烘焙的 `NavMeshSurface`；MVP 旧场景仍走手写 `UnitRoute` 路点，且没有路网图数据。

## 3. Harness 编译验证（`.agent/scripts/agent.py` + `Assets/Editor/HarnessUnityVerify.cs`）

| 组件 | 职责 |
|---|---|
| `HarnessUnityVerify`（Editor-only） | 菜单 `Tools/Harness/Verify Compilation`；订阅 `CompilationPipeline` 事件；`CleanBuildCache` 强制重编译；写原始证据 `.agent/.local/unity-verify-editor.json`；domain reload 后按 pending 标记补写。 |
| `agent.py unity-verify` | 批处理路径（拒绝占锁）、日志解析、写规范记录 `.agent/.local/unity-verify.json` 与事件。 |
| `agent.py unity-verify --adopt-editor` | 校验编辑器原始证据，计算漂移，盖编译签名/Git，写规范记录。 |
| `agent.py unity-verify --status` | 只读摘要；0 pass/fresh，1 fail/stale，2 无记录。 |
| `compile_signature()` | 只覆盖编译输入；用于 stale 判定。 |
| `unity_lock_state()` | 读 `Library/EditorInstance.json` + `Temp/UnityLockfile`，`tasklist` 探活。 |
| `HarnessUnityTest`（Editor-only） | 菜单 `Tools/Harness/Run EditMode Tests`；`TestRunnerApi` 在编辑器内跑测试；写 `.agent/.local/unity-test-editor.json`。 |
| `agent.py unity-test` | 只读/采纳测试证据；`--adopt-editor` 盖章为 `.agent/.local/unity-test.json`。 |
| `Assets/Tests/EditMode/` | `Vindicator.EditModeTests.asmdef`（Editor-only）；引用 `Vindicator.Gameplay`、`Vindicator.Gameplay.Editor`、`Vindicator.TestSupport`，不引用 `Assembly-CSharp`。`BuildingSceneTests` 以只读方式检查 `RoadWorkflow_Test` 结构，并验证菜单清理幂等。 |
| 共享/本机划分 | 提交 `memory/`、`tasks/backlog.json`、`project.json`、`scripts/`、`schemas/`、`reports/`；忽略 `state.json`、`tasks/{active,handoff}.json`、`events/`、`indexes/*.json`、`.local/`（见决策 0006）。 |

数据流：编辑器/批处理 → 原始证据 → Python 盖章 → 规范记录 → `status` / `session-start` / `validate`。

## 4. 不变量（改动前先检查）

1. **不修改 SO 资产做运行时状态**：状态放 `StatBlock` / 运行时组件。
2. **能力统一走 `UnitAbility`**：不要让 `Unit` 直接硬编码具体能力逻辑。
3. **贴地移动的高度来自探测，不来自路点 Y**；路点只表达水平路线。
4. **单位与地面层分离**：敌人/塔放在 Ignore Raycast 层，`groundMask` 必须排除该层；新增单位层时同步维护 mask。当前未提交修改把 `groundMask.m_Bits` 设为 `51`（层 0/1/4/5，排除层 2），保留用户改动。
5. **塔“关闭攻击”= `abilitiesEnabled = false`**，不要删能力组件。
6. **停步余量很窄**：实测 2.06m vs 有效射程 2.1m（仅 4cm）；调整碰撞体/射程/停步距离要重跑验收。
7. **MVP 与 RoadWorkflow_Test 分工随轮次变化**：MVP 是 `UnitMover` 贴地路线的旧验收场景；`RoadWorkflow_Test` 是 NavMesh AI/拦截验收场景（2026-10-29 起）。跨场景改动前先确认当前验收场景。
8. **`.meta`/GUID 完整性**：移动或新增资产必须保留配对；场景/预制体的脚本引用依赖 GUID。
9. **harness 共享与本机分离**：`state.json`、`tasks/{active,handoff}.json`、`events/` 是本机状态，可能不存在；跨机以 `backlog.json` + `git log` 为准。本机 Unity 路径放 `.agent/.local/env.json`，不要写回 `project.json`。
10. **缓存的 `IDamageable` 目标必须先做假空判断**：目标对象被销毁后接口引用仍非 null，直接读 `IsAlive`/`DamageTransform` 会得到陈旧数据或抛 `MissingReferenceException`。先 `target as UnityEngine.Object == null`，再读属性。
11. **血条与投射物不得给单位增加 Collider，也不要挂在单位子层级**：`UnitHealth.Awake` 缓存 `GetComponentsInChildren<Collider>`，`DistanceTo` 会取所有子碰撞体的最近表面；血条运行时对象必须独立、无碰撞体，箭矢预制体也必须无 Collider 并放在 Ignore Raycast 层，否则会改变射程/触发距离判定。
12. **投射物参数在预制体上，不在塔模板上**：速度 20 / 触发范围 1 / 5s 寿命配置在 `Arrow_Projectile.prefab` 的 `Projectile` 组件；`AttackAbility` 只持有 `projectilePrefab` 与 `muzzleHeight`。共享的 `Tower_Arrow.prefab` 保持攻击关闭，攻击塔使用 `Tower_Arrow_Projectile.prefab`。
13. **废墟不可被攻击 = 禁用 `UnitHealth`**：`UnitHealth.OnDisable` 会从 `UnitRegistry` 注销，敌人索敌/溅射/拦截因此都选不中。建成时重新启用并注册；不要改用图层或“标记位”实现，否则索敌仍会命中。
14. **建造进度存在 `UnitBuilding` 实例，不写回 `UnitDefinition`**：中断（公民死亡/离开）后进度保留；判定用水平距离 `UnitBuilding.DistanceTo` 与 `interactRange`（默认 1.5），不要用 3D 距离。
15. **公民 Idle 决策顺序固定**：废墟 > 已建成建筑 > 敌人 > Idle。建筑存在时不索敌；废墟优先于已建成建筑（`preferRuined`）。
16. **开发者模式球体是纯展示对象**：独立 GameObject、无 Collider、Ignore Raycast 层；只读 `IPerceptionSource.PerceptionRange`，不得参与索敌/贴地探测。关闭时清空全部球体，重复开关不泄漏。`CitizenBrain.Tick(deltaTime)` 公开仅为 EditMode 可驱动，运行时仍由 `Update` 调用。

## 5. 资产关系（概览）

- `MVP.unity` 引用单位预制体、`UnitRoute`、`UnitData` 模板与材质；`Home` 由 `UnitDemoSetup` 挂 `UnitHealth`。
- `RoadWorkflow_Test.unity` 引用 `TDRoadNetwork` 与生成的路面网格/材质。
- `Enemy_Normal.prefab` / `Tower_Arrow.prefab` 引用对应 `UnitDefinition` 与运行时脚本。
- `Enemy_Normal_Nav.prefab` / `Warrior_Guard.prefab` / `Tower_Arrow_Projectile.prefab` / `Citizen.prefab` / `Ruin_Tower.prefab` 带 `HealthBar`；`Arrow_Projectile.prefab` 无碰撞体、Ignore Raycast 层，挂 `Projectile`（20 / 1 / 5s）。
- `Citizen.prefab` 引用 `UnitData/Citizen.asset`（30/2/2/2/1.5/感知2/生产力100）与 `Citizen_Mat`；`Ruin_Tower.prefab` 引用 `Tower_Arrow.asset` 与 `Ruin_Mat`/箭塔材质，挂 `UnitBuilding`/`BuildProgressBar`。
- `RoadWorkflow_Test.unity` 另有 `AI_Citizen_0/1` 与 `DeveloperMode`（`DeveloperModeController`）；场景由 `Tools ▸ Unit ▸ Create Building Test Scene` 重建。
- 菜单会重新生成这些预制体并保路径覆盖（保 GUID）；手改预制体后重跑菜单会被覆盖。
- 详细引用关系见生成的 `.agent/indexes/assets.json`（不提交，运行 `index` 后生成）。
