@{
    # ===== LaunchExitCodes 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-LaunchExitCodes.ps1）=====
    # 语义：退出码是契约而非注释 —— 常量退出码 ⊆ 契约 ExitCodes；契约非零码 ⊆ 实现常量码；
    #       0 只由 S6 的进程结构事实产生（禁实现内 `exit 0` 常量）。
    Schema = 'gate-baseline-v1'
    Gate   = 'LaunchExitCodes'

    ExecutorRelative  = @{ Value = 'Frame\PowerShellScript\Share\Launch\Invoke-LaunchEntry.ps1'; Origin = 'Derived'; DueWhen = '' }
    WaitAnchor        = @{ Value = 'WaitForExit('; Origin = 'Derived'; DueWhen = '' }
    ProcessExitAnchor = @{ Value = 'exit $processExitCode'; Origin = 'Derived'; DueWhen = '' }
    LiteralExitPattern = @{ Value = '(?m)^\s*Exit-Launch\s+-Code\s+(\d+)\s*$'; Origin = 'Derived'; DueWhen = '' }
    ZeroLiteralPattern = @{ Value = '(?m)^\s*Exit-Launch\s+-Code\s+0\s*$'; Origin = 'Verdict'; DueWhen = 'K3 判据本体：0 不得为默认值（仅 S6 进程退出码可为 0）' }
}
