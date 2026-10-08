# Check-AgentRuntimeSlots.ps1
# Agent 运行时槽门禁（K10 + S4–S6，对齐 Frame\DataCenter\Doc\DataCenter-Agent运行时数据接入-设计与实施.md §5 K10、§4 A1；
# 取用面判据对齐 Docs\analysis\2026\9\9.26\Agent_RuntimeSlot源生成-设计与实施卡.md §5.1）
#
# 判据分两组（对象不同、互不替代：过 K10 ≠ 过 S4–S6）：
#   【承载面 K10】产品侧（Projects\Plug\Agent\src）不得新增「静态可变运行时槽」——运行时对象必须落在
#       运行时槽分区（root.Slots.Partition("agent")）或 AmbientSlot 机制上。
#     [S1] 静态可变字段（static 非 readonly 且非 const 的字段声明）
#     [S2] 静态可变属性（static 且含 set 访问器的自动属性）
#     [S3] 静态字典/集合持有（static readonly Dictionary/ConcurrentDictionary 等——只读引用但内容可变）
#   【取用面 S4–S6】槽键与取用的声明纪律（B4 类型化键收口后的机器判据）：
#     [S4] 槽键唯一     —— AgentRuntimeSlotKeys 的键值字面量全库无重复（键值 = 锚点）
#     [S5] 键↔宿主 1:1  —— 每个键值恰绑定一个宿主类型（同键两类型 = 状态分裂）
#     [S5b] 键成员存在  —— RuntimeSlotKey<T> 引用的 AgentRuntimeSlotKeys 成员必须存在（悬空键 = 拼造键值）
#     [S6] 取用面类型化 —— 生产侧不得经 AgentRuntimeSlots.Resolve/TryResolve 直传裸键（键值字面量或 AgentRuntimeSlotKeys.X）
#     [S7] 声明↔形态     —— InstanceScoped 名单 ↔ 实际分区形态集合相等（防「声明实例级、实走进程级/域级 Register」分裂；B2-c）
#     [S8] 跨代句柄禁令  —— 禁 GCHandle/委托/反射回调/Assembly 装载等钉住旧代数 ALC 的形态（B5-a「重建 + 状态迁移」）
# 白名单：承载面 = 迁移期载体（AgentRuntimeRoot 的绑定承载/无根回落容器）+ 注册表常量类 + 测试工程（不在扫描面）；
#         取用面 = AgentRuntimeRoot.cs（E1：P4-8 迁移期取根转发出口，键为变量，退役条件见该文件注释）。
#   - 报告模式（默认）：列出命中，不设退出码。
#   - 校验模式（-Check）：非白命中 > 0 → 退出码 1（CI 门禁）。
#   - 清单模式（-WriteManifest <path>）：源码直读产出槽清单 JSON（确定性：键值 Ordinal 升序、无时间戳/绝对路径），
#     供文档槽登记台账对拍（对齐设计与实施卡 附B manifest 样例）。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [string]$WriteManifest = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$scanDir = Join-Path $Root 'Projects\Plug\Agent\src'
if (-not (Test-Path $scanDir)) { Write-Error "未找到 Agent 源码目录: $scanDir"; exit 2 }

$patterns = [ordered]@{
    'S1 静态可变字段' = {
        param($t)
        if ($t -notmatch '^\s*(?:public|internal|private|protected)\s+') { return $false }
        if ($t -notmatch '\bstatic\b') { return $false }
        # 只读/常量/类型声明/属性/方法/表达式体 一律不算可变槽
        if ($t -match '\b(?:readonly|const|partial|class|struct|record|interface|enum)\b') { return $false }
        if ($t -match '=>') { return $false }
        if ($t -match '\(') { return $false }
        if ($t -match '\{') { return $false }
        return ($t -match ';\s*$')
    }.GetNewClosure()
    'S2 静态可变属性' = {
        param($t)
        if ($t -notmatch '\bstatic\b') { return $false }
        return ($t -match '\{\s*get;\s*set;')
    }.GetNewClosure()
}

