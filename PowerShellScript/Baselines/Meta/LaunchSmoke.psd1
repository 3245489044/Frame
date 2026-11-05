@{
    # ===== LaunchSmoke 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-LaunchSmoke.ps1）=====
    # 语义：macro 判据「能启动」= 越过 bootstrap（进程结束且未新增启动失败/函数库降级诊断）。
    #      判据载体 = 进程退出 + 诊断文件集合（结构事实；禁读 console 文本）。
    Schema = 'gate-baseline-v1'
    Gate   = 'LaunchSmoke'

    TimeoutSeconds = @{ Value = 90; Origin = 'Derived'; DueWhen = '' }
    ProbeExec      = @{ Value = 'Engine_GetStatus'; Origin = 'Derived'; DueWhen = '' }
    LauncherRelative = @{ Value = 'Projects\FileDriven\BuildOutput\${Configuration}\FileDriven.Launcher.exe'; Origin = 'Derived'; DueWhen = '' }
}
