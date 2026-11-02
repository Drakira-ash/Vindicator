# 架构与不变量

> 依据：项目文档 + 代码符号核对，2026-10-28。实现细节以源码与 `YC/`、`YXZ/` 文档为准；两者冲突时先核对代码。

## 1. 单位系统（`Assets/Script/Unit/`）

| 模块 | 职责 |
|---|---|
| `Unit` | 单位根组件（`RequireComponent(UnitHealth)`）；`Awake` 初始化属性/能力，`Update` 调度能力，`ActivateAbilities` 统一授权。 |
| `UnitDefinition` | ScriptableObject 模板：名称、阵营、血量、攻击、移速、攻击间隔、射程、溅射、能否移动/攻击。 |
| `StatBlock` | 运行时可变属性容器，按 `StatType` 存取，支持修正值；**运行时不改 SO 资产**。 |
| `UnitHealth` | `IDamageable`；受伤/死亡事件、延迟销毁、注册进 `UnitRegistry`。 |
| `UnitAbility` / `AttackAbility` | 能力基类与攻击能力；冷却、有效射程、可选溅射；`damageDelay`（默认 0.3s）起手后才结算伤害，起手期间目标死亡则落空。配置 `projectilePrefab` 时改为在 `transform.position + up * muzzleHeight` 生成 `Projectile` 并发射；预制体缺组件时记 error 并回退直接伤害。 |
| `Projectile` | 追踪投射物：`Launch(target, damage, splash, source)` 后每 tick 检查寿命/目标存活 → 进入 `triggerRange`（按碰撞体表面距离）结算 → 否则按目标当前位置重算速度矢量（标量恒定）→ 扫掠保护防穿模 → 位移并朝向；`resolved` 保证单次结算；发射时快照来源阵营。 |
| `UnitLocomotion` | 移动后端抽象：`SetPaused` / `MoveTo(worldPos)` / `Stop`；AI 只依赖该接口。 |
| `UnitMotor` | NavMeshAgent 后端：速度每帧取 `StatBlock.MoveSpeed`；攻击时 `isStopped`，恢复后继续寻路；未落在 NavMesh 时 `Warp` 到最近点。 |
| `UnitMover` | 贴地路点/坐标后端（实现 `UnitLocomotion`）：路点只给水平走向，高度由脚下 `SphereCast` 探测决定；无 route 时可用 `MoveTo` 追坐标。 |
| `EnemyBrain` | Idle → Patrol → Chase → Attack；每 0.25s 索敌；到达巡逻点 0.5m 内或未配巡逻点转 Idle（只 Stop 一次）；目标进入射程则暂停并攻击；接受 `UnitEvents.InterceptRequested` 转火拦截者。 |
| `WarriorBrain` | Idle → Chase → Attack → Return；检测 10m、拴绳 14m；锁定敌人时发出拦截请求。 |
| `UnitEvents` | 全局战斗事件；`InterceptRequested` 携带拦截者 `Unit` 与被拦截的 `UnitHealth`。 |
| `UnitSpawner` | 测试用生成器：按间隔生成敌人并设置巡逻终点，出生点吸附 NavMesh。 |
| `UnitTurretBrain` | 塔 AI：原地索敌与开火；目标走出射程立即清空并重索敌；是否可攻击由 `Unit.abilitiesEnabled` 控制。 |
| `HealthBar` | 世界空间跟随血条：`Awake` 在单位外单独建 Background/Fill 双 Quad（无 Collider、Ignore Raycast 层），`LateUpdate` 跟随 `DamageTransform + up * heightOffset` 并 billboard 朝向主相机；订阅 `HealthChanged` 刷新宽度；销毁时清理血条对象。 |
| `UnitRegistry` | 静态注册表；`FindNearestHostile` 线性搜索。 |
| `UnitRoute` | 路点序列（Transform 列表）。 |
| `UnitTeam`, `IDamageable` | 阵营枚举/工具与伤害接口。 |
| `Editor/UnitDemoSetup.cs` | 菜单 `Tools ▸ Unit ▸ Create Unit Test Scene`，重建模板/材质/预制体/22 点路线/塔/敌人/Home；共享助手 `EnsureArrowProjectilePrefab`、`EnsureHealthBar`、`CreateProjectileTowerPrefab`。 |
| `Editor/UnitAITestSetup.cs` | 菜单 `Tools ▸ Unit ▸ Create AI Test Scene`，在 `RoadWorkflow_Test` 生成 4 战士 + 2 塔 + 30 敌人生成器 + 巡逻点，可重复执行；`BuildAITestScene(bool)` 由两个菜单共用。 |
| `Editor/UnitProjectileTestSetup.cs` | 菜单 `Tools ▸ Unit ▸ Create Projectile Test Scene`：同一场景，但塔使用 `Tower_Arrow_Projectile.prefab`（攻击开启 + 发射箭矢），单位与塔带血条。 |

数据流：`UnitDefinition` → `Unit.Awake` 写入 `StatBlock` 与血量 → Brain 索敌（`UnitRegistry`）→ `Unit.ActivateAbilities` → `AttackAbility` 起手/延迟结算 → `UnitHealth` 事件/死亡 → 注册表注销 → Brain 重新索敌、返回驻守点或解除拦截。

投射物路径：`AttackAbility` 起手结束（`pendingHits` 到期）→ 若配置 `projectilePrefab` 则在枪口生成 `Projectile.Launch` → 每 tick 追踪/扫掠 → 进入触发范围调用一次 `TakeDamage`（可选溅射）→ `Destroy`；目标中途死亡或超时则只销毁不结算。`HealthBar` 通过 `HealthChanged` 与 `DamageTransform` 与上述链路解耦。

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
| `Assets/Tests/EditMode/` | `Vindicator.EditModeTests.asmdef`（Editor-only）+ 资产冒烟测试；不引用 `Assembly-CSharp`。 |
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

## 5. 资产关系（概览）

- `MVP.unity` 引用单位预制体、`UnitRoute`、`UnitData` 模板与材质；`Home` 由 `UnitDemoSetup` 挂 `UnitHealth`。
- `RoadWorkflow_Test.unity` 引用 `TDRoadNetwork` 与生成的路面网格/材质。
- `Enemy_Normal.prefab` / `Tower_Arrow.prefab` 引用对应 `UnitDefinition` 与运行时脚本。
- `Enemy_Normal_Nav.prefab` / `Warrior_Guard.prefab` / `Tower_Arrow_Projectile.prefab` 带 `HealthBar`；`Arrow_Projectile.prefab` 无碰撞体、Ignore Raycast 层，挂 `Projectile`（20 / 1 / 5s）。
- 菜单会重新生成这些预制体并保路径覆盖（保 GUID）；手改预制体后重跑菜单会被覆盖。
- 详细引用关系见生成的 `.agent/indexes/assets.json`（不提交，运行 `index` 后生成）。
