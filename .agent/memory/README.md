# 项目持久记忆

本目录保存**跨会话**有价值的项目知识。与 `~/.claude` 的工具级记忆不同，这里的记录随仓库共享、可审查、可校验。

## 分类

| 文件 | 内容 |
|---|---|
| [facts.md](facts.md) | 已核实的项目/环境事实（含核实日期与证据）。 |
| [architecture.md](architecture.md) | 模块职责、数据流、重要接口、不变量。 |
| [operational.md](operational.md) | 已验证命令、环境要求、已知失败模式与限制。 |
| [lessons.md](lessons.md) | 可复用的踩坑与经验（含适用条件与来源）。 |
| [decisions/](decisions/) | 架构决策记录（ADR）：决定、理由、替代方案、后果。 |

## 记录规则

1. 区分**已核实事实**与**假设/提案**；假设必须显式标注。
2. 记录来源：文件路径、命令输出、提交号、日期。
3. 信息过期时**更新或标记取代**，不要堆叠互相矛盾的指令。
4. 不复制会话全文；只保留能实质改善未来工作的内容。
5. 不写入密钥、token、凭据或无关个人信息。
6. 与 `YC/`、`YXZ/` 的详细设计文档互相链接，不重复长篇内容。

## 当前决策

- [0001 harness 位置与提交策略](decisions/0001-harness-location-and-scope.md)
- [0002 先做静态索引，不依赖 Unity](decisions/0002-static-indexing-first.md)
- [0003 启动时对账，无常驻守护进程](decisions/0003-startup-reconciliation-without-daemon.md)
- [0004 Unity 编译验证：双路径 + 编译输入签名](decisions/0004-unity-compile-verification.md)
- [0005 不接 CI；验证只在本地编辑器内完成](decisions/0005-no-ci-local-verification.md)
- [0006 `.agent/` 共享知识与本机状态分离](decisions/0006-shared-vs-local-state.md)
- [0007 NavMesh AI、移动抽象与事件式拦截](decisions/0007-navmesh-ai-combat.md)
