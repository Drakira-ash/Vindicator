# 开发者模式：单位生成与销毁

## 验收标准

**A. 交互**

- [ ]【A1】按 `~` 进入/退出开发者模式；`F2` 保留为别名，非开发者模式下 `K`/`S`/`D` 均无效。
  > 原话：“按~进入开发者模式”
- [ ]【A2】开发者模式下按 `K` 销毁当前点选/框选中的单位，销毁后选择清空。
  > 原话：“按K对选中单位进行销毁”“支持对点选/框选按键销毁”
- [ ]【A3】按 `S` 在鼠标位置生成民兵；按 `D` 在鼠标位置生成普通敌人/劫匪。
  > 原话：“按S在鼠标位置生成民兵 按D在鼠标位置生成 普通敌人/劫匪”
- [ ]【A4】生成点优先吸附到 NavMesh（半径可配置）；吸附失败时使用鼠标射线命中点，不因不在网格上而拒绝生成。
  > 原话：“支持在任意位置放置单位”

**B. 面板**

- [ ]【B1】面板可展开/收回（标题栏按钮或 `F4`），仅在开发者模式开启时显示。
  > 原话：“一个可以展开/收回的UI面板”
- [ ]【B2】面板按我方 / 敌方分组列出可生成单位类型（民兵、普通敌人），并显示当前场上数量。
- [ ]【B3】选中类型后进入放置模式：鼠标位置显示单位预览，左键在任意位置放置，右键或 `Esc` 取消。
  > 原话：“支持进行敌方单位和我方单位的预览，在任意位置生成单位”
- [ ]【B4】面板列出场上所有存活单位（含敌方），每行可单独销毁；另提供“销毁选中 (K)”入口。
  > 原话：“销毁任意士兵与敌人”

**C. 不干扰玩法**

- [ ]【C1】鼠标位于开发者面板上时，左键不会开始框选、右键不会下发移动/攻击命令。
- [ ]【C2】放置模式下左键只放置单位，不触发框选；右键只取消放置。
- [ ]【C3】生成的单位正常注册到 `UnitRegistry`，阵营、模板与预制体一致（民兵 Player + `WarriorBrain`，敌人 Enemy + `EnemyBrain`）；预览幽灵不注册、不可被选中、无 Collider。
- [ ]【C4】销毁走正常死亡路径（`UnitHealth`），小队/选择/注册表能自行清理，不残留空引用。

**D. 场景与工程验证**

- [ ]【D1】测试场景验收：先销毁在场现存民兵，再通过销毁/生成操作让玩家方赢得本场袭击（PlayMode 手动或 API 驱动）。
- [ ]【D2】EditMode 测试覆盖：目录解析、生成阵营与组件、任意位置生成、`K` 销毁选中、销毁任意单位、面板展开/收回状态。
- [ ]【D3】Unity 编译 0 error / 0 warning；EditMode 全通过；`index` + `validate` 0 error。
- [ ]【D4】新增 `.cs` / `.asset` / 目录均有配对 `.meta`；`Assets/Scenes/MVP.unity` 用户未提交改动保留。

## 审核范围

- `Assets/Script/Unit/Developer/DeveloperSpawnCatalog.cs`
- `Assets/Script/Unit/Developer/DeveloperSpawnController.cs`
- `Assets/Script/Unit/Developer/DeveloperSpawnPanel.cs`
- `Assets/Script/Unit/DeveloperModeController.cs`（`~` 键）
- `Assets/Script/Unit/Player/PlayerCommandController.cs`（面板/放置模式屏蔽世界输入）
- `Assets/Resources/Developer/SpawnCatalog.asset`
- `Assets/Tests/EditMode/DeveloperSpawnTests.cs`
- 文档：`YC/公民与废墟设计_2026_11_10.md`（开发者模式章节）

不审：`ProjectSettings/*`、`Assets/Scenes/MVP.unity`、与本计划无关的既有改动。

## 需求来源

- 用户会话原话（2026-11-15，`新计划.txt`）：开发者模式单位销毁/生成、`~` 进入、`K`/`S`/`D` 热键、可展开面板、任意位置放置、销毁任意士兵与敌人
- `.agent/tasks/backlog.json` 的 `developer-unit-spawn`
