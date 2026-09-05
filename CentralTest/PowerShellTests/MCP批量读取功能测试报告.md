# MCP 批量读取功能测试报告

> 测试日期：2026-06-25  
> 测试环境：Windows 11 + PowerShell 5.1  
> 测试模块：`BasicMethod-FileAccess.psm1` (v1.0)

---

## 测试概览

| 场景 | 测试项 | 状态 | 说明 |
|------|--------|:----:|------|
| **场景1** | `BatchRead-File` 批量读取 | ✅ 通过 | 支持通配符路径，返回完整元数据 |
| **场景2** | `ctx_batch_execute` 批量命令 | ✅ 通过 | 并行执行，自动索引，查询检索 |
| **场景3** | `BatchReplace-InFile` 批量替换 | ❌ 部分失败 | 发现 3 个 Bug |
| **场景4** | `BatchWrite-File` 合并/模板 | ❌ 失败 | 发现 2 个 Bug |
| **场景5** | `ctx_execute` 沙箱批量处理 | ✅ 通过 | JavaScript 环境正常 |

---

## 场景1：BatchRead-File 批量读取 ✅

### 测试结果

| 通配符模式 | 匹配文件数 | 状态 |
|-----------|:---------:|:----:|
| `CsharpPrj/**/*.csproj` | **65** 个 | ✅ |
| `PowerShellScript/AIDecisionFramework/*.ps1` | **6** 个 | ✅ |
| `docs/*.md` | **234** 个 | ✅ |
| `*.md` + `*.ps1`（混合） | 大量 | ✅ |
| `README.md` -Raw 模式 | 1 个（原始字符串） | ✅ |

### 结论
- `BatchRead-File` 工作正常，支持通配符递归搜索
- 返回每个文件的：路径、相对路径、行数、内容、大小
- `-Raw` 参数可返回原始字符串而非行数组

---

## 场景2：ctx_batch_execute 批量命令 ✅

### 测试结果

| 命令 | 结果 | 说明 |
|------|:----:|------|
| `node --version` | ✅ | 返回 Node.js 版本 |
| `npm --version` | ✅ | 返回 npm 版本 |
| `python3 --version` | ✅ | 返回 Python 版本 |
| `df -h` | ✅ | 返回磁盘空间 |

### 结论
- `ctx_batch_execute` 在 Docker Linux 沙箱中运行
- **无法直接访问 Windows 文件系统**（`powershell`、`cmd.exe` 不可用）
- 支持 `concurrency` 参数并行执行
- 输出自动索引到知识库，支持 `queries` 检索
- 适合：Linux 兼容命令、代码分析、数据处理

---

## 场景3：BatchReplace-InFile 批量替换 ❌

### 发现的 Bug

| Bug | 函数 | 错误信息 | 原因分析 |
|-----|------|---------|---------|
| **Bug #1** | `_WriteContentToFile` | `Cannot bind parameter 'Encoding'. Cannot convert UTF8Encoding to FileSystemCmdletProviderEncoding` | `Get-PSEncoding` 返回编码对象，但 `Set-Content -Encoding` 需要枚举值 |
| **Bug #2** | `BatchWrite-File -Merge` | `[System.Object[]] does not contain a method named 'op_Subtraction'` | 数组操作错误 |
| **Bug #3** | `BatchWrite-File -Variables` | `[System.Char] does not contain a method named 'Replace'` | `$Content` 被当作 `[char]` 处理 |

### 影响范围
- `BatchReplace-InFile` 的 SEARCH/REPLACE 块和正则替换均受影响
- `BatchWrite-File` 的合并模式和模板变量替换均受影响
- 根本原因：`_WriteContentToFile` 内部函数中 `Get-PSEncoding` 返回类型与 `Set-Content` 参数类型不兼容

---

## 场景4：ctx_execute 沙箱批量处理 ✅

### 测试结果
- 在 JavaScript 沙箱中创建 3 个测试文件
- 批量读取并分析：行数、词数、字符数
- 聚合统计：总行数 10、总词数 36
- 自动清理临时文件

### 结论
- `ctx_execute` 适合在沙箱内进行数据处理
- 数据不进对话上下文，仅输出摘要
- 支持 JavaScript/TypeScript/Python 等多种语言

---

## 总结

### 工作正常的批量读取路径

```
BatchRead-File (PowerShell)  → 直接读取 Windows 文件系统 ✅
ctx_batch_execute (MCP)      → Docker 沙箱，仅 Linux 命令 ✅
ctx_execute (MCP)            → Docker 沙箱，代码处理 ✅
```

### 需要修复的问题

`BasicMethod-FileAccess.psm1` 模块中的 `_WriteContentToFile` 函数存在编码类型兼容性问题，影响 `BatchReplace-InFile` 和 `BatchWrite-File` 两个批量操作函数。建议修复 `Get-PSEncoding` 的返回值类型，使其兼容 `Set-Content -Encoding` 参数。

### 测试脚本位置

- `CentralTest/PowerShellTests/Test-BatchRead.ps1` - 批量读取测试
- `CentralTest/PowerShellTests/Test-BatchReplace.ps1` - 批量替换测试
