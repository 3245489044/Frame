@{
    # ===== Frame\Invoke 单一入口分发表（纯数据，单源）=====
    # 消费方 = Frame\Invoke\Invoke-Frame.ps1（经 Modules\Dispatcher 的 Import-DispatchTable/Get-DispatchEntry）。
    # 收敛前 19 个 *.ps1 薄转发件 → 本表 19 行；另新增诊断面 read-agentdb（共 20 行）
    # （结构事实；守卫 Check-InvokeEntrypoints 判据⑤ 校验 Target 存在性）。
    #
    # 表项字段：
    #   Kind    : Ps（转发到 PowerShell 脚本） | SpecCli（转发到 SpecSourceGen.Cli）
    #   Target  : Ps 目标路径（相对 Frame\Invoke）
    #   Verb    : SpecCli 子命令（gen / chain-get / …）
    #   Map     : 入口形参名 → 目标形参名（值透传；仅显式绑定或列于 Always 时下发）
    #   Always  : 未显式绑定也下发（取入口默认值）的形参名
    #   Fixed   : 常量目标形参（如转发到「单一入口 + -Action」形态）
    #   Options : SpecCli 选项表 @{ Param; Token; Always?; Switch?; Repeat? }
    #
    # 依赖方向（硬约束）：Frame\Invoke 只转发，不含业务逻辑；本表须为纯数据（Import-PowerShellDataFile）。
    Schema  = 'frame-invoke-v1'
    Entries = @{

        # ---------- 检索 / 校验 ----------
        'search-repo' = @{
            Kind = 'Ps'
            Target = '..\PowerShellScript\Share\Search\Search-Repo.ps1'
            Map = @{ Pattern = 'Pattern'; PathSpec = 'PathSpec'; Repo = 'Repo'; Literal = 'Literal'; Regex = 'Regex'; Untracked = 'Untracked'; List = 'List'; MaxFiles = 'MaxFiles'; FailOnEmpty = 'FailOnEmpty' }
            Always = @('Pattern')
        }
        'test-jsonfiles' = @{
            Kind = 'Ps'
            Target = '..\PowerShellScript\Share\Validate\Test-JsonFiles.ps1'
            Map = @{ Path = 'Path'; Filter = 'Filter'; Exclude = 'Exclude'; Schema = 'Schema'; Quiet = 'Quiet'; IncludeDeprecated = 'IncludeDeprecated' }
            Always = @('Path')
        }

        # ---------- 诊断 / 数据检视 ----------
        'read-agentdb' = @{
            Kind = 'Ps'
            Target = '..\PowerShellScript\Share\Data\Read-AgentDb.ps1'
            Map = @{ Store = 'Store'; Table = 'Table'; Collection = 'Collection'; Where = 'Where'; Limit = 'Limit'; StateRoot = 'StateRoot'; DllDir = 'DllDir'; List = 'List'; Json = 'Json' }
        }
        'watch-observe' = @{
            Kind = 'Ps'
            Target = '..\PowerShellScript\Share\Observe\Watch-AgentObserve.ps1'
            Map = @{ PipeName = 'PipeName'; PipePrefix = 'PipePrefix'; List = 'List'; Probe = 'Probe'; ScopeId = 'ScopeId'; Kind = 'Kind'; Backlog = 'Backlog'; Query = 'Query'; Limit = 'Limit'; TimeoutMs = 'TimeoutMs'; WindowSec = 'WindowSec'; MaxEvents = 'MaxEvents'; Json = 'Json'; Quiet = 'Quiet' }
        }

        # ---------- 门禁 ----------
        'invoke-gatecheck' = @{
            Kind = 'Ps'
            Target = '..\PowerShellScript\Operation\DSL\Tooling\Invoke-GateCheck.ps1'
            Map = @{ All = 'All'; Only = 'Only'; Manifest = 'Manifest'; Tier = 'Tier'; Parallel = 'Parallel' }
        }

        # ---------- 验证 / 回归 / 定位（转发到 CentralTest 单一实现入口）----------
        'invoke-verify' = @{
            Kind = 'Ps'
            Target = '..\..\Environment\CentralTest\Invoke-CentralTest.ps1'
            Map = @{ Changed = 'Changed'; Plan = 'Plan'; Configuration = 'Configuration'; TestSet = 'TestSet'; GrantFile = 'GrantFile' }
            Fixed = @{ Action = 'verify' }
        }
        'invoke-regression' = @{
            Kind = 'Ps'
            Target = '..\..\Environment\CentralTest\Invoke-CentralTest.ps1'
            Map = @{ Matrix = 'Matrix'; Test = 'Test'; Project = 'Project'; Case = 'Case'; Parallel = 'Parallel'; NoKnownRedOk = 'NoKnownRedOk'; WithGates = 'WithGates' }
            Always = @('Matrix')
            Fixed = @{ Action = 'regression' }
        }
        'locate-test' = @{
            Kind = 'Ps'
            Target = '..\..\Environment\CentralTest\Invoke-CentralTest.ps1'
            Map = @{ Name = 'Name'; Configuration = 'Configuration' }
            Always = @('Name')
            Fixed = @{ Action = 'locate-test' }
        }
        'locate-build' = @{
            Kind = 'Ps'
            Target = '..\..\Environment\CentralTest\Invoke-CentralTest.ps1'
            Map = @{ Changed = 'Changed'; Configuration = 'Configuration'; TestSet = 'TestSet'; Json = 'Json'; OutFile = 'OutFile' }
            Always = @('Changed')
            Fixed = @{ Action = 'locate-build' }
        }
        'locate-port' = @{
            Kind = 'Ps'
            Target = '..\..\Environment\CentralTest\Invoke-CentralTest.ps1'
            Map = @{ Key = 'Key'; All = 'All'; Json = 'Json'; OutFile = 'OutFile'; ShowDetail = 'ShowDetail' }
            Fixed = @{ Action = 'locate-port' }
        }

        # ---------- 生效 ----------
        'invoke-buildeffective' = @{
            Kind = 'Ps'
            Target = '..\..\Projects\BuildFramework\Build-Effective.ps1'
            Map = @{ Configuration = 'Configuration'; Mode = 'Mode'; Assert = 'Assert' }
        }

        # ---------- SpecSourceGen.Cli（生成 / 链 / 引用 / 清点）----------
        'invoke-specgen' = @{
            Kind = 'SpecCli'
            Verb = 'gen'
            Options = @(
                @{ Param = 'Project'; Token = '--project'; Always = $true }
                @{ Param = 'Source'; Token = '--source'; Always = $true; Repeat = $true }
                @{ Param = 'Config'; Token = '--config' }
                @{ Param = 'Ports'; Token = '--ports' }
                @{ Param = 'Out'; Token = '--out' }
                @{ Param = 'Manifest'; Token = '--manifest'; Switch = $true }
                @{ Param = 'DryRun'; Token = '--dry-run'; Switch = $true }
            )
        }
        'get-spec-chain' = @{
            Kind = 'SpecCli'
            Verb = 'chain-get'
            Options = @(
                @{ Param = 'CasesRoot'; Token = '--cases'; Always = $true }
                @{ Param = 'Out'; Token = '--out' }
                @{ Param = 'MinShare'; Token = '--min-share' }
                @{ Param = 'Explain'; Token = '--explain'; Switch = $true }
                @{ Param = 'Check'; Token = '--check'; Switch = $true }
            )
        }
        'new-spec-chain' = @{
            Kind = 'SpecCli'
            Verb = 'chain-new'
            Options = @(
                @{ Param = 'Suite'; Token = '--suite'; Always = $true }
                @{ Param = 'Name'; Token = '--name'; Always = $true }
                @{ Param = 'From'; Token = '--from' }
                @{ Param = 'Write'; Token = '--write'; Switch = $true }
            )
        }
        'use-spec-chain' = @{
            Kind = 'SpecCli'
            Verb = 'chain-use'
            Options = @(
                @{ Param = 'Suite'; Token = '--suite'; Always = $true }
                @{ Param = 'Chain'; Token = '--chain'; Always = $true }
                @{ Param = 'Case'; Token = '--case' }
                @{ Param = 'Write'; Token = '--write'; Switch = $true }
            )
        }
        'set-spec-chain' = @{
            Kind = 'SpecCli'
            Verb = 'chain-set'
            Options = @(
                @{ Param = 'Suite'; Token = '--suite'; Always = $true }
                @{ Param = 'Chain'; Token = '--chain'; Always = $true }
                @{ Param = 'From'; Token = '--from' }
                @{ Param = 'JsonPayload'; Token = '--json' }
                @{ Param = 'Write'; Token = '--write'; Switch = $true }
            )
        }
        'remove-spec-chain' = @{
            Kind = 'SpecCli'
            Verb = 'chain-remove'
            Options = @(
                @{ Param = 'Suite'; Token = '--suite'; Always = $true }
                @{ Param = 'Chain'; Token = '--chain'; Always = $true }
                @{ Param = 'Write'; Token = '--write'; Switch = $true }
            )
        }
        'new-specchains' = @{
            Kind = 'SpecCli'
            Verb = 'chains-new'
            Options = @(
                @{ Param = 'CasesRoot'; Token = '--cases'; Always = $true }
                @{ Param = 'MinUses'; Token = '--min-share' }
                @{ Param = 'Write'; Token = '--write'; Switch = $true }
            )
        }
        'foreach-speccase' = @{
            Kind = 'SpecCli'
            Verb = 'case-list'
            Options = @(
                @{ Param = 'CasesRoot'; Token = '--cases'; Always = $true }
                @{ Param = 'SuitePattern'; Token = '--suite-pattern' }
                @{ Param = 'CasePattern'; Token = '--case-pattern' }
                @{ Param = 'Shape'; Token = '--shape' }
                @{ Param = 'Out'; Token = '--out' }
            )
        }
        'test-specrefs' = @{
            Kind = 'SpecCli'
            Verb = 'refs'
            Options = @(
                @{ Param = 'CasesRoot'; Token = '--cases'; Always = $true }
                @{ Param = 'Check'; Token = '--check'; Switch = $true }
            )
        }
        'get-specgenerated' = @{
            Kind = 'SpecCli'
            Verb = 'generated-list'
            Options = @(
                @{ Param = 'Manifest'; Token = '--manifest'; Always = $true }
                @{ Param = 'Verify'; Token = '--verify'; Switch = $true }
                @{ Param = 'Json'; Token = '--emit-json'; Switch = $true }
            )
        }
    }
}