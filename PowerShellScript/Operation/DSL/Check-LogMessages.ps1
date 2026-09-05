#requires -Version 5.1
# @requires Get-ProjectRoot, Get-FileList, Get-MsgIdDynamicMatches, Get-MsgIdLiteralCallMatches
<#
.SYNOPSIS
    日志消息注册集中校验器（F4/F5 落地工具）。

.DESCRIPTION
    扫描全库 *_log_messages.json 与 C# 源码，校验日志消息定义完整性：
      检查1  JSON 完整性：每个 *_log_messages.json 均为合法 JSON 且含 messages 节点。
      检查2  重复检测：同一文件内 msgId 不得重复。
      检查3  动态 msgId 反模式：C# 源码中传给日志调用/ConfigFileLoaderLog 的 msgId
              不得用字符串插值 $"..." 拼接可变数据（文件名等）——否则每新增一类数据须注册
              N 个变体，是「日志消息定义未找到」反复出现的结构性来源（应静态化 + payload 上下文）。
              （PS7 下经 Roslyn 精确解析首参表达式类型判定；PS5.1 回退正则。）
      检查4  引用未注册（启发式）：C# 源码中硬编码的 msgId 字符串字面量（.Log("...") 首参）
              若未在任何 *_log_messages.json 注册，显式列表并带源文件行:列（缺省 Warn；-Strict 时 fail）。
              （PS7 下经 Roslyn 精确解析首参字符串字面量并取解码值；PS5.1 回退正则。）
      检查5  孤儿候选（注册→无代码引用）：某 msgId 已在 *_log_messages.json 注册，但全库 C# 源码
              无「带引号字面量」引用（经 keyPrefix 感知候选，避免 keyPrefix+msgId 拆分配置误报）。
              （缺省 Warn；-StrictOrphans 时 fail，供 CI 决策冗余清理。）



    设计：检查 1-3 为确定性规则，判定精确；检查 4/5 为启发式，仅报告不阻断（除非 -Strict/-StrictOrphans）。

    框架复用：msgId 的源码解析与正则以单一来源收于 StringTools.LogMessages 模块，本脚本仅经
    `# @requires` 按需加载后调用；仓库根用 Get-ProjectRoot，文件收集用 Get-FileList。

.PARAMETER Root
    仓库根目录（默认 = Get-ProjectRoot 自动检测到含 .git 的仓库根）。
.PARAMETER Strict
    检查 4 的未注册引用也作为错误（exit 非零）；缺省仅 Warn。
.PARAMETER FailOnDynamic
    检查 3 的动态 msgId 反模式作为错误（exit 非零）；缺省仅 Warn。
.PARAMETER StrictOrphans
    检查 5 的孤儿候选（注册但代码无引用）作为错误（exit 非零）；缺省仅 Warn。



.EXAMPLE
    .\Check-LogMessages.ps1
    .\Check-LogMessages.ps1 -Strict -FailOnDynamic
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Strict,
    [switch]$FailOnDynamic,
    [switch]$StrictOrphans
)

$ErrorActionPreference = 'Stop'

# ---- 框架加载（owner-aware；经 PScriptManager\Framework 编译表解析，路径无关）----
$__repo = $PSScriptRoot
while ($__repo -and -not (Test-Path -LiteralPath (Join-Path $__repo '.git'))) { $__repo = Split-Path -Parent $__repo }
if (-not $__repo) { throw '无法定位仓库根（.git）' }
. (Join-Path $__repo 'PowerShellScript\PScriptManager\Framework\FrameworkRequires.ps1')
Import-FrameworkRequires -Name @('Get-ProjectRoot','Get-FileList','Get-MsgIdDynamicMatches','Get-MsgIdLiteralCallMatches','Get-MsgIdSourceIndex','Get-MsgIdKeyPrefixIndex','Test-MsgIdReferenced') -Owner @{ 'Get-ProjectRoot' = 'PowerShellScript/Modules/Core/ReadProjectConfig.ps1' }

# ---- 仓库根定位 ----
# 委托框架 Get-ProjectRoot（.git 标记）。脚本原判据为「含 CsharpPrj」，
# 两者在本仓库内等价（CsharpPrj 与 .git 均位于仓库根，上层无 .git）。
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Get-ProjectRoot -StartPath $PSScriptRoot
}
if (-not (Test-Path $Root)) { throw "Root 不存在: $Root" }
Write-Host "[Check-LogMessages] 仓库根: $Root" -ForegroundColor Cyan

# 统一排除目录（单一来源，覆盖生成/构建/备份产物）
$excludeDirs = @('node_modules', 'obj', 'bin', '_deprecated', 'backups', 'BuildOutput', '.vs', '.git')
$issues = [System.Collections.Generic.List[object]]::new()
$jsonFiles = @(Get-FileList -Path $Root -Include @('*_log_messages.json') -ExcludeDir $excludeDirs)

Write-Host ("[Check-LogMessages] 发现 {0} 个日志消息定义文件" -f $jsonFiles.Count)

# ---- 检查 1 + 2：JSON 完整性与文件内重复 ----
$allRegistered = @{}   # msgId -> Set(文件)（跨文件归集）
foreach ($f in $jsonFiles) {
    try {
        $j = Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
    } catch {
        $issues.Add([pscustomobject]@{ Check = 1; File = $f.FullName; Id = ''; Detail = "JSON 解析失败: $($_.Exception.Message)" })
        continue
    }
    if ($null -eq $j -or $null -eq $j.messages) {
        $issues.Add([pscustomobject]@{ Check = 1; File = $f.FullName; Id = ''; Detail = '缺少 messages 节点' })
        continue
    }
    $names = @($j.messages.PSObject.Properties.Name | Where-Object { $null -ne $_ })
    if ($names.Count -ne @($names | Sort-Object -Unique).Count) {
        $dup = $names | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name
        $issues.Add([pscustomobject]@{ Check = 2; File = $f.FullName; Id = ($dup -join ', '); Detail = '文件内 msgId 重复' })
    }
    foreach ($n in $names) {
        if (-not $allRegistered.ContainsKey($n)) { $allRegistered[$n] = [System.Collections.Generic.HashSet[string]]::new() }
        [void]$allRegistered[$n].Add($f.FullName)
    }
}

# ---- 检查 3：动态 msgId 反模式（字符串插值拼进 msgId）----
$dynamicFound = @()
foreach ($f in (Get-FileList -Path $Root -Include @('*.cs') -ExcludeDir $excludeDirs)) {
    $text = (Get-Content -LiteralPath $f.FullName -Raw -ErrorAction SilentlyContinue); if ($null -eq $text) { $text = '' }
    $dynamicFound += Get-MsgIdDynamicMatches -Text $text -File $f.FullName
}
foreach ($d in $dynamicFound) {
    $loc = if ($d.Line -gt 0) { " [$($d.Line):$($d.Column)]" } else { '' }
    $issues.Add([pscustomobject]@{ Check = 3; File = $d.File; Id = $d.Text; Detail = "动态 msgId（字符串插值拼可变数据）——应静态化 + payload 上下文$loc" })
}

