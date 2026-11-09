#requires -Version 5.1
<#
.SYNOPSIS
    门禁登记清单唯一装载器：schema 归一 + Spec 目录重扫 + 形状校验（三处同一口径）。

.DESCRIPTION
    背景：`Operation\DSL\Registry\GateManifests.psd1` 曾以 12 种键形状共存（Tier 240 / Family 136 /
    Form 136 / Carrier 31，且 101 条缺 Tier、10 条缺 Note、1 条缺 Args），每个消费者各写一份
    「缺键取默认」的分支（聚合器 / 索引编译器 / 分类守卫），口径必然分叉。

    本模块把「怎么读清单」收敛为一处（schema 'gate-manifest-v2'）：

      核心五键（每条必填，禁空）：
        Domain  分类轴（真源 = directory-map.json 的 gateDomains 键）
        Tier    执行分层 ∈ {fast, full}（fast = 只跑读产物类轻量件）
        Args    传给门脚本的开关表（默认 @{}；聚合器据此 CLI 化）
        Expect  期望退出码 ∈ {0, 1}
        Note    该件守什么（一句话；空 = 登记错误）

      分类轴（成对出现，仅呈现域 Agent / Ui 适用）：
        Family  ∈ Declaration/Contract/Runtime/Presentation/Observability/Boundary/Meta
        Form    ∈ SingleSource/Closure/Boundary/Ratchet/Drift/Consistency

      说明（可选，无机器消费）：
        Carrier 判据载体（结构事实描述，禁执行者叙述）

    重扫：`-SpecRoot` 指向 `Gate\Spec\`，目录内每份 `*.gate.psd1`（Schema='gate-spec-v1'）自描述
    一份门禁（Id/Domain/Tier/Desc）。清单无同 Id 行时按 spec 自动登记（Source='Spec'）；
    清单行存在则以清单为准（Note/Args/Tier 可覆盖）。⇒ 后续「门 → spec」迁移批次不再逐条改清单。

.PARAMETER Path
    GateManifests.psd1 绝对路径。

.PARAMETER SpecRoot
    Gate\Spec 目录（绝对路径）。为空 = 不重扫（仅清单口径）。

.PARAMETER NoSpecMerge
    关闭重扫，只做归一。

.NOTES
    用法（消费者）：
        Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateManifest.psm1') -Force
        $gates = Import-GateManifest -Path $manifestPath -SpecRoot (Join-Path (Get-MappedPath -Key 'CentralTestGate') 'Spec')
#>

$script:GM_Required  = @('Domain', 'Note')                 # 文件内必填（承载信息，无默认值可推）
$script:GM_Defaulted = @('Tier', 'Args', 'Expect')          # 允许缺省（缺省值已声明；装载器补齐，禁消费者各写一份）
$script:GM_Optional  = @('Family', 'Form', 'Carrier')       # Family/Form 成对，Carrier 仅说明
$script:GM_TierEnum   = @('fast', 'full')
$script:GM_FamilyEnum = @('Declaration', 'Contract', 'Runtime', 'Presentation', 'Observability', 'Boundary', 'Meta')
$script:GM_FormEnum   = @('SingleSource', 'Closure', 'Boundary', 'Ratchet', 'Drift', 'Consistency')

<#
.SYNOPSIS
    schema 声明（唯一真源；守卫报告与文档均读此处，禁另写一份枚举）。
#>
function Get-GateManifestSchema {
    return [ordered]@{
        Schema    = 'gate-manifest-v2'
        Required  = @($script:GM_Required)
        Defaulted = @($script:GM_Defaulted)
        Optional  = @($script:GM_Optional)
        Axis     = @('Family', 'Form')
        Enums    = [ordered]@{
            Tier   = @($script:GM_TierEnum)
            Expect = @(0, 1)
            Family = @($script:GM_FamilyEnum)
            Form   = @($script:GM_FormEnum)
        }
        Defaults = [ordered]@{
            Tier   = 'full'
            Args   = @{}
            Expect = 0
            Note   = ''
        }
    }
}

<#
.SYNOPSIS
    单条目归一（缺键按 schema 默认补齐；不改写原表）。
#>
function ConvertTo-GateManifestEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Spec
    )
    if (-not ($Spec -is [hashtable]) -and -not ($Spec -is [System.Collections.Specialized.OrderedDictionary])) {
        throw "门禁登记项 '$Name' 不是哈希表（值类型：$($Spec.GetType().Name)）"
    }
    $get = { param($k) if ($Spec.Contains($k)) { $Spec[$k] } else { $null } }
    # 返回普通哈希表（非 OrderedDictionary）：消费者习惯用 .ContainsKey()，两者方法集不同，禁混用。
    $out = @{
        Domain = [string](& $get 'Domain')
        Tier   = if ([string]::IsNullOrWhiteSpace([string](& $get 'Tier'))) { 'full' } else { [string](& $get 'Tier') }
        Args   = if ($null -eq (& $get 'Args')) { @{} } else { & $get 'Args' }
        Expect = if ($null -eq (& $get 'Expect')) { 0 } else { [int](& $get 'Expect') }
        Note   = if ($null -eq (& $get 'Note')) { '' } else { [string](& $get 'Note') }
        Source = 'File'
    }
    foreach ($k in @('Family', 'Form', 'Carrier')) {
        $v = & $get $k
        if ($null -ne $v -and -not [string]::IsNullOrWhiteSpace([string]$v)) { $out[$k] = [string]$v }
    }
    return $out
}

<#
.SYNOPSIS
    读取清单文件为常量哈希表（AST + 逐条 SafeGetValue，语义等同 Import-PowerShellDataFile）。

.DESCRIPTION
    为何不用 Import-PowerShellDataFile：Windows PowerShell 5.1 在对本清单调用它时，
    登记行数 ≥ 367 即抛
      "Cannot generate a Windows PowerShell object for a ScriptBlock evaluating dynamic expressions"
    （实测：366 行 OK / 367 行 FAIL，与本批新增 20 门直接冲突；逐条 AST SafeGetValue 全部成功）。
    故改为：整文件解析一次 → 只取顶层 hashtable → 每个值 SafeGetValue（仍强制常量表达式，
    不接受变量/命令/子表达式，数据文件安全性不降）。
#>
function Read-GateManifestData {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "门禁清单不存在：$Path" }
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    if (@($errors).Count -gt 0) {
        $e0 = @($errors)[0]
        throw ("门禁清单语法错误：{0}（L{1}：{2}）" -f $Path, $e0.Extent.StartLineNumber, $e0.Message)
    }
    $stmt = $ast.EndBlock.Statements | Select-Object -First 1
    $ht = if ($stmt -and $stmt.PipelineElements.Count -gt 0) { $stmt.PipelineElements[0].Expression } else { $null }
    if (-not ($ht -is [System.Management.Automation.Language.HashtableAst])) {
        throw "门禁清单顶层不是单一哈希表：$Path"
    }
    $out = @{}
    foreach ($kv in $ht.KeyValuePairs) {
        $name = [string]$kv.Item1.Value
        try {
            $out[$name] = $kv.Item2.SafeGetValue()
        } catch {
            throw "登记项 '$name' 含非常量表达式（数据文件只接受字面量）：$($_.Exception.Message)"
        }
    }
    return $out
}

<#
.SYNOPSIS
    装载清单（归一 + Spec 目录重扫），返回按名升序的有序字典。
#>
function Import-GateManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$SpecRoot = '',
        [switch]$NoSpecMerge
    )
    $raw = Read-GateManifestData -Path $Path

    $out = @{}
    foreach ($name in $raw.Keys) {
        $out[[string]$name] = ConvertTo-GateManifestEntry -Name ([string]$name) -Spec $raw[$name]
    }

    if (-not $NoSpecMerge -and -not [string]::IsNullOrWhiteSpace($SpecRoot) -and (Test-Path -LiteralPath $SpecRoot)) {
        foreach ($f in (Get-ChildItem -LiteralPath $SpecRoot -Filter '*.gate.psd1' -File -ErrorAction SilentlyContinue)) {
            try { $s = Import-PowerShellDataFile -LiteralPath $f.FullName } catch { continue }
            if ([string]$s.Schema -ne 'gate-spec-v1') { continue }
            $id = [string]$s.Id
            if ([string]::IsNullOrWhiteSpace($id)) { $id = $f.BaseName -replace '\.gate$', '' }
            if ($out.ContainsKey($id)) { continue }
            $out[$id] = @{
                Domain  = [string]$s.Domain
                Tier    = if ([string]::IsNullOrWhiteSpace([string]$s.Tier)) { 'full' } else { [string]$s.Tier }
                Args    = @{ Check = $true }
                Expect  = 0
                Note    = if ($null -eq $s.Desc) { '' } else { [string]$s.Desc }
                Source  = 'Spec'
                SpecPath = $f.FullName
            }
        }
    }

    $sorted = @{}
    foreach ($k in ($out.Keys | Sort-Object)) { $sorted[$k] = $out[$k] }
    return $sorted
}

<#
.SYNOPSIS
    形状校验（守卫 T8 的唯一判据实现）：未声明键 / 缺核心键 / 空说明 / Tier·Expect 越界 / 轴不成对。

.DESCRIPTION
    只管**形状**（键集与成对性），不管语义枚举：Family/Form 的合法值由门禁分类守卫的 T6/T7
    在「适用面 = Agent」上判定，本件不重复（也不放宽）。

    Spec 重扫得到的行由本模块按 schema 构造，天然合形，故只校验文件内登记行。
#>
function Test-GateManifestShape {
    param([Parameter(Mandatory = $true)][string]$Path)
    $violations = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $Path)) { $violations.Add("[T8] 清单不存在：$Path"); return $violations }
    $raw = Read-GateManifestData -Path $Path
    $allowed = @($script:GM_Required + $script:GM_Defaulted + $script:GM_Optional)

    foreach ($name in ($raw.Keys | Sort-Object)) {
        $spec = $raw[$name]
        if (-not ($spec -is [hashtable]) -and -not ($spec -is [System.Collections.Specialized.OrderedDictionary])) {
            $violations.Add("[T8] $name — 登记项不是哈希表（值类型：$($spec.GetType().Name)）")
            continue
        }
        foreach ($k in $spec.Keys) {
            if ($allowed -notcontains $k) {
                $violations.Add("[T8] $name — 未声明键 '$k'（允许集：$($allowed -join '/')）")
            }
        }
        foreach ($k in $script:GM_Required) {
            if (-not $spec.Contains($k)) { $violations.Add("[T8] $name — 缺核心键 '$k'") }
        }
        if ($spec.Contains('Note') -and [string]::IsNullOrWhiteSpace([string]$spec['Note'])) {
            $violations.Add("[T8] $name — Note 为空（登记须说明该件守什么）")
        }
        if ($spec.Contains('Tier') -and ($script:GM_TierEnum -notcontains [string]$spec['Tier'])) {
            $violations.Add("[T8] $name — Tier '$($spec['Tier'])' 不在枚举 [$($script:GM_TierEnum -join '/')]（聚合器 -Tier 只认这两个值）")
        }
        if ($spec.Contains('Expect') -and ([int]$spec['Expect'] -notin @(0, 1))) {
            $violations.Add("[T8] $name — Expect '$($spec['Expect'])' 不在枚举 [0/1]")
        }
        if ($spec.Contains('Args') -and -not ($spec['Args'] -is [hashtable])) {
            $violations.Add("[T8] $name — Args 不是哈希表（值类型：$($spec['Args'].GetType().Name)）")
        }
        $hasFamily = $spec.Contains('Family') -and -not [string]::IsNullOrWhiteSpace([string]$spec['Family'])
        $hasForm   = $spec.Contains('Form') -and -not [string]::IsNullOrWhiteSpace([string]$spec['Form'])
        if ($hasFamily -ne $hasForm) {
            $violations.Add("[T8] $name — Family/Form 轴不成对（Family=$(if ($hasFamily) { $spec['Family'] } else { '∅' }) / Form=$(if ($hasForm) { $spec['Form'] } else { '∅' })）")
        }
    }
    return $violations
}

Export-ModuleMember -Function Get-GateManifestSchema, Import-GateManifest, Test-GateManifestShape
