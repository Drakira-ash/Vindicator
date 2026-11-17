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
| `EnemyBrain` | Idle → Patrol → Chase → Attack；每 0.25s 索敌；到达巡逻点 0.5m 内 `Halt` 转 Idle，未配巡逻点则 `Stop` + `SetPaused(false)` 把移动交还路线；目标进入射程则暂停并攻击。追击写 `UnitTargetState.ChaseTarget`、攻击写 `LockTarget`，Idle/巡逻/死亡清空。`interceptEnabled` 默认 `false`（2026-11-08 计划暂时取消拦截），关闭时不响应 `InterceptRequested`；开启后恢复旧转火逻辑。目标丢失当帧先 `Halt` 回 Idle，下一帧再判定。状态机见 `YC/单位状态机_2026_11_08.md`。 |
| `WarriorBrain` | Idle → Move → Chase → Attack → Return；基础检测 **3m**、拴绳 14m；小队姿态会把有效检测切到 **戒备 6m / 驻守 3m**。`UnitCommander.HasOrder` 时玩家命令优先（移动/集火），命令结束后恢复驻守。跟随移动命令带 `FollowOrder`：到点不清命令，由小队统一结束；命令 true→false 时把 `guardPosition` 更新为当前位置。驻守姿态下拴绳锚点改为小队集结点、半径 5m；戒备姿态在目标丢失时原地停留（`StayInPlace`），不返回旧驻守点。追击/攻击维护 `ChaseTarget`/`LockTarget`，驻守/返回/死亡清空。`interceptEnabled` 默认 `false`，关闭时只锁定不请求拦截。接战距离取双方射程较小值。 |
| `UnitEvents` | 全局战斗事件；`InterceptRequested` / `InterceptReleased` 携带拦截者 `Unit` 与被拦截的 `UnitHealth`。 |
| `UnitSpawner` | 测试用生成器：按间隔生成敌人并设置巡逻终点，出生点吸附 NavMesh。 |
| `UnitTurretBrain` | 塔 AI：原地索敌与开火；目标走出射程立即清空并重索敌；攻击时写 `LockTarget`，无目标清空；`StateName` 输出 `Idle/Attack`；是否可攻击由 `Unit.abilitiesEnabled` 控制。 |
| `HealthBar` | 世界空间跟随血条：`Awake` 在单位外单独建 Background/Fill 双 Quad（无 Collider、Ignore Raycast 层），`LateUpdate` 跟随 `DamageTransform + up * heightOffset` 并 billboard 朝向主相机；订阅 `HealthChanged` 刷新宽度；`SetVisible(bool)` 供废墟隐藏/建成恢复；销毁时清理血条对象。 |
| `UnitBuilding` | 可修建建筑：`BuildingStatus.Ruined/Operational`；进度 0..`buildRequired`（默认 500）存在运行时实例上；废墟阶段禁用 `UnitHealth`/炮台/能力并隐藏血条，建成后恢复；`Garrison(Unit)` 仅同队且已建成时成功，进驻者运行时销毁（EditMode 下停用）。 |
| `UnitBuildingRegistry` | 静态建筑注册表；`FindNearestFriendly(..., preferRuined)` 先找废墟、再退回最近建筑。 |
| `BuildProgressBar` | 废墟建造条：独立跟随对象、双 Quad、无 Collider；订阅 `ProgressChanged`/`Completed`，建成隐藏。 |
| `CitizenBrain` | Idle → MoveToRepair/Repair / MoveToGarrison / Chase/Attack；Idle 时建筑优先（废墟优先于已建成），无建筑才索敌；按 `Productivity` 每秒推进建造点；建成后立即进驻并销毁自身；死亡停止行为。索敌时维护 `ChaseTarget`/`LockTarget`，修建/进驻/Idle/死亡清空。`Tick(deltaTime)` 公开便于 EditMode 驱动。 |
| `AI/FormationLayout` | 纯函数编队站位：`Line` 一排、`Column` 一列、`Grid` 近似方形；锚点为队形中心，槽位按 `spacing` 对称展开，右方向 = `Cross(up, forward)`；可选 `NavMesh.SamplePosition` 吸附，EditMode 无 NavMesh 时保留原槽位。 |
| `AI/UnitCommander` | 每单位命令接收器：`SquadId`、`Move`/`Attack` 命令与目标；Brain 读取执行，命令完成/目标死亡后清空。`TryGetAttackTarget` 供 Brain 安全取目标。 |
| `AI/UnitTargetState` | 单位目标指针数据表：`ChaseTarget`/`LockTarget` 两个 `Transform`，Brain 维护、开发者表只读；销毁对象读取为 null；`Ensure` 自动补挂。 |
| `AI/Squad` / `SquadRegistry` / `SquadBrain` | 运行期小队：成员、队形、锚点、集结点、姿态、集火目标；`SquadRegistry` 自动编号/建队/解散（成员被整体移走或全员阵亡后自动 `Disband`，不留 0 人空壳）；`SquadBrain` 行为树 `攻击命令 > 移动命令 > 自动接敌 > Idle`，输出 `Idle/Moving/Engaging`。移动为**锚点跟随**：`MoveTo` 只记录目标，`Advance(dt)` 每帧推进锚点、限速转向、按队形重算槽位并重发 `IssueMove(..., follow:true)`；起步等队形误差 ≤1m，行进中 >2m 减速、>3.5m 停车等待，到点后用 `settleRadius=0.3m` 判定成员落位并整队转向。槽位身份用 `UnitCommander.SlotIndex` 稳定绑定，死亡时只让越界成员补位。`Squad.ManualControl`：玩家给部分成员下过个体命令后为 `true`，行为树跳过自动接敌，直到 `MoveTo`/`Attack` 等小队级命令解除。姿态与集结点见下一行。小队不写场景。 |
| `AI/SquadPosture` / `Squad.RallyPoint` | 姿态：友军默认 `Garrison`、敌方规则默认 `Alert`（敌方 Squad 尚未创建）。`SetPosture` 推送成员 Brain：戒备感知 6、战后不回位；驻守感知 3、拴绳锚点为集结点且 5m 内结束追击并回槽位。`RallyPoint` 建队时取质心、整队移动完成后自动更新为落点；`SetRallyPoint` 为手动 API。`SquadBrain.EnforceGarrisonLeash` 每帧检查集火目标是否离开集结点 5m，`FindEnemy` 也只返回 5m 内敌人。 |
| `AI/UnitSelection` | 玩家当前选择（静态列表 + `Changed` 事件），与输入层解耦，EditMode 可直接设置。 |
| `Player/PlayerCommandController` | RTS 操作：左键框选/点选、Shift 追加、F1 全选、右键移动/攻击、G 编队（编号自增）、1-9 选择小队、Ctrl+1-9 指定编号、`Z` 循环阵型、`X` 切换姿态；运行期自动创建。右键成功移动后调用 `MoveDestinationFlag.Show` 在落点插旗（1.5s、无碰撞、Ignore Raycast）。**默认不编队**：右键移动/攻击只给选中单位下个体命令（移动保持质心相对位置），只有 `SelectionSquad()` 判定"选择恰好覆盖某小队全部存活成员"时才走 `Squad.MoveTo/Attack`；下个体命令前 `DropSquadOrders()` 让所属小队放弃旧命令并 `SetManualControl(true)`。`LegacySelectionEnabled` 由 `SelectionCommandBridge` 在 UI_Test 关闭，避免与新 UI 的 SelectionInputReader 重复左键选择；右键命令与热键保留。框选时绘制选择框、选中单位画绿色标记；判定用 Renderer 屏幕包围盒（无 Renderer 时枢轴 ±8px）；`[Select]` / `[Order]` / `[Squad]` / `[Formation]` / `[Posture]` 日志可确认输入与命令是否到达。 |
| `Player/SquadPanel` | uGUI 编队面板：运行期创建 Canvas，显示“已选 N / 可指挥 M”与小队编号/人数/队形/状态/姿态，支持组建/选择/解散；Tab 收起。 |
| `Player/MoveDestinationFlag` | 移动落点旗子：运行时 primitive（旗杆+布面），无 Collider、Ignore Raycast；`ScaleAt` 为可测纯曲线，出现 0.15s、保持、0.35s 缩没，总寿命 1.5s。 |
| `Developer/DeveloperTableData` / `DeveloperDataTable` | F3 开发者数据表：小队表（含姿态）与单位表（含追逐/锁定指针、小队编号）；IMGUI 绘制，仅开发者模式可见，销毁对象显示 `-`。 |
| `IPerceptionSource` | 统一感知范围来源：`EnemyBrain`/`WarriorBrain`/`CitizenBrain` 优先读 `StatBlock.PerceptionRange`（为 0 时回退各自的序列化字段），`UnitTurretBrain` 返回攻击范围且禁用时为 0。 |
| `DeveloperMode` / `DeveloperModeController` | `~`（`BackQuote`）开关，`F2` 为旧别名；`startEnabled` 初始值（默认 `false`，需在 Inspector 或场景里显式打开）；为所有 `IPerceptionSource` 单位画半透明球（独立对象、无 Collider、Ignore Raycast、按阵营着色）；关闭时清空，无单位的球会被回收。`RuntimeInitializeOnLoadMethod` 自动创建，无需手动放置。编辑模式销毁走 `DestroyImmediate`（`DestroyNow`），运行时走 `Destroy`；`DontDestroyOnLoad` 只在 `Application.isPlaying` 时调用，EditMode 测试可直接驱动 `Awake`。球体材质优先用 `Assets/Resources/Shaders/PerceptionVolume.shader`（模板 `Comp NotEqual`，同一像素只画第一个通过深度测试的球——透明队列按远到近绘制，因此通常是较远的那个；重叠不叠加透明度；填充 0.10 + Fresnel 边缘），找不到 shader 才退回 URP Unlit 半透明；材质按阵营各一份共享。 |
| `Developer/DeveloperSpawnCatalog` / `DeveloperSpawnController` / `DeveloperSpawnPanel` | 开发者模式单位生成/销毁：`~` 进入后 `K` 销毁选择、`S`/`D` 在鼠标处生成民兵/普通敌人；目录资产 `Assets/Resources/Developer/SpawnCatalog.asset`（`militia`→`Warrior_Guard`、`enemy`→`Enemy_Normal_Nav`）按阵营列出条目。生成点优先射线命中，其次与 `y=0` 平面求交，最后 `NavMesh.SamplePosition` 吸附（失败保留原坐标）。生成敌人时继承场景 `UnitSpawner.PatrolGoal`（其次已有 `EnemyBrain.PatrolGoal`）并 `SetPatrolGoal`，否则预制体无巡逻点会原地待命。面板（右侧 IMGUI，`F4` 或标题栏展开）按阵营预览并进入放置模式（鼠标处显示禁用全部 `Behaviour`/`Collider` 的预览实例，左键放置、右键/`Esc` 取消），并逐行销毁任意单位。销毁走 `UnitHealth.TakeDamage` 正常死亡路径。`PlayerCommandController` 在放置模式或鼠标位于面板上时跳过框选/右键（IMGUI 不经过 EventSystem）；开发者模式下 `IsSelectable` 放开到任意存活单位（含敌人）以便编辑/销毁，但 `IsCommandable` 仍只允许 Player+`WarriorBrain`，右键移动/攻击与编队都会过滤敌人；关闭开发者模式时控制器监听 `DeveloperMode.Changed`，立即把仅用于编辑的敌人移出选择。 |
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

