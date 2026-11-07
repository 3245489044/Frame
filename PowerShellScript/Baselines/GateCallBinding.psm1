#requires -Version 5.1
<#
.SYNOPSIS
    门禁调用参数绑定静态检查 —— AST 取调用点 + 真实 param 块比对（非正则凑数）。

.DESCRIPTION
    动机（8357aff6 事故）：`Invoke-GateCountChecks` 无 `-Label` 参数，四处调用在
    `PARSE OK=56 / FAIL=0` 下全绿，一运行即参数绑定失败。本检查把该类问题前移到静态期。

    判定面（只认 AST 与 `param` 块）：
      1. 未知具名参数 —— 调用了被调函数 `param` 块里不存在的名字（运行期必炸）。
      2. 必填缺失   —— 无默认值、非 `[switch]`、且所有候选参数集都要求、却未传（运行期必炸）。
      3. 互斥冲突   —— 同时传了两个在任一参数集中都不共存的参数（参数集语义错误）。
      4. 不可判定   —— 出现 splat（`@args` / `@PSBoundParameters`）时，splat 携带的参数无法静态展开；
                      仍会报已写明具名参数的错误，但整体标注 Undecidable，不判红也不判绿。

    不误报的三条规则：
      · 调用名若在本文件内有同名 `function` 定义 ⇒ 视为本地函数，跳过（如 Check-KeyGrammar 自带 Get-CodeLines）。
      · 位置参数按 `param` 块的 `Position` 绑定，不按名字猜。
      · 通用参数（Verbose/Debug/ErrorAction/...）不算未知。

    落点：Baselines 可复用函数；由组合门或 CI 直接调用（`-FailOnViolation` 时按违规退出 1）。
.PARAMETER Root
    仓库根（缺省按本文件位置上溯）。
.PARAMETER FrameworkPath
    框架模块路径（相对 Root 或绝对）。缺省 = Baselines 模块 + TextOps + Node 共享面。
.PARAMETER ScanPath
    扫描目录（相对 Root 或绝对）。缺省 = Environment\CentralTest\Tools\Gate + Frame\PowerShellScript。
.PARAMETER FailOnViolation
    有 BindingFailure / MissingMandatory / Conflict 时 exit 1（Undecidable 不算违规）。
.OUTPUTS
    [{ Severity; File; Line; Function; Parameter; Issue; Detail }]
#>

$script:GateBindingCommonParameters = @(
    'Verbose', 'Debug', 'ErrorAction', 'WarningAction', 'InformationAction', 'ProgressAction',
    'ErrorVariable', 'WarningVariable', 'InformationVariable', 'OutVariable', 'OutBuffer',
    'PipelineVariable', 'WhatIf', 'Confirm'
)

function Get-GateBindingRoot {
    param([string]$Start)
    $m = [regex]::Match($Start, '^(.+?)\\(?:Frame|Projects|Environment|Runtime)\\')
    if ($m.Success) { return $m.Groups[1].Value }
    return $Start
}

<#
.SYNOPSIS
    解析单个 param 块为签名（参数名 → 必填/开关/位置/所属参数集/有无默认值）。
