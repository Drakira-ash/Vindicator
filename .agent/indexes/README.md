# 生成的索引（不提交）

本目录下的 `*.json` 由 `python .agent/scripts/agent.py index` 从当前工作树重建，**不纳入 Git**（见 `.agent/.gitignore`），避免场景/资产频繁改动造成索引 churn 与多人合并冲突。

## 文件

| 文件 | 内容 |
|---|---|
| `files.json` | 源码、资产、文档、配置的文件清单（大小 + SHA-256；不含 `.meta`）。 |
| `assets.json` | Unity 资产目录：GUID、导入器、引用关系、重复内容候选、无引用候选（低置信）。 |
| `code-map.json` | C# 类型/公开方法/菜单项/跨文件类型引用（启发式正则解析）。 |
| `dependencies.json` | Unity 版本、包依赖、程序集、测试资产探测。 |
| `manifest.json` | 源签名（内容哈希）、生成时间、各索引哈希、计数。 |

## 重建与新鲜度

```bash
python .agent/scripts/agent.py index      # 强制重建
python .agent/scripts/agent.py status     # 比较签名，报告 stale
python .agent/scripts/agent.py session-start  # 发现 stale 时自动重建
```

索引是**只读推断**，不是验收依据。资产“无引用候选”不代表可删除：可能存在动态加载、反射、运行时生成引用。删除资产前必须人工确认。

当前不提取图片尺寸、音频时长、模型面数等媒体元数据；项目核心资产是脚本、场景、预制体与材质。需要时再扩展。