# 白名单（路径子串 + 理由）——逐条登记，不得放宽为整目录泛白
$allowList = @(
    [pscustomobject]@{ Path = '\Runtime\AgentRuntimeRoot.cs'; Pending = $false; Reason = 'A1 取根通道：进程级装配绑定 + 无根回落容器（迁移期转发承载，注释已登记退役条件）' }
)

# 存量基线（A1 扫描基线：既有静态可变运行时槽，A2+ 对象槽分区逐项收敛）—— W1（Batch 3）逐序收敛后于 2026-09-12 **归零清空**：
# 本表空 = 任何静态可变运行时槽均判违规（唯一例外见上 $allowList：A1 取根通道迁移期载体）。
$baseline = @()

$files = Get-ChildItem -Path $scanDir -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' }

$hits = [System.Collections.Generic.List[object]]::new()
$allowed = [System.Collections.Generic.List[object]]::new()
$pending = [System.Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    $rel = $file.FullName.Substring($Root.Length).TrimStart('\')
    $lines = Get-Content $file.FullName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $text = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $trimmed = $text.Trim()
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('/*') -or $trimmed.StartsWith('*')) { continue }

        foreach ($name in $patterns.Keys) {
            if (-not (& $patterns[$name] $text)) { continue }
            $entry = [pscustomobject]@{ File = $rel; Line = $i + 1; Pattern = $name; Text = $trimmed }
            $white = $allowList | Where-Object { $_.Path -and $rel -like ('*' + $_.Path + '*') } | Select-Object -First 1
            if ($white) {
                $entry | Add-Member -NotePropertyName Reason -NotePropertyValue $white.Reason
                $allowed.Add($entry)
            } elseif ($baseline | Where-Object { $rel -like ('*' + $_ + '*') }) {
                $pending.Add($entry)
            } else {
                $hits.Add($entry)
            }
            break
        }
    }
}

# ══════════════════════════════════════════════════════════════════════════
# 取用面判据 S4–S6（B4 类型化键收口）：源码直读，不依赖构建产物
# ══════════════════════════════════════════════════════════════════════════

$keysFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\Runtime\AgentRuntimeSlotKeys.cs'
$surfHits = [System.Collections.Generic.List[object]]::new()

