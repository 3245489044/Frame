#requires -Version 5.1
# @requires Get-ProjectRoot, Get-FileList, Get-MsgIdSourceIndex, Test-MsgIdReferenced, Remove-MsgBlock
<#
.SYNOPSIS
    冗余日志键清理工具（F4/F5 落地工具，与 Check-LogMessages.ps1 配套）。

.DESCRIPTION
    扫描全库 *_log_messages.json 与 C# 源码，删除「代码中无对应调用方」的日志键：
      规则  仅保留 msgId 被代码以字面量引用的定义：.Log("msgId" 或 new ConfigFileLoaderLog("msgId")。
            其余所有 msgId（未被任何代码引用的）从它们出现的每一个 *_log_messages.json 中删除
            ——同一 msgId 若在多个文件里都有注册，只要未被引用，会从这些文件全部清除（自动覆盖跨文件场景）。

    防误删（前缀感知判定）：
      运行时日志按 keyPrefix（如 fde:/exec:/fs:）拆分调用——代码只写无前缀 msgId，但 JSON 键名
      可能是「keyPrefix+msgId」或「无前缀 msgId」两种形态。脚本自动从 framework.config.json 读取
      keyPrefix 白名单，对每个键构造「原键 / 去前缀 / 加前缀」候选集，任一候选命中源码字面量即视为
      已引用，绝不删除。既适配 fde:WinFormApplier_Apply_start 这类带前缀键，也覆盖无前缀经典键。

    安全设计：
      备份  每次执行先将被修改的 JSON 复制到 backups 目录（按执行时间戳分目录），便于回滚。
      预览  支持 -WhatIf：仅列出将删除的 msgId 与文件，不落盘。
      回归校验（-Verify） 删除前/后各运行一次 Check-LogMessages.ps1 -Strict，比较「引用未注册」
            项。若删除引入了新的回归（代码引用的 msgId 被本次删除），自动从已备份文件逐文件回退，
            并输出详细错误报告（含 msgId、代码引用处、应恢复的注册文件），进程以非零退出码结束。

    框架复用：msgId 正则、源码索引、引用判定、块删除以单一来源收于 StringTools.LogMessages 模块，
    本脚本仅经 `# @requires` 按需加载后调用；仓库根用 Get-ProjectRoot，文件收集用 Get-FileList。
    备份/写回保留本脚本自有的按运行目录 + 相对名展平机制（支撑 -Verify 逐文件回退），不强制换框架备份。

    说明：动态拼接的 msgId（.Log($"prefix{...}")）为反模式，本身不会静态注册，不受本脚本保护；仅字面量调用视为「被引用」。

.PARAMETER Root
    仓库根目录（默认 = Get-ProjectRoot 自动检测到含 .git 的仓库根）。
.PARAMETER BackupDir
    备份根目录（默认 = PowerShellScript\backups，即 D:\Project\Program\WorkFlowPrj\PowerShellScript\backups）。
.PARAMETER Verify
    开关。真实删除前、后各跑一次 Check-LogMessages 做回归校验；失败自动回退 + 报错 + exit 1。
.PARAMETER CheckScript
    校验脚本路径；缺省取本目录下的 Check-LogMessages.ps1。

.EXAMPLE
    .\Remove-RedundantLogMessages.ps1                       # 直接删除并备份
    .\Remove-RedundantLogMessages.ps1 -WhatIf              # 只预览，不改文件
    .\Remove-RedundantLogMessages.ps1 -BackupDir D:\bak    # 自定义备份目录
    .\Remove-RedundantLogMessages.ps1 -Verify              # 删除前后用 Check-LogMessages 校验，失败自动回退
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param(
    [string]$Root = '',
    [string]$BackupDir = '',

    # 校验开关：真实执行删除前、后各跑一次 Check-LogMessages.ps1，
    # 若「引用未注册」出现新增项（删除造成的回归），自动从备份逐文件回退并给出错误报告，exit 非零。
    [switch]$Verify,
    # 校验脚本路径（默认取同目录下的 Check-LogMessages.ps1）
    [string]$CheckScript = ''
)

$ErrorActionPreference = 'Stop'

# ---- 模块加载（IoC 按需加载；@requires 见文件头）----
# ---- 模块加载（路径无关：按真实文件名向上自愈定位）----
$__psp = $PSScriptRoot
while ($__psp -and -not (Test-Path -LiteralPath (Join-Path $__psp 'Modules\Import-Modules.ps1'))) { $__psp = Split-Path -Parent $__psp }
if (-not $__psp) { throw '无法定位 Modules\Import-Modules.ps1（PS 框架根）' }
. (Join-Path $__psp 'Modules\Import-Modules.ps1')

# ---- 仓库根定位（对齐 Check-LogMessages.ps1 / Get-ProjectRoot .git 标记）----
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Get-ProjectRoot -StartPath $PSScriptRoot
}
if (-not (Test-Path $Root)) { throw "Root 不存在: $Root" }
Write-Host "[Remove-RedundantLogMessages] 仓库根: $Root" -ForegroundColor Cyan