#>
function ConvertTo-GateBindingSignature {
    param(
        [Parameter(Mandatory = $true)]$FunctionAst,
        [Parameter(Mandatory = $true)][string]$Source
    )

    $paramBlock = $FunctionAst.Body.ParamBlock
    $defaultSet = ''
    if ($null -ne $paramBlock) {
        foreach ($attr in $paramBlock.Attributes) {
            if ($attr -is [System.Management.Automation.Language.AttributeAst] -and $attr.TypeName.Name -eq 'CmdletBinding') {
                foreach ($na in $attr.NamedArguments) {
                    if ($na.ArgumentName -eq 'DefaultParameterSetName' -and $na.Argument -is [System.Management.Automation.Language.StringConstantExpressionAst]) {
                        $defaultSet = [string]$na.Argument.Value
                    }
                }
            }
        }
    }

    $parameters = @{}
    $sets = @()
    if ($null -ne $paramBlock) {
        foreach ($pa in $paramBlock.Parameters) {
            $name = $pa.Name.VariablePath.UserPath
            $isSwitch = ($pa.StaticType -eq [System.Management.Automation.SwitchParameter])
            $hasDefault = ($null -ne $pa.DefaultValue)
            $mandatory = $false
            $position = -1
            $setNames = @()
            foreach ($attr in $pa.Attributes) {
                if ($attr -isnot [System.Management.Automation.Language.AttributeAst]) { continue }
                if ($attr.TypeName.Name -ne 'Parameter') { continue }
                foreach ($na in $attr.NamedArguments) {
                    switch ($na.ArgumentName) {
                        'Mandatory' {
                            if ($na.Argument -is [System.Management.Automation.Language.ConstantExpressionAst]) {
                                $mandatory = [bool]$na.Argument.Value
                            }
                            else { $mandatory = $true }
                        }
                        'Position' {
                            if ($na.Argument -is [System.Management.Automation.Language.ConstantExpressionAst]) {
                                $position = [int]$na.Argument.Value
                            }
                        }
                        'ParameterSetName' {
                            if ($na.Argument -is [System.Management.Automation.Language.ConstantExpressionAst]) {
                                $setNames += [string]$na.Argument.Value
                            }
                        }
                    }
                }
            }
            if ($setNames.Count -eq 0) { $setNames += '__ALL__' }

            $parameters[$name] = [pscustomobject]@{
                Name       = $name
                Switch     = $isSwitch
                Mandatory  = ($mandatory -and -not $isSwitch -and -not $hasDefault)
                Position   = $position
                Sets       = $setNames
                HasDefault = $hasDefault
            }
            foreach ($s in $setNames) { if ($s -ne '__ALL__' -and $sets -notcontains $s) { $sets += $s } }
        }
    }

    return [pscustomobject]@{
        Name                 = $FunctionAst.Name
        Source               = $Source
        Parameters           = $parameters
        ParameterSets        = $sets
        DefaultParameterSet  = $defaultSet
    }
}

<#
.SYNOPSIS
    收集框架模块里所有 function 的签名（同名以后定义者为准，与运行期一致）。
#>
function Get-GateBindingSignatureMap {
    param([Parameter(Mandatory = $true)][string[]]$FrameworkPath)

    $map = @{}
    foreach ($p in @($FrameworkPath)) {
        if (-not (Test-Path -LiteralPath $p)) { continue }
        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($p, [ref]$tokens, [ref]$errors)
        if ($null -eq $ast) { continue }
        $funcs = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)
        foreach ($f in @($funcs)) {
            $map[$f.Name] = ConvertTo-GateBindingSignature -FunctionAst $f -Source $p
        }
    }
    return $map
}

<#
.SYNOPSIS
    静态检查框架函数调用点的参数绑定。
