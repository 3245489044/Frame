@{
    # ===== LaunchSmoke 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-LaunchSmoke.ps1）=====
    # 语义：macro 判据「能启动」= GUI 进程存活 AliveSeconds 且未新增 bootstrap 失败/函数库降级诊断。
    #      判据载体 = 进程生命周期 + 诊断文件集合（结构事实；禁读 console 文本）。
    #      启动器路径由装配闭包解析（契约 Host.Group），基线不再写 BuildOutput 路径。
    Schema = 'gate-baseline-v1'
    Gate   = 'LaunchSmoke'

    AliveSeconds   = @{ Value = 5; Origin = 'Verdict'; DueWhen = 'GUI 存活断言窗口（bootstrap 已越过的最小观察期）' }
    DefaultProduct = @{ Value = 'filedriven'; Origin = 'Derived'; DueWhen = '' }
}

