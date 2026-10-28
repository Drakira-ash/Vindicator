# 0006 — `.agent/` 共享知识与本机状态分离

- 日期：2026-10-29
- 状态：接受
- 影响组件：`.agent/.gitignore`、`agent.py`（state/active/handoff/事件/编辑器路径）、`AGENTS.md`、记忆文档

## 背景

`.agent/` 要随仓库给多个开发者共用，但其中一部分文件每次会话都会变化：

- `state.json`、`tasks/active.json`、`tasks/handoff.json`、`events/events.jsonl` 记录的是**某一台机器上的上一次会话**；
- `indexes/*.json`、`.local/` 是生成物/本机证据；
- `project.json` 里的 `engine.editor` 是某台机器的 Unity 安装路径。

如果全部提交，两人会不断产生无关冲突，且会把“我的下一步”误当成项目事实。

## 决定

**共享（提交）**：`memory/`、`tasks/backlog.json`、`project.json`、`scripts/`、`schemas/`、`reports/`。

**本机（`.agent/.gitignore` 忽略）**：`state.json`、`tasks/active.json`、`tasks/handoff.json`、`events/`、`indexes/*.json`、`.local/`。

配套行为：

- 缺失本机文件是正常状态。`validate` 对缺失的 state/active/handoff 记 `skip`；`session-start`/`checkpoint` 自动用默认值创建。
- `validate` 新增 `git-ignore-local` 检查，防止本机状态文件被误提交。
- 跨机交接以 `tasks/backlog.json`（任务与验收标准）+ `git log`（实际改动）为准。
- Unity 安装路径移到 `.agent/.local/env.json` 的 `unity_editor`；`project.json` 只保留与机器无关的版本/管线信息。

## 替代方案

1. **全部提交**：无需改代码，但每台机器的会话状态互相覆盖，合并冲突频繁。
2. **全部不提交**：不会冲突，但 `backlog.json`、`memory/` 这类共享知识也无法同步。
3. **用 Git LFS / 单独分支存状态**：复杂度远超收益。

## 后果

- 新克隆上 `state.json` 等不存在；agent 不得把它们当作“必须存在”的证据。
- 检查“上次会话结论”只能看本机文件；跨机信息来自 backlog 与提交历史。
- 每台新机器需在 `.agent/.local/env.json` 填 `unity_editor`，否则批处理编译与路径检查显示“找不到编辑器”。
- 若未来团队规模变大、需要集中式任务看板，重审本决策（可能需要外部系统而非仓库内 JSON）。
