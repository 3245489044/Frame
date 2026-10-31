# Check-ShareMessagingBoundaries.ps1
# 通用信息传递内核边界守卫（Share.Messaging）：领域无关化 / 零依赖 / 单一状态机 / 直连禁令 / 登记面。
# -Check 违规 exit 1；默认报告模式。只读仓库。

param([switch]$Check)

$ErrorActionPreference = 'Stop'

$repo = $PSScriptRoot
while ($repo -and $repo -ne (Split-Path $repo -Parent)) {
    if ((Test-Path (Join-Path $repo 'Frame')) -and ((Test-Path (Join-Path $repo '.clinerules')) -or (Test-Path (Join-Path $repo 'clinerules')))) { break }
    $repo = Split-Path $repo -Parent
}

$kernelDir  = Join-Path $repo 'Frame\CsharpPrj\Share\Frameworks\Messaging'
$shareDir   = Join-Path $repo 'Frame\CsharpPrj\Share'
$modules    = Join-Path $repo 'Frame\CsharpPrj\modules.json'
$bakeRefs   = Join-Path $repo 'Projects\BuildFramework\Graph\props\CsharpBakeRefs.props'
$shareProj  = Join-Path $repo 'Frame\CsharpPrj\Share\Share.csproj'
$adapterDir = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Modules\Framework\Messaging'

$violations = [System.Collections.Generic.List[string]]::new()

$forbidden = @(
    @{ Pattern = 'Agent\.';               Why = '禁 Agent 依赖' },
    @{ Pattern = 'DateTime\.UtcNow';      Why = '禁取时钟' },
    @{ Pattern = 'DateTimeOffset\.UtcNow';Why = '禁取时钟' },
    @{ Pattern = 'HttpClient';            Why = '禁网络依赖' },
    @{ Pattern = 'ITransferKeyStore';     Why = '禁存储端口' },
    @{ Pattern = 'ISessionSend';          Why = '禁会话原语' },
    @{ Pattern = 'SignalIntake';          Why = '禁信号框架依赖' },
    @{ Pattern = 'xfer:';                 Why = '禁物理键文法' },
    @{ Pattern = 'sig:';                  Why = '禁物理键文法' },
    @{ Pattern = '"history"';             Why = '禁领域字面量' },
    @{ Pattern = '"task"';                Why = '禁领域字面量' },
    @{ Pattern = '"subagent"';            Why = '禁领域字面量' }
)

function Test-ForbiddenText([string]$text) {
    foreach ($item in $forbidden) { if ($text -match $item.Pattern) { return $item } }
    return $null
}

# C7 正/负对照（防判据恒真/恒假）
if ($null -eq (Test-ForbiddenText 'using Agent.Contract; DateTimeOffset.UtcNow; "xfer:agent"')) {
    $violations.Add('[C7] 负对照失败：合成非法样本未被拒')
}
if ($null -ne (Test-ForbiddenText 'public sealed record MessageEnvelope(string MessageId);')) {
    $violations.Add('[C7] 正对照失败：合法样本被误拒')
}

# C1/C3 内核逐行扫描
if (-not (Test-Path -LiteralPath $kernelDir)) {
    $violations.Add("[C1] 内核目录缺失：$kernelDir")
} else {
    $files = Get-ChildItem -LiteralPath $kernelDir -Recurse -Filter '*.cs' -File
    if ($files.Count -lt 10) { $violations.Add("[C1] 内核源码文件数异常：$($files.Count) < 10") }
    foreach ($file in $files) {
        $lineNo = 0
        foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
            $lineNo++
            $hit = Test-ForbiddenText $line
            if ($null -ne $hit) { $violations.Add("[C1/C3] $($file.Name):$lineNo 命中 $($hit.Pattern)（$($hit.Why)）") }
        }
    }

    # C4 单一状态机
    $allowStates = @('MessageState', 'MessageObservationState')
    foreach ($file in $files) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        foreach ($m in [regex]::Matches($text, 'public\s+enum\s+([A-Za-z_][A-Za-z0-9_]*)')) {
            $name = $m.Groups[1].Value
            if (($name -match '(State|Status)$') -and ($allowStates -notcontains $name)) {
                $violations.Add("[C4] $($file.Name) 定义第二套状态枚举：$name")
            }
        }
    }
}

# C2 依赖倒挂
$csproj = Join-Path $kernelDir 'Share.Messaging.csproj'
if (-not (Test-Path -LiteralPath $csproj)) {
    $violations.Add("[C2] 内核 csproj 缺失：$csproj")
} elseif ((Get-Content -LiteralPath $csproj -Raw) -match '<ProjectReference') {
    $violations.Add('[C2] Share.Messaging.csproj 含 ProjectReference（须 BCL-only）')
}

# C5 直连禁令（Share 树全域）
if (Test-Path -LiteralPath $shareDir) {
    foreach ($file in Get-ChildItem -LiteralPath $shareDir -Recurse -Filter '*.cs' -File) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        foreach ($token in @('ITransferKeyStore', 'ISessionSend', 'SignalIntake')) {
            if ($text -match [regex]::Escape($token)) {
                $violations.Add("[C5] $($file.FullName) 引用 $token（只允许 Agent 适配器目录）")
            }
        }
    }
}

# C6 登记面（三处）
foreach ($pair in @(
    @{ Path = $modules;   Token = '"Share.Messaging"';         Name = 'modules.json' },
    @{ Path = $shareProj; Token = 'Proj_Share_Messaging';      Name = 'Share.csproj' },
    @{ Path = $bakeRefs;  Token = 'Include="Share.Messaging"'; Name = 'CsharpBakeRefs.props' }
)) {
    if (-not (Test-Path -LiteralPath $pair.Path)) { $violations.Add("[C6] 缺文件：$($pair.Name)"); continue }
    if ((Get-Content -LiteralPath $pair.Path -Raw) -notmatch [regex]::Escape($pair.Token)) {
        $violations.Add("[C6] $($pair.Name) 未登记 $($pair.Token)")
    }
}

# C8 适配器面
if (-not (Test-Path -LiteralPath $adapterDir)) {
    $violations.Add("[C8] Agent 适配器目录缺失：$adapterDir")
} else {
    $count = (Get-ChildItem -LiteralPath $adapterDir -Filter '*Transport.cs' -File).Count
    if ($count -lt 4) { $violations.Add("[C8] 传输适配器件数异常：$count < 4") }
}

Write-Host '============================================================'
Write-Host '  Share.Messaging 边界守卫'
Write-Host '============================================================'
Write-Host ("  违规: {0}" -f $violations.Count)
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }

if ($Check -and $violations.Count -gt 0) { exit 1 }
exit 0