小队/玩家路径：`PlayerCommandController`（框选/右键/热键）→ `UnitSelection` → `SquadRegistry`/`Squad`（站位 `FormationLayout`，`Advance` 锚点跟随）→ `UnitCommander.IssueMove/IssueAttack` → 成员 Brain 的 `ExecuteOrder` → `UnitLocomotion`/`UnitAbility`。Brain 在追击/攻击时写 `UnitTargetState`，`SquadBrain` 行为树每帧输出 `Idle/Moving/Engaging`，`DeveloperTableData` 只读两张表供 F3 展示。

### UI 选择与兵牌（`Assets/Script/UI/`，2026-11-16）

- 选择：`SelectionService` 是唯一真相源；`SelectionInputReader` 左键框选/点选、`Esc` 清空（右键留给 `PlayerCommandController` 下命令）；`SelectionHighlighter` 只追加 `SelectionOutline` 材质槽，不替换本体材质；`UnitSelectableBootstrap` 给运行时生成的单位补 `UnitSelectable`。
- `SelectionCommandBridge` 在 `UI_Test` 中双向同步 `SelectionService` ↔ `UnitSelection`，并关闭 `PlayerCommandController.LegacySelectionEnabled`（旧左键选择与 IMGUI 标记），右键命令/热键保留。没有它，点卡片后右键命令看不到选中单位。
- 面板：`RosterModel` 按阵营分栏、友军按 `SquadId` 合组；`RosterPanelView` 用单卡池 + 兵牌池渲染，`+X` 按组计数；`SquadCardView` 点击选中全队并展开成员浮层（成员行点击单选），右上剑/盾切换姿态，浮层底部「横队/纵队/方阵」按钮；`SquadRosterGrouping` 是无依赖纯逻辑，EditMode 覆盖。卡片背景与面板必须有 `raycastTarget`，否则点击穿透场景。
- `UI_Test`：`Tools/UI/Update Selection Test Scene In Place` 原地补 `UnitSelectable`、重建 UI、加 `SelectionCommandBridge`、确保 `DeveloperMode`；保留 4 战士、2 投射塔、刷怪器、巡逻点。开发者生成面板是 IMGUI，`SelectionInputReader` 必须查 `DeveloperSpawnController.BlocksWorldInput` 才能避免点面板误框选。