#>
function Test-GateCallBinding {
    [CmdletBinding()]
    param(
        [string]$Root = '',
        [string[]]$FrameworkPath = @(),
        [string[]]$ScanPath = @(),
        [switch]$FailOnViolation
    )

    if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateBindingRoot -Start $PSScriptRoot }
    $Root = ([string]$Root).TrimEnd([char[]]@('\', '/'))

    if (@($FrameworkPath).Count -eq 0) {
        $FrameworkPath = @(
            'Frame\PowerShellScript\Baselines\GateRuntime.psm1',
            'Frame\PowerShellScript\Baselines\GateParsing.psm1',
            'Frame\PowerShellScript\Baselines\GateFileIndex.psm1',
            'Frame\PowerShellScript\Baselines\GateStructureFacts.psm1',
            'Frame\PowerShellScript\Baselines\GateFacts.psm1',
            'Frame\PowerShellScript\Baselines\GateClosure.psm1',
            'Frame\PowerShellScript\Baselines\GateBaseline.psm1',
            'Frame\PowerShellScript\Baselines\DeclarationPathResolver.psm1',
            'Frame\PowerShellScript\Baselines\GateFaceReconciliation.psm1',
            'Frame\PowerShellScript\Baselines\ArtifactFreshness.psm1',
            'Frame\PowerShellScript\Baselines\VerifyScope.psm1',
            'Frame\PowerShellScript\Operation\DSL\Meta\TextOps.psm1',
            'Frame\PowerShellScript\Modules\Node\NodeGateCommon.ps1',
            'Frame\PowerShellScript\Modules\Node\NodePlacementSurface.psm1'
        )
    }
    $resolvedFramework = @()
    foreach ($p in @($FrameworkPath)) {
        $full = if ([System.IO.Path]::IsPathRooted($p)) { $p } else { Join-Path $Root $p }
        if (Test-Path -LiteralPath $full) { $resolvedFramework += $full }
    }
    $signatures = Get-GateBindingSignatureMap -FrameworkPath $resolvedFramework

    if (@($ScanPath).Count -eq 0) {
        $ScanPath = @('Environment\CentralTest\Tools\Gate', 'Frame\PowerShellScript')
    }
    $targets = @()
    foreach ($d in @($ScanPath)) {
        $full = if ([System.IO.Path]::IsPathRooted($d)) { $d } else { Join-Path $Root $d }
        if (-not (Test-Path -LiteralPath $full)) { continue }
        $targets += @(Get-ChildItem -LiteralPath $full -Recurse -File -Include *.ps1, *.psm1 -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput|_deprecated|Runtime\\Payload|Runtime\.staging)\\' })
    }

    $results = [System.Collections.Generic.List[object]]::new()
    $namePattern = ($signatures.Keys | ForEach-Object { [regex]::Escape($_) }) -join '|'
    if ([string]::IsNullOrEmpty($namePattern)) {
        return @()
    }
    $quick = [regex]::new("(?i)\b($namePattern)\b")

    foreach ($tf in $targets) {
        $raw = [System.IO.File]::ReadAllText($tf.FullName)
        # 粗筛只为性能：命中任一框架函数名才做 AST（判据本身仍是 AST）。
        if (-not $quick.IsMatch($raw)) { continue }

        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($tf.FullName, [ref]$tokens, [ref]$errors)
        if ($null -eq $ast) { continue }

        $localFns = @{}
        foreach ($f in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
            $localFns[$f.Name] = $true
        }

        $cmds = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true)
        foreach ($cmd in @($cmds)) {
            $name = $cmd.GetCommandName()
            if ([string]::IsNullOrWhiteSpace($name)) { continue }
            if ($localFns.ContainsKey($name)) { continue }
            if (-not $signatures.ContainsKey($name)) { continue }

            $sig = $signatures[$name]
            $named = @{}
            $positional = 0
            $splat = $false

            $elements = @($cmd.CommandElements)
            for ($i = 1; $i -lt $elements.Count; $i++) {
                $el = $elements[$i]
                if ($el -is [System.Management.Automation.Language.CommandParameterAst]) {
                    $named[$el.ParameterName] = $true
                    continue
                }
                if ($el -is [System.Management.Automation.Language.VariableExpressionAst] -and $el.Splat) {
                    $splat = $true
                    continue
                }
                $positional++
            }

            $unknown = @()
            foreach ($n in $named.Keys) {
                if ($sig.Parameters.ContainsKey($n)) { continue }
                if ($script:GateBindingCommonParameters -contains $n) { continue }
                $unknown += $n
            }

            $line = $cmd.Extent.StartLineNumber
            if ($unknown.Count -gt 0) {
                $results.Add([pscustomobject]@{
                    Severity  = 'BindingFailure'
                    File      = $tf.FullName
                    Line      = $line
                    Function  = $name
                    Parameter = ($unknown -join ',')
                    Issue     = "未知参数（$($sig.Source | Split-Path -Leaf):$name 的 param 块无此名）"
                    Detail    = "传入了 $($unknown -join ',')；运行期必然 ParameterBindingException"
                })
            }

            if ($splat) {
                $results.Add([pscustomobject]@{
                    Severity  = 'Undecidable'
                    File      = $tf.FullName
                    Line      = $line
                    Function  = $name
                    Parameter = '@splat'
                    Issue     = 'splat 展开，参数集不可静态判定'
                    Detail    = '已写明具名参数的检查仍然生效；splat 携带部分不判红/不判绿'
                })
                continue
            }

            # 位置参数 → 按 Position 绑定（仅用于「是否已传」判断）。
            $byPosition = @{}
            foreach ($p in $sig.Parameters.Values) {
                if ($p.Position -ge 0) { $byPosition[[int]$p.Position] = $p.Name }
            }
            $pos = 0
            foreach ($k in ($byPosition.Keys | Sort-Object)) {
                if ($pos -ge $positional) { break }
                if (-not $named.ContainsKey($byPosition[$k])) { $named[$byPosition[$k]] = $true }
                $pos++
            }

            # 候选参数集：默认集优先；否则由已传参数唯一确定；否则全候选。
            $candidates = @()
            if ($sig.ParameterSets.Count -eq 0) {
                $candidates = @('__ALL__')
            }
            elseif ($sig.ParameterSets.Count -eq 1) {
                $candidates = @($sig.ParameterSets[0])
            }
            else {
                $deduced = @($sig.ParameterSets | Where-Object {
                    $sn = $_
                    $requiredInSet = @($sig.Parameters.Values | Where-Object { $_.Sets -contains $sn -and $_.Mandatory } | ForEach-Object { $_.Name })
                    $requiredInSet.Count -gt 0 -and @($requiredInSet | Where-Object { $named.ContainsKey($_) }).Count -ge 1
                })
                $candidates = if ($deduced.Count -gt 0) { $deduced } else { @($sig.ParameterSets) }
            }
            if (-not [string]::IsNullOrWhiteSpace($sig.DefaultParameterSet) -and $sig.ParameterSets.Contains($sig.DefaultParameterSet)) {
                $candidates = @($sig.DefaultParameterSet)
            }

            # 必填缺失：任一候选集被满足即通过；否则报「最少缺失」的那一组。
            $bestMissing = $null
            $satisfied = $false
            foreach ($setName in $candidates) {
                $missing = @()
                foreach ($p in $sig.Parameters.Values) {
                    if (-not $p.Mandatory) { continue }
                    if ($setName -ne '__ALL__' -and $p.Sets -notcontains $setName -and $p.Sets -notcontains '__ALL__') { continue }
                    if (-not $named.ContainsKey($p.Name)) { $missing += $p.Name }
                }
                if ($missing.Count -eq 0) { $satisfied = $true; break }
                if ($null -eq $bestMissing -or $missing.Count -lt $bestMissing.Count) { $bestMissing = $missing }
            }
            if (-not $satisfied -and $null -ne $bestMissing) {
                $results.Add([pscustomobject]@{
                    Severity  = 'MissingMandatory'
                    File      = $tf.FullName
                    Line      = $line
                    Function  = $name
                    Parameter = ($bestMissing -join ',')
                    Issue     = "必填参数未传（参数集 $(if ($candidates.Count -eq 1) { $candidates[0] } else { '任一候选' })）"
                    Detail    = "缺 $($bestMissing -join ',')；无默认值且非 switch，调用必然失败或交互提示"
                })
            }

            # 互斥冲突：两个已传参数在任一参数集中都不共存。
            if ($sig.ParameterSets.Count -gt 1 -and $named.Count -gt 1) {
                $passed = @($sig.Parameters.Values | Where-Object { $named.ContainsKey($_.Name) -and $_.Sets -notcontains '__ALL__' })
                for ($a = 0; $a -lt $passed.Count; $a++) {
                    for ($b = $a + 1; $b -lt $passed.Count; $b++) {
                        $pa = $passed[$a]; $pb = $passed[$b]
                        $overlap = @($pa.Sets | Where-Object { $pb.Sets -contains $_ })
                        if ($overlap.Count -eq 0) {
                            $results.Add([pscustomobject]@{
                                Severity  = 'Conflict'
                                File      = $tf.FullName
                                Line      = $line
                                Function  = $name
                                Parameter = "$($pa.Name),$($pb.Name)"
                                Issue     = '参数集互斥（同时传入不会共存）'
                                Detail    = "$($pa.Name) ∈ {$($pa.Sets -join ',')}；$($pb.Name) ∈ {$($pb.Sets -join ',')}"
                            })
                        }
                    }
                }
            }
        }
    }

    $hard = @($results | Where-Object { $_.Severity -in @('BindingFailure', 'MissingMandatory', 'Conflict') })
    if ($FailOnViolation -and $hard.Count -gt 0) {
        Write-Host ("[GateCallBinding] 违规 {0} 项（不可判定 {1} 项）" -f $hard.Count, (@($results | Where-Object { $_.Severity -eq 'Undecidable' }).Count)) -ForegroundColor Red
        foreach ($v in $hard) {
            Write-Host ("  - [{0}] {1}:{2} {3} ← {4} :: {5}" -f $v.Severity, (Split-Path $v.File -Leaf), $v.Line, $v.Function, $v.Parameter, $v.Detail) -ForegroundColor Red
        }
        exit 1
    }
    return $results
}

Export-ModuleMember -Function @(
    'Get-GateBindingSignatureMap',
    'Test-GateCallBinding'
)
