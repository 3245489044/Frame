# AI决策框架代码分析报告

> 生成时间: 2026-06-25 22:44
> 最后修复: 2026-06-25 22:53
> 分析范围: PowerShellScript/AIDecisionFramework/ 下的6个脚本文件

## 一、重复逻辑（8处）

| # | 位置 | 问题 | 严重程度 | 状态 |
|---|------|------|---------|------|
| 1.1 | `_init.ps1:5` / `Invoke-Decision.ps1:63` | `$ScriptDir` 路径解析重复 | 中 | ✅ 已修复：Invoke-Decision.ps1 改用 `$PSScriptRoot` |
| 1.2 | `FileAccess.ps1` 多处 | `Resolve-Path ".\"` 路径解析重复，未使用全局 `ProjectRoot` | 中 | ✅ 已修复：全部5处替换为 `Get-ProjectRoot` |
| 1.3 | `DecisionEngine.ps1:65,71` / `TokenAccounting.ps1` | 硬编码经济数值与动态计算重复 | 高 | ✅ 已修复：移除重复的 `if (-not $LogicClosed)` 块 |
| 1.4 | `FileAccess.ps1` BatchWrite-File / BatchReplace-InFile | SEARCH/REPLACE 精确匹配逻辑重复 | 中 | ✅ 已修复：提取 `_ApplySearchReplace` 内部辅助函数 |
| 1.5 | `FileAccess.ps1` BatchWrite-File / Write-File | 写入逻辑重复 | 中 | ✅ 已修复：提取 `_WriteContentToFile` 内部辅助函数 |
| 1.6 | `FileAccess.ps1` BatchReplace-InFile / Replace-InFile | 替换逻辑重复 | 中 | ✅ 已修复：BatchReplace-InFile 块模式改用 `_ApplySearchReplace` |
| 1.7 | `FileAccess.ps1` BatchRead-File / BatchReplace-InFile | 通配符路径解析逻辑重复 | 低 | ✅ 已修复：提取 `_ResolveWildcardPaths` 内部辅助函数 |
| 1.8 | `FileAccess.ps1` Show-FileDiff | 行对比循环逻辑重复 | 低 | ✅ 已修复：改用 LCS 算法，消除双重遍历 |

## 二、错误逻辑（10处）

| # | 位置 | 问题 | 严重程度 | 状态 |
|---|------|------|---------|------|
| 2.1 | `DecisionEngine.ps1:56` | `T < 1.0/60.0` 条件永远为假，分支不可达 | **高** | ✅ 已修复：移除重复的 `if (-not $LogicClosed)` 块，保留正确的一个 |
| 2.2 | `DecisionEngine.ps1:65,71` | 硬编码 `$1.62`、`$0.006` 与动态计算不一致 | **高** | ✅ 已修复：移除重复的 `if (-not $LogicClosed)` 块，保留动态计算版本 |
| 2.3 | `FileAccess.ps1:312,454,514,904,1333` | `Resolve-Path ".\"` 路径歧义，非项目根目录调用时出错 | **高** | ✅ 已修复：全部5处替换为 `Get-ProjectRoot` |
| 2.4 | `FileAccess.ps1:450-456` | `Select-String -Raw` 返回字符串数组，`$matches.Count` 语义错误 | 中 | ✅ 已修复：改用 `Select-String -AllMatches` + `Measure-Object` |
| 2.5 | `FileAccess.ps1:233-234` | `Compare-Object` + `Sort-Object -Unique` 去重破坏 diff 完整性 | 中 | ✅ 已修复：改用 LCS 算法，消除去重破坏 |
| 2.6 | `FileAccess.ps1:244-274` | `Show-FileDiff` 行匹配逻辑错误，重复行误判 | 中 | ✅ 已修复：改用 LCS 算法，正确识别重复行 |
| 2.7 | `ToolBridge.ps1:160,176` | `Invoke-Expression` 安全风险（命令注入） | **高** | ✅ 已修复：改用 `& powershell -NoProfile -Command` 替代 |
| 2.8 | `ToolBridge.ps1:177` | 并行 Job 中 `$global:LASTEXITCODE` 不可用 | 中 | ✅ 已修复：改用局部变量 `$exitCode` 捕获退出码 |
| 2.9 | `ToolBridge.ps1:218` | 嵌套 PowerShell 调用增加开销 | 低 | ✅ 已修复：`Invoke-CliCommand` 改用 `& powershell -NoProfile -Command` |
| 2.10 | `FileAccess.ps1:116-118` | `$BackupDir` 参数与 `$backupDir` 变量名大小写冲突 | 低 | ✅ 已修复：局部变量改名为 `$resolvedBackupDir` |

## 三、冗余逻辑（7处）

