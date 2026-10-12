@{
    # ===== 生成物 → 生成器 登记表（守卫 = Operation\DSL\Check-GeneratedArtifactsDrift.ps1）=====
    # 语义：产物唯一手写源 = 生成器的输入；产物本体禁止手工编辑。
    #   守卫三段：① 产物存在 ② 哨兵在位（手改即失守） ③ 复算到 temp 逐字比对 ⇒ 漂移即红。
    # 未登记者不检；新增生成物必须在此登记（否则为无人看守产物）。
    Schema    = 'generated-artifacts-v1'
    Canonical = 'Frame\PowerShellScript\Baselines\README.md'

    Artifacts = @(
        @{
            Name               = 'ModulePathsProps'
            Artifact           = 'Frame\CsharpPrj\generated\Directory.Build.props.modules.generated.props'
            Generator          = 'Frame\PowerShellScript\Share\Solution\Generate-ModulePaths.ps1'
            GeneratorArgs      = @{ OutputFile = '{TEMP}' }
            Kind               = 'Text'
            IgnoreLinePatterns = @('生成时间:')
            SentinelPattern    = '此文件由 Generate-ModulePaths.ps1 自动生成'
            FixCommand         = 'pwsh Frame\PowerShellScript\Share\Solution\Generate-ModulePaths.ps1'
            Origin             = 'Derived'
            DueWhen            = ''
        }
        @{
            Name               = 'FunctionIndex'
            Artifact           = 'Frame\PowerShellScript\PScriptManager\Framework\ps-function-index.json'
            Generator          = 'Frame\PowerShellScript\PScriptManager\Framework\Build-FunctionIndex.ps1'
            GeneratorArgs      = @{ IndexOut = '{TEMP}' }
            Kind               = 'Json'
            IgnoreJsonFields   = @('builtAt')
            SentinelPattern    = 'ps-function-owner-index-v1'
            FixCommand         = 'pwsh Frame\PowerShellScript\PScriptManager\Framework\Build-FunctionIndex.ps1'
            Origin             = 'Derived'
            DueWhen            = ''
        }
        @{
            Name               = 'ContractIndex'
            Artifact           = 'Frame\CsharpPrj\generated\ContractIndex.json'
            Generator          = 'Frame\PowerShellScript\Share\Index\Build-ContractIndex.ps1'
            GeneratorArgs      = @{ Update = $true; OutFile = '{TEMP}' }
            Kind               = 'Json'
            IgnoreJsonFields   = @('builtAt')
            SentinelPattern    = 'contract-index-v1'
            FixCommand         = 'pwsh Frame\PowerShellScript\Share\Index\Build-ContractIndex.ps1 -Update'
            Origin             = 'Derived'
            DueWhen            = ''
        }
        @{
            Name               = 'DeclaredManifest'
            Artifact           = 'Projects\Plug\Agent\src\Agent.Core\Declared\declared-manifest.json'
            Generator          = 'Frame\PowerShellScript\Share\Usharp\Generate-DeclaredBundle.ps1'
            GeneratorArgs      = @{ ConfigRoot = 'Projects\Plug\Agent\Config'; OutFile = '{TEMP}'; Kind = 'manifest' }
            Kind               = 'Json'
            IgnoreJsonFields   = @()
            SentinelPattern    = 'declared-manifest-v1'
            FixCommand         = 'pwsh Frame\PowerShellScript\Share\Usharp\Generate-DeclaredBundle.ps1 -ConfigRoot Projects\Plug\Agent\Config -OutDir Projects\Plug\Agent\src\Agent.Core\Declared'
            Origin             = 'Derived'
            DueWhen            = ''
        }
        @{
            Name               = 'DeclaredRoundSpec'
            Artifact           = 'Projects\Plug\Agent\src\Agent.Core\Declared\round-spec.generated.json'
            Generator          = 'Frame\PowerShellScript\Share\Usharp\Generate-DeclaredBundle.ps1'
            GeneratorArgs      = @{ ConfigRoot = 'Projects\Plug\Agent\Config'; OutFile = '{TEMP}'; Kind = 'roundspec' }
            Kind               = 'Json'
            IgnoreJsonFields   = @()
            SentinelPattern    = 'AgentLoop'
            FixCommand         = 'pwsh Frame\PowerShellScript\Share\Usharp\Generate-DeclaredBundle.ps1 -ConfigRoot Projects\Plug\Agent\Config -OutDir Projects\Plug\Agent\src\Agent.Core\Declared'
            Origin             = 'Derived'
            DueWhen            = ''
        }
        @{
            Name               = 'DeclaredReferenceSchema'
            Artifact           = 'Projects\Plug\Agent\src\Agent.Core\Declared\reference-schema.generated.json'
            Generator          = 'Frame\PowerShellScript\Share\Usharp\Generate-DeclaredBundle.ps1'
            GeneratorArgs      = @{ ConfigRoot = 'Projects\Plug\Agent\Config'; OutFile = '{TEMP}'; Kind = 'schema' }
            Kind               = 'Json'
            IgnoreJsonFields   = @()
            SentinelPattern    = 'cross_db_link'
            FixCommand         = 'pwsh Frame\PowerShellScript\Share\Usharp\Generate-DeclaredBundle.ps1 -ConfigRoot Projects\Plug\Agent\Config -OutDir Projects\Plug\Agent\src\Agent.Core\Declared'
            Origin             = 'Derived'
            DueWhen            = ''
        }
        @{
            Name               = 'GeneratedDeclaredContracts'
            Artifact           = 'Projects\Plug\Agent\src\Agent.Contract\WorkflowFunction\GeneratedDeclaredContracts.g.cs'
            Generator          = 'Frame\PowerShellScript\Share\Usharp\Generate-DeclaredConstants.ps1'
            GeneratorArgs      = @{ Manifest = 'Projects\Plug\Agent\src\Agent.Core\Declared\declared-manifest.json'; OutFile = '{TEMP}' }
            Kind               = 'Text'
            IgnoreLinePatterns = @()
            SentinelPattern    = '此文件由 Generate-DeclaredConstants.ps1'
            FixCommand         = 'pwsh Frame\PowerShellScript\Share\Usharp\Generate-DeclaredConstants.ps1'
            Origin             = 'Derived'
            DueWhen            = ''
        }
    )
}