# ---- 备份目录 ----
if ([string]::IsNullOrWhiteSpace($BackupDir)) {
    # PSScriptRoot = ...\PowerShellScript\DSL，其父目录即 PowerShellScript
    $BackupDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'backups'
}
if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null }
$runStamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$runBackupDir = Join-Path $BackupDir $runStamp
New-Item -ItemType Directory -Path $runBackupDir -Force | Out-Null
Write-Host "[Remove-RedundantLogMessages] 备份目录: $runBackupDir" -ForegroundColor Cyan

# ---- 校验脚本定位 ----
# 与 Check-LogMessages.ps1 配套：做「删除前 → 删除后」回归校验，失败则回退。
$verifyScript = ''
if ($Verify) {
    if ([string]::IsNullOrWhiteSpace($CheckScript)) {
        $verifyScript = Join-Path $PSScriptRoot 'Check-LogMessages.ps1'
    } else {
        $verifyScript = $CheckScript
    }
    if (-not (Test-Path $verifyScript)) { throw "CheckScript 不存在: $verifyScript" }
    Write-Host "[Remove-RedundantLogMessages] 使用校验脚本: $verifyScript" -ForegroundColor Cyan
}
$deletedBackups = [System.Collections.Generic.List[object]]::new()  # 被改文件的 {file, backup} 记录，用于回退
$removedById  = @{}        # msgId -> 曾注册的 JSON 文件集合（错误报告定位用）
$baselineExprs = [System.Collections.Generic.HashSet[string]]::new() # 删除前「引用未注册」指纹（file|msgId）
$validationErrors = [System.Collections.Generic.List[string]]::new() # 删除后新增的回归项
$verifyFail = $false

# 运行一次 Check-LogMessages -Strict，返回收集到的「引用未注册」指纹集合（file|msgId）。
# 只关注检查4的「代码引用但未在任何 *_log_messages.json 注册」项——这正是删除导致的回归信号。
function Invoke-CheckUnregistered {
    param([string]$Script, [string]$Root)
    $exprSet = [System.Collections.Generic.HashSet[string]]::new()
    try {
        $output = & $Script -Root $Root -Strict 2>&1 | Out-String
        foreach ($line in ($output -split "`n")) {
            $t = $line.Trim()
            if ($t -notmatch '^\[.*\]') { continue }   # 仅解析 `  [msgId] file  详情` 行形态
            # 行形态: `  [msgId] <file>  代码引用但未在任何 *_log_messages.json 注册`
            if ($t -match '\[([^\]]+)\]\s+(\S+[^ ]*)\s+.*未在任何') {
                if ($Matches[1] -and $Matches[2]) { [void]$exprSet.Add(($Matches[2] + '|' + $Matches[1])) }
            }
        }
    } catch {
        Write-Host ("  [校验] 解析校验输出异常（忽略）: {0}" -f $_.Exception.Message) -ForegroundColor Yellow
    }
    return $exprSet
}

