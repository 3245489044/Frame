@{
    # ===== LaunchEntrypoint 门禁基线（单源；守卫 = Operation\DSL\Process\Check-LaunchEntrypoint.ps1）=====
    # 语义：K1 入口零逻辑 —— 外部 bat 只是唯一入口的 2 行转发器（≤ MaxEffectiveLines，禁控制流/编码/pause）。
    #       判据载体 = bat 文本结构（去注释/空行后的行数 + 禁用 token），禁执行者叙述。
    Schema = 'gate-baseline-v1'
    Gate   = 'LaunchEntrypoint'

    EntryDirRelative   = @{ Value = 'EnableMent'; Origin = 'Derived'; DueWhen = '' }
    MaxEffectiveLines  = @{ Value = 5; Origin = 'Verdict'; DueWhen = 'K1 判据本体（过渡白名单可≤5行，目标态 2 行）' }
    ForbiddenTokens    = @{ Value = @('if', 'set', 'pause', 'chcp', 'call', 'for'); Origin = 'Verdict'; DueWhen = 'K1 禁用 token 集（D6 解析事故面）' }
    RequiredPattern    = @{ Value = 'Invoke-LaunchBootstrap.ps1'; Origin = 'Verdict'; DueWhen = '唯一入口：bat 只经引导件解析 pwsh（禁裸调 pwsh，双击环境 PATH 无此命令）' }
    RequiredProductArg = @{ Value = '-Product'; Origin = 'Derived'; DueWhen = '' }
    MinEntryScripts    = @{ Value = 2; Origin = 'Derived'; DueWhen = '' }
}