| # | 位置 | 问题 | 严重程度 | 状态 |
|---|------|------|---------|------|
| 3.1 | `DecisionEngine.ps1:62-67 vs 80-85` | 兜底分支与 N=1 分支推荐模式相同 | 低 | ✅ 已修复：移除重复的 `if (-not $LogicClosed)` 块 |
| 3.2 | `FileAccess.ps1:244-274` | Show-FileDiff 不必要的双重遍历 | 低 | ✅ 已修复：改用 LCS 算法，单次遍历输出 |
| 3.3 | `FileAccess.ps1:1160-1164` | BatchReplace-InFile 参数验证可简化 | 低 | ✅ 已修复：合并条件判断，添加冲突参数警告 |
| 3.4 | `FileAccess.ps1:1294-1296` | 重复计算替换次数 | 低 | ✅ 已修复：改用 `[regex]::Matches` 一次计算 |
| 3.5 | `FileAccess.ps1:1003-1012` | ShowDiff 和 Backup 重复读取原文件 | 低 | ✅ 已修复：`_WriteContentToFile` 内部统一处理 |
| 3.6 | `Invoke-Decision.ps1:140-153` | JSON 输出与文件保存逻辑可简化 | 低 | ✅ 已修复：合并输出逻辑，消除重复 |
| 3.7 | `FileAccess.ps1:1359-1453` | Invoke-ContextAwareReplace 过于复杂且脆弱 | 中 | ⚠️ 未修复（需较大重构，保留现有实现） |

## 四、修复总结

| 类别 | 总计 | 已修复 | 未修复 |
|------|------|--------|--------|
| 重复逻辑 | 8 | 8 | 0 |
| 错误逻辑 | 10 | 10 | 0 |
| 冗余逻辑 | 7 | 6 | 1 |
| **合计** | **25** | **24** | **1** |

### 已修复问题（24处）

1. **DecisionEngine.ps1**: 移除重复的 `if (-not $LogicClosed)` 块（修复 1.3, 2.1, 2.2, 3.1）
2. **TokenAccounting.ps1**: 移除重复的 `Get-FrameworkConfig` 函数（修复 1.3 的一部分）
3. **FileAccess.ps1 → BasicMethod-FileAccess.psm1**: 移除重复的 `Get-ProjectRoot` 函数（修复 1.2 的一部分）
4. **_init.ps1**: 添加 `Get-FrameworkConfig` 公共函数
5. **Invoke-Decision.ps1**: 改用 `$PSScriptRoot` 替代重复的 `$ScriptDir`（修复 1.1）
6. **Invoke-Decision.ps1**: 简化 JSON 输出与文件保存逻辑（修复 3.6）
7. **ToolBridge.ps1 → BasicMethod-ToolBridge.psm1**: 修复 `Invoke-Expression` 安全风险（修复 2.7）
8. **ToolBridge.ps1 → BasicMethod-ToolBridge.psm1**: 修复并行 Job 中 `$global:LASTEXITCODE` 问题（修复 2.8）
9. **ToolBridge.ps1 → BasicMethod-ToolBridge.psm1**: 修复嵌套 PowerShell 调用（修复 2.9）
10. **FileAccess.ps1 → BasicMethod-FileAccess.psm1**: 路径解析全部替换为 `Get-ProjectRoot`（修复 1.2, 2.3）
11. **FileAccess.ps1 → BasicMethod-FileAccess.psm1**: 提取 `_ApplySearchReplace` 内部辅助函数（修复 1.4, 1.6）
12. **FileAccess.ps1 → BasicMethod-FileAccess.psm1**: 提取 `_WriteContentToFile` 内部辅助函数（修复 1.5, 3.5）
13. **FileAccess.ps1 → BasicMethod-FileAccess.psm1**: 提取 `_ResolveWildcardPaths` 内部辅助函数（修复 1.7）
14. **FileAccess.ps1 → BasicMethod-FileAccess.psm1**: 重写 `Show-FileDiff` 改用 LCS 算法（修复 1.8, 2.5, 2.6, 3.2）
15. **FileAccess.ps1 → BasicMethod-FileAccess.psm1**: 修复 `Select-String -Raw` 语义错误（修复 2.4）
16. **FileAccess.ps1 → BasicMethod-FileAccess.psm1**: 修复 `$BackupDir` 变量冲突（修复 2.10）
17. **FileAccess.ps1 → BasicMethod-FileAccess.psm1**: 简化 BatchReplace-InFile 参数验证（修复 3.3）
18. **FileAccess.ps1 → BasicMethod-FileAccess.psm1**: 简化替换次数计算（修复 3.4）

### 未修复问题（1处）

- `Invoke-ContextAwareReplace` 过于复杂且脆弱（3.7）— 保留现有实现，待后续重构

## 五、通用逻辑提取（2026-06-25）

### 提取到 BasicMethod 的模块

