@{
    # ===== 基线分类索引（A1 角色 × A2 域 × A3 来源 × A4 形态；守卫 = Check-GateBaselines U12）=====
    # 语义：分类的唯一可判定面。文件系统 ↔ 本表**双射**；Role=Data 的 Domain 与 GateManifests.psd1 同源。
    #   目录轴只落 A1（扩展名 / `_` 前缀）与 A2（子目录 <Domain>\）；A3/A4 只登记不落目录（禁目录维度爆炸）。
    #   Role=Data ⇒ 文件名须 = <Gate>.psd1；遗留不符须显式 NameDeviation + DueWhen（可见化债务，禁静默例外）。
    Schema    = 'baseline-catalog-v1'
    Canonical = 'Frame\PowerShellScript\Baselines\README.md'

    Entries = @(
        # ---- Role=Data（A2 域序与 directory-map.json 的 gateDomains 一致）----
        @{ File = 'DelegateEntry.psd1'; Role = 'Data'; Domain = 'Agent'; Gate = 'DelegateEntry'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-DelegateEntry.ps1' }
        @{ File = 'EffectiveFace.psd1'; Role = 'Data'; Domain = 'Agent'; Gate = 'EffectiveFace'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-EffectiveFace.ps1' }
        @{ File = 'ExecutionLaneGate.psd1'; Role = 'Data'; Domain = 'Agent'; Gate = 'ExecutionLaneGate'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-ExecutionLaneGate.ps1' }
        @{ File = 'FixedSleepBaseline.psd1'; Role = 'Data'; Domain = 'Agent'; Gate = 'ScriptNoFixedSleep'; Shape = 'Table'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-ScriptNoFixedSleep.ps1'; NameDeviation = '文件名遗留（应 = ScriptNoFixedSleep.psd1）'; DueWhen = 'B1 重命名批次落地时' }
        @{ File = 'IntentContract.psd1'; Role = 'Data'; Domain = 'Agent'; Gate = 'IntentContract'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-IntentContract.ps1' }
        @{ File = 'LaneCatalog.psd1'; Role = 'Data'; Domain = 'Agent'; Gate = 'LaneCatalog'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-LaneCatalog.ps1' }
        @{ File = 'PromptSegments.psd1'; Role = 'Data'; Domain = 'Agent'; Gate = 'PromptSegments'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-PromptSegments.ps1' }
        @{ File = 'WorkflowRegistration.psd1'; Role = 'Data'; Domain = 'Agent'; Gate = 'WorkflowRegistration'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-WorkflowRegistration.ps1' }
        @{ File = 'DualCarrierExclusion.psd1'; Role = 'Data'; Domain = 'Data'; Gate = 'DualCarrierExclusion'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Data\Check-DualCarrierExclusion.ps1' }
        @{ File = 'FrameworkActivation.psd1'; Role = 'Data'; Domain = 'Data'; Gate = 'FrameworkActivation'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Data\Check-FrameworkActivation.ps1' }
        @{ File = 'DeclarationEditSingleSource.psd1'; Role = 'Data'; Domain = 'Declarations'; Gate = 'DeclarationEditSingleSource'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Declarations\Check-DeclarationEditSingleSource.ps1'; UnregisteredGate = '未在 GateManifests 登记（脚本存在、未被聚合器调度）'; DueWhen = '登记进 GateManifests 或退役时' }
        @{ File = 'FuncCallSingleSource.psd1'; Role = 'Data'; Domain = 'Declarations'; Gate = 'FuncCallSingleSource'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Declarations\Check-FuncCallSingleSource.ps1'; UnregisteredGate = '未在 GateManifests 登记（脚本存在、未被聚合器调度）'; DueWhen = '登记进 GateManifests 或退役时' }
        @{ File = 'InitGuards.psd1'; Role = 'Data'; Domain = 'Declarations'; Gate = 'InitGuards'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Declarations\Check-InitGuards.ps1' }
        @{ File = 'OrchestrationKernel.psd1'; Role = 'Data'; Domain = 'Frame'; Gate = 'OrchestrationKernel'; Shape = 'Flat'; Origins = @('Debt'); Consumer = ''; DueWhen = 'B1 处置：判据已内联于 Check-OrchestrationKernel.ps1（全仓零引用 ⇒ 删或改事实行接入）' }
        @{ File = 'AntiPatternFamily.psd1'; Role = 'Data'; Domain = 'Meta'; Gate = 'AntiPatternFamily'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-AntiPatternFamily.ps1' }
        @{ File = 'GateFileIndex.psd1'; Role = 'Data'; Domain = 'Meta'; Gate = 'GateFileIndex'; Shape = 'Grid'; Origins = @('Derived', 'Debt'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-GateFileIndex.ps1' }
        @{ File = 'OperationLayerIsolation.psd1'; Role = 'Data'; Domain = 'Meta'; Gate = 'OperationLayerIsolation'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-OperationLayerIsolation.ps1' }
        @{ File = 'SemanticCentralization.psd1'; Role = 'Data'; Domain = 'Meta'; Gate = 'SemanticCentralization'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-SemanticCentralization.ps1' }
        @{ File = 'AgentFrameworkSingleSource.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'AgentFrameworkSingleSource'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Node\Check-AgentFrameworkSingleSource.ps1' }
        @{ File = 'AgentOrchestrationFlow.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'AgentOrchestrationFlow'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Node\Check-AgentOrchestrationFlow.ps1' }
        @{ File = 'DomainWireSingleSource.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'DomainWireSingleSource'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Node\Check-DomainWireSingleSource.ps1' }
        @{ File = 'NodeCollectPort.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'NodeCollectPort'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Node\Check-NodeCollectPort.ps1' }
        @{ File = 'NodeDeclSingleSource.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'NodeDeclSingleSource'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Node\Check-NodeDeclSingleSource.ps1' }
        @{ File = 'NodeFlowDomainMatch.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'NodeFlowDomainMatch'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Node\Check-NodeFlowDomainMatch.ps1' }
        @{ File = 'NodeHierarchyRefs.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'NodeHierarchyRefs'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Node\Check-NodeHierarchyRefs.ps1' }
        @{ File = 'NodeKeyAddress.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'NodeKeyAddress'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Node\Check-NodeKeyAddress.ps1' }
        @{ File = 'NodeLocConsistency.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'NodeLocConsistency'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Node\Check-NodeLocConsistency.ps1' }
        @{ File = 'NodeLocPlacement.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'NodeLocPlacement'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Node\Check-NodeLocPlacement.ps1' }
        @{ File = 'ObservabilitySurface.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'ObservabilitySurface'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Node\Check-ObservabilitySurface.ps1' }
        @{ File = 'ReferenceVocabulary.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'ReferenceVocabulary'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Node\Check-ReferenceVocabulary.ps1' }
        @{ File = 'Wiring.psd1'; Role = 'Data'; Domain = 'Node'; Gate = 'Wiring'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Node\Check-Wiring.ps1' }
        @{ File = 'KeyGrammar.psd1'; Role = 'Data'; Domain = 'Parsing'; Gate = 'KeyGrammar'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Parsing\Check-KeyGrammar.ps1' }
        @{ File = 'RecognizerVocabulary.psd1'; Role = 'Data'; Domain = 'Parsing'; Gate = 'RecognizerVocabulary'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Modules\Parsing\Check-RecognizerVocabulary.ps1' }
        @{ File = 'BatchScope.psd1'; Role = 'Data'; Domain = 'Process'; Gate = 'BatchScope'; Shape = 'Grid'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Process\Check-BatchScope.ps1' }
        @{ File = 'InvokeEntrypoints.psd1'; Role = 'Data'; Domain = 'Process'; Gate = 'InvokeEntrypoints'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Process\Check-InvokeEntrypoints.ps1' }
        @{ File = 'TestDoubleParity.psd1'; Role = 'Data'; Domain = 'Process'; Gate = 'TestDoubleParity'; Shape = 'Grid'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Process\Check-TestDoubleParity.ps1' }
        @{ File = 'VerifyProportionality.psd1'; Role = 'Data'; Domain = 'Process'; Gate = 'VerifyProportionality'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Process\Check-VerifyProportionality.ps1' }
        @{ File = 'VerifySingleShot.psd1'; Role = 'Data'; Domain = 'Process'; Gate = 'VerifySingleShot'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Process\Check-VerifySingleShot.ps1' }
        @{ File = 'SpecBoundary.psd1'; Role = 'Data'; Domain = 'Spec'; Gate = 'SpecBoundary'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecBoundary.ps1' }
        @{ File = 'SpecGeneratedBoundary.psd1'; Role = 'Data'; Domain = 'Spec'; Gate = 'SpecGeneratedBoundary'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecGeneratedBoundary.ps1' }
        @{ File = 'SpecToolingContract.psd1'; Role = 'Data'; Domain = 'Spec'; Gate = 'SpecToolingContract'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecToolingContract.ps1' }
        @{ File = 'HtmlViewerVocabulary.psd1'; Role = 'Data'; Domain = 'Usharp'; Gate = 'HtmlViewerVocabulary'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Usharp\Check-HtmlViewerVocabulary.ps1' }
        @{ File = 'ToolFaceDeclaration.psd1'; Role = 'Data'; Domain = 'Usharp'; Gate = 'ToolFaceDeclaration'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Usharp\Check-ToolFaceDeclaration.ps1' }
        @{ File = 'UsharpDocForm.psd1'; Role = 'Data'; Domain = 'Usharp'; Gate = 'UsharpDocForm'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Usharp\Check-UsharpDocForm.ps1' }
        @{ File = 'UsharpGetParity.psd1'; Role = 'Data'; Domain = 'Usharp'; Gate = 'UsharpGetParity'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Usharp\Check-UsharpGetParity.ps1' }
        @{ File = 'UsharpJoint.psd1'; Role = 'Data'; Domain = 'Usharp'; Gate = 'UsharpJoint'; Shape = 'FactRow'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Usharp\Check-UsharpJoint.ps1' }
        @{ File = 'BuildFrameworkModules.psd1'; Role = 'Data'; Domain = 'Build'; Gate = 'BuildFrameworkModules'; Shape = 'Grid'; Origins = @('Debt'); Consumer = 'Projects\BuildFramework\Gates\Check-BuildFrameworkModules.ps1'; DueWhen = 'B1 收口：裸值/行形态转事实行（U9/U11 既有红）；归属域可迁 Projects\BuildFramework' }
        @{ File = '_catalog.psd1'; Role = 'Register'; Domain = 'Infra'; Gate = ''; Shape = 'Registry'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-GateBaselines.ps1' }
        # ---- Role=Register（`_` 前缀清册/登记表；不进单件判据面）----
        @{ File = '_gate-timing.psd1'; Role = 'Register'; Domain = 'Infra'; Gate = ''; Shape = 'Registry'; Origins = @('Derived'); Consumer = '' }
        @{ File = '_generated-artifacts.psd1'; Role = 'Register'; Domain = 'Infra'; Gate = ''; Shape = 'Registry'; Origins = @('Derived'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-GeneratedArtifactsDrift.ps1' }
        @{ File = '_inline-baselines.psd1'; Role = 'Register'; Domain = 'Infra'; Gate = ''; Shape = 'Registry'; Origins = @('Debt'); Consumer = 'Frame\PowerShellScript\Operation\DSL\Meta\Check-GateBaselines.ps1' }
        @{ File = '_verify-scope.psd1'; Role = 'Register'; Domain = 'Infra'; Gate = ''; Shape = 'Registry'; Origins = @('Derived', 'Verdict'); Consumer = 'Environment\CentralTest\Internal\Invoke-Verify.ps1' }
        # ---- Role=Lib（原语代码；改动面大，固定留根，禁按域搬迁）----
        @{ File = 'GateBaseline.psm1'; Role = 'Lib'; Domain = 'Infra'; Gate = ''; Shape = 'Code'; Origins = @('Derived'); Consumer = '' }
        @{ File = 'GateFileIndex.psm1'; Role = 'Lib'; Domain = 'Infra'; Gate = ''; Shape = 'Code'; Origins = @('Derived'); Consumer = '' }
        @{ File = 'GateRuntime.psm1'; Role = 'Lib'; Domain = 'Infra'; Gate = ''; Shape = 'Code'; Origins = @('Derived'); Consumer = '' }
        @{ File = 'ArtifactFreshness.psm1'; Role = 'Lib'; Domain = 'Infra'; Gate = ''; Shape = 'Code'; Origins = @('Derived'); Consumer = '' }
        @{ File = 'VerifyScope.psm1'; Role = 'Lib'; Domain = 'Infra'; Gate = ''; Shape = 'Code'; Origins = @('Derived'); Consumer = '' }
    )
}