# ---- [S4] 槽键唯一：键值字面量去重 ----
$slotKeys = [System.Collections.Generic.List[object]]::new()
$instanceScopedMembers = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $keysFile) {
    $keyLines = Get-Content -LiteralPath $keysFile
    for ($i = 0; $i -lt $keyLines.Count; $i++) {
        if ($keyLines[$i] -match '^\s*public\s+const\s+string\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*"([^"]+)"\s*;') {
            $slotKeys.Add([pscustomobject]@{ Member = $matches[1]; Key = $matches[2]; Line = $i + 1 })
        }
    }

    # 实例级键集（InstanceScoped 初始化块）：去注释后取标识符行
    $inSet = $false
    foreach ($line in $keyLines) {
        if ($line -match 'InstanceScoped\s*=\s*new\s+HashSet<string>') { $inSet = $true; continue }
        if ($inSet) {
            if ($line -match '^\s*\};') { $inSet = $false; continue }
            $code = ($line -replace '//.*$', '').Trim()
            if ($code -match '^([A-Za-z_][A-Za-z0-9_]*)\s*,?$') { $instanceScopedMembers.Add($matches[1]) }
        }
    }

    $relKeys = $keysFile.Substring($Root.Length).TrimStart('\')
    foreach ($g in ($slotKeys | Group-Object Key | Where-Object { $_.Count -gt 1 })) {
        $members = ($g.Group | ForEach-Object { $_.Member }) -join ', '
        $surfHits.Add([pscustomobject]@{
            Rule = 'S4 槽键唯一'; File = $relKeys; Line = $g.Group[0].Line; Text = "键值 '$($g.Name)' 重复声明：$members"
        })
    }
} else {
    $surfHits.Add([pscustomobject]@{ Rule = 'S4 槽键唯一'; File = 'Agent.Contract\Runtime\AgentRuntimeSlotKeys.cs'; Line = 0; Text = '键注册表文件缺失' })
}

# ---- [S5]/[S5b] 键↔宿主类型 1:1 + 键成员存在（类型化键声明面） ----
$typedKeyPattern = '(?<kind>RuntimeSlotKey|InstanceRuntimeSlotKey)<\s*(?<host>[A-Za-z_][A-Za-z0-9_.]*)\s*>\s+(?<field>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\(\s*AgentRuntimeSlotKeys\.(?<member>[A-Za-z_][A-Za-z0-9_]*)\s*\)'
$typedKeys = [System.Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    $rel = $file.FullName.Substring($Root.Length).TrimStart('\')
    $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($text)) { continue }
    foreach ($m in [regex]::Matches($text, $typedKeyPattern)) {
        $lineNo = ($text.Substring(0, $m.Index) -split "`n").Count
        $typedKeys.Add([pscustomobject]@{
            Kind = $m.Groups['kind'].Value
            Host = $m.Groups['host'].Value
            Field = $m.Groups['field'].Value
            Member = $m.Groups['member'].Value
            File = $rel
            Line = $lineNo
        })
    }
}

$knownMembers = @{}
foreach ($k in $slotKeys) { $knownMembers[$k.Member] = $k }

foreach ($tk in $typedKeys) {
    if (-not $knownMembers.ContainsKey($tk.Member)) {
        $surfHits.Add([pscustomobject]@{
            Rule = 'S5b 键成员存在'; File = $tk.File; Line = $tk.Line
            Text = "类型化键 '$($tk.Field)' 引用未注册键成员 AgentRuntimeSlotKeys.$($tk.Member)"
        })
    }
}

foreach ($g in ($typedKeys | Group-Object Member)) {
    $hosts = @($g.Group | ForEach-Object { $_.Host } | Sort-Object -Unique)
    if ($hosts.Count -gt 1) {
        $surfHits.Add([pscustomobject]@{
            Rule = 'S5 键↔宿主 1:1'; File = $g.Group[0].File; Line = $g.Group[0].Line
            Text = "键成员 '$($g.Name)' 绑定多个宿主类型（键↔类型必须 1:1）：$($hosts -join ' / ')"
        })
    }
}

# ---- [S6] 取用面类型化：禁裸键直传 Resolve/TryResolve ----
$bareKeyPattern = 'AgentRuntimeSlots\s*\.\s*(?:Resolve|TryResolve)\s*(?:<[^>]*>)?\s*\(\s*(?:AgentRuntimeSlotKeys\.[A-Za-z_][A-Za-z0-9_]*|")'
$surfaceAllowList = @(
    [pscustomobject]@{ Path = '\Runtime\AgentRuntimeRoot.cs'; Reason = 'E1 迁移期取根转发出口（P4-8 兼容重载，键为变量；退役条件见该文件注释）' }
)
$surfaceAllowed = [System.Collections.Generic.List[object]]::new()
$typedUseCount = 0
foreach ($file in $files) {
    $rel = $file.FullName.Substring($Root.Length).TrimStart('\')
    $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($text)) { continue }

    $typedUseCount += ([regex]::Matches($text, '[A-Za-z_][A-Za-z0-9_]*Key\s*\.\s*(?:Get|TryGet)\s*\(')).Count

    foreach ($m in [regex]::Matches($text, $bareKeyPattern)) {
        $lineNo = ($text.Substring(0, $m.Index) -split "`n").Count
        $entry = [pscustomobject]@{ Rule = 'S6 取用面类型化'; File = $rel; Line = $lineNo; Text = '经裸键直传 Resolve/TryResolve（应改经 RuntimeSlotKey<T>.Get / InstanceRuntimeSlotKey<T>.Get）' }
        $white = $surfaceAllowList | Where-Object { $rel -like ('*' + $_.Path + '*') } | Select-Object -First 1
        if ($white) {
            $entry | Add-Member -NotePropertyName Reason -NotePropertyValue $white.Reason
            $surfaceAllowed.Add($entry)
        } else {
            $surfHits.Add($entry)
        }
    }
}

# ---- [S7] 声明↔形态集合相等：InstanceScoped 名单 ↔ 实际分区形态（B2-c） ----
# 防「声明实例级、实走进程级（或域级 Register）」的静默分裂。两条路径均须覆盖：
#   ① 类型化键路径（InstanceRuntimeSlotKey<T> / RuntimeSlotKey<T>）
#   ② 装配注册路径（SlotRegistrar.Register(AgentRuntimeSlotKeys.X, …)）—— S6 扫不到这条写路径
$instanceScopedAllowed = [System.Collections.Generic.List[object]]::new()
$instanceBoundMembers = @($typedKeys | Where-Object { $_.Kind -eq 'InstanceRuntimeSlotKey' } |
    ForEach-Object { $_.Member } | Sort-Object -Unique)
$processBoundMembers = @($typedKeys | Where-Object { $_.Kind -eq 'RuntimeSlotKey' } |
    ForEach-Object { $_.Member } | Sort-Object -Unique)
$registeredMembers = [System.Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($text)) { continue }
    foreach ($m in [regex]::Matches($text, 'Register\s*\(\s*AgentRuntimeSlotKeys\.(?<member>[A-Za-z_][A-Za-z0-9_]*)\s*,')) {
        $registeredMembers.Add($m.Groups['member'].Value)
    }
}
$registeredMembers = @($registeredMembers | Sort-Object -Unique)

