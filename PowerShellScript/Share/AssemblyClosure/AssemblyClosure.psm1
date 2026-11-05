# ============================================================
# AssemblyClosure.psm1 — 装配闭包单一实现
#   职责：把「装载组声明 → 根 / 清单 / 闭包判定」收成一份实现。
#   消费者（唯一实现，禁各自拼路径）：
#     · Operation\DSL\Meta\Check-AssemblyClosure.ps1（门禁）
#     · Operation\DSL\Meta\Check-BuildClosure.ps1（闭包同源）
#     · Share\Build\Test-BuildGate.ps1（关键产物改由闭包派生）
#     · Share\Build\Invoke-QuickBuild.ps1（扩展清单不再内联）
#     · Share\Launch\Invoke-Launch.ps1（启动前前置断言）
#   判据载体 = 声明文件（assembly-closure.json）∪ deps.json ∪ 磁盘资产；
#   禁读执行者叙述（权威: axioms SEP.JudgmentCarrierStructural）。
# ============================================================

function Get-ClosureRepoRoot {
    param([string]$StartPath = $PSScriptRoot)
    $cur = $StartPath
    while ($cur) {
        if ((Test-Path -LiteralPath (Join-Path $cur 'Frame')) -and
            (Test-Path -LiteralPath (Join-Path $cur 'Projects'))) { return (Resolve-Path -LiteralPath $cur).Path }
        $parent = Split-Path -Parent $cur
        if ($parent -eq $cur) { break }
        $cur = $parent
    }
    throw "装配闭包：仓库超根定位失败（起点=$StartPath）"
}

