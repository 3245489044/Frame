# Invoke-PsContract.ps1 — stage 化执行（复用 C# IPipelineStage 思想）
# stage 链: ValidateParams -> ValidateEnv -> ExecuteFunction -> CollectResult
# 注：内联 stage 而非 scriptblock wrapper，确保 . $path 与 & $function 同 scope（PS 5.1 scoping）

function Invoke-PsContract {
    param(
        [Parameter(Mandatory)][AllowNull()][hashtable]$Contract,
        [Parameter(Mandatory)][AllowNull()][hashtable]$Arguments,
        [scriptblock]$AfterStageHook
    )
    $ctx = [PSCustomObject]@{
        stages = [System.Collections.Generic.List[string]]::new()
        aborted = $false
        stage = ""
        raw = $null
        result = $null
    }

    function script:_InvokeHook {
        $ctx.stages.Add($ctx.stage)
        if ($AfterStageHook) { & $AfterStageHook $ctx }
    }

    # Stage 1: ValidateParams
    if (-not $ctx.aborted) {
        $ctx.stage = "ValidateParams"
        $p = Test-PsContractParams -Contract $Contract -Arguments $Arguments
        if (-not $p.IsValid) {
            $ctx.result = New-PsResult -Status failure -Message "param validation failed" -Errors $p.Errors
            $ctx.aborted = $true
        }
        script:_InvokeHook
    }
    # Stage 2: ValidateEnv
    if (-not $ctx.aborted) {
        $ctx.stage = "ValidateEnv"
        $e = Test-PsContractEnv -Contract $Contract
        if (-not $e.IsValid) {
            $ctx.result = New-PsResult -Status failure -Message "env validation failed" -Errors $e.Errors
            $ctx.aborted = $true
        }
        script:_InvokeHook
    }
    # Stage 3: ExecuteFunction（Import-Module 加载 .psm1，函数进 session scope）
    if (-not $ctx.aborted) {
        $ctx.stage = "ExecuteFunction"
        try {
            Import-Module $Contract.identity.path -Force -ErrorAction Stop
            $ctx.raw = & $Contract.identity.function @Arguments
        } catch {
            $ctx.result = New-PsResult -Status failure -Message "execution error" -Errors @($_.Exception.Message)
            $ctx.aborted = $true
        }
        script:_InvokeHook
    }
    # Stage 4: CollectResult
    if (-not $ctx.aborted) {
        $ctx.stage = "CollectResult"
        $ctx.result = ConvertTo-PsResult -InputObject $ctx.raw
        script:_InvokeHook
    }

    # 兜底
    if ($null -eq $ctx.result) {
        $ctx.result = New-PsResult -Status failure -Message "no stage produced result"
    }

    # 注入 stage 链到 data（Hashtable / 真实 PSCustomObject 可扩展成员 → data 保持平铺；字符串/数组/原始类型才包 payload）
    # 注：不能用 $data -is [pscustomobject] 判定——函数经管道返回的字符串也可能 -is [pscustomobject]，须按真实运行时类型 GetType() 区分。
    $stagesArr = $ctx.stages.ToArray()
    $data = $ctx.result.data
    $dataType = if ($null -ne $data) { $data.GetType() } else { $null }
    $isStruct = ($dataType -eq [System.Collections.Hashtable]) -or ($dataType -eq [System.Management.Automation.PSCustomObject])
    if ($null -ne $data -and $isStruct) {
        if ($dataType -eq [System.Collections.Hashtable]) { $data["_stages"] = $stagesArr }
        else { $data.PSObject.Properties.Add([System.Management.Automation.PSNoteProperty]::new("_stages", $stagesArr)) }
    } else {
        $ctx.result = New-PsResult -Status $ctx.result.status -Message $ctx.result.message `
            -Data @{ payload = $data; _stages = $stagesArr } -Errors $ctx.result.errors
    }
    return $ctx.result
}