# ---- 删除前基线（仅 -Verify 时）----
if ($verifyScript) {
    Write-Host ("`n[校验] 删除前运行 Check-LogMessages（基线）..." ) -ForegroundColor Cyan
    $baselineExprs = Invoke-CheckUnregistered -Script $verifyScript -Root $Root
    Write-Host ("[校验] 基线「引用未注册」{0} 项" -f $baselineExprs.Count) -ForegroundColor Cyan
}

# 统一排除目录（单一来源，覆盖生成/构建/备份产物）
$excludeDirs = @('node_modules', 'obj', 'bin', '_deprecated', 'backups', 'BuildOutput', '.vs', '.git')

# ---- 收集代码字面量引用的 msgId（调用方）----
# 判定做成「子串命中」+「正则命中」双重：msgId 命名足够独特（ClassName_MethodName_*），
# 只要该 msgId 字符串出现在任一 .cs 文件文本中即视为被引用。
# 子串命中可覆盖所有「非首参/走变量/换方法名」的调用形态（如 .Info("id")、ConfigFileLoaderLog
# 第二参数、TryRegisterAsync 的任意字符串参数、三目拼 msgId 变量后 Log 等），
# 从而避免旧正则（仅 .Log("id") 首参 / ConfigFileLoaderLog 首参）的漏判误删。
$csFiles = @(Get-FileList -Path $Root -Include @('*.cs') -ExcludeDir $excludeDirs)

# 统一构建源码引用索引（全文拼接 + 标识符样 msgId 候选集），委托 StringTools.LogMessages。
$srcIdx = Get-MsgIdSourceIndex -Files $csFiles
$allCsText = $srcIdx.Blob
$referenced = $srcIdx.Ids
Write-Host ("[Remove-RedundantLogMessages] C# 文件 {0} 个，代码文本字节数 {1}，正则命中候选 msgId {2} 个" -f $csFiles.Count, $allCsText.Length, $referenced.Count)

