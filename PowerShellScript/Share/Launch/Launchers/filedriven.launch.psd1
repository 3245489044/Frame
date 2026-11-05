@{
    # ============================================================
    # 启动契约（LaunchContract）—— filedriven
    #   唯一真源：启动面的全部常量（退出口 / 参数白名单 / 环境 / 窗口 / trace / 前置）
    #   消费方：Share\Launch\Invoke-LaunchEntry.ps1（S0–S7）· Invoke-LaunchSelfTest.ps1
    #           · Operation\DSL\Meta\Check-LaunchContract.ps1 / Check-LaunchExitCodes.ps1 / Check-LaunchTrace.ps1
    #   判据载体：表项 + 文件存在性（结构事实）；装载面根只写 Group，由装配闭包解析，禁写路径。
    # ============================================================
    Schema = 'launch-contract-v1'
    Product = 'filedriven'

    # 缺省入口（入口 id ↔ 批文件名的映射见 Entries；id 用 ASCII，批文件名可含中文）
    DefaultEntry = 'gui'

    Host = @{
        Group       = 'filedriven'
        Entry       = 'FileDriven.Launcher.exe'
        WorkingRoot = 'RepoRoot'
        Interactive = @{
            Script = 'Frame\PowerShellScript\FileDriven\FileDriven\运行测试GUI模式.ps1'
        }
    }

    Build = @{
        Required      = $true
        Entry         = 'Projects\BuildFramework\Entry\Build-Effective.ps1'
        Mode          = 'dev'
        Configuration = 'Debug'
    }

    Preconditions = @('assembly-closure')

    ContractPreflight = @{
        Script  = 'Projects\BuildFramework\Safety\Assert-UiLaunchContract.ps1'
        Default = 'off'
        Allowed = @('off', 'load', 'stale')
    }

    Smoke = @{
        Script         = 'Frame\PowerShellScript\Share\Launch\Invoke-LaunchSmoke.ps1'
        TimeoutSeconds = 90
        AliveSeconds   = 5
        Default        = $false
    }

    ExitCodes = @{
        0 = 'ok'
        2 = 'precondition'
        3 = 'closure'
        4 = 'build'
        5 = 'busy'
        6 = 'smoke'
    }

    # 框架开关白名单（白名单外的一律报错退出 2，禁静默透传）
    Parameters = @{
        '--no-build'           = 'flag'
        '--fast'               = 'flag'
        '--full'               = 'flag'
        '--build-only'         = 'flag'
        '--gate-only'          = 'flag'
        '--diagnose'           = 'flag'
        '--kill-host'          = 'flag'
        '--any-instance'       = 'flag'
        '--smoke'              = 'flag'
        '--no-pause'           = 'flag'
        '--interactive'        = 'flag'
        '--contract-preflight' = 'value'
        '--log'                = 'value'
        '--configuration'      = 'value'
    }

    # 原样转交宿主的参数（值型：无 '=' 时吞下一个非 '--' token）
    Passthrough = @('--file', '--script', '--exec', '--cli', '--config', '--mode', '--batch', '--no-gui', '--help')

    # 入口映射：每个 EnableMent\*.bat 恰映射到一个 id（K2 判据：双向覆盖）
    Entries = @{
        gui         = @{
            Script      = 'UI启动.bat'
            Args        = @()
            Env         = @{}
            Interactive = $false
        }
        interactive = @{
            Script      = '交互启动.bat'
            Args        = @('--interactive', '--log', 'WdUI.All,FileDriven.All')
            Env         = @{
                FILEDRIVEN_BATCH_NAME   = '交互启动测试'
                FILEDRIVEN_BATCH_SOURCE = '交互启动.bat'
                FILEDRIVEN_BATCH_DESC   = '从交互启动.bat 启动的测试运行'
            }
            Interactive = $true
        }
    }

    # 启动上下文（空值才注入；调用方显式设置优先）
    Environment = @{
        AGENT_DATACENTER_ROOT_CELL          = '1'
        FILEDRIVEN_REQUIRE_ASSEMBLY_CLOSURE = '1'
        FILEDRIVEN_BATCH_NAME               = 'UI 启动测试'
        FILEDRIVEN_BATCH_SOURCE             = 'UI启动.bat'
        FILEDRIVEN_BATCH_DESC               = '从 UI启动.bat 启动的 GUI 模式运行'
    }

    Window = @{
        HoldOnExit    = 'until-key'
        HoldOnFailure = 'always'
        Title         = 'FileDriven Launcher'
    }

    Trace = @{
        Dir      = 'Data\Tests\launch'
        Tee      = @('stdout', 'stderr')
        KeepDays = 14
    }

    Gates = @('LaunchEntrypoint', 'LaunchContract', 'LaunchExitCodes', 'LaunchTrace')
}
