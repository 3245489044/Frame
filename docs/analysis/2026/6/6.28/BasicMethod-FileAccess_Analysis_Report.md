# BasicMethod-FileAccess.psm1 分析报告

> 分析日期：2025-06-25
> 文件路径：`PowerShellScript/BasicMethod/BasicMethod-FileAccess.psm1`
> 文件行数：1422 行

---

## 一、重复逻辑（Duplication）

### 1.1 路径解析逻辑重复（5+ 处）

各函数各自实现了路径解析逻辑，没有统一抽象：

| 函数 | 行号 | 实现方式 |
|------|------|---------|
| `Read-File` | L691 | `Resolve-Path $Path -ErrorAction Stop` |
| `Search-Files` | L742 | 三目：绝对路径直接使用，否则 `Resolve-Path` |
| `Write-File` | L811-815 | 三目：绝对路径直接使用，否则 `Join-Path (Get-ProjectRoot) $Path` |
| `List-Directory` | L951 | 同 `Search-Files` |
| `BatchWrite-File` | L1089, L1130 | 同 `Write-File` |

**建议**：提取统一的 `_ResolvePath` 内部函数，封装"绝对路径直接返回，相对路径拼接项目根目录"的逻辑。

### 1.2 文件读取逻辑重复（4 处）

```powershell
Get-Content -Path $xxx -Raw
```

| 函数 | 行号 |
|------|------|
| `Read-File` | L692 |
| `Replace-InFile` | L858 |
| `BatchReplace-InFile` | L1229 |
| `BatchWrite-File -Merge` | L1090 |
| `Analyze-File` | L915 |

**建议**：提取 `_ReadFileContent` 内部函数。

### 1.3 内容分割逻辑重复（5 处）

```powershell
$content -split "`r`n|`n"
```

| 函数 | 行号 |
|------|------|
| `Show-FileDiff` | L321-322 |
| `Read-File` | L698 |
| `Analyze-File` | L916 |
| `BatchRead-File` | L1001 |
| `Invoke-ContextAwareReplace` | L1353-1355 |

**建议**：提取 `_SplitLines` 内部函数。

### 1.4 备份逻辑重复（3 处）

- `_WriteContentToFile`（L617-619）— 内部辅助函数，已封装备份
- `Replace-InFile`（L874-877）— 手动调用 `Backup-File`，未复用 `_WriteContentToFile`
- `BatchReplace-InFile`（L1288-1290）— 同上

**建议**：`Replace-InFile` 和 `BatchReplace-InFile` 应复用 `_WriteContentToFile` 的写入流程。

### 1.5 校验逻辑重复（3 处）

- `_WriteContentToFile`（L642-648）— 已封装 SHA256 校验
- `Replace-InFile`（L883-889）— 手动实现校验
- `BatchReplace-InFile`（L1297-1303）— 手动实现校验

**建议**：统一复用 `_WriteContentToFile`。

### 1.6 写入逻辑重复（3 处）

- `_WriteContentToFile`（L629-637）— 完整写入流程（编码处理、目录创建、备份、校验）
- `Replace-InFile`（L879）— `Set-Content -Encoding UTF8`（硬编码）
- `BatchReplace-InFile`（L1293）— `Set-Content -Encoding UTF8`（硬编码）

**建议**：`Replace-InFile` 和 `BatchReplace-InFile` 应复用 `_WriteContentToFile`。

### 1.7 `Get-ProjectRoot` 调用重复

- `Search-Files`（L755）
- `BatchRead-File`（L1002）
- `BatchReplace-InFile`（L1307）

每个结果项都重复调用 `(Get-ProjectRoot)` 计算相对路径。

**建议**：在函数开头缓存一次 `$projectRoot = Get-ProjectRoot`。

---

## 二、冗余逻辑（Redundancy）

### 2.1 `_ResolveWildcardPaths` 双重文件过滤

```powershell
# L537-538
Get-ChildItem -Path $resolvedRoot.Path -Recurse -Filter $searchPattern -File |
    Where-Object { $_.Name -like $FilePattern }
```

- `-Filter $searchPattern` 已经按通配符过滤了文件
- `Where-Object { $_.Name -like $FilePattern }` 再次过滤，且 `$FilePattern` 默认值为 `"*"`（匹配全部）
- 绝大多数情况下第二次过滤是空操作

**建议**：移除 `Where-Object` 过滤，或将 `$FilePattern` 的语义与 `$searchPattern` 合并。

### 2.2 `Search-Files` 双重结果截断

```powershell
# L746
Get-ChildItem ... -File -ErrorAction SilentlyContinue |
    Select-Object -First 200 |   # ← 硬编码 magic number
    ...
# L762-764
if ($results.Count -gt $MaxResults) {
    $results = $results | Select-Object -First $MaxResults  # ← 再次截断
}
```

- `-First 200` 是硬编码的 magic number
- 与 `$MaxResults`（默认 50）的关系不清晰

**建议**：移除 `-First 200`，统一使用 `$MaxResults` 控制。

### 2.3 `Confirm-Replacement` 上下文截断无意义

```powershell
# L425-429
$startLine = [math]::Max(0, $Context.Count - 3)
for ($i = $startLine; $i -lt $Context.Count; $i++) { ... }
```

- 只显示最后 3 行上下文
- 当 `$Context` 很短时（如只有 1-2 行），截断没有意义

**建议**：当上下文行数较少时显示全部，较多时再截断。

### 2.4 `BatchWrite-File` 多余数组转换

```powershell
# L1118-1119
$pathArray = if ($Path -is [array]) { $Path } else { @($Path) }
$contentArray = if ($Content -is [array]) { $Content } else { @($Content) }
```

- L1121-1123 的循环 `for ($i = 0; $i -lt $pathArray.Count; $i++)` 已经能处理单值情况
- 数组转换是多余的

**建议**：直接使用 `$Path` 和 `$Content`，或使用 `@()` 强制数组化。

### 2.5 `Show-FileDiff` LCS 算法复杂度

- 使用 O(m×n) 的完整 DP 表计算 LCS
- 对于大文件（数千行）会消耗大量内存和 CPU
- PowerShell 脚本场景下，更简单的行对比算法可能更合适

**建议**：考虑使用 Myers diff 算法，或限制最大对比行数。

---

## 三、错误与缺陷（Bugs）

### 3.1 [严重] `_ResolveWildcardPaths` 通配符搜索路径错误

```powershell
# L531-539
if ($RawPath.Contains('*') -or $RawPath.Contains('?')) {
    $searchRoot = Split-Path $RawPath -Parent
    $searchPattern = Split-Path $RawPath -Leaf
    if (-not $searchRoot -or $searchRoot -eq $RawPath) { $searchRoot = "." }
    $resolvedRoot = Resolve-Path $searchRoot -ErrorAction SilentlyContinue
    ...
    return Get-ChildItem -Path $resolvedRoot.Path -Recurse -Filter $searchPattern -File ...
}
```

**问题**：当 `$RawPath` 为 `"*.cs"` 时：
- `Split-Path -Parent "*.cs"` → 空字符串
- `$searchRoot` 被设为 `"."` → 解析为**当前工作目录**（`pwd`）
- 从当前工作目录递归搜索，而非**项目根目录**

**影响**：`BatchRead-File -Paths @("*.cs")` 和 `BatchReplace-InFile -Paths @("*.cs")` 可能找不到预期文件。

**修复**：当 `$searchRoot` 为空时，应使用 `Get-ProjectRoot` 而非 `"."`。

### 3.2 [严重] `Replace-InFile` 使用 `-replace` 正则运算符

```powershell
# L860-863
$searchPattern = if ($Escape) { [regex]::Escape($Search) } else { $Search }
$newContent = $content -replace $searchPattern, $Replace
```

**问题**：
- PowerShell 的 `-replace` 运算符始终使用正则表达式匹配
- 当 `$Escape` 为 `$false`（默认）时，`$Search` 中的正则特殊字符（如 `.`, `*`, `[`, `$`）会被解释为正则语法
- 与函数文档声称的"类似 replace_in_file"的精确匹配行为不一致
- 也与 `_ApplySearchReplace`（使用 `IndexOf` 精确匹配）的行为不一致

**示例**：`Replace-InFile -Path "test.txt" -Search "count[1]" -Replace "total"` 会因 `[1]` 被解释为字符类而匹配 `count1`、`counta` 等。

**修复**：默认使用 `_ApplySearchReplace` 做精确匹配，`-Escape` 开关改为 `-Regex` 启用正则模式。

### 3.3 [中等] `Replace-InFile` 和 `BatchReplace-InFile` 硬编码 UTF8

```powershell
# L879
Set-Content -Path $resolvedPath -Value $newContent -Encoding UTF8
# L1293
Set-Content -Path $file -Value $content -Encoding UTF8
```

**问题**：
- 没有使用 `Get-PSEncoding` 或 `_WriteContentToFile` 的编码检测逻辑
- 硬编码为 UTF8，如果原文件是 GBK 或 UTF16，写入后编码会改变
- 与 `Write-File` 和 `BatchWrite-File` 的可配置编码行为不一致

**修复**：复用 `_WriteContentToFile` 的写入流程，或至少检测原文件编码后保持。

### 3.4 [低] `BatchReplace-InFile` 交互模式死代码

```powershell
# L1271-1278
if ($Interactive -and -not $applyAll) {
    $confirm = Confirm-Replacement ...
    if (-not $confirm.Confirmed) {
        if ($confirm.ApplyAll) { $applyAll = $true }  # ← 死代码
        continue
    }
    if ($confirm.ApplyAll) { $applyAll = $true }
}
```

**问题**：L1274 的 `if ($confirm.ApplyAll)` 永远不会为 `$true`，因为：
- `Confirm-Replacement`（L437）：`Confirmed = $response -in @("y", "a")`
- 当 `Confirmed` 为 `$false` 时，`$response` 只能是 `"n"`，`ApplyAll` 为 `$false`

**修复**：移除 L1274 的死代码。

### 3.5 [低] `Get-ProjectRoot` 回退路径硬编码

```powershell
# L87
$fallback = Resolve-Path "$PSScriptRoot\..\.." -ErrorAction SilentlyContinue
```

**问题**：假设模块位于 `PowerShellScript/BasicMethod/` 目录下，回退两级到项目根目录。如果模块被移动到其他位置，这个假设会失效。

**修复**：移除硬编码回退，或使用更健壮的查找策略（如持续向上查找 `.git` 直到根目录）。

### 3.6 [低] `Search-Files` 路径类型不一致

```powershell
# L742
$resolvedPath = if ([System.IO.Path]::IsPathRooted($Path)) { $Path } else { Resolve-Path $Path -ErrorAction Stop }
```

**问题**：当 `$Path` 是绝对路径时，`$resolvedPath` 是普通字符串；当是相对路径时，是 `PathInfo` 对象。类型不一致可能导致后续维护问题。

**修复**：统一使用 `Resolve-Path` 或统一使用字符串路径。

---

## 四、代码异味（Code Smells）

### 4.1 函数职责边界模糊

- `BatchWrite-File` 同时承担了"单文件写入"、"批量写入"、"合并写入"三种职责
- `BatchReplace-InFile` 同时承担了"SEARCH/REPLACE 块替换"、"正则替换"、"上下文感知替换"三种职责
- 函数名以 `Batch` 开头，但实际也处理单文件场景

### 4.2 内部函数命名不规范

- `_ApplySearchReplace`、`_ResolveWildcardPaths`、`_WriteContentToFile` 以下划线开头
- PowerShell 社区惯例使用 `-` 分隔的动词-名词命名，下划线前缀不符合惯例
- 建议改为 `Invoke-PrivateApplySearchReplace` 或使用 `[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]` 属性隐藏

### 4.3 错误处理不一致

- `Read-File`（L691）：`Resolve-Path -ErrorAction Stop` → 抛出终止错误
- `_ResolveWildcardPaths`（L535-536）：`Resolve-Path -ErrorAction SilentlyContinue` → 静默失败返回空数组
- `Write-File`（L811-815）：不解析路径，直接拼接 → 可能写入错误位置

---

## 五、修复优先级建议

| 优先级 | 问题 | 影响范围 | 修复难度 |
|--------|------|---------|---------|
| P0 | 3.1 通配符搜索路径错误 | 所有批量操作 | 低 |
| P0 | 3.2 `-replace` 正则语义不符 | `Replace-InFile` 调用方 | 中 |
| P1 | 3.3 硬编码 UTF8 | 非 UTF8 文件替换 | 低 |
| P1 | 1.1-1.7 重复逻辑提取 | 可维护性 | 中 |
| P2 | 2.1-2.5 冗余逻辑清理 | 代码整洁度 | 低 |
| P2 | 3.4-3.6 小缺陷修复 | 边缘情况 | 低 |
