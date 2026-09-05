<#
.SYNOPSIS
    扫描日志消息定义 JSON 中未被代码引用的冗余项

.DESCRIPTION
    读取一个包含 messages 节点的日志消息定义 JSON 文件（如 execution_log_messages.json / backend_log_messages.json），
    递归扫描指定根目录下的源文件（默认 *.cs），提取其中出现的字符串字面量并与 JSON 定义键比对，
    输出：
    - 统计摘要（定义总数 / 被引用数 / 冗余数）
    - 冗余列表（JSON 定义但代码未引用）
    - 可选：被引用（保留）列表
    - 可选：每个被引用 ID 的引用计数

    安全模式：仅扫描分析，不修改任何文件。

.PARAMETER JsonPath
    日志消息定义 JSON 文件路径（必填）。JSON 需含 "messages" 节点，键为 msgId。
    不提供时默认：{PSScriptRoot}\..\..\FileDriven\FileDriven.Execution\Diagnostics\execution_log_messages.json

.PARAMETER RootPath
    递归扫描的源文件根目录（默认：当前工作目录）。

.PARAMETER FilePattern
    要扫描的文件通配符（默认：*.cs）。

.PARAMETER StrictMode
    严格模式开关。默认 $false 时提取源文件中所有字符串字面量并与 JSON 键比对（最全面，可捕获跨行/间接引用）；
    $true 时仅匹配 -LogPattern 命中的调用形式。

.PARAMETER LogPattern
    严格模式下使用的日志调用正则（需含命名捕获组 <id>）。默认匹配 ExecutionLog.Log/BackEndLog.Log/LogBase.Log/PlaceholderLog.Log 的字符串参数。

.PARAMETER OutputPath
    若提供，将完整结果写入该文本文件（UTF8）；否则输出到控制台。

.PARAMETER IncludeUsed
    同时输出被引用的（保留）定义列表。

.PARAMETER IncludeUsageCount
    统计并输出每个被引用 ID 在源文件中出现的次数（宽松模式下为字符串字面量出现次数）。

.EXAMPLE
    # 直接执行：默认扫描 execution_log_messages.json
    .\PowerShellScript\FileDriven\Scan-LogMessageRedundancy.ps1

.EXAMPLE
    # 指定 JSON 与扫描根目录
    .\PowerShellScript\FileDriven\Scan-LogMessageRedundancy.ps1 -JsonPath "D:\...\backend_log_messages.json" -RootPath "D:\...\CsharpPrj"

.EXAMPLE
    # 点源加载后调用函数
    . "D:\...\PowerShellScript\FileDriven\Scan-LogMessageRedundancy.ps1"
    Find-LogMessageRedundancy -JsonPath "..." -RootPath "..." -IncludeUsed -IncludeUsageCount

.EXAMPLE
    # 严格模式：仅统计形如 xxx.Log("msgId" 的调用
    .\PowerShellScript\FileDriven\Scan-LogMessageRedundancy.ps1 -JsonPath "..." -RootPath "..." -StrictMode

.NOTES
    仅作只读分析，不删除/修改任何文件。
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false, Position = 0)]
    [string]$JsonPath = (Join-Path $PSScriptRoot '..\..\FileDriven\FileDriven.Execution\Diagnostics\execution_log_messages.json'),

    [Parameter(Mandatory = $false, Position = 1)]
    [string]$RootPath = (Get-Location).Path,

    [Parameter(Mandatory = $false)]
    [string]$FilePattern = '*.cs',

    [Parameter(Mandatory = $false)]
    [switch]$StrictMode,

    [Parameter(Mandatory = $false)]
    [string]$LogPattern = '(?:ExecutionLog|BackEndLog|LogBase|PlaceholderLog)\.Log\(\s*["''](?<id>[^"'']+)["'']',

    [Parameter(Mandatory = $false)]
    [string]$OutputPath,

    [Parameter(Mandatory = $false)]
    [switch]$IncludeUsed,

    [Parameter(Mandatory = $false)]
    [switch]$IncludeUsageCount
)

