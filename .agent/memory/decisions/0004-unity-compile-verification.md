# 0004 — Unity 编译验证：双路径 + 编译输入签名

- 日期：2026-10-28
- 状态：接受
- 影响组件：`agent.py unity-verify`、`Assets/Editor/HarnessUnityVerify.cs`、`validate`、`.local/` 证据文件

## 背景

Harness 早期只做静态检查，无法证明 C# 真的能编译。Unity 编译有两条现实路径：

1. **批处理**（`-batchmode -nographics -quit`）：无需人工，但要求编辑器关闭；编辑器打开时项目锁被占用，强行运行会失败或卡住。
2. **编辑器内编译**：编辑器开着时唯一可行；因为编辑器掌握权威的编译结果（`EditorUtility.scriptCompilationFailed`、`CompilerMessage[]`），比外部解析日志更可靠。

两条路径覆盖不同状态：编辑器开着时只能走编辑器路径，编辑器关着时批处理可直接跑。本项目不做 CI，批处理仅作为“编辑器关闭时的补充验证”，不承载自动化流水线。

## 决定

- **两条路径，一份规范记录**（`.agent/.local/unity-verify.json`）。
  - 批处理路径：`agent.py unity-verify` 启动 Unity、解析 `-logFile`、写记录。
  - 编辑器路径：`HarnessUnityVerify` 强制全量重编译并写原始证据 `.agent/.local/unity-verify-editor.json`；`agent.py unity-verify --adopt-editor` 校验并盖章为规范记录。
- **C# 只写原始证据，Python 负责盖章。** 签名、Git、漂移判断只在 Python 侧实现，避免两套逻辑分叉。
- **新鲜度用窄签名 `compile_signature`**：仅 `.cs` / `.asmdef` / `.rsp`、`Packages/manifest.json` / `packages-lock.json` / `csc.rsp` / `mcs.rsp`、`ProjectSettings/ProjectVersion.txt` / `ProjectSettings.asset`、根级 `csc.rsp` / `mcs.rsp`。改文档、场景、预制体不会让编译证据过期；改代码/包/项目设置会。
- **漂移双判据**：签名不一致，或任一编译输入 mtime 晚于 `compiled_at`（+1s 容差）→ stale。
- **退出码契约**：0 通过；1 失败/过期；2 配置错误或缺证据；3 编辑器占锁拒绝；4 超时。
- **锁检测安全第一**：读 `Library/EditorInstance.json` 的 PID，用 `tasklist` 探活；不用 `os.kill(pid, 0)`（Windows 上会杀进程）。锁定即拒绝，不尝试关闭编辑器。
- **证据文件全部落在 `.agent/.local/`**（已 gitignored）：它们是本机一次性产物，不应进入仓库。

## 替代方案

1. **只做批处理**：会迫使开发者关闭编辑器才能验证，日常摩擦大。
2. **只做编辑器路径**：编辑器关闭时无法验证；批处理保留为补充手段，不扩展到测试。
3. **外部解析 Editor.log 判定 pass/fail**：日志格式随版本变化；编辑器 API 更稳定。
4. **用全量工作树签名判断新鲜度**：改一行文档就让编译证据过期，噪音太大。

## 后果

- 编辑器开着 → MCP 触发菜单 + `--adopt-editor`；编辑器关着 → 直接 `unity-verify`。
- 强制重编译需要 `CleanBuildCache`，耗时约 1–2 分钟；常规快速验证不强制。
- `assemblies_compiled` 可能为空、`not_required` 可能包含全部程序集，这是 Unity 事件语义，不代表没编译；判定依赖编译 API 与 Editor.log。见 lessons 14、15。
- 批处理路径的端到端运行仍待验证（需关闭 Unity）；拒绝路径已实测（exit 3）。在跑通前，文档必须标注“已实现、未端到端运行”。