# ---- 检查 4（启发式）：硬编码 msgId 字符串字面量未在任何文件注册 ----
$unregistered = @()
foreach ($f in (Get-FileList -Path $Root -Include @('*.cs') -ExcludeDir $excludeDirs)) {
    $text = (Get-Content -LiteralPath $f.FullName -Raw -ErrorAction SilentlyContinue); if ($null -eq $text) { $text = '' }
    foreach ($lit in (Get-MsgIdLiteralCallMatches -Text $text -File $f.FullName)) {
        if (-not $allRegistered.ContainsKey($lit.Id)) {
            $unregistered += [pscustomobject]@{ File = $lit.File; Id = $lit.Id; Line = $lit.Line; Column = $lit.Column }
        }
    }
}
$unregistered = $unregistered | Sort-Object Id -Unique
foreach ($u in $unregistered) {
    $loc = if ($u.Line -gt 0) { " [$($u.Line):$($u.Column)]" } else { '' }
    $issues.Add([pscustomobject]@{ Check = 4; File = $u.File; Id = $u.Id; Detail = "代码引用但未在任何 *_log_messages.json 注册$loc" })
}
# ---- 检查 5（孤儿候选）：注册但代码无引用（经 keyPrefix 感知候选，防前缀拆分配置误报）----
$csFiles5 = @(Get-FileList -Path $Root -Include @('*.cs') -ExcludeDir $excludeDirs)
$csIdx5 = Get-MsgIdSourceIndex -Files $csFiles5
$cfgFiles5 = @(Get-FileList -Path $Root -Include @('framework.config.json') -ExcludeDir $excludeDirs | ForEach-Object { $_.FullName })
$kpm5 = Get-MsgIdKeyPrefixIndex -Root $Root -ConfigFiles $cfgFiles5
$orphanFound = @()
foreach ($f in $jsonFiles) {
    try { $j5 = Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json } catch { continue }
    if ($null -eq $j5 -or $null -eq $j5.messages) { continue }
    $filePrefixes5 = if ($kpm5.ByFile.ContainsKey($f.FullName)) { @($kpm5.ByFile[$f.FullName]) } else { @($kpm5.All) }
    foreach ($n in @($j5.messages.PSObject.Properties.Name | Where-Object { $null -ne $_ })) {
        if (-not (Test-MsgIdReferenced -Id $n -AllCsText $csIdx5.Blob -Prefixes $filePrefixes5 -ReferencedSet $csIdx5.Ids)) {
            $orphanFound += [pscustomobject]@{ File = $f.FullName; Id = $n }
        }
    }
}
$orphanFound = $orphanFound | Sort-Object Id -Unique
foreach ($o in $orphanFound) {
    $issues.Add([pscustomobject]@{ Check = 5; File = $o.File; Id = $o.Id; Detail = '注册但无代码字面量引用（孤儿候选）' })
}



# ---- 报告 ----
$byCheck = $issues | Group-Object Check
foreach ($g in ($byCheck | Sort-Object { [int]$_.Name })) {
    $sev = switch ([int]$g.Name) { 1 { '错误' } 2 { '错误' } 3 { $(if ($FailOnDynamic) { '错误' } else { '警告' }) } 4 { $(if ($Strict) { '错误' } else { '警告' }) } 5 { $(if ($StrictOrphans) { '错误' } else { '警告' }) } default { '警告' } }
    Write-Host ("`n--- 检查{0}：{1} 项（{2}） ---" -f $g.Name, $g.Count, $sev) -ForegroundColor $(if ($sev -eq '错误') { 'Red' } else { 'Yellow' })
    foreach ($i in $g.Group | Select-Object -First 40) {
        Write-Host ("  [{0}] {1}  {2}" -f $i.Id, $i.File, $i.Detail)
    }
    if ($g.Count -gt 40) { Write-Host ("  ... 其余 {0} 项" -f ($g.Count - 40)) }
}

Write-Host ("`n[Check-LogMessages] 定义文件 {0} 个，注册 msgId {1} 个，共发现 {2} 项问题（动态 {3} / 未注册 {4} / 孤儿 {5} / 其他 {6}）" -f `
    $jsonFiles.Count, $allRegistered.Count, $issues.Count, $dynamicFound.Count, $unregistered.Count, $orphanFound.Count,
    ($issues.Count - $dynamicFound.Count - $unregistered.Count - $orphanFound.Count))

# ---- 退出码：检查 1/2 恒错误；3 依 -FailOnDynamic；4 依 -Strict ----
$hardErrors = @($issues | Where-Object { [int]$_.Check -le 2 })
$dynamicErrors = @($issues | Where-Object { [int]$_.Check -eq 3 -and $FailOnDynamic })
$unregErrors = @($issues | Where-Object { [int]$_.Check -eq 4 -and $Strict })
$orphanErrors = @($issues | Where-Object { [int]$_.Check -eq 5 -and $StrictOrphans })
$exitCode = if ($hardErrors.Count -gt 0 -or $dynamicErrors.Count -gt 0 -or $unregErrors.Count -gt 0 -or $orphanErrors.Count -gt 0) { 1 } else { 0 }
exit $exitCode

