@{
    # ===== LaunchTrace 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-LaunchTrace.ps1）=====
    # 语义：任何一次启动/失败都能只用 trace 文件复算（头部可复算 + 阶段行 + 判定证据）。
    Schema = 'gate-baseline-v1'
    Gate   = 'LaunchTrace'

    ExecutorRelative = @{ Value = 'Frame\PowerShellScript\Share\Launch\Invoke-LaunchEntry.ps1'; Origin = 'Derived'; DueWhen = '' }
    LogMarkers       = @{ Value = @('trace_version=1', 'contract_sha256=', '[S2] 参数解析', 'decision='); Origin = 'Derived'; DueWhen = '' }
    DiagnoseArgs     = @{ Value = @('--diagnose', '--no-pause'); Origin = 'Derived'; DueWhen = '' }
    MaxSeconds       = @{ Value = 60; Origin = 'Derived'; DueWhen = '' }
}
