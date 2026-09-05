# Legacy 目录 — 一次性脚本归档

此目录存放**一次性使用**的脚本，保留仅用于历史参考，不建议再次使用。

## 脚本说明

| 脚本 | 原始用途 | 替代方案 |
|------|----------|----------|
| `fix_coderunner_config.ps1` | 修复 VS Code Code Runner 配置 | 已通用化为 `Update-VSCodeConfig.ps1` |
| `register_entry.ps1` | 注册 VS Code 启动项 | 已通用化为 `Update-EntriesConfig.ps1` |
| `sync_vscode_entries.ps1` | 同步 VS Code 启动项工作目录 | 已通用化为 `Update-EntriesConfig.ps1` |

## 注意事项

- 这些脚本包含硬编码路径，不适用于其他环境
- 请使用 Tools 目录下的通用脚本替代
- 如需参考实现逻辑，可查看这些脚本的源代码
