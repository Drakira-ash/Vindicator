# 0001 — Harness 放在 `.agent/`，元数据跟踪、生成索引不跟踪

- 日期：2026-10-28
- 状态：接受
- 影响组件：仓库结构、协作流程

## 背景

项目已有 `YC/`（跟踪的项目笔记）与 `YXZ/`（另一成员的文档），但没有跨会话的机器可读状态；`.claude/` 被 `.gitignore` 忽略，不能作为共享持久层。需要一套与工具无关、可审查、可重建的项目记忆与检查机制。

## 决定

- 在仓库根创建 `.agent/`，并**纳入 Git**：`project.json`、`state.json`、`memory/`、`tasks/`、`schemas/`、`scripts/`、`events/`、`reports/`。
- **不跟踪**可重建的详细索引（`.agent/indexes/*.json`）与本地运行时状态（`.agent/.local/`），由 `.agent/.gitignore` 排除。
- 根级 `AGENTS.md` 作为会话入口；`CLAUDE.md` 仅 `@AGENTS.md` 导入，避免两套指令漂移。
- `YC/`、`YXZ/` 保持原用途，harness 只链接、不复制长篇设计文档。

## 替代方案

1. 放在 `.claude/`：已被忽略、绑定特定工具，不能满足共享与跨工具目标。
2. 全部写进 `YC/`：适合人类笔记，但缺少 schema、状态机、可执行检查，且易与设计文档混杂。
3. 外部 wiki/数据库：需要网络与权限，不能离线、不能随分支审查。

## 后果

- Harness 变更会出现在 diff 中，可审查、可回滚。
- 索引 churn 被隔离；新克隆需运行 `session-start` 重建索引（设计如此）。
- `state.json` / `events.jsonl` 仍可能产生小冲突；事件为追加式，冲突通常可逐行合并。
- 若未来多人并行频繁写 state，可改为“任务文件拆分 + 事件合并”，届时重审本决策。