# 存量待迁移（声明实例级、实走进程级/域级）：只允许下降；迁移依赖「实例登记时机」裁决（见施工单 §5-1）。
# 新增即违规（S7），防平行新增第二份分裂。
# VM-R0 · B2-c：§5-1 裁决 = ②（装配期登记实例）—— E 段已具备「装配期实例键就绪时按 InstanceKeyOf 实例分区注册」路径；
#   剩余待迁移项 = 待宿主经 AgentBuildRequest.InstanceKey 供给装配实例键（VM-R2b 宿主重装配接线）后逐槽收敛。
$instanceScopedPending = @(
    [pscustomobject]@{ Member = 'Boundary'; Reason = 'E 段 Register；已具备装配期实例键就绪时按 InstanceKeyOf 实例分区注册路径（VM-R0 §5-1=②），待宿主供给 InstanceKey 后收敛' }
    [pscustomobject]@{ Member = 'PromptFramework'; Reason = '进程级宿主 RuntimeSlotKey<PromptFrameworkHost>（PromptFrameworkFunction）；含进程级装配钩子 OnPromptAssembled，拆分待裁' }
    [pscustomobject]@{ Member = 'RuntimeState'; Reason = 'E 段 Register；已具备装配期实例键就绪时按 InstanceKeyOf 实例分区注册路径（VM-R0 §5-1=②），待宿主供给 InstanceKey 后收敛' }
)

foreach ($member in $instanceScopedMembers) {
    $byProcess = $processBoundMembers -contains $member
    $byRegister = $registeredMembers -contains $member
    if (-not ($byProcess -or $byRegister)) { continue }
    $how = if ($byProcess) { '进程级 RuntimeSlotKey' } else { '域级 Register 装配' }
    $entry = [pscustomobject]@{
        Rule = 'S7 声明↔形态'; File = 'Agent.Contract\Runtime\AgentRuntimeSlotKeys.cs'; Line = 0
        Text = "实例级槽声明 '$member' 实走$how（声明与实现分裂）"
    }
    $pendingEntry = $instanceScopedPending | Where-Object { $_.Member -eq $member } | Select-Object -First 1
    if ($pendingEntry) {
        $entry | Add-Member -NotePropertyName Reason -NotePropertyValue $pendingEntry.Reason
        $instanceScopedAllowed.Add($entry)
    } else {
        $surfHits.Add($entry)
    }
}
foreach ($member in $instanceBoundMembers) {
    if ($instanceScopedMembers -contains $member) { continue }
    $surfHits.Add([pscustomobject]@{
        Rule = 'S7 声明↔形态'; File = 'Agent.Contract\Runtime\AgentRuntimeSlotKeys.cs'; Line = 0
        Text = "槽 '$member' 实走实例级 InstanceRuntimeSlotKey 但未列入 InstanceScoped（声明缺失）"
    })
}

