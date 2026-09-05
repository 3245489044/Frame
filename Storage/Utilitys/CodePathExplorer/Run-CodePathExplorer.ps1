<#
.SYNOPSIS
    CodePathExplorer 启动脚本 - 可配置输入输出路径的代码路径分析工具

.DESCRIPTION
    基于 RoslynMethod 构建的函数关系解析工具，从字段层面到函数层面到 class 层面对整个代码库进行分层解析。
    本脚本封装了 CodePathExplorer.exe 的调用，提供更友好的参数配置方式。

.PARAMETER SourcePath
    要分析的代码库路径。默认值为当前工作目录。

.PARAMETER OutputDir
    输出目录，用于存放 Markdown 报告。默认值为脚本所在目录下的 "Output" 文件夹。

.PARAMETER LogOutputDir
    日志输出目录（与 LogViewer 兼容）。如果指定，分析结果会以日志格式输出到此目录。
    默认不输出日志。

.PARAMETER BatchId
    日志批次 ID（可选）。仅在指定 -LogOutputDir 时有效，默认自动生成。

.PARAMETER NoMarkdown
    指定此开关则不导出 Markdown 报告。

.PARAMETER NoInteractive
    指定此开关则分析完成后不等待按键，直接退出。

.PARAMETER Build
    指定此开关则强制重新构建项目后再运行。

.PARAMETER Help
    显示此帮助信息。

.EXAMPLE
    # 分析当前目录
    .\Run-CodePathExplorer.ps1

.EXAMPLE
    # 分析指定路径，输出到指定目录
    .\Run-CodePathExplorer.ps1 -SourcePath "D:\Project\Program\WorkFlowPrj" -OutputDir "D:\Reports"

.EXAMPLE
    # 分析并同时输出日志
    .\Run-CodePathExplorer.ps1 -SourcePath "D:\Project" -OutputDir "D:\Reports" -LogOutputDir "D:\Logs"

.EXAMPLE
    # 非交互模式，不生成 Markdown
    .\Run-CodePathExplorer.ps1 -SourcePath "D:\Project" -NoMarkdown -NoInteractive

.NOTES
    作者: WorkFlowPrj
    版本: 1.0
#>

param(
    [Parameter(Mandatory = $false, Position = 0)]
    [string]$SourcePath = "",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "",

    [Parameter(Mandatory = $false)]
    [string]$LogOutputDir = "",

    [Parameter(Mandatory = $false)]
    [string]$BatchId = "",

    [switch]$NoMarkdown,

    [switch]$NoInteractive,

    [switch]$Build,

    [switch]$Help
)

# 显示帮助
if ($Help) {
    Get-Help -Name $MyInvocation.MyCommand.Path -Detailed
    exit 0
}

# 获取脚本所在目录
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = $ScriptDir
$ExePath = Join-Path -Path $ProjectDir -ChildPath "bin\Debug\net10.0\CodePathExplorer.exe"

# 处理 SourcePath 默认值
if ([string]::IsNullOrEmpty($SourcePath)) {
    $SourcePath = (Get-Location).Path
}

# 处理 OutputDir 默认值
if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path -Path $ScriptDir -ChildPath "Output"
}

# 确保输出目录存在
if (-not (Test-Path $OutputDir)) {
    New-Item -Path $OutputDir -ItemType Directory -Force | Out-Null
    Write-Host "已创建输出目录: $OutputDir" -ForegroundColor Green
}

# 验证 SourcePath
if (-not (Test-Path $SourcePath)) {
    Write-Host "错误: 指定的代码库路径不存在: $SourcePath" -ForegroundColor Red
    exit 1
}

# 如果需要，先构建项目
if ($Build -or -not (Test-Path $ExePath)) {
    Write-Host "正在构建 CodePathExplorer..." -ForegroundColor Cyan
    $buildResult = & dotnet build "$ProjectDir\CodePathExplorer.csproj" -nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "构建失败:" -ForegroundColor Red
        $buildResult | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        exit 1
    }
    Write-Host "构建成功!" -ForegroundColor Green
}

# 检查 exe 是否存在
if (-not (Test-Path $ExePath)) {
    Write-Host "错误: 未找到 CodePathExplorer.exe，请先构建项目。" -ForegroundColor Red
    exit 1
}

# 构建命令行参数
$ExeArgs = @()

# SourcePath 作为第一个位置参数
$ExeArgs += "`"$SourcePath`""

# 指定 Markdown 输出路径（使用 OutputDir）
$ExeArgs += "--markdown-output"
$ExeArgs += "`"$OutputDir`""

# 如果指定了 LogOutputDir，添加 --log-output 参数
if (-not [string]::IsNullOrEmpty($LogOutputDir)) {
    $ExeArgs += "--log-output"
    $ExeArgs += "`"$LogOutputDir`""
}

# 如果指定了 BatchId，添加 --batch-id 参数
if (-not [string]::IsNullOrEmpty($BatchId)) {
    $ExeArgs += "--batch-id"
    $ExeArgs += "$BatchId"
}

# 如果指定了 NoMarkdown，添加 --no-markdown 参数
if ($NoMarkdown) {
    $ExeArgs += "--no-markdown"
}

# 如果指定了 NoInteractive，添加 --no-interactive 参数
if ($NoInteractive) {
    $ExeArgs += "--no-interactive"
}

Write-Host "╔══════════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║           CodePathExplorer PowerShell Launcher                     ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""
Write-Host "  分析路径: $SourcePath" -ForegroundColor Yellow
Write-Host "  输出目录: $OutputDir" -ForegroundColor Yellow
if (-not [string]::IsNullOrEmpty($LogOutputDir)) {
    Write-Host "  日志目录: $LogOutputDir" -ForegroundColor Yellow
}
Write-Host ""

# 运行 CodePathExplorer
try {
    Write-Host "正在启动 CodePathExplorer..." -ForegroundColor Green
    & $ExePath $ExeArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "CodePathExplorer 执行失败，退出代码: $LASTEXITCODE" -ForegroundColor Red
    }
    else {
        Write-Host ""
        Write-Host "分析完成!" -ForegroundColor Green
        Write-Host "输出目录: $OutputDir" -ForegroundColor Green
        
        # 检查输出目录中是否有生成的报告
        $reportFiles = Get-ChildItem -Path $OutputDir -Filter "*CodePathExplorer*" -ErrorAction SilentlyContinue
        if ($reportFiles) {
            Write-Host "生成的文件:" -ForegroundColor Cyan
            $reportFiles | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor White }
        }
    }
}
catch {
    Write-Host "运行 CodePathExplorer 时发生错误: $_" -ForegroundColor Red
    exit 1
}