详细实现记录：[YC/Unit实现记录_2026_10_28.md](../../YC/Unit实现记录_2026_10_28.md)、[YC/ToDo_2026_10_29.md](../../YC/ToDo_2026_10_29.md)、[YC/单位状态机_2026_11_08.md](../../YC/单位状态机_2026_11_08.md)。设计依据：[YC/Research/README.md](../../YC/Research/README.md)。

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
17. **追逐/锁定指针互斥，玩家命令优先于个体 AI**：追击只写 `ChaseTarget` 并清 `LockTarget`，攻击只写 `LockTarget` 并清 `ChaseTarget`，待命/死亡都清空。`UnitCommander.HasOrder` 为真时 Brain 必须先执行命令，不得自行索敌；命令完成或目标死亡后命令自动清空并恢复个体行为。
18. **拦截默认关闭但代码保留**：`WarriorBrain.interceptEnabled` 与 `EnemyBrain.interceptEnabled` 默认 `false`；恢复旧拦截行为只改开关，不要删除 `UnitEvents` 事件链或测试。
19. **小队移动是“锚点跟随”，不是一次性发终点**：`Squad.MoveTo` 只设目标和起始锚点，`SquadBrain.Tick` 每帧调 `Squad.Advance(dt)` 推进锚点并重算槽位。成员命令必须带 `follow=true`，到点不清命令；完成由 `Advance` 在“锚点到达 + 全员进 `settleRadius`”时统一触发。槽位身份用 `UnitCommander.SlotIndex`，不要在成员增删时按列表下标重排。NavMesh 吸附失败时不要硬推：槽位退到锚点（锚点本身不在 NavMesh 时保留原槽位）。
20. **姿态与集结点是 Squad 级权威状态**：友军默认 `Garrison`、敌方规则默认 `Alert`；基础感知 3、戒备 6、驻守 3。驻守把成员拴绳锚点设为 `RallyPoint`、半径 5m；戒备在目标丢失后原地停留。集结点建队取质心、整队移动完成自动更新为落点；`SetRallyPoint` 为手动 API。不要在 `UnitDefinition` 上写运行时姿态。
21. **UI 选择必须同步到 UnitSelection**：`UI_Test` 里 `SelectionCommandBridge` 双向同步 `SelectionService` ↔ `UnitSelection`，并关闭 `PlayerCommandController.LegacySelectionEnabled`；`SelectionInputReader` 右键不清选择（右键用于下命令），只有 Esc 清空。兵牌/单卡必须有可命中的 `raycastTarget` Graphic，面板空白处加透明命中层，否则点击会穿透到场景。

## 5. 资产关系（概览）

- `MVP.unity` 引用单位预制体、`UnitRoute`、`UnitData` 模板与材质；`Home` 由 `UnitDemoSetup` 挂 `UnitHealth`。
- `RoadWorkflow_Test.unity` 引用 `TDRoadNetwork` 与生成的路面网格/材质。
- `Enemy_Normal.prefab` / `Tower_Arrow.prefab` 引用对应 `UnitDefinition` 与运行时脚本。
- `Enemy_Normal_Nav.prefab` / `Warrior_Guard.prefab` / `Tower_Arrow_Projectile.prefab` / `Citizen.prefab` / `Ruin_Tower.prefab` 带 `HealthBar`；`Arrow_Projectile.prefab` 无碰撞体、Ignore Raycast 层，挂 `Projectile`（20 / 1 / 5s）。
- `Citizen.prefab` 引用 `UnitData/Citizen.asset`（30/2/2/2/1.5/感知2/生产力100）与 `Citizen_Mat`；`Ruin_Tower.prefab` 引用 `Tower_Arrow.asset` 与 `Ruin_Mat`/箭塔材质，挂 `UnitBuilding`/`BuildProgressBar`。
- `RoadWorkflow_Test.unity` 另有 `AI_Citizen_0/1` 与 `DeveloperMode`（`DeveloperModeController`）；场景由 `Tools ▸ Unit ▸ Create Building Test Scene` 重建。
- 菜单会重新生成这些预制体并保路径覆盖（保 GUID）；手改预制体后重跑菜单会被覆盖。
- 详细引用关系见生成的 `.agent/indexes/assets.json`（不提交，运行 `index` 后生成）。