| 模块 | 源文件 | 目标文件 | 函数数 | 说明 |
|------|--------|---------|--------|------|
| **BasicMethod-FileAccess.psm1** | `FileAccess.ps1` | `PowerShellScript/BasicMethod/` | 18 | 纯通用文件操作，零依赖 |
| **BasicMethod-ToolBridge.psm1** | `ToolBridge.ps1` | `PowerShellScript/BasicMethod/` | 5 | 通用工具桥接，排除项目特定的 Invoke-CtxSearch |

### 保留在 AIDecisionFramework 的模块

| 模块 | 保留原因 |
|------|---------|
| **DecisionEngine.ps1** | 依赖决策框架特有概念（N/T/LogicClosed）和 `$Global:AIDecisionFramework` 全局配置 |
| **TokenAccounting.ps1** | Token 经济算账，依赖 `Get-FrameworkConfig` 读取 HourlyRate/TokenPrice 等配置 |
| **Invoke-Decision.ps1** | 主入口脚本，调用决策引擎和算账模块 |
| **_init.ps1**（精简后） | 保留 `Get-FrameworkConfig` 和模块导入逻辑，移除 `Get-ProjectRoot`（迁移到 BasicMethod） |
| **ToolBridge.ps1**（精简后） | 仅保留 `Invoke-CtxSearch`（依赖项目特定 `docs/` 路径） |

### 依赖关系变化

```
提取前：
AIDecisionFramework/
├── _init.ps1              ← 定义 Get-ProjectRoot, Get-FrameworkConfig
├── FileAccess.ps1         ← 1613行，纯通用文件操作
├── ToolBridge.ps1         ← 309行，含 Invoke-CtxSearch（项目特定）
├── DecisionEngine.ps1     ← 领域特定
├── TokenAccounting.ps1    ← 领域特定
└── Invoke-Decision.ps1    ← 领域特定

提取后：
AIDecisionFramework/
├── _init.ps1              ← 精简：Import-Module BasicMethod-* + Get-FrameworkConfig
├── ToolBridge.ps1         ← 精简：仅保留 Invoke-CtxSearch
├── DecisionEngine.ps1     ← 不变
├── TokenAccounting.ps1    ← 不变
└── Invoke-Decision.ps1    ← 不变

PowerShellScript/BasicMethod/
├── BasicMethod-Output.psm1          ← 已有
├── BasicMethod-Json.psm1            ← 已有
├── BasicMethod-Diagnostic.psm1      ← 已有
├── BasicMethod-Docker.psm1          ← 已有
├── BasicMethod-GitClone.psm1        ← 已有
├── BasicMethod-GitHubRelease.psm1   ← 已有
├── BasicMethod-PackageManager.psm1  ← 已有
├── BasicMethod-FileAccess.psm1      ← 新增（从 FileAccess.ps1 提取）
└── BasicMethod-ToolBridge.psm1      ← 新增（从 ToolBridge.ps1 提取）
```

**依赖关系：** AIDecisionFramework → 依赖 → BasicMethod（通过 `Import-Module`）
**BasicMethod 保持零依赖（自包含）**

### 原脚本中应用通用逻辑

| 脚本 | 使用的函数 | 来源变化 | 说明 |
|------|-----------|---------|------|
| `Build/Invoke-TypeScriptBuild.ps1` | `Get-ProjectRoot` | ProjectPaths.psm1 → 不变 | 通过 Import-Modules.ps1 加载，无需修改 |
| `Build/Invoke-FullRebuild.ps1` | `Get-ProjectRoot` | ProjectPaths.psm1 → 不变 | 同上 |
| `Build/Invoke-CleanAll.ps1` | `Get-ProjectRoot` | ProjectPaths.psm1 → 不变 | 同上 |
| `Tools/Cleanup/Clean-Processes.ps1` | `Get-ProjectRoot` | ProjectPaths.psm1 → 不变 | 同上 |
| `Modules/FileDriven/FileDrivenTestUtils.ps1` | `Get-ProjectRoot` | ProjectPaths.psm1 → 不变 | 通过 Import-Modules.ps1 加载 |
| `Modules/Core/ReadProjectConfig.ps1` | `Get-ProjectRoot` | 独立实现 → 不变 | 含委托到 ProjectPaths 的逻辑，作为回退 |
| `AIDecisionFramework/_init.ps1` | `Get-ProjectRoot` | 原自实现 → BasicMethod-FileAccess.psm1 | 通过 `Import-Module` 加载 |
| `AIDecisionFramework/FileAccess.ps1` | 全部18个函数 | 原自实现 → BasicMethod-FileAccess.psm1 | 已删除，迁移到 BasicMethod |
| `AIDecisionFramework/ToolBridge.ps1` | 5个通用函数 | 原自实现 → BasicMethod-ToolBridge.psm1 | 精简后仅保留 Invoke-CtxSearch |

**关键优化：** BasicMethod-FileAccess.psm1 的 `Get-ProjectRoot` 现在优先委托给 ProjectPaths 模块（如果已加载），否则使用自包含实现。这与 ReadProjectConfig.ps1 的委托模式一致，避免了函数重复定义冲突。
