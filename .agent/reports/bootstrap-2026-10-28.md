# Harness Bootstrap 报告 — 2026-10-28

## 结论

已在现有 Unity 仓库上建立项目级、可审查、可重建的 agent harness。核心能力（会话启动对账、静态索引、校验、交接、审计事件）已实际运行验证；Unity 编译/PlayMode 与常驻后台监控**未实现**，已在文档与状态中明确标注，不声称已具备。

**用户未提交工作已保留**：`Assets/Scenes/MVP.unity` 的 4 行改动（`UnitDemo_Enemy` 的 `groundMask.m_Bits = 51`）未修改、未回退。除新增 `AGENTS.md`、`CLAUDE.md`、`.agent/` 及临时自测文件外，未改动项目资产/脚本/场景。

## 发现的环境

- Unity `6000.0.39f1`（VersionInfo 核实）。编辑器安装路径属本机信息，现由 `.agent/.local/env.json` 提供，不再写入共享文件。
- URP `17.0.3`；47 个直接包依赖；Test Framework 已装但**无测试资产/asmdef**。
- C#：25 个文件、33 个类型；Unit 系统 + TDWorkflow + TutorialInfo。
- 资产：51 个非 `.meta` 文件、74 个项目 GUID；`.meta` 配对完整；无缺失脚本 GUID（借助 `Library/PackageCache` 解析）。
- 分支 `feature/npc-ai`，HEAD `daca2ed`，相对 `origin/main` ahead 4；`MVP.unity` 有未提交改动。
- 工具：Python 3.9.21、jsonschema 4.23.0、Node 22.17.0、dotnet 7.0.304。
- Unity MCP 包已安装，但本次会话没有 `mcp__UnityMCP__*` 工具，连接状态未确认。

## 创建的产物

| 路径 | 作用 | 跟踪 |
|---|---|---|
| `AGENTS.md` | 会话入口：启动/命令/规则/限制/安全边界 | 是 |
| `CLAUDE.md` | 仅 `@AGENTS.md` 导入，避免双重指令 | 是 |
| `.agent/project.json` | 项目身份、路径、命令、验证状态 | 是 |
| `.agent/state.json` | 分支/提交/工作树、当前任务、下一步、索引状态 | 是 |
| `.agent/memory/` | 事实、架构、操作、经验 + 3 个 ADR | 是 |
| `.agent/tasks/backlog.json` | 7 项候选任务（含验收标准/来源/阻塞） | 是 |
| `.agent/tasks/active.json` | 当前任务（当前为空） | 是 |
| `.agent/tasks/handoff.json` | 会话交接（由 checkpoint 生成） | 是 |
| `.agent/events/events.jsonl` | 追加式审计事件 | 是 |
| `.agent/schemas/` | 7 个 JSON Schema | 是 |
| `.agent/scripts/agent.py` | 索引/启动/校验/交接/watch/hooks CLI | 是 |
| `.agent/indexes/*.json` | 可重建详细索引 | **否**（`.agent/.gitignore`） |
| `.agent/.local/` | 运行时心跳、校验输出、包 GUID 缓存 | **否** |
| `.agent/reports/` | 本报告 | 是 |

## 实际执行的命令与结果

| 命令 | 结果 |
|---|---|
| 仓库/工具链探查（`git status/log`、目录、包清单、日志、编辑器 VersionInfo） | 完成；见上文。 |
| `python -m py_compile .agent/scripts/agent.py` | 通过。 |
| `python .agent/scripts/agent.py index` | 通过；首次约 1.6s；`source_files=166, files=96, assets=51, code_files=25, types=33, packages=47, unused_candidates=5`。 |
| `python .agent/scripts/agent.py validate` | 通过（无 error）。首次 3 个预期 warning：缺 handoff、两个 backlog 来源指向当时未生成的报告。 |
| `python .agent/scripts/agent.py status` | 通过；能报告 Git、索引、watch、任务、下一步、上次校验。 |
| `python .agent/scripts/agent.py session-start`（冷启动） | 删除索引与 `.local` 后重建成功；优化后 **5.7s**（优化前 59.6s，原因是逐文件 `Path.resolve()` 扫描 13k 包 meta）。 |
| `watch`（连续模式） | 创建临时文件后检测到变化，写入 `stale=true`、`reason=watch: source changed`；进程终止后清理临时文件与心跳。 |
| `watch --once` | 基线记录、变化检测两条路径均验证；已修复“首次轮询即退出”的缺陷。 |
| `hooks install` / `status` / `uninstall` | managed hooks 可安装、识别、卸载；测试后已卸载，未留下 hook。 |
| 索引确定性测试 | 连续两次 index，去除 `generated_at` 后 4 个 payload 的 canonical SHA-256 完全一致：`true`。 |
| 过期检测测试 | 临时改动后 `status` 报“过期”；删除临时文件后恢复“新鲜”。 |
| 新鲜会话模拟 | 删除 `.agent/indexes/*.json` 与 `.agent/.local/` 后 `session-start` 自动重建并恢复上下文。 |
| Unity 批处理编译 | **未运行**（避免占锁/未经确认）；命令模板记录在 `project.json`。 |
| PlayMode 验收 / Unity MCP 操作 | **未运行**；需人工或已连接 MCP。 |