# ---- 收集 logging keyPrefix 白名单（framework.config.json）----
# 用途：日志基础设施会把 keyPrefix 拆分调用——JSON 键名既可能是「keyPrefix+msgId」（如
# fde:WinFormApplier_Apply_start），也可能是「无前缀 msgId」（如 WinFormApplier_Apply_start），
# 而代码只写无前缀的 Log("WinFormApplier_Apply_start")。若无白名单、不去前缀做匹配，
# 带前缀键永远无法命中源码字面量，会被误判未引用而误删。
$allKeyPrefixes  = [System.Collections.Generic.HashSet[string]]::new()
$fileKeyPrefixes = @{}   # 各 JSON 绝对路径 -> HashSet[该文件关联的 keyPrefix 集合]
foreach ($cfg in Get-FileList -Path $Root -Include @('framework.config.json') -ExcludeDir $excludeDirs) {
    try { $cfgJson = Get-Content -LiteralPath $cfg.FullName -Raw | ConvertFrom-Json } catch { continue }
    if ($null -eq $cfgJson -or $null -eq $cfgJson.logging -or $null -eq $cfgJson.logging.modules) { continue }
    foreach ($m in $cfgJson.logging.modules) {
        if ($null -eq $m -or [string]::IsNullOrWhiteSpace($m.keyPrefix)) { continue }
        [void]$allKeyPrefixes.Add($m.keyPrefix)
        if ([string]::IsNullOrWhiteSpace($m.path)) { continue }
        try {
            $rel = ([string]$m.path).TrimStart('\', '/')
            $abs = if ([System.IO.Path]::IsPathRooted($rel)) { $rel } else { Join-Path $Root $rel }
            $full = [System.IO.Path]::GetFullPath($abs)
            if (-not $fileKeyPrefixes.ContainsKey($full)) { $fileKeyPrefixes[$full] = [System.Collections.Generic.HashSet[string]]::new() }
            [void]$fileKeyPrefixes[$full].Add($m.keyPrefix)
        } catch { /* 单条映射异常不影响整体 */ }
    }
}
Write-Host ("[Remove-RedundantLogMessages] 发现 keyPrefix {0} 个，映射 JSON 文件 {1} 个" -f $allKeyPrefixes.Count, $fileKeyPrefixes.Count) -ForegroundColor Cyan

# ---- 逐个 JSON 文件删除未引用 msgId ----
$jsonFiles = @(Get-FileList -Path $Root -Include @('*_log_messages.json') -ExcludeDir $excludeDirs)

$totalDeleted = 0
$totalFiles = 0
foreach ($f in $jsonFiles) {
    try {
        $j = Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
    } catch {
        Write-Host ("  [跳过] JSON 解析失败: {0}  ({1})" -f $f.FullName, $_.Exception.Message) -ForegroundColor Yellow
        continue
    }
    if ($null -eq $j -or $null -eq $j.messages) {
        Write-Host ("  [跳过] 缺少 messages 节点: {0}" -f $f.FullName) -ForegroundColor Yellow
        continue
    }

    # 主判据：msgId 命中任一候选形态即视为被引用（.Log("id")/.Info("id")/ConfigFileLoaderLog 任参/
    # 三目拼变量/TryRegister 传入等，凡代码字面量真出现必带引号）。候选中含「去 keyPrefix 前缀」与
    # 「加 keyPrefix 前缀」两种形态，以适配 fde:X / exec:X 等 keyPrefix 拆分配置，避免误删。
    # 文件级前缀来自 framework.config.json 映射；未命中映射的文件回退用全部 keyPrefix 白名单。
    $filePrefixes = if ($fileKeyPrefixes.ContainsKey($f.FullName)) { @($fileKeyPrefixes[$f.FullName]) } else { @($allKeyPrefixes) }
    $removals = @($j.messages.PSObject.Properties.Name |
        Where-Object { $null -ne $_ -and -not (Test-MsgIdReferenced -Id $_ -AllCsText $allCsText -Prefixes $filePrefixes -ReferencedSet $referenced) })
    if ($removals.Count -eq 0) { continue }

    $totalFiles++
    Write-Host ("`n--- {0}：待删除 {1} 个未引用日志键 ---" -f $f.FullName, $removals.Count) -ForegroundColor Yellow
    foreach ($id in $removals) { $totalDeleted++; Write-Host ("  - {0}" -f $id) }

    # -WhatIf 只预览；真正执行时才备份+重写
    if ($PSCmdlet.ShouldProcess($f.FullName, "备份并重写，删除 $($removals.Count) 个未引用日志键")) {
        # 备份到 backups\<timestamp>\<相对路径名>（用相对路径避免不同模块同名文件互相覆盖）
        $relName = $f.FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '_').Replace('/', '_')
        $backupPath = Join-Path $runBackupDir $relName
        Copy-Item -LiteralPath $f.FullName -Destination $backupPath -Force

        # 记录每个被删键的注册来源（错误报告定位用），并登记备份以便失败回退
        foreach ($id in $removals) {
            if (-not $removedById.ContainsKey($id)) { $removedById[$id] = [System.Collections.Generic.HashSet[string]]::new() }
            [void]$removedById[$id].Add($f.FullName)
        }

        # 文本级块删除：委托 StringTools.LogMessages，保留原文格式/模板/缩进
        $text = Get-Content -LiteralPath $f.FullName -Raw
        $newText = Remove-MsgBlock -Text $text -IdList $removals
        try {
            $null = $newText | ConvertFrom-Json
            Set-Content -LiteralPath $f.FullName -Value $newText -Encoding UTF8
            $deletedBackups.Add([pscustomobject]@{ File = $f.FullName; Backup = $backupPath })
            Write-Host ("  [已写回+备份] {0}" -f $f.FullName) -ForegroundColor Green
        } catch {
            Write-Host ("  !! 删除后 JSON 校验失败，已跳过并保留备份: {0} ({1})" -f $f.FullName, $_.Exception.Message) -ForegroundColor Red
        }
    }
}

