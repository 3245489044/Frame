@{
    # ===== BinaryContract 门禁基线（单源；守卫 = Projects\BuildFramework\Gates\Check-BinaryContract.ps1）=====
    # 事实行 = @{ Value; Origin; DueWhen }（GateBaselines U9/U11）；消费端一律读 .Value。
    Schema = 'gate-baseline-v1'
    Gate   = 'BinaryContract'

    # 面 = 探针目录**有序**集合（顺序 = 运行期探针顺序：宿主权威目录在前，扩展目录在后）。
    # 同名程序集重复 ⇒ 先到者胜（与运行期探针同构）；内容不一致者按 P3 单列（偏斜面）。
    # {conf} = -Configuration 代入值。
    Faces = @{
        Value   = @{
            dev = @(
                @{ Id = 'frame'; Dir = 'Frame\CsharpPrj\BuildOutput\{conf}'; Required = $true }
                @{ Id = 'agent'; Dir = 'Projects\Plug\Agent\BuildOutput\{conf}'; Required = $false }
                @{ Id = 'engine'; Dir = 'Projects\Plug\Engine\BuildOutput\{conf}'; Required = $false }
                @{ Id = 'produce'; Dir = 'Projects\Plug\Produce\BuildOutput\{conf}'; Required = $false }
                @{ Id = 'filedriven'; Dir = 'Projects\FileDriven\BuildOutput\{conf}'; Required = $false }
                @{ Id = 'prjbridge'; Dir = 'Projects\PrjBridge\bin\{conf}\net10.0-windows'; Required = $false }
            )
        }
        Origin  = 'Derived'
        DueWhen = ''
    }

    # 共享面程序集（P3 内容级单副本的判据面；名字级白名单 → 内容指纹）。
    SharedSurfaceGlobs = @{
        Value   = @('FrontEndDriven', 'Plate*', 'Share*', 'Strategy*', 'Modules*', 'Route', 'PluginHost', 'FileDriven*', 'AgentHostContract', 'PrjBridge', 'UiBackend*', 'TConfig', 'BasicMethod')
        Origin  = 'Derived'
        DueWhen = ''
    }

    # 正对照下限（面内引用数）：低于下限 ⇒ 判据无标的（假绿）。
    MinReferences = @{ Value = 200; Origin = 'Derived'; DueWhen = '' }

    # P3 是否阻断（false = 仅可见化报告；true = 阻断）。启用前置 = 偏斜面处置完毕。
    EnforceSharedSurface = @{ Value = $false; Origin = 'Verdict'; DueWhen = 'BC-3：偏斜面（如 Engine 9/28 副本）处置后置 true' }

    # P2 依赖指纹是否阻断（旁车 <asm>.contract.json 由 BC-2 的 BinaryContractStamp.targets 生成）。
    EnforceDependencyFingerprint = @{ Value = $true; Origin = 'Verdict'; DueWhen = 'BC-2 已落地（PrjBridge 启用旁车）；扩面至其它二进制引用工程后复核并更新本行' }
}