function Find-LogMessageRedundancy {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$JsonPath,

        [Parameter(Mandatory = $false, Position = 1)]
        [string]$RootPath = (Get-Location).Path,

        [Parameter(Mandatory = $false)]
        [string]$FilePattern = '*.cs',

        [Parameter(Mandatory = $false)]
        [switch]$StrictMode,

        [Parameter(Mandatory = $false)]
        [string]$LogPattern = '(?:ExecutionLog|BackEndLog|LogBase|PlaceholderLog)\.Log\(\s*["''](?<id>[^"'']+)["'']',

        [Parameter(Mandatory = $false)]
        [string]$OutputPath,

        [Parameter(Mandatory = $false)]
        [switch]$IncludeUsed,

        [Parameter(Mandatory = $false)]
        [switch]$IncludeUsageCount
    )

    # ---------- 1. 读取 JSON 定义 ----------
    if (-not (Test-Path -LiteralPath $JsonPath -PathType Leaf)) {
        throw "JsonPath 不存在: $JsonPath"
    }

    try {
        $json = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "解析 JSON 失败: $JsonPath ($($_.Exception.Message))"
    }

    if ($null -eq $json.messages) {
        throw "JSON 中缺少 messages 节点: $JsonPath"
    }

    $definedIds = @($json.messages.PSObject.Properties | ForEach-Object { $_.Name })
    $definedSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($id in $definedIds) { [void]$definedSet.Add($id) }

    # ---------- 2. 扫描源文件 ----------
    if (-not (Test-Path -LiteralPath $RootPath -PathType Container)) {
        throw "RootPath 不存在: $RootPath"
    }

    $files = Get-ChildItem -LiteralPath $RootPath -Recurse -File -Filter $FilePattern -ErrorAction SilentlyContinue
    if ($files.Count -eq 0) {
        Write-Warning "未找到匹配的源文件: $FilePattern (RootPath=$RootPath)"
    }

    $usageCount = @{}  # id -> 出现次数
    $matcher = $null

    if ($StrictMode) {
        try { $matcher = [regex]::new($LogPattern, [System.Text.RegularExpressions.RegexOptions]::Compiled) }
        catch { throw "LogPattern 正则无效: $($_.Exception.Message)" }
    }
    else {
        # 宽松模式：匹配字符串字面量（含转义字符）
        $matcher = [regex]::new('"((?:[^"\\]|\\.)*)"', [System.Text.RegularExpressions.RegexOptions]::Compiled)
    }

    $fileCount = 0
    $utf8 = New-Object System.Text.UTF8Encoding($false)

    foreach ($file in $files) {
        $fileCount++
        try {
            $content = [System.IO.File]::ReadAllText($file.FullName, $utf8)
        }
        catch {
            Write-Warning "读取文件失败，跳过: $($file.FullName) ($($_.Exception.Message))"
            continue
        }

        $matches = $matcher.Matches($content)
        foreach ($m in $matches) {
            $token = if ($StrictMode) { $m.Groups['id'].Value } else { $m.Groups[1].Value }
            if ($definedSet.Contains($token)) {
                if ($usageCount.ContainsKey($token)) { $usageCount[$token]++ } else { $usageCount[$token] = 1 }
            }
        }
    }

    # ---------- 3. 对比统计 ----------
    $usedIds = @($usageCount.Keys | Sort-Object)
    $unusedIds = @($definedIds | Where-Object { -not $usageCount.ContainsKey($_) } | Sort-Object)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('=== 日志消息定义冗余扫描结果 ===')
    $lines.Add("JSON 定义文件 : $JsonPath")
    $lines.Add("扫描根目录     : $RootPath")
    $lines.Add("文件过滤       : $FilePattern")
    $lines.Add("模式           : $(if ($StrictMode) { '严格(LogPattern)' } else { '宽松(字符串字面量)' })")
    $lines.Add("扫描文件数     : $fileCount")
    $lines.Add("")
    $lines.Add("定义总数     : $($definedIds.Count)")
    $lines.Add("被引用数     : $($usedIds.Count)")
    $lines.Add("冗余数       : $($unusedIds.Count)")

    $lines.Add("")
    $lines.Add("=== 冗余列表（JSON 定义但代码未引用）===")
    if ($unusedIds.Count -eq 0) {
        $lines.Add("（无冗余）")
    }
    else {
        foreach ($id in $unusedIds) { $lines.Add($id) }
    }

    if ($IncludeUsed) {
        $lines.Add("")
        $lines.Add("=== 保留列表（被代码引用）===")
        foreach ($id in $usedIds) {
            if ($IncludeUsageCount) {
                $lines.Add("$id`t$($usageCount[$id])")
            }
            else {
                $lines.Add($id)
            }
        }
    }
    elseif ($IncludeUsageCount) {
        $lines.Add("")
        $lines.Add("=== 引用计数（仅被引用的 ID）===")
        foreach ($id in $usedIds) {
            $lines.Add("$id`t$($usageCount[$id])")
        }
    }

    $result = $lines -join [Environment]::NewLine

    if ($OutputPath) {
        $outDir = Split-Path -Parent $OutputPath
        if ($outDir -and -not (Test-Path -LiteralPath $outDir -PathType Container)) {
            New-Item -ItemType Directory -Path $outDir -Force | Out-Null
        }
        Set-Content -LiteralPath $OutputPath -Value $result -Encoding UTF8
        Write-Host "结果已写入: $OutputPath"
        Write-Host "统计: 定义=$($definedIds.Count), 被引用=$($usedIds.Count), 冗余=$($unusedIds.Count)"
    }
    else {
        Write-Output $result
    }
}

# 直接执行入口：以 -File 方式运行（非点源加载）时执行一次扫描
if ($MyInvocation.InvocationName -ne '.') {
    Find-LogMessageRedundancy -JsonPath $JsonPath -RootPath $RootPath -FilePattern $FilePattern -StrictMode:$StrictMode -LogPattern $LogPattern -OutputPath $OutputPath -IncludeUsed:$IncludeUsed -IncludeUsageCount:$IncludeUsageCount
}