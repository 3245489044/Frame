<#
.SYNOPSIS
    配置/声明面 JSON 批量校验（替代手写 foreach + ConvertFrom-Json）
.DESCRIPTION
    批量校验 JSON 文件语法有效性，支持：
    - 文件或目录（目录递归）
    - 可配置过滤与排除规则
    - 逐行输出 OK / FAIL，尾部汇总
    - 退出码：全通过 0；任一失败 1

    注：jsonc 格式先剥注释再解析（复用 BasicMethod\Core\Json.psm1，若无则降级为普通 JSON）。
# @requires Write-Result, Write-Warn, Write-Section
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string[]]$Path,
    [string]$Filter = '*.json',
    [string]$Exclude = '',
    [string]$Schema = '',
    [switch]$Quiet,
    [switch]$IncludeDeprecated
)

if (-not $Exclude) {
    if ($IncludeDeprecated) {
        $Exclude = 'BuildOutput|obj|bin|node_modules|backups|TestResults'
    } else {
        $Exclude = 'BuildOutput|obj|bin|_deprecated|node_modules|backups|TestResults'
    }
}

# 按需加载模块
. (Join-Path $PSScriptRoot "..\..\Modules\Import-Modules.ps1")

# ---- 收集文件 ----
$files = New-Object System.Collections.Generic.List[System.IO.FileInfo]

foreach ($p in $Path) {
    if (-not (Test-Path -LiteralPath $p)) {
        Write-Warn "路径不存在：$p"
        continue
    }

    if (Test-Path -LiteralPath $p -PathType Container) {
        Get-ChildItem -LiteralPath $p -Recurse -File -Filter $Filter -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch $Exclude } |
            ForEach-Object { $files.Add($_) }
    } else {
        $item = Get-Item -LiteralPath $p -ErrorAction SilentlyContinue
        if ($item) { $files.Add($item) }
    }
}

if ($files.Count -eq 0) {
    Write-Warn "未找到任何匹配文件（Filter=$Filter）"
    exit 1
}

# ---- 逐文件校验 ----
$ok = 0
$bad = 0
$failedFiles = @()

Write-Section "JSON 校验（共 $($files.Count) 个文件）"

foreach ($f in $files) {
    try {
        $raw = Get-Content -Raw -LiteralPath $f.FullName -Encoding UTF8 -ErrorAction Stop

        # jsonc 处理：剥行注释与块注释（简易实现，不处理字符串内的 // /*）
        if ($f.Extension -eq '.jsonc') {
            $clean = $raw -replace '(?m)//.*$', ''
            $clean = $clean -replace '(?s)/\*.*?\*/', ''
            $null = $clean | ConvertFrom-Json -ErrorAction Stop
        } else {
            $null = $raw | ConvertFrom-Json -ErrorAction Stop
        }

        if (-not $Quiet) {
            Write-Result "OK" $f.FullName
        }
        $ok++
    } catch {
        Write-Result "FAIL" ("{0} : {1}" -f $f.FullName, $_.Exception.Message)
        $bad++
        $failedFiles += $f.FullName
    }
}

Write-Result "INFO" "合计 文件 $($ok + $bad) / 通过 $ok / 失败 $bad"

if ($bad -gt 0) { exit 1 }
exit 0
