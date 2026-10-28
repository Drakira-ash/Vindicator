# 架构与不变量

> 依据：项目文档 + 代码符号核对，2026-10-28。实现细节以源码与 `YC/`、`YXZ/` 文档为准；两者冲突时先核对代码。

## 1. 单位系统（`Assets/Script/Unit/`）

| 模块 | 职责 |
|---|---|
| `Unit` | 单位根组件（`RequireComponent(UnitHealth)`）；`Awake` 初始化属性/能力，`Update` 调度能力，`ActivateAbilities` 统一授权。 |
| `UnitDefinition` | ScriptableObject 模板：名称、阵营、血量、攻击、移速、攻击间隔、射程、溅射、能否移动/攻击。 |
| `StatBlock` | 运行时可变属性容器，按 `StatType` 存取，支持修正值；**运行时不改 SO 资产**。 |
| `UnitHealth` | `IDamageable`；受伤/死亡事件、延迟销毁、注册进 `UnitRegistry`。 |
| `UnitAbility` / `AttackAbility` | 能力基类与攻击能力；冷却、有效射程、可选溅射。新增能力继承基类即可被 `Unit` 调度。 |
| `UnitMover` | 沿 `UnitRoute` 路点移动；路点只给水平走向，高度由脚下 `SphereCast` 探测决定。 |
| `EnemyBrain` | 每 0.25s 索敌；目标进入射程则暂停移动并攻击；目标死亡/丢失后恢复。 |
| `UnitTurretBrain` | 塔 AI：原地索敌与开火；是否可攻击由 `Unit.abilitiesEnabled` 控制。 |
| `UnitRegistry` | 静态注册表；`FindNearestHostile` 线性搜索。 |
| `UnitRoute` | 路点序列（Transform 列表）。 |
| `UnitTeam`, `IDamageable` | 阵营枚举/工具与伤害接口。 |
| `Editor/UnitDemoSetup.cs` | 菜单 `Tools ▸ Unit ▸ Create Unit Test Scene`，重建模板/材质/预制体/22 点路线/塔/敌人/Home。 |

数据流：`UnitDefinition` → `Unit.Awake` 写入 `StatBlock` 与血量 → `EnemyBrain` 索敌 → `Unit.ActivateAbilities` → `AttackAbility` 造成伤害 → `UnitHealth` 事件/死亡 → 注册表注销 → Brain 恢复移动或转火。

详细实现记录：[YC/Unit实现记录_2026_10_28.md](../../YC/Unit实现记录_2026_10_28.md)。设计依据：[YC/Research/README.md](../../YC/Research/README.md)。

## 2. 路网系统（`Assets/Script/TDWorkflow/`）

| 模块 | 职责 |
|---|---|
| `TDRoadNetwork` | 路网数据（节点 + 无向边）；BFS `TryGetNodePath`、`FindNearestNode`、端点查询。 |
| `TDRoadMeshBuilder` | 纯几何：边 → 贝塞尔中心线 → 带宽度网格；处理弯角与路口补片。与路面使用同一条中心线。 |
| `TDRoadSplineAuthoring` | 样条创作辅助。 |
| `Editor/*` | 场景视图画路/拖拽/吸附/删除、参数面板、旧 PolyShape 路导入。 |

生成结果：`Assets/TDWorkflow/Generated/RoadWorkflow_Test_Road.asset`（网格资产）及若干材质。完整操作说明：[YXZ/道路编辑工作流.md](../../YXZ/道路编辑工作流.md)。

**尚未桥接**：单位仍走手写 `UnitRoute` 路点，不沿 `TDRoadNetwork` 寻路；MVP 场景没有路网图数据。

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
7. **MVP 是验收场景，RoadWorkflow_Test 是路网工具场景**：跨场景改动前先确认；路网文档明确当时未改 MVP。
8. **`.meta`/GUID 完整性**：移动或新增资产必须保留配对；场景/预制体的脚本引用依赖 GUID。
9. **harness 共享与本机分离**：`state.json`、`tasks/{active,handoff}.json`、`events/` 是本机状态，可能不存在；跨机以 `backlog.json` + `git log` 为准。本机 Unity 路径放 `.agent/.local/env.json`，不要写回 `project.json`。

## 5. 资产关系（概览）

- `MVP.unity` 引用单位预制体、`UnitRoute`、`UnitData` 模板与材质；`Home` 由 `UnitDemoSetup` 挂 `UnitHealth`。
- `RoadWorkflow_Test.unity` 引用 `TDRoadNetwork` 与生成的路面网格/材质。
- `Enemy_Normal.prefab` / `Tower_Arrow.prefab` 引用对应 `UnitDefinition` 与运行时脚本。
- 详细引用关系见生成的 `.agent/indexes/assets.json`（不提交，运行 `index` 后生成）。
