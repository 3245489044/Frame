<#
.SYNOPSIS
    标准存储资源节点写入（薄垫片）—— store.resource 确定性动作

.DESCRIPTION
    把 {resourceType,id,symbol,metadata} 委托给存储桥（C# 单一写入口
    IResourceReferenceStore.AddResourceAsync，ur_resources SQLite 真源）。
    PowerShell 只做参数校验与传输委托，不复制 ur_resources schema（无双真源，SingleImplementation）。
    存储桥配置读自 Agent/Config/ps1-runtime.json 的 storage 段；未配置 / host 缺失 →
    failure + storage_bridge_missing（供调用方诊断，不写库）。

.EXAMPLE
    Store-Resource -ResourceType analysis -Id "Storage\xxx" -Symbol "analysis:Storage\xxx" -Metadata $json
#>
function Store-Resource {
    param(
        [string]$ResourceType = "node",
        [Parameter(Mandatory)]
        [string]$Id,

        [string]$Symbol = "",

        $Metadata = $null,

        # 存储桥配置（由薄绑定 Invoke-AgentStep 注入；Storage 模块不自寻 Agent 路径）
        $Storage = $null
    )

    if ([string]::IsNullOrWhiteSpace($Id)) {
        return New-PsResult -Status failure -Message "param validation failed" -Errors @("Id_required")
    }

    if ($null -eq $Storage -or [string]::IsNullOrWhiteSpace($Storage.host) -or -not (Test-Path $Storage.host)) {
        return New-PsResult -Status failure -Message "storage bridge not configured" -Errors @("storage_bridge_missing")
    }

    $rt = if ([string]::IsNullOrWhiteSpace($ResourceType)) { "node" } else { $ResourceType }
    $sym = if ([string]::IsNullOrWhiteSpace($Symbol)) { "" } else { $Symbol }
    $metaJson = ""
    if ($null -ne $Metadata) {
        $metaJson = if ($Metadata -is [string]) { $Metadata } else { $Metadata | ConvertTo-Json -Depth 10 -Compress }
    }

    $cmd = if ([string]::IsNullOrWhiteSpace($Storage.command)) { "store-resource" } else { $Storage.command }
    try {
        $args = @($cmd, "--resourceType", $rt, "--id", $Id, "--symbol", $sym)
        if ($metaJson) { $args += @("--metadata", $metaJson) }
        $output = & $Storage.host @args 2>&1
        $text = ($output | Out-String).Trim()
        if (-not $text) {
            return New-PsResult -Status failure -Message "storage bridge empty output" -Errors @("storage_bridge_empty")
        }
        # 桥返回的完整信封 {status,message,data,errors}；Invoke-PsContract 会对 data 注入 _stages 且保持平铺
        return ($text | ConvertFrom-Json)
    } catch {
        return New-PsResult -Status failure -Message "storage bridge invocation error" -Errors @("storage_bridge_error", $_.Exception.Message)
    }
}

Export-ModuleMember -Function Store-Resource