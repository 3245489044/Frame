# StringTools 模块使用指南

> 如何在 Script 目录下的其他脚本中应用 StringTools 模块

---

## 目录

1. [模块概述](#1-模块概述)
2. [加载方式](#2-加载方式)
3. [Core 模块函数详解](#3-core-模块函数详解)
4. [FileBatch 模块函数详解](#4-filebatch-模块函数详解)
5. [StructuredText 模块函数详解](#5-structuredtext-模块函数详解)
6. [典型应用场景](#6-典型应用场景)
7. [最佳实践](#7-最佳实践)
8. [常见问题](#8-常见问题)

---

## 1. 模块概述

StringTools 位于 `Script/Modules/StringTools/`，由 **3 个子模块**组成：

| 模块文件 | 功能 | 导出函数数 |
|---------|------|-----------|
| `StringTools.Core.psm1` | 核心字符串/正则操作 | 10 个 |
| `StringTools.FileBatch.psm1` | 文件级批量字符串处理 | 5 个 |
| `StringTools.StructuredText.psm1` | 结构化文本解析 | 5 个 |

**依赖关系：**
- `StringTools.FileBatch.psm1` 依赖 `Core\FileSystemHelpers.psm1`（`Backup-File` 函数）
- `StringTools.StructuredText.psm1` 无外部依赖

---

## 2. 加载方式

### 方式一：通过 Import-Modules.ps1 统一加载（推荐）

```powershell
# 在脚本开头引入（一行代码加载所有模块）
. "$PSScriptRoot\..\Modules\Import-Modules.ps1"

# 然后直接使用所有 StringTools 函数
Invoke-SimpleReplace -InputObject $content -OldValue "Old" -NewValue "New"
Invoke-RegexExtract -InputObject $text -Pattern "(\d+\.\d+\.\d+)"
```

**优点：** 一行代码加载所有模块，按依赖顺序自动处理
**适用：** 大多数工具脚本

### 方式二：单独加载 StringTools 子模块

```powershell
# 只加载 Core 模块
$modulePath = Join-Path $PSScriptRoot "..\Modules\StringTools\StringTools.Core.psm1"
Import-Module $modulePath -Force -DisableNameChecking

# 加载 FileBatch（需先加载 Core，因为依赖 Backup-File）
$corePath = Join-Path $PSScriptRoot "..\Modules\Core\FileSystemHelpers.psm1"
Import-Module $corePath -Force -DisableNameChecking
$batchPath = Join-Path $PSScriptRoot "..\Modules\StringTools\StringTools.FileBatch.psm1"
Import-Module $batchPath -Force -DisableNameChecking
```

**优点：** 按需加载，减少开销
**适用：** 轻量脚本，只需 StringTools 功能

### 方式三：通过 ScriptLauncher 启动（自动加载）

如果脚本通过 `ScriptLauncher.ps1` 启动，且启动器中已预加载模块，则脚本中无需再写加载代码。

```powershell
# 启动器调用（假设启动器已加载模块）
.\ScriptLauncher.ps1 -Tool my-tool
```

---

## 3. Core 模块函数详解

### 3.1 Invoke-SimpleReplace — 简单文本替换

```powershell
# 单规则替换
Invoke-SimpleReplace -InputObject "Hello World" -OldValue "World" -NewValue "PowerShell"
# 返回: "Hello PowerShell"

# 管道输入
"Hello World" | Invoke-SimpleReplace -OldValue "World" -NewValue "PowerShell"

# 多规则替换
Invoke-SimpleReplace -InputObject "A B C" -Rules @(
    @{ Old = "A"; New = "X" },
    @{ Old = "B"; New = "Y" }
)
# 返回: "X Y C"

# 区分大小写
Invoke-SimpleReplace -InputObject "Hello hello" -OldValue "hello" -NewValue "Hi" -CaseSensitive
# 返回: "Hello Hi"

# 跳过特定前缀行（如 using 语句、注释）
Invoke-SimpleReplace -InputObject $fileContent -OldValue "OldName" -NewValue "NewName" `
    -SkipLines @("using ", "// ", "/// ")
```

### 3.2 Invoke-RegexExtract — 正则匹配提取

```powershell
# 提取所有匹配
Invoke-RegexExtract -InputObject "Version 1.0.0 and 2.0.0" -Pattern "(\d+\.\d+\.\d+)"
# 返回: @("1.0.0", "2.0.0")

# 提取命名捕获组
Invoke-RegexExtract -InputObject "<Version>1.0.0</Version>" `
    -Pattern "<Version>(?<ver>.*?)</Version>" -GroupName "ver"
# 返回: "1.0.0"

# 提取索引捕获组
Invoke-RegexExtract -InputObject "key=value" -Pattern "(\w+)=(\w+)" -GroupName 2
# 返回: "value"

# 只返回第一个匹配
Invoke-RegexExtract -InputObject "A1 B2 C3" -Pattern "\w\d" -FirstMatch
# 返回: "A1"
```

### 3.3 Invoke-RegexExtractToHashtable — 命名捕获组转哈希表

```powershell
# 将命名捕获组转为哈希表
$result = Invoke-RegexExtractToHashtable -InputObject "name=John;age=30" `
    -Pattern "(?<key>\w+)=(?<value>\w+)"
$result.key   # "name"
$result.value # "John"
```

### 3.4 Invoke-RegexReplace — 正则替换

```powershell
# 标准正则替换
Invoke-RegexReplace -InputObject "Hello 123 World" -Pattern "\d+" -Replacement "NUM"
# 返回: "Hello NUM World"

# ScriptBlock 回调替换（交换位置）
Invoke-RegexReplace -InputObject "a=1 b=2" -Pattern "(\w+)=(\w+)" -ScriptBlock {
    param($match)
    return "$($match.Groups[2].Value)=$($match.Groups[1].Value)"
}
# 返回: "1=a 2=b"

# 限制替换次数
Invoke-RegexReplace -InputObject "A A A A" -Pattern "A" -Replacement "B" -Count 2
# 返回: "B B A A"
```

### 3.5 Invoke-BracketAwareSplit — 括号感知分割

```powershell
# 跳过括号内的分隔符
Invoke-BracketAwareSplit -InputObject "a, b, {c, d}, e"
# 返回: @("a", "b", "{c, d}", "e")

# 跳过引号内的分隔符
Invoke-BracketAwareSplit -InputObject "a, 'b, c', d"
# 返回: @("a", "'b, c'", "d")

# 移除空条目
Invoke-BracketAwareSplit -InputObject "a,,b" -RemoveEmptyEntries
# 返回: @("a", "b")
```

### 3.6 Invoke-BracketAwareReplace — 括号感知替换

```powershell
# 只替换括号外的内容
Invoke-BracketAwareReplace -InputObject "a, {b, c}" -Pattern "," -Replacement ";"
# 返回: "a; {b, c}"
```

### 3.7 Invoke-RemoveInsideSymbols — 移除括号内内容

```powershell
# 移除花括号内的内容（连符号一起移除）
Invoke-RemoveInsideSymbols -InputObject "a {b c} d" -OpenSymbol "{" -CloseSymbol "}"
# 返回: "a  d"

# 保留符号
Invoke-RemoveInsideSymbols -InputObject "a {b} d" -OpenSymbol "{" -CloseSymbol "}" -IncludeSymbols:$false
# 返回: "a  d"
```

### 3.8 Convert-NameCase — 命名风格转换

```powershell
# snake_case → PascalCase
Convert-NameCase -InputObject "hello_world" -ToCase "Pascal"
# 返回: "HelloWorld"

# PascalCase → snake_case
Convert-NameCase -InputObject "HelloWorld" -ToCase "Snake"
# 返回: "hello_world"

# PascalCase → camelCase
Convert-NameCase -InputObject "HelloWorld" -ToCase "Camel"
# 返回: "helloWorld"

# PascalCase → kebab-case
Convert-NameCase -InputObject "HelloWorld" -ToCase "Kebab"
# 返回: "hello-world"

# 自动检测输入格式
Convert-NameCase -InputObject "my_variable_name" -ToCase "Pascal"
# 返回: "MyVariableName"
```

### 3.9 Invoke-ReplacePlaceholder — 占位符替换

```powershell
# 简单占位符替换
Invoke-ReplacePlaceholder -InputObject "Hello {name}!" -Parameters @{ name = "World" }
# 返回: "Hello World!"

# 带默认值
Invoke-ReplacePlaceholder -InputObject "{name|Guest}!" -Parameters @{}
# 返回: "Guest!"

# 多个占位符
Invoke-ReplacePlaceholder -InputObject "{a} {b}" -Parameters @{ a = "Hello"; b = "World" }
# 返回: "Hello World"
```

### 3.10 Invoke-FilterLines — 行过滤

```powershell
# 跳过指定前缀的行
$content = "using System;`r`nnamespace Test`r`n{`r`n// comment`r`nclass Foo {}`r`n}"
Invoke-FilterLines -InputObject $content -SkipLines @("using ", "// ")
# 返回: @("namespace Test", "{", "class Foo {}", "}")

# 正则模式过滤
Invoke-FilterLines -InputObject "line1`n#comment`nline2" -SkipPattern "^#"
# 返回: @("line1", "line2")
```

---

## 4. FileBatch 模块函数详解

### 4.1 Get-FileList — 获取文件列表

```powershell
# 获取所有 .cs 文件
Get-FileList -Path "src/" -Include "*.cs"

# 排除特定目录和文件
Get-FileList -Path "src/" -Include "*.cs" `
    -Exclude @("*AssemblyInfo*", "*.g.cs") `
    -ExcludeDir @(".git", "node_modules", "bin", "obj")
```

### 4.2 Backup-FileWithTimestamp — 带时间戳备份

```powershell
# 备份文件（自动生成带时间戳的备份名）
$backupPath = Backup-FileWithTimestamp -FilePath "src/MyFile.cs"
# 返回: "src/backups/MyFile_20260623_063000.cs.bak"
```

### 4.3 Invoke-FileReplace — 批量文件内容替换

```powershell
# 简单替换
Invoke-FileReplace -Path "src/" -OldValue "OldClassName" -NewValue "NewClassName" `
    -FilePattern "*.cs" -Backup

# 正则替换
Invoke-FileReplace -Path "src/" -Pattern "(?<![a-zA-Z.])OldName(?![a-zA-Z])" `
    -Replacement "NewName" -FilePattern "*.cs"

# 多规则替换
Invoke-FileReplace -Path "src/" -Rules @(
    @{ Type = "simple"; Old = "A"; New = "B" },
    @{ Type = "regex"; Pattern = "\d+\.\d+"; Replacement = "0.0" }
) -FilePattern "*.cs"

# 使用 JSON 配置文件
Invoke-FileReplace -Path "src/" -ConfigPath "replace-rules.json"

# 预览模式（不实际修改）
Invoke-FileReplace -Path "src/" -OldValue "Old" -NewValue "New" -WhatIf
```

### 4.4 Invoke-FileRegexExtract — 从文件中批量提取

```powershell
# 从所有 .csproj 中提取版本号
Invoke-FileRegexExtract -Path "src/" -Pattern "<Version>(.*?)</Version>" `
    -GroupName 1 -FilePattern "*.csproj"

# 按文件分组返回
Invoke-FileRegexExtract -Path "src/" -Pattern "<PackageReference Include="(.*?)"" `
    -GroupName 1 -FilePattern "*.csproj" -GroupByFile
```

### 4.5 Invoke-FileTransform — 文件变换管道

```powershell
# 将所有文件中的制表符替换为4个空格
Invoke-FileTransform -Path "src/" -ScriptBlock {
    param($content)
    $content -replace "`t", "    "
} -FilePattern "*.cs"
```

---

## 5. StructuredText 模块函数详解

### 5.1 ConvertFrom-SlnFile — 解析 .sln 文件

```powershell
# 解析解决方案文件
$sln = ConvertFrom-SlnFile -Path "WorkFlowPrj.sln"

# 获取项目列表
$sln.Projects | Select-Object Name, Path, Guid

# 获取虚拟文件夹
$sln.Folders

# 获取嵌套关系
$sln.NestedProjects
```

### 5.2 Get-SlnProjectGuidMap — 项目 GUID 映射

```powershell
$sln = ConvertFrom-SlnFile -Path "WorkFlowPrj.sln"
$guidMap = Get-SlnProjectGuidMap -SlnObject $sln
$guidMap["BasicMethod"]  # 返回 BasicMethod 项目的 GUID
```

### 5.3 Get-SlnProjectPathMap — 项目路径映射

```powershell
$sln = ConvertFrom-SlnFile -Path "WorkFlowPrj.sln"
$pathMap = Get-SlnProjectPathMap -SlnObject $sln
$pathMap["FileDriven"]  # 返回 "FileDriven\FileDriven.csproj"
```

### 5.4 ConvertFrom-DotnetListOutput — 解析 dotnet list 输出

```powershell
# 解析 dotnet list package 输出
dotnet list package --outdated 2>&1 | ConvertFrom-DotnetListOutput

# 返回结构化对象数组，包含 ProjectName, PackageName, RequestedVersion, ResolvedVersion
```

### 5.5 ConvertFrom-KeyValueText — 解析键值对文本

```powershell
# 解析 key=value 格式
$config = ConvertFrom-KeyValueText -InputObject "name=John`nage=30`n# comment"
$config["name"]  # "John"
$config["age"]   # "30"

# 自定义分隔符
ConvertFrom-KeyValueText -InputObject "name:John" -Delimiter ":"
```

---

## 6. 典型应用场景

### 场景 1：脚本中批量重命名代码元素

```powershell
. "$PSScriptRoot\..\Modules\Import-Modules.ps1"

# 预览将要修改的内容
Invoke-FileReplace -Path "src/" -OldValue "OldClassName" -NewValue "NewClassName" `
    -FilePattern "*.cs" -WhatIf

# 确认后执行（自动备份）
Invoke-FileReplace -Path "src/" -OldValue "OldClassName" -NewValue "NewClassName" `
    -FilePattern "*.cs" -Backup
```

### 场景 2：解析 .sln 获取项目信息

```powershell
. "$PSScriptRoot\..\Modules\Import-Modules.ps1"

$sln = ConvertFrom-SlnFile -Path "WorkFlowPrj.sln"
$sln.Projects | Select-Object Name, Path

# 获取 GUID 映射
$guidMap = Get-SlnProjectGuidMap -SlnObject $sln
```

### 场景 3：模板字符串处理

```powershell
. "$PSScriptRoot\..\Modules\Import-Modules.ps1"

$template = @"
namespace {namespace} {
    class {name} : {baseClass} {
        {body}
    }
}
"@

$result = Invoke-ReplacePlaceholder -InputObject $template -Parameters @{
    namespace = "MyApp"
    name = "Program"
    baseClass = "BaseClass"
    body = "// TODO"
}
```

### 场景 4：命名风格统一转换

```powershell
. "$PSScriptRoot\..\Modules\Import-Modules.ps1"

# 数据库字段名 → C# 属性名
$dbField = "user_full_name"
$propName = Convert-NameCase -InputObject $dbField -ToCase "Pascal"
# $propName = "UserFullName"

# C# 属性名 → JSON 字段名
$csProp = "UserFullName"
$jsonField = Convert-NameCase -InputObject $csProp -ToCase "Camel"
# $jsonField = "userFullName"
```

### 场景 5：日志/配置文件解析

```powershell
. "$PSScriptRoot\..\Modules\Import-Modules.ps1"

# 解析 .env 文件
$envContent = Get-Content ".env" -Raw
$envConfig = ConvertFrom-KeyValueText -InputObject $envContent
$dbConn = $envConfig["DATABASE_CONNECTION"]
```

### 场景 6：代码审查/分析

```powershell
. "$PSScriptRoot\..\Modules\Import-Modules.ps1"

# 从 .cs 文件中提取所有 using 语句
$content = Get-Content "src/MyFile.cs" -Raw
$usings = Invoke-RegexExtract -InputObject $content -Pattern "using\s+([\w.]+);" -GroupName 1
$usings | Sort-Object | Get-Unique
```

### 场景 7：批量文件变换

```powershell
. "$PSScriptRoot\..\Modules\Import-Modules.ps1"

# 将所有 .cs 文件中的制表符替换为空格
Invoke-FileTransform -Path "src/" -ScriptBlock {
    param($content)
    $content -replace "`t", "    "
} -FilePattern "*.cs" -Backup
```

---

## 7. 最佳实践

### 7.1 模块加载位置

```powershell
# ✅ 推荐：脚本顶部加载
. "$PSScriptRoot\..\Modules\Import-Modules.ps1"

# 然后使用函数...
```

### 7.2 路径解析

```powershell
# 脚本所在目录
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# 项目根目录（Script 的父目录）
$projectRoot = (Get-Item $scriptDir).Parent.FullName

# 模块目录
$modulesDir = Join-Path $scriptDir "..\Modules"
```

### 7.3 预览模式

```powershell
# 所有修改操作都先用 -WhatIf 预览
Invoke-FileReplace -Path "src/" -OldValue "Old" -NewValue "New" -WhatIf

# 确认无误后再执行
Invoke-FileReplace -Path "src/" -OldValue "Old" -NewValue "New" -Backup
```

### 7.4 错误处理

```powershell
. "$PSScriptRoot\..\Modules\Import-Modules.ps1"

try {
    Invoke-FileReplace -Path "src/" -OldValue "Old" -NewValue "New" -FilePattern "*.cs"
} catch {
    Write-Error "替换失败: $_"
}
```

### 7.5 避免重复加载

`Import-Modules.ps1` 已内置 `$SkipModules` 机制：

```powershell
# 在加载前设置跳过列表
$global:SkipModules = @{
    'Core\FileSystemHelpers.psm1' = $true
}

# 然后加载，被跳过的模块不会重复加载
. "$PSScriptRoot\..\Modules\Import-Modules.ps1"
```

---

## 8. 常见问题

### Q: 加载模块时报错 "File not found"

**原因：** 路径计算错误
**解决：** 确认脚本位置与模块目录的相对关系

```powershell
# 调试路径
$modulePath = Join-Path $PSScriptRoot "..\Modules\StringTools\StringTools.Core.psm1"
Write-Host "模块路径: $modulePath"
Test-Path $modulePath  # 确认文件存在
```

### Q: 函数名冲突

**原因：** 多个模块导出了同名函数
**解决：** 使用模块限定名

```powershell
# 使用模块限定名避免冲突
Import-Module "StringTools.Core.psm1" -Prefix "ST"
ST\Invoke-SimpleReplace -InputObject "Hello" -OldValue "H" -NewValue "J"
```

### Q: FileBatch 的 Backup-FileWithTimestamp 报错

**原因：** 缺少 Core\FileSystemHelpers.psm1 依赖
**解决：** 使用 Import-Modules.ps1 统一加载（自动处理依赖顺序）

### Q: 如何只测试不修改？

```powershell
# 使用 -WhatIf 参数
Invoke-FileReplace -Path "src/" -OldValue "Old" -NewValue "New" -WhatIf

# 或使用 -CheckOnly（某些工具支持）
```

---

> **相关文件：**
> - `Script/Modules/StringTools/StringTools.Core.psm1`
> - `Script/Modules/StringTools/StringTools.FileBatch.psm1`
> - `Script/Modules/StringTools/StringTools.StructuredText.psm1`
> - `Script/Modules/StringTools/replace-rules.json`
> - `Script/Modules/Import-Modules.ps1`
> - `Script/Modules/StringTools/StringTools.Tests.ps1`
