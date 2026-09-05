# ContractScaffold.psm1 — 合规脚手架生成（动词优先标签 + sidecar）
# 边界: null spec / 缺 action / 缺 function / 空参数 / 目录不存在 均安全兜底

function New-PsContractScript {
    param([hashtable]$Spec, [string]$OutputPath)
    try {
        # 边界: spec 为 null
        if (-not $Spec) { return New-PsResult -Status failure -Message "spec null" -Errors @("spec_null") }
        # 边界: 缺 action
        if (-not $Spec.ContainsKey("action") -or -not $Spec.action) { return New-PsResult -Status failure -Message "spec missing action" -Errors @("action_missing") }
        # 边界: 缺 function
        if (-not $Spec.ContainsKey("function") -or -not $Spec.function) { return New-PsResult -Status failure -Message "spec missing function" -Errors @("function_missing") }

        $fn = $Spec.function
        $params = if ($Spec.ContainsKey("params") -and $Spec.params) { @($Spec.params) } else { @() }

        # 生成 param 块（string/int/switch 分支，default 对 string 加引号）
        $paramLines = @()
        foreach ($p in $params) {
            $line = "    [$($p.type)]`$$($p.name)"
            if ($p.ContainsKey("default") -and $null -ne $p.default) {
                switch ($p.type) {
                    "string" { $line += " = `"$($p.default)`"" }
                    "switch" { }
                    default { $line += " = $($p.default)" }
                }
            }
            $paramLines += $line
        }
        $paramBlock = if ($paramLines.Count -gt 0) { ($paramLines -join ",`n") } else { "" }

        $verb = if ($Spec.ContainsKey("verb") -and $Spec.verb) { $Spec.verb } else { ($Spec.action -split '\.')[0] }
        $object = if ($Spec.ContainsKey("object") -and $Spec.object) { $Spec.object } else { (($Spec.action -split '\.') | Select-Object -Skip 1) -join '.' }
        $purpose = if ($Spec.ContainsKey("purpose") -and $Spec.purpose) { $Spec.purpose } else { "" }

        $body = @"
<#
.SYNOPSIS
    $purpose
#>
# @ps-action: $($Spec.action)
# @verb: $verb
# @object: $object
# @exports: $fn
# @requires: PsRuntime

function $fn {
    param(
$paramBlock
    )
    # TODO: 由 SK 规划器填入实现逻辑，成功后返回 New-PsResult
    return New-PsResult -Status failure -Message "not implemented" -Errors @("scaffold 未填实现")
}

Export-ModuleMember -Function $fn
"@
        # 边界: OutputPath 所在目录不存在时 Set-Content 抛异常，被 catch 捕获
        Set-Content -Path $OutputPath -Value $body -Encoding UTF8

        $sidecar = $OutputPath -replace '\.(ps1|psm1)$', '.contracts.json'
        $contract = @{
            identity = @{
                action = $Spec.action; verb = $verb; object = $object
                purpose = $purpose; function = $fn; path = (Split-Path $OutputPath -Leaf)
            }
            params = $params
            output_schema = @{ status = "success|failure"; message = "string"; data = "object"; errors = "array" }
            env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() }
        }
        @{ contracts = @($contract) } | ConvertTo-Json -Depth 6 | Set-Content -Path $sidecar -Encoding UTF8 -ErrorAction Stop

        return New-PsResult -Status success -Message "scaffold generated" -Data @{ path = $OutputPath; sidecar = $sidecar }
    } catch {
        return New-PsResult -Status failure -Message "scaffold error" -Errors @($_.Exception.Message)
    }
}

Export-ModuleMember -Function New-PsContractScript
