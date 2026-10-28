# 项目事实（已核实）

> 核实日期：2026-10-28。环境变化后请更新本文件，不要保留互相矛盾的版本。

## 仓库

- 仓库根（本机路径，其他机器不同）：`D:/workspace/game/Vindicator`；远端 `https://github.com/Drakira-ash/Vindicator.git`；主分支 `main`。
- Bootstrap 时分支：`feature/npc-ai`，HEAD `daca2ed`，相对 `origin/main` ahead 4 / behind 0。
- 工作树存在用户未提交修改：`Assets/Scenes/MVP.unity`（+4 行，`UnitDemo_Enemy` 预制体实例的 `groundMask.m_Bits = 51`）。**必须保留，不要回退。**
- Git 跟踪文件约 166 个；`.claude/` 被忽略；`YC/` 被跟踪。
- Bootstrap 前没有根级 `AGENTS.md` / `CLAUDE.md` / `.agent/`。

## 引擎与包

- Unity `6000.0.39f1`（`ProjectSettings/ProjectVersion.txt`）。
- 编辑器 `ProductVersion = 6000.0.39f1_15ea7ed0b100`。**安装路径是本机信息**，写在 `.agent/.local/env.json` 的 `unity_editor`，不进 `project.json`。
- 渲染管线：URP `17.0.3`。
- 直接包依赖 47 个（`Packages/manifest.json`）。关键项：Input System `1.13.0`、AI Navigation `2.0.6`、Splines `2.7.2`、ProBuilder `6.0.9`、Timeline `1.8.7`、Test Framework `1.4.6`、Unity MCP（Git URL，lock hash `81542a7e...`）。
- Unity MCP 的**可用性按机器/会话而异**：2026-10-28 在本机核实 `mcp__UnityMCP__*` 可调用。实例名与编辑器 PID 是运行时信息，用 `mcpforunity://instances` 现场确认，不写入共享记忆。

## 代码与资产

- `Assets/Script` 下 C#：Unit 系统 15 个文件（含 `Editor/UnitDemoSetup.cs`），TDWorkflow 9 个文件（含 5 个 Editor 脚本）；另有 TutorialInfo 模板脚本等。静态索引统计：25 个 `.cs`、33 个类型、15 个 Behaviour、7 个 Editor 脚本。
- 资产：51 个非 `.meta` 文件、74 个项目 GUID；`Assets/` 下 `.meta` 配对完整（0 缺失 / 0 孤立）；场景/预制体中未发现缺失脚本 GUID（借助 `Library/PackageCache` 解析包 GUID）。
- 验收场景：`Assets/Scenes/MVP.unity`。
- 路网工具测试场景：`Assets/Scenes/RoadWorkflow_Test.unity`（含 `TDRoadNetwork` 图数据；MVP 的旧 PolyShape 路没有图数据）。
- 单位模板：`Assets/UnitData/Enemy_Normal.asset`、`Tower_Arrow.asset`；预制体：`Assets/Prefabs/Unit/Enemy_Normal.prefab`、`Tower_Arrow.prefab`。
- 项目运行代码无 `.asmdef`；程序集为 `Assembly-CSharp` / `Assembly-CSharp-Editor`。测试程序集为 `Vindicator.EditModeTests`（`Assets/Tests/EditMode/Vindicator.EditModeTests.asmdef`，Editor-only）。
- 测试资产：`Assets/Tests/EditMode/ProjectSmokeTests.cs` 5 个用例（单位模板、预制体、MVP 场景存在性），只依赖 `AssetDatabase`，不引用 `Assembly-CSharp`。
- `Assets/Editor/HarnessUnityVerify.cs`：只读编译验证脚本，菜单 `Tools/Harness/Verify Compilation`，输出 `.agent/.local/unity-verify-editor.json`。
- `Assets/Editor/HarnessUnityTest.cs`：编辑器内一键 EditMode 测试脚本，菜单 `Tools/Harness/Run EditMode Tests`，输出 `.agent/.local/unity-test-editor.json`。
- 编译验证证据：2026-10-28T17:21:47Z 强制全量重编译 pass，0 error / 0 warning，耗时 60.61s（含 `Vindicator.EditModeTests`）；签名 `sha256:b5246d9e...`（33 个编译输入）。
- EditMode 测试证据：2026-10-28T17:19:13Z pass，5/5，2.89s。

## 工具链

- `python` = Python 3.9.21（`/c/deeplearning/python`）；`python3` 也可用。
- `jsonschema` 4.23.0 已安装（harness 用它校验元数据）。
- Node `v22.17.0`；dotnet `7.0.304`（本 bootstrap 未用于构建）。
- 未发现根级 `README` 或定时任务配置。本项目不做 CI（见 [decisions/0005-no-ci-local-verification.md](decisions/0005-no-ci-local-verification.md)）。

## 索引基线（bootstrap 最终）

`source_files=166`（含 `.meta`）、`files=96`、`assets=51`、`code_files=25`、`types=33`、`packages=47`、`unused_candidates=5`。签名与计数会随工作树变化，以 `.agent/indexes/manifest.json` 为准。
