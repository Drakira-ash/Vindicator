# GitHub 仓库英文名调整说明（20261025_193118）

## 目标
- 将当前仓库名从 `-` 改为英文名（建议：`believer`）。

## 当前状态
- 本环境执行 `gh` 命令失败（终端未识别 gh）。
- 需在你本地可用 gh 的终端执行重命名命令。

## 建议命令
```powershell
gh repo edit Drakira-ash/- --name believer
git remote set-url origin https://github.com/Drakira-ash/believer.git
git remote -v
```

## 参数解释
- `--name believer`：将仓库名重命名为 believer。
- `git remote set-url origin ...`：同步本地远程地址到新仓库名。
- `git remote -v`：检查本地远程地址是否已更新。
