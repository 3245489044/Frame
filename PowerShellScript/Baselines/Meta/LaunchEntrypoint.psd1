@{
    # ===== LaunchEntrypoint 门禁基线（单源；守卫 = Operation\DSL\Process\Check-LaunchEntrypoint.ps1）=====
    # 语义：启动入口唯一 —— 外部 bat 一律薄转发到 Invoke-Frame -Action launch，禁直呼 exe/ps1。
    Schema = 'gate-baseline-v1'
    Gate   = 'LaunchEntrypoint'

    EntryDirRelative   = @{ Value = 'EnableMent'; Origin = 'Derived'; DueWhen = '' }
    ForbiddenPatterns  = @{ Value = @('FileDriven.Launcher.exe', '运行测试GUI模式.ps1'); Origin = 'Verdict'; DueWhen = '' }
    RequiredPattern    = @{ Value = 'Invoke-Frame.ps1'; Origin = 'Verdict'; DueWhen = '' }
    MinEntryScripts    = @{ Value = 2; Origin = 'Derived'; DueWhen = '' }
}
