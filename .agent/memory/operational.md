# 操作知识与环境

## 已验证的 harness 命令（2026-10-28）

| 命令 | 结果 |
|---|---|
| `python .agent/scripts/agent.py index` | 通过。首次生成约 1.6s；`source_files=164, files=94, assets=51, code_files=25, types=33, packages=47`。 |
| `python .agent/scripts/agent.py validate` | 通过（无 error）。首次运行有 3 个 warning（当时行为：缺 `tasks/handoff.json`、两个 backlog 来源指向尚未生成的 bootstrap 报告）；现在缺本机交接记录记为 `skip`，不再算 warning。 |
| `python .agent/scripts/agent.py status` | 通过；能报告 Git、索引过期/新鲜、watch 状态、任务、下一步、上次校验。 |
| `python .agent/scripts/agent.py session-start` | 通过。冷启动（删索引与 `.local`）5.7s；暖启动 1.49s；最终 0 error / 0 warning。 |
| `python .agent/scripts/agent.py validate --strict` | 通过；19/19 检查 PASS，0 error / 0 warning。 |
| `python .agent/scripts/agent.py checkpoint ...` | 通过；写入 handoff/state，校验 0 warning。 |
| `python .agent/scripts/agent.py watch`（连续/单次） | 通过；两种模式都能检测变化并标记 stale；测试后无残留进程。 |
| `python .agent/scripts/agent.py hooks install/status/uninstall` | 通过；managed hook 可识别/卸载，当前未安装。 |
| `python .agent/scripts/agent.py unity-verify --adopt-editor` | 通过（exit 0）。采纳编辑器证据；编译签名 30 文件，无漂移。 |
| `python .agent/scripts/agent.py unity-verify --status` | 通过（exit 0）；pass / fresh。 |
| `python .agent/scripts/agent.py unity-verify`（编辑器占锁） | 通过拒绝路径（exit 3）；未启动 Unity 进程。端到端批处理尚未运行。 |
| 菜单 `Tools/Harness/Run EditMode Tests` + `unity-test --adopt-editor` | 通过（exit 0）。5/5，2.89s；编辑器内运行，不关闭 Unity。 |

校验覆盖：JSON Schema、`project.json` 路径、索引新鲜度、任务引用与来源、Markdown 本地链接、`.agent/` 是否被忽略、本机状态文件是否被忽略（`git-ignore-local`）、Unity `.meta` 配对、GUID 唯一性、场景/预制体缺失脚本、重复内容资产、state 与 Git 一致性、常见敏感文件。

## 共享 vs 本机（多人协作）

- **共享（提交）**：`memory/`、`tasks/backlog.json`、`project.json`、`scripts/`、`schemas/`、`reports/`。
- **本机（`.agent/.gitignore` 忽略）**：`state.json`、`tasks/active.json`、`tasks/handoff.json`、`events/`、`indexes/*.json`、`.local/`。
- 本机文件缺失是**正常状态**（新克隆/换机器）：`validate` 对缺失的 state/active/handoff 记 `skip` 而非 error；`session-start`/`checkpoint` 会用默认值创建。
- 跨机交接靠 `tasks/backlog.json` + `git log`；`state.json` 只反映本机上次会话。
- 本机 Unity 安装路径写在 `.agent/.local/env.json`：`{"unity_editor": "D:/.../Unity.exe"}`；`editor_path()` 优先读它，其次回退旧版 `project.json` 的 `engine.editor`。
- 设计依据见 [decisions/0006-shared-vs-local-state.md](decisions/0006-shared-vs-local-state.md)。

## 索引策略

- 详细索引写在 `.agent/indexes/*.json`，**被 `.agent/.gitignore` 忽略**；可随时重建，避免多人/频繁场景改动造成 churn。
- `manifest.json` 保存内容签名；`status` / `session-start` 比较签名判断过期。
- `.agent/` 本身不参与签名，所以写 state/event 不会让索引自失效。
- 索引“无引用候选”是**低置信提示**，可能因动态加载/反射/运行时引用而误报；不得据此删除资产。

## 新鲜克隆 / 无 Unity 导入时

- 静态索引无需 Unity 即可运行。
- 缺少 `Library/PackageCache` 时，缺失脚本检查会跳过（`missing-scripts: skip`）；需要先在 Unity 打开项目完成导入，才能完整解析包 GUID。
- 首次 `validate` 会扫描包缓存并写 `.agent/.local/package-guid-cache.json`；之后走缓存（key = `packages-lock.json` 哈希）。

## 实时感知

- 默认**没有**常驻进程。`watch` 是前台命令；`hooks install` 是显式可选安装，只写本机 `.git/hooks`，不随仓库分发。
- 已实现但未启用：`watch`、Git `post-commit` / `post-checkout` 钩子。
- 没有接入 Windows 任务计划或其他调度器。
- 不要声称“正在监控”；只能说“下次 session-start 会对账”或“watch 已启动（前台）”。

## Unity 编译验证

两条路径，共用同一份规范记录 `.agent/.local/unity-verify.json`：