Write-Host ("`n[Remove-RedundantLogMessages] 处理 JSON {0} 个，删除未引用 msgId {1} 个；备份位于 {2}" -f $totalFiles, $totalDeleted, $runBackupDir)

# ---- 删除后校验与失败回退（仅 -Verify 时）----
if ($verifyScript) {
    Write-Host ("`n[校验] 删除后运行 Check-LogMessages（复检）..." ) -ForegroundColor Cyan
    $afterExprs = Invoke-CheckUnregistered -Script $verifyScript -Root $Root
    Write-Host ("[校验] 复检「引用未注册」{0} 项" -f $afterExprs.Count) -ForegroundColor Cyan

    # 判定：复检中出现而基线没有的「引用未注册」= 本次删除造成的回归
    $newExprs = @($afterExprs | Where-Object { -not $baselineExprs.Contains($_) })
    if ($newExprs.Count -gt 0) {
        $verifyFail = $true
        if ($deletedBackups.Count -gt 0 -and $PSCmdlet.ShouldProcess('回退', "校验失败，从备份还原 $($deletedBackups.Count) 个被修改文件")) {
            foreach ($rec in $deletedBackups) {
                try {
                    Copy-Item -LiteralPath $rec.Backup -Destination $rec.File -Force
                    Write-Host ("  [已回退] {0}  ← {1}" -f $rec.File, $rec.Backup) -ForegroundColor Magenta
                } catch {
                    Write-Host ("  !! 回退失败: {0} ({1})" -f $rec.File, $_.Exception.Message) -ForegroundColor Red
                }
            }
        }
    } else {
        Write-Host ("[校验] 通过：删除未造成「引用未注册」回归") -ForegroundColor Green
    }
}

# ---- 错误报告（仅校验失败时输出详细定位信息，指明应回退内容）----
if ($verifyFail) {
    Write-Host ("`n================ 校验失败错误报告 ================") -ForegroundColor Red
    Write-Host ("发现 {0} 项新增「引用未注册」：这些 msgId 被代码引用但本次被删除，已回退以下文件的原始内容以获得一致性：" -f $newExprs.Count) -ForegroundColor Red
    foreach ($expr in $newExprs) {
        $parts = $expr -split '\|', 2
        $codeFile = $parts[0]; $msgId = $parts[1]
        $regFiles = if ($removedById.ContainsKey($msgId)) { @($removedById[$msgId]) } else { @() }
        Write-Host ("`n  msgId: {0}" -f $msgId) -ForegroundColor Red
        Write-Host ("    代码引用处: {0}" -f $codeFile) -ForegroundColor Red
        if ($regFiles.Count -gt 0) {
            Write-Host ("    本次删除该注册的文件（应恢复）:") -ForegroundColor Red
            foreach ($rf in $regFiles) { Write-Host ("      - {0}" -f $rf) -ForegroundColor Red }
        } else {
            Write-Host ("    本次未在已删除文件中找到该注册（可能基线即缺失，需人工核查）") -ForegroundColor Yellow
        }
    }
    if ($deletedBackups.Count -eq 0 -and $runBackupDir) {
        Write-Host ("`n  提示：本轮未产生可回退的写回记录，若确实误删请从备份目录手工恢复：") -ForegroundColor Yellow
        Write-Host ("    {0}" -f $runBackupDir) -ForegroundColor Yellow
    }
    Write-Host ("=============================================") -ForegroundColor Red
    exit 1
}

Write-Host ("`n[Remove-RedundantLogMessages] 完成。" )
exit 0

