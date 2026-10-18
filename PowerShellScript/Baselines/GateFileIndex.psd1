@{
    # ===== GateFileIndex 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-GateFileIndex.ps1）=====
    # 语义（R1）：门禁源文件枚举统一走 Baselines\GateFileIndex.psm1（git ls-files 索引）；
    #   递归 Get-ChildItem 仅允许用于「运行期数据 / 被 gitignore 的生成物」，且须登记债务。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）；DueWhen 中 MaxHits 为单件上限。
    Schema = 'gate-baseline-v1'
    Gate   = 'GateFileIndex'

    Allowlist = @(
        @{ Value = 'Frame\PowerShellScript\Modules\Node\Check-AgentFrameworkSingleSource.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=2）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Modules\Node\Check-AgentOrchestrationFlow.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Modules\Node\Check-DbPageGatherWiring.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=2）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Modules\Node\Check-NodeFramework.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=3）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Modules\Node\Check-NodeGather.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=3）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Modules\Node\Check-NodeLocConsistency.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Modules\Node\Check-NodePortPurity.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=3）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Modules\Parsing\Check-RecognizerVocabulary.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-AgentDataSeparation.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=2）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-AgentLayerBoundary.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=2）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-AgentStorageIsolation.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-CircuitSemantics.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=11）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-DomainResolveSingleEntry.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-EffectiveModeSingleSource.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=2）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-EntrySingleSource.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-HistoryAssemblySurface.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-LlmRoutingSurface.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-ReadinessSingleEntry.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-RecordSingleTextForm.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-RefreshFrameworkBoundary.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-RowIdentityBareKey.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-ScriptNoFixedSleep.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-SendDeclarationCoverage.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-TemplateAccessSingleEntry.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-ToolCallConvergence.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-WriteCapabilitySinglePredicate.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Data\Check-ConversationConversion.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Data\Check-DataCenterGenericity.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=4）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Data\Check-DataContracts.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Data\Check-FactStandard.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Data\Check-FrameworkActivation.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Data\Check-SingleWriter.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Data\Check-SqliteTablePolicySingleSource.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Data\Check-TableConvergenceSingleEntry.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Declarations\Check-DeclarationEditSingleSource.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Declarations\Check-DeclarationReloadRegistry.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=3）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Declarations\Check-FuncCallSingleSource.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Declarations\Check-InitGuards.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeySpaceCoverage.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=2）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-ContractRegistry.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=2）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-ExceptionGateway.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-FallbackInventory.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-GateBaselines.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=2）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-GateTaxonomy.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-OperationLayerIsolation.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-SemanticCentralization.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Node\Check-DomainWireSingleSource.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Node\Check-FreshnessFamilySingleSource.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Node\Check-StoreReadPortSingleTable.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Node\Check-TextClipSingleSource.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=2）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Process\Check-AnalysisDocGrounding.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Process\Check-BatchLineEndings.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Process\Check-InvokeEntrypoints.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Process\Check-TestDoubleParity.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=2）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecBoundary.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=2）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecGeneratedBoundary.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=5）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecPortLedger.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecToolingContract.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=1）；到期：R1 收尾时清零或转 Derived' }
        @{ Value = 'Frame\PowerShellScript\Operation\DSL\Usharp\Check-FsSinkEntry.ps1'; Origin = 'Debt'; DueWhen = 'R1 分批迁移中（MaxHits=2）；到期：R1 收尾时清零或转 Derived' }
    )
}