1. **编辑器证据（已端到端验证）**
   - 编辑器内菜单 `Tools/Harness/Verify Compilation` → `Assets/Editor/HarnessUnityVerify.cs` → 写 `.agent/.local/unity-verify-editor.json`。
   - 然后 `python .agent/scripts/agent.py unity-verify --adopt-editor`：校验 `kind`、结果，计算 mtime 漂移，盖章编译签名与 Git，写规范记录与 `unity.verify` 事件。
   - `compiled_at` 必须晚于所有编译输入 mtime，否则标记 drift（exit 1）。
2. **批处理**
   - `python .agent/scripts/agent.py unity-verify` 调 `Unity.exe -batchmode -nographics -quit -projectPath ... -logFile .agent/.local/unity-verify.log`。
   - 默认超时 900s（`--timeout`）；解析日志中的 `error CS####` / 编译失败标记。
   - 编辑器占锁时**拒绝运行**（exit 3），不会杀进程、不会读 `os.kill`。
   - 端到端**尚未运行**：需要先关闭 Unity 编辑器。关闭前应先征得用户同意。

退出码：0 pass；1 fail/stale；2 配置错误/缺证据；3 编辑器占锁；4 超时。

- 锁检测：读 `Library/EditorInstance.json` 的 `process_id`，用 `tasklist /FI "PID eq N"` 判断存活；`Temp/UnityLockfile` 存在且进程未知时保守视为占用。
- 新鲜度只看 `compile_signature`（`.cs`/`.asmdef`/`.rsp`、`Packages/manifest.json`、`packages-lock.json`、`csc.rsp`、`ProjectSettings/ProjectVersion.txt`、`ProjectSettings/ProjectSettings.asset`）。改文档/场景不会让编译证据过期。
- `validate` 集成：缺记录→skip；有未采纳编辑器证据→warning+skip；fail/error/timeout→error；stale→warning；pass 且新鲜→pass。
- **`assemblies_compiled` 可能为空，不代表没编译。** 强制重编译时编译器事件报 `not required` 也很常见；以 `EditorUtility.scriptCompilationFailed`、编译消息与 `Editor.log` 为准。

## EditMode 测试（本地一键）

- Unity 菜单 `Tools/Harness/Run EditMode Tests` → `TestRunnerApi` 在编辑器内运行，不需要关闭 Unity、不进入 PlayMode。
- 原始证据 `.agent/.local/unity-test-editor.json`；`agent.py unity-test --adopt-editor` 盖章为 `.agent/.local/unity-test.json`（exit 0 仅当 pass 且无漂移）；`--status` 只读，0 pass/fresh、1 fail/empty/stale、2 无记录。
- 测试程序集 `Assets/Tests/EditMode/Vindicator.EditModeTests.asmdef`（Editor-only、`UNITY_INCLUDE_TESTS`）。**asmdef 程序集不能引用预定义程序集**，所以测试不能直接 `using` 项目类型（`StatBlock` 等）；目前用 `AssetDatabase` 做不依赖项目类型的冒烟测试。要测项目类型，需先把运行时代码拆进 asmdef（未做，改动面大）。
- 域重载会中断测试运行；脚本检测到残留 pending 文件时只删除并警告，不伪造结果。
- **测试结果新鲜度同样用 `compile_signature`**：改 `.cs`/`.asmdef` 后测试记录变 stale，需重跑。

## PlayMode 验收

- 无自动化测试资产；PlayMode 验收需人工或 MCP。
- 编辑器窗口失焦时游戏循环会暂停；后台观察前设置 `Application.runInBackground = true`。首帧读取可能发生在 `Awake` 前，需等初始化完成再采样。
- 已有验收记录使用 `Time.timeScale=10`、控制台观察 `[Unit]` / `[AI]` 日志；结果见 [YC/ToDo_2026_10_28.md](../../YC/ToDo_2026_10_28.md)。
- Unity MCP 的可用性按机器/会话而异：先确认本机编辑器已启动并连接（`mcpforunity://instances`），再使用 `mcp__UnityMCP__*`。

## 常见失败模式

| 现象 | 处理 |
|---|---|
| `索引过期` | `python .agent/scripts/agent.py index`；或直接 `session-start`。 |
| `lock timeout: .agent/.local/index.lock` | 另一个 index 进程未结束；确认后等待/清理（stale 10 分钟自动接管）。 |
| `缺少 .meta` / 孤立 `.meta` | 回到 Unity 让它重新导入，或人工恢复配对；不要随意删 GUID。 |
| `缺失脚本 GUID` | 脚本被删/移动或包缺失；先检查 `.meta` 与包缓存。 |
| `state 与 Git 不一致` | 正常（HEAD 已前进）；`session-start` 会同步。 |
| 事件追加失败 | 检查 `.agent/events/` 权限；失败不会中断主流程。 |
| Python 3.9 兼容 | 避免 3.10+ API（例如 `Path.write_text(..., newline=)` 在 3.9 不存在）。 |

## 环境事实

- Python 3.9.21、jsonschema 4.23.0、Node 22.17.0、dotnet 7.0.304。
- Git Bash（win32）环境；路径使用正斜杠；Git 状态输出用 `-c core.quotepath=false` 以正确显示中文文件名。
- Unity 引擎安装路径是本机信息，写在 `.agent/.local/env.json` 的 `unity_editor`；共享文档中只记录版本（6000.0.39f1）。