## 校验覆盖

JSON Schema；`project.json` 路径；索引新鲜度；任务 id/验收标准/来源；Markdown 本地链接；`.agent/` 是否被忽略；Unity `.meta` 配对；GUID 唯一性；场景/预制体缺失脚本；重复内容资产；state 与 Git 一致性；常见敏感文件。

## 已知限制

1. **索引是启发式静态解析**：不是编译器，不保证捕获编译错误、序列化错误、运行时引用。
2. **无 Unity 编译/自动化测试**：见 backlog `harness-unity-verify`（编译验证已于后续实现）。
3. **无常驻监控/调度器**：默认只有 `session-start` 对账；`watch` 需手动前台启动；Git hooks 需显式安装且只影响本机。
4. **`unused_candidates=5` 为低置信提示**（4 个生成材质 + Readme），不得据此删除。
5. **127 个未解析 GUID** 来自包/内置资源；缺失脚本检查已用 `Library/PackageCache` 的 `.cs.meta` 解析，结果为 0 缺失。无 `Library/` 时该检查跳过，不误报。
6. **冷启动需扫描包缓存**（约 3s）；首次无 `Library/` 的新克隆会跳过缺失脚本检查，需先在 Unity 导入。
7. **审计事件含自测记录**：watch 检测、hooks 安装/卸载、预期失败的校验事件均保留，属于真实工具活动。
8. **索引文件不提交**：新克隆首次需运行 `session-start`；这是为避免场景频繁改动造成 churn/冲突。

## 验收清单

- [x] 检查现有项目与环境。
- [x] 根级指令与 harness 入口存在（`AGENTS.md`、`CLAUDE.md`）。
- [x] 持久知识有明确结构（facts/architecture/operational/lessons/decisions）。
- [x] 初始文件/资产/代码/依赖索引已生成（限制已记录）。
- [x] 启动对账可执行（冷启动模拟通过）。
- [x] 索引可重复生成且不损坏项目（确定性测试通过；原子写 + 锁）。
- [x] 状态命令报告真实可核实信息。
- [x] 发现构建/测试命令；安全的静态检查已运行；Unity 编译未运行且已记录。
- [x] 实际结果与未解决失败已记录。
- [x] 后台感知已实现并测试（watch / hooks）；常驻缺失已明确说明，测试后未留下运行进程或 hook。
- [x] 新会话可在无原对话情况下恢复上下文（模拟验证）。
- [x] 未静默丢弃现有工作（MVP.unity 改动保留；自测文件已清理）。

## 建议下一步

1. 与用户确认功能优先级：小改动 `unit-face-target`，或先补 `harness-unity-verify`。
2. 决定是否启用 `watch` / Git hooks（backlog `harness-live-awareness`）。
3. 获得许可后，用 Unity 批处理验证一次编译，并把结果写回 `project.json.verification`。
4. 用户明确要求时再提交 harness 变更；不要自动提交。

## 最终校验记录（2026-10-28）

- `python .agent/scripts/agent.py validate --strict`：19/19 检查 PASS，**0 error / 0 warning**，exit 0。
- `checkpoint`：交接写入成功，校验 0 error / 0 warning，exit 0。
- 暖启动 `session-start`：**1.49s**，索引新鲜，校验 0/0，exit 0。
- 冷启动模拟（删除索引与 `.local`）：**5.7s**，自动重建并恢复任务上下文（当时仅剩报告/handoff 未生成的预期错误）。
- `git status` 仅显示：`M Assets/Scenes/MVP.unity`、`?? .agent/`、`?? AGENTS.md`、`?? CLAUDE.md`；`indexes/*.json` 与 `.local/` 已验证被忽略。
- 状态复核：hooks 未安装、watch 未运行、无残留监听进程。
- 用户改动 `MVP.unity` 自始至终未修改。
