<#
.SYNOPSIS
    一键清除项目中所有 C# 注释（基于 Roslyn 语法树，安全区分字符串/字面量）
.DESCRIPTION
    调用 Roslyn 语法分析器清除全部注释（单行 //、多行 /* */、XML 文档注释 ///）。
    复用 RoslynMethod\RoslynMethod.Generation\SyntaxOptimizer.cs 的注释移除逻辑，
    语法树保证字符串、字符字面量、verbatim 字符串、raw string 等邻接内容不受影响。
.PARAMETER Path
    目标根目录（默认：项目根 D:\Project\Program\WorkFlowPrj）
.PARAMETER WhatIf
    预览模式：仅统计将修改的文件数与注释数，不写入任何文件
.PARAMETER NoBuild
    跳过 C# 工具的自动构建（使用上次构建产物）
.PARAMETER SkipConfirm
    跳过执行前确认提示（默认会确认一次）
.EXAMPLE
    .\Clear-CSharpComments.ps1
.EXAMPLE
    .\Clear-CSharpComments.ps1 -Path "D:\Project\Program\WorkFlowPrj\Engine" -WhatIf
.EXAMPLE
    .\Clear-CSharpComments.ps1 -WhatIf -SkipConfirm
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Path,
    [switch]$NoBuild,
    [switch]$SkipConfirm
)

$ErrorActionPreference = 'Stop'

# ============================================================
# 路径解析
# ============================================================
$ProjectRoot = if ($Path) {
    (Resolve-Path $Path).Path
} else {
    (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

if (-not (Test-Path $ProjectRoot)) {
    throw "目录不存在: $ProjectRoot"
}

$ToolProject = Join-Path $ProjectRoot 'CsharpPrj\Plate\RoslynMethod.Tools\ClearCSharpComments\ClearCSharpComments.csproj'
if (-not (Test-Path $ToolProject)) {
    throw "未找到 C# 工具项目: $ToolProject"
}

Write-Host "╔══════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     Clear-CSharpComments.ps1 - 清除 C# 注释工具   ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""
Write-Host "📁 目标目录: $ProjectRoot" -ForegroundColor Cyan
Write-Host "🛠  工具项目: $ToolProject" -ForegroundColor Cyan
Write-Host ""

# ============================================================
# 确认提示
# ============================================================
if (-not $SkipConfirm -and -not $WhatIfPreference) {
    Write-Host "⚠ 该操作将批量修改所有 .cs 文件（无备份，依赖 git 回滚）！" -ForegroundColor Yellow
    $answer = Read-Host "确认执行？(Y/N)"
    if ($answer -notin @('Y', 'y', 'YES', 'Yes', 'yes')) {
        Write-Host "已取消。" -ForegroundColor Yellow
        exit 0
    }
}

# ============================================================
# 构建 C# 工具
# ============================================================
if (-not $NoBuild) {
    Write-Host "🔨 构建 ClearCSharpComments 工具..." -ForegroundColor Cyan
    dotnet build $ToolProject -v quiet 2>&1 | ForEach-Object { Write-Host $_ -ForegroundColor DarkGray }
    if ($LASTEXITCODE -ne 0) {
        throw "构建失败，退出码: $LASTEXITCODE"
    }
    Write-Host "✅ 构建完成" -ForegroundColor Green
    Write-Host ""
}

# ============================================================
# 组装参数并执行
# ============================================================
$toolArgs = @('run', '--project', $ToolProject, '--', '-path', "`"$ProjectRoot`"")
if ($WhatIfPreference) {
    $toolArgs += '-whatif'
}

Write-Host "🚀 开始清除注释..." -ForegroundColor Cyan
Write-Host ""
& dotnet @toolArgs
$exitCode = $LASTEXITCODE

Write-Host ""
if ($exitCode -eq 0) {
    Write-Host "✅ 处理完成" -ForegroundColor Green
} else {
    Write-Host "❌ 处理过程中存在失败项（详见上方失败列表）" -ForegroundColor Red
}

exit $exitCode