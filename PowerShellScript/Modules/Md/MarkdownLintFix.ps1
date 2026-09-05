# MarkdownLintFix.ps1
# 通用 Markdown 文档格式修复脚本入口
# 使用方式：
#   .\MarkdownLintFix.ps1 -Path "docs/Introduction.md"
#   .\MarkdownLintFix.ps1 -Path "docs/" -Recurse
#   .\MarkdownLintFix.ps1 -Path "docs/" -Recurse -WhatIf

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Path,

    [switch]$Recurse,

    [switch]$WhatIf,

    [switch]$NoBackup,

    [string[]]$Rules = @('MD041', 'MD034', 'MD060', 'MD032', 'MD058', 'MD031', 'MD040')
)

# 获取脚本所在目录
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulePath = Join-Path $scriptDir "MarkdownLintFix.psm1"

# 导入模块
if (-not (Test-Path $modulePath)) {
    Write-Error "模块文件不存在: $modulePath"
    exit 1
}
Import-Module $modulePath -Force

# 如果没有指定 Path，显示帮助
if (-not $Path) {
    Write-Host @"
MarkdownLintFix - Markdown 文档格式修复工具

用法:
    .\MarkdownLintFix.ps1 -Path <文件或目录> [-Recurse] [-WhatIf] [-NoBackup] [-Rules @('MD041', ...)]

参数:
    -Path      要修复的文件或目录路径
    -Recurse   递归处理目录下的所有 .md 文件
    -WhatIf    预览模式，只显示要修改的内容，不实际写入
    -NoBackup  不创建 .bak 备份文件
    -Rules     要修复的规则列表（默认修复所有支持的规则）

支持的规则:
    MD041  - 确保文件首行是 H1 标题
    MD034  - 裸 URL 加反引号包裹
    MD060  - 统一表格 | 两侧空格
    MD032  - 列表前后加空行
    MD058  - 表格前后加空行
    MD031  - 代码块前后加空行
    MD040  - 代码块加语言标记

示例:
    .\MarkdownLintFix.ps1 -Path "CsharpPrj/BasicMethod/Map/Introduction.md"
    .\MarkdownLintFix.ps1 -Path "docs/" -Recurse
    .\MarkdownLintFix.ps1 -Path "docs/" -Recurse -WhatIf
    .\MarkdownLintFix.ps1 -Path "docs/" -Recurse -Rules @('MD060', 'MD032')
"@
    exit 0
}

# 调用模块主函数
Invoke-MarkdownLintFix -Path $Path -Recurse:$Recurse -WhatIf:$WhatIf -NoBackup:$NoBackup -Rules $Rules