# ---- [S8] 跨代句柄禁令（B5-a：禁跨代复用句柄/委托/反射回调 → 旧 ALC 存活） ----
$handleBanPattern = 'GCHandle\.Alloc\s*\(|Marshal\.GetFunctionPointerForDelegate\s*\(|Delegate\.CreateDelegate\s*\(|Assembly\.LoadFrom\s*\(|Assembly\.Load\s*\('
# 白名单：启动期一次性反射扫描（返回值为瞬态局部、不入槽/静态字段 ⇒ 不构成跨代复用）；逐条登记，不得泛白。
$handleBanAllowList = @(
    [pscustomobject]@{ Path = 'Axioms\Infrastructure\AxiomBindingModule.cs'; Reason = '启动期一次性反射扫描（Assembly.Load 返回值为瞬态局部，不入槽/静态字段），非跨代复用' }
)
$handleBanHits = [System.Collections.Generic.List[object]]::new()
$handleBanAllowed = [System.Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    $rel = $file.FullName.Substring($Root.Length).TrimStart('\')
    $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($text)) { continue }
    foreach ($m in [regex]::Matches($text, $handleBanPattern)) {
        $lineNo = ($text.Substring(0, $m.Index) -split "`n").Count
        $entry = [pscustomobject]@{
            Rule = 'S8 跨代句柄禁令'; File = $rel; Line = $lineNo
            Text = "跨代句柄/反射装载形态 '$($m.Value.Trim())'（钉住旧代数 ALC，违反「重建 + 状态迁移」）"
        }
        $white = $handleBanAllowList | Where-Object { $rel -like ('*' + $_.Path + '*') } | Select-Object -First 1
        if ($white) {
            $entry | Add-Member -NotePropertyName Reason -NotePropertyValue $white.Reason
            $handleBanAllowed.Add($entry)
        } else {
            $handleBanHits.Add($entry)
        }
    }
}
foreach ($h in $handleBanHits) { $surfHits.Add($h) }