function Get-AssemblyClosureConfig {
    param([string]$RepoRoot, [string]$ConfigPath)
    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        $ConfigPath = Join-Path $RepoRoot 'Projects\Information\DrivenConfig\Configs\assembly-closure.json'
    }
    if (-not (Test-Path -LiteralPath $ConfigPath)) { throw "装配闭包声明缺失：$ConfigPath" }
    return (Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Expand-ClosureRoot {
    param($Group, [string]$RepoRoot, [string]$Configuration = 'Debug')
    $rel = [string]$Group.root
    $rel = $rel -replace '\$\{Configuration\}', $Configuration
    $rel = $rel -replace '/', '\'
    return [IO.Path]::GetFullPath((Join-Path $RepoRoot $rel))
}

function Get-ClosureGroups {
    param([string]$RepoRoot, [string]$ConfigPath)
    $cfg = Get-AssemblyClosureConfig -RepoRoot $RepoRoot -ConfigPath $ConfigPath
    return @($cfg.groups)
}

# 闭包判定：返回单组状态。Missing 为空 ⇒ 该组闭包完整。
function Get-ClosureGroupState {
    param($Group, [string]$RepoRoot, [string]$Configuration = 'Debug')

    $root = Expand-ClosureRoot -Group $Group -RepoRoot $RepoRoot -Configuration $Configuration
    $manifest = $null
    if (-not [string]::IsNullOrWhiteSpace([string]$Group.manifest)) {
        $manifest = Join-Path $root ([string]$Group.manifest)
    }

    $missing = New-Object System.Collections.Generic.List[string]
    $rootExists = Test-Path -LiteralPath $root
    $manifestExists = ($null -ne $manifest) -and (Test-Path -LiteralPath $manifest)

    if (-not $rootExists) {
        $missing.Add("root:$root")
    } else {
        foreach ($entry in @($Group.entry)) {
            if ([string]::IsNullOrWhiteSpace([string]$entry)) { continue }
            if (-not (Test-Path -LiteralPath (Join-Path $root ([string]$entry)))) {
                $missing.Add("entry:$entry")
            }
        }
    }

    if ($null -ne $manifest -and -not $manifestExists) {
        $missing.Add("manifest:$($Group.manifest)")
    }

    # 依赖闭包：deps.json 中前缀命中库的 runtime 资产须全部落盘（派生，不手抄清单）。
    if ($manifestExists) {
        try {
            $deps = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
            $prefixes = @($Group.prefixes | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
            if ($prefixes.Count -gt 0) {
                foreach ($targetProp in $deps.targets.PSObject.Properties) {
                    foreach ($libProp in $targetProp.Value.PSObject.Properties) {
                        $libName = ([string]$libProp.Name).Split('/')[0]
                        $hit = $false
                        foreach ($p in $prefixes) { if ($libName -like ("{0}*" -f $p)) { $hit = $true; break } }
                        if (-not $hit) { continue }
                        $runtime = $libProp.Value.runtime
                        if ($null -eq $runtime) { continue }
                        foreach ($assetProp in $runtime.PSObject.Properties) {
                            $asset = ([string]$assetProp.Name) -replace '/', '\'
                            if ($asset -match '^(runtimes|ref)\\') { continue }
                            if (-not (Test-Path -LiteralPath (Join-Path $root $asset))) {
                                $missing.Add("asset:$libName/$asset")
                            }
                        }
                    }
                }
            }
        }
        catch {
            $missing.Add("deps-unreadable:$($Group.manifest)")
        }
    }

    return [pscustomobject]@{
        Id             = [string]$Group.id
        Required       = [bool]$Group.required
        BuildTarget    = [string]$Group.buildTarget
        Prefixes       = @($Group.prefixes)
        Root           = $root
        Manifest       = $manifest
        RootExists     = $rootExists
        ManifestExists = $manifestExists
        Missing        = @($missing)
    }
}

function Get-ClosureStates {
    param([string]$RepoRoot, [string]$Configuration = 'Debug', [string]$ConfigPath)
    $states = foreach ($g in (Get-ClosureGroups -RepoRoot $RepoRoot -ConfigPath $ConfigPath)) {
        Get-ClosureGroupState -Group $g -RepoRoot $RepoRoot -Configuration $Configuration
    }
    return @($states)
}

# 函数库 → 装配组 归属（单一来源 = function-libraries.json；不复制类型名清单）。
function Get-ClosureFunctionLibraryOwnership {
    param([string]$RepoRoot, [string]$Configuration = 'Debug', [string]$ConfigPath)

    $libsPath = Join-Path $RepoRoot 'Projects\Information\DrivenConfig\Configs\function-libraries.json'
    if (-not (Test-Path -LiteralPath $libsPath)) { throw "函数库清单缺失：$libsPath" }
    $libs = @((Get-Content -LiteralPath $libsPath -Raw -Encoding UTF8 | ConvertFrom-Json).libraries |
        Where-Object { $_.enabled -ne $false })

    $roots = @(Get-ClosureStates -RepoRoot $RepoRoot -Configuration $Configuration -ConfigPath $ConfigPath |
        ForEach-Object { [pscustomobject]@{ Id = $_.Id; Root = $_.Root; Prefixes = @($_.Prefixes) } })

    $result = foreach ($lib in $libs) {
        $typeStr = [string]$lib.type
        $asm = ''
        if ($typeStr -match ',\s*([^,]+)\s*$') { $asm = $Matches[1].Trim() }
        $found = New-Object System.Collections.Generic.List[string]
        if ($asm) {
            # 归属判据：程序集名前缀命中唯一装配组 ⇒ 只认该组（其它目录里的同名件是复制件，不是第二装载源）；
            # 无前缀命中（共享库，如 FileDriven.Business）：存在性 ≥1 即可。
            $matched = @($roots | Where-Object {
                $ps = @($_.Prefixes | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
                @($ps | Where-Object { $asm -like ("{0}*" -f $_) }).Count -gt 0
            })
            if ($matched.Count -eq 1) {
                if (Test-Path -LiteralPath (Join-Path $matched[0].Root ("{0}.dll" -f $asm))) { $found.Add($matched[0].Id) }
            }
            else {
                foreach ($r in $roots) {
                    if (Test-Path -LiteralPath (Join-Path $r.Root ("{0}.dll" -f $asm))) { $found.Add($r.Id) }
                }
            }
        }
        [pscustomobject]@{
            Name        = [string]$lib.name
            Assembly    = $asm
            Required    = [bool]$lib.required
            FoundGroups = @($found)
        }
    }
    return @($result)
}

Export-ModuleMember -Function Get-ClosureRepoRoot, Get-AssemblyClosureConfig, Get-ClosureGroups,
    Get-ClosureGroupState, Get-ClosureStates, Expand-ClosureRoot, Get-ClosureFunctionLibraryOwnership