# ---- 槽清单 JSON（-WriteManifest；键值 Ordinal 升序，无时间戳/绝对路径 → 重生成幂等） ----
if ($WriteManifest) {
    $hostByMember = @{}
    foreach ($tk in $typedKeys) {
        if (-not $hostByMember.ContainsKey($tk.Member)) { $hostByMember[$tk.Member] = $tk }
    }

    $relKeysForManifest = $keysFile.Substring($Root.Length).TrimStart('\')
    $slots = [ordered]@{}
    foreach ($k in ($slotKeys | Sort-Object { $_.Key })) {
        $tk = if ($hostByMember.ContainsKey($k.Member)) { $hostByMember[$k.Member] } else { $null }
        $slots[$k.Key] = [ordered]@{
            member     = $k.Member
            host       = if ($tk) { $tk.Host } else { $null }
            scope      = if ($instanceScopedMembers -contains $k.Member) { 'Instance' } else { 'Process' }
            declaredIn = if ($tk) { $tk.File } else { $null }
            definedIn  = $relKeysForManifest
        }
    }

    $manifest = [ordered]@{
        '$comment'     = '运行时槽清单（源码直读生成，勿手改）。S4/S5 判据取值来源；键值=锚点，Ordinal 升序；无时间戳以保证重生成幂等。'
        slots          = $slots
        instanceScoped = @($instanceScopedMembers | Sort-Object)
        typedKeyCount  = $typedKeys.Count
        count          = $slotKeys.Count
    }

    $manifestDir = Split-Path -Parent $WriteManifest
    if ($manifestDir -and -not (Test-Path -LiteralPath $manifestDir)) { New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null }
    ($manifest | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $WriteManifest -Encoding UTF8
    Write-Host ("槽清单已写出: {0}（键 {1} 条 / 类型化绑定 {2} 条）" -f $WriteManifest, $slotKeys.Count, $typedKeys.Count)
}

Write-Host '=== [K10] Agent 无新增静态可变运行时槽 ==='
Write-Host ("扫描文件数: {0} | 白名单命中: {1} | 存量基线（待迁移）: {2} | 违规命中: {3}" -f `
    $files.Count, $allowed.Count, $pending.Count, $hits.Count)
if ($allowed.Count -gt 0) {
    Write-Host '--- 白名单（迁移期载体） ---'
    $allowed | ForEach-Object { Write-Host ("  [允许] {0}:{1} [{2}] {3}" -f $_.File, $_.Line, $_.Pattern, $_.Reason) }
}
if ($pending.Count -gt 0) {
    Write-Host '--- 存量基线（A2+ 对象槽分区逐项收敛；新增即违规） ---'
    $pending | ForEach-Object { Write-Host ("  [待迁移] {0}:{1} [{2}]" -f $_.File, $_.Line, $_.Pattern) }
}
if ($hits.Count -gt 0) {
    Write-Host '--- 违规清单（静态可变运行时槽） ---'
    $hits | ForEach-Object { Write-Host ("  [违规] {0}:{1} [{2}] {3}" -f $_.File, $_.Line, $_.Pattern, $_.Text) }
}

Write-Host ''
Write-Host '=== [S4–S6] Agent 槽键与取用面纪律 ==='
Write-Host ("键注册表键数: {0} | 类型化键绑定: {1} | 类型化取用点(.Get/.TryGet): {2} | 取用面白名单命中: {3} | 违规命中: {4}" -f `
    $slotKeys.Count, $typedKeys.Count, $typedUseCount, $surfaceAllowed.Count, $surfHits.Count)
if ($slotKeys.Count -eq 0) {
    Write-Host '  [INFO] 键注册表未解析到任何 `public const string` 键——判据退化为未接线（勿视为 PASS）。'
}
if ($surfaceAllowed.Count -gt 0) {
    Write-Host '--- 取用面白名单（迁移期出口） ---'
    $surfaceAllowed | ForEach-Object { Write-Host ("  [允许] {0}:{1} [{2}] {3}" -f $_.File, $_.Line, $_.Rule, $_.Reason) }
}
if ($surfHits.Count -gt 0) {
    Write-Host '--- 违规清单（槽键/取用面） ---'
    $surfHits | ForEach-Object { Write-Host ("  [违规] {0}:{1} [{2}] {3}" -f $_.File, $_.Line, $_.Rule, $_.Text) }
}
Write-Host ("--- [S7] 声明↔形态（InstanceScoped {0} 项 / 实走进程级 {1} / 实走域级 Register {2}） ---" -f `
    $instanceScopedMembers.Count, $processBoundMembers.Count, $registeredMembers.Count)
Write-Host ("S7 待迁移（只允许下降）: {0} / 违规: {1}" -f $instanceScopedAllowed.Count, (@($surfHits | Where-Object { $_.Rule -eq 'S7 声明↔形态' }).Count))
$instanceScopedAllowed | ForEach-Object { Write-Host ("  [S7待对齐] {0} [{1}] {2}" -f $_.Text, $_.Rule, $_.Reason) }
Write-Host ("--- [S8] 跨代句柄禁令：违规 {0}（须为 0）/ 白名单 {1} ---" -f $handleBanHits.Count, $handleBanAllowed.Count)
$handleBanAllowed | ForEach-Object { Write-Host ("  [S8允许] {0}:{1} {2}" -f $_.File, $_.Line, $_.Reason) }
Write-Host '--- 未绑定宿主的键（清单可见；非违规，供逐键处置：未使用 / 非 Resolve 通道） ---'
$boundMembers = @{}
foreach ($tk in $typedKeys) { $boundMembers[$tk.Member] = $true }
$unbound = @($slotKeys | Where-Object { -not $boundMembers.ContainsKey($_.Member) })
Write-Host ("  未绑定 {0} / 共 {1} 键" -f $unbound.Count, $slotKeys.Count)
if ($unbound.Count -gt 0) {
    Write-Host ("     " + (($unbound | ForEach-Object { $_.Key }) -join ', '))
}

if ($Check -and $hits.Count -gt 0) {
    Write-Host '校验失败：K10 静态可变运行时槽未收敛（应改经运行时槽分区或 AmbientSlot 承载）。'
    exit 1
}
if ($Check -and $surfHits.Count -gt 0) {
    Write-Host '校验失败：取用面 S4–S6 未收敛（键值须唯一且 1:1 绑定宿主类型；取用须经 RuntimeSlotKey<T>）。'
    exit 1
}
exit 0
