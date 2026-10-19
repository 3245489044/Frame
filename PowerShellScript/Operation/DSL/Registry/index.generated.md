# 门禁索引（生成产物，禁手改）

> 由 `Operation\DSL\Tooling\Build-GateIndex.ps1` 从 `Operation\DSL\Registry\GateManifests.psd1` 的 `Domain` 字段编译生成。
> 新增/改域/改名后重跑生成器（幂等）；守卫 = `Operation\DSL\Meta\Check-GateTaxonomy.ps1`。

<!-- PROTO_AUTO:gateIndex:BEGIN -->

## Agent（42 件）

| Gate | Tier | Expect | 脚本 |
|---|---|---|---|
| `ActionCatalogSingleSource` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-ActionCatalogSingleSource.ps1` |
| `AgentBuildPoint` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-AgentBuildPoint.ps1` |
| `AgentDataSeparation` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-AgentDataSeparation.ps1` |
| `AgentDecisionCoreBoundary` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-AgentDecisionCoreBoundary.ps1` |
| `AgentProcessSurface` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-AgentProcessSurface.ps1` |
| `AgentRuntimeSlots` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-AgentRuntimeSlots.ps1` |
| `AgentRuntimeUIConfig` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-AgentRuntimeUIConfig.ps1` |
| `CircuitSemantics` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-CircuitSemantics.ps1` |
| `DbRenderPurity` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-DbRenderPurity.ps1` |
| `DbToolbarDictAlloc` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-DbToolbarDictAlloc.ps1` |
| `DetailReadWriteSamePort` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-DetailReadWriteSamePort.ps1` |
| `DomainResolveSingleEntry` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-DomainResolveSingleEntry.ps1` |
| `EchoConstants` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-EchoConstants.ps1` |
| `EffectiveFace` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-EffectiveFace.ps1` |
| `EffectiveModeSingleSource` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-EffectiveModeSingleSource.ps1` |
| `EntrySingleSource` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-EntrySingleSource.ps1` |
| `GatherDiagnosticsSingleSource` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-GatherDiagnosticsSingleSource.ps1` |
| `GenerationLayout` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-GenerationLayout.ps1` |
| `GenerationRebuildChain` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-GenerationRebuildChain.ps1` |
| `HistoryAssemblySurface` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-HistoryAssemblySurface.ps1` |
| `HostAccessNoRestate` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-HostAccessNoRestate.ps1` |
| `IntentContract` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-IntentContract.ps1` |
| `IntentMappingNoLiteral` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-IntentMappingNoLiteral.ps1` |
| `IntentParity` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-IntentParity.ps1` |
| `IntentSupportsProjected` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-IntentSupportsProjected.ps1` |
| `LlmRoutingSurface` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-LlmRoutingSurface.ps1` |
| `OperationRowCarriesActor` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-OperationRowCarriesActor.ps1` |
| `ReadinessSingleEntry` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-ReadinessSingleEntry.ps1` |
| `RecordDraftSingleSource` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-RecordDraftSingleSource.ps1` |
| `RecordSingleTextForm` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-RecordSingleTextForm.ps1` |
| `RefreshFrameworkBoundary` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-RefreshFrameworkBoundary.ps1` |
| `RenderNoDomainBranch` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-RenderNoDomainBranch.ps1` |
| `RowIdentityBareKey` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-RowIdentityBareKey.ps1` |
| `SaveDispatchNoDomainSwitch` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-SaveDispatchNoDomainSwitch.ps1` |
| `ScriptNoFixedSleep` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-ScriptNoFixedSleep.ps1` |
| `SendDeclarationCoverage` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-SendDeclarationCoverage.ps1` |
| `TemplateAccessSingleEntry` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-TemplateAccessSingleEntry.ps1` |
| `ToolCallConvergence` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-ToolCallConvergence.ps1` |
| `UiSelectionExternalization` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-UiSelectionExternalization.ps1` |
| `UnknownVisible` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-UnknownVisible.ps1` |
| `VmKernelReferenceBoundary` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-VmKernelReferenceBoundary.ps1` |
| `WriteCapabilitySinglePredicate` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-WriteCapabilitySinglePredicate.ps1` |

## Build（4 件）

| Gate | Tier | Expect | 脚本 |
|---|---|---|---|
| `BakeFramework` | full | 0 | `Projects\BuildFramework\Bake\Check-BakeFramework.ps1` |
| `BuildFrameworkModules` | full | 0 | `Projects\BuildFramework\Gates\Check-BuildFrameworkModules.ps1` |
| `BuildTargetMap` | full | 0 | `Projects\BuildFramework\Gates\Check-BuildTargetMap.ps1` |
| `DistributionIncrement` | full | 0 | `Projects\BuildFramework\Gates\Check-DistributionIncrement.ps1` |

## Data（13 件）

| Gate | Tier | Expect | 脚本 |
|---|---|---|---|
| `BatchStoreSingleSource` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-BatchStoreSingleSource.ps1` |
| `ConversationConversion` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-ConversationConversion.ps1` |
| `ConversationReadSingleSource` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-ConversationReadSingleSource.ps1` |
| `DataCenterGenericity` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-DataCenterGenericity.ps1` |
| `DataContracts` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-DataContracts.ps1` |
| `DualCarrierExclusion` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-DualCarrierExclusion.ps1` |
| `FactStandard` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-FactStandard.ps1` |
| `FrameworkActivation` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-FrameworkActivation.ps1` |
| `SessionDuplication` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-SessionDuplication.ps1` |
| `SingleWriter` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-SingleWriter.ps1` |
| `SqliteTablePolicySingleSource` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-SqliteTablePolicySingleSource.ps1` |
| `TableConvergenceSingleEntry` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-TableConvergenceSingleEntry.ps1` |
| `TableWriteDeclaration` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Data\Check-TableWriteDeclaration.ps1` |

## Declarations（3 件）

| Gate | Tier | Expect | 脚本 |
|---|---|---|---|
| `ClinerulesReferences` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Declarations\Check-ClinerulesReferences.ps1` |
| `InitGuards` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Declarations\Check-InitGuards.ps1` |
| `LogMessages` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Declarations\Check-LogMessages.ps1` |

## KeySpace（1 件）

| Gate | Tier | Expect | 脚本 |
|---|---|---|---|
| `KeySpaceCoverage` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeySpaceCoverage.ps1` |

## Meta（12 件）

| Gate | Tier | Expect | 脚本 |
|---|---|---|---|
| `AntiPatternFamily` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Meta\Check-AntiPatternFamily.ps1` |
| `ContractRegistry` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Meta\Check-ContractRegistry.ps1` |
| `DeclaredBundle` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Meta\Check-DeclaredBundle.ps1` |
| `FallbackInventory` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Meta\Check-FallbackInventory.ps1` |
| `GateBaselines` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Meta\Check-GateBaselines.ps1` |
| `GateFileIndex` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Meta\Check-GateFileIndex.ps1` |
| `GateRegistry` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Meta\Check-GateRegistry.ps1` |
| `GateTaxonomy` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Meta\Check-GateTaxonomy.ps1` |
| `GeneratedArtifactsDrift` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Meta\Check-GeneratedArtifactsDrift.ps1` |
| `OperationLayerIsolation` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Meta\Check-OperationLayerIsolation.ps1` |
| `PrjBridgeDecoupling` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Meta\Check-BridgeDecoupling.ps1` |
| `SemanticCentralization` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Meta\Check-SemanticCentralization.ps1` |

## Node（26 件）

| Gate | Tier | Expect | 脚本 |
|---|---|---|---|
| `AgentFrameworkSingleSource` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-AgentFrameworkSingleSource.ps1` |
| `AgentOrchestrationFlow` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-AgentOrchestrationFlow.ps1` |
| `AgentRuntimeUIConfigFreshness` | fast | 0 | `Frame\PowerShellScript\Modules\Node\Check-AgentRuntimeUIConfigFreshness.ps1` |
| `DbPageGatherWiring` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-DbPageGatherWiring.ps1` |
| `DomainRosterSingleSource` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Node\Check-DomainRosterSingleSource.ps1` |
| `DomainWireSingleSource` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Node\Check-DomainWireSingleSource.ps1` |
| `FreshnessFamilySingleSource` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Node\Check-FreshnessFamilySingleSource.ps1` |
| `GatherFieldMapping` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Node\Check-GatherFieldMapping.ps1` |
| `NodeCollectPort` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-NodeCollectPort.ps1` |
| `NodeDeclSingleSource` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-NodeDeclSingleSource.ps1` |
| `NodeFlowDomainMatch` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-NodeFlowDomainMatch.ps1` |
| `NodeFramework` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-NodeFramework.ps1` |
| `NodeGather` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-NodeGather.ps1` |
| `NodeHierarchyRefs` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-NodeHierarchyRefs.ps1` |
| `NodeKeyAddress` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-NodeKeyAddress.ps1` |
| `NodeLocConsistency` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-NodeLocConsistency.ps1` |
| `NodeLocPlacement` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-NodeLocPlacement.ps1` |
| `NodePortPurity` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-NodePortPurity.ps1` |
| `NodeWriteBypass` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-NodeWriteBypass.ps1` |
| `ObservabilitySurface` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-ObservabilitySurface.ps1` |
| `ReferenceExclusion` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-ReferenceExclusion.ps1` |
| `ReferenceVocabulary` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-ReferenceVocabulary.ps1` |
| `StoreReadPayloadRecordForm` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Node\Check-StoreReadPayloadRecordForm.ps1` |
| `StoreReadPortSingleTable` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Node\Check-StoreReadPortSingleTable.ps1` |
| `TextClipSingleSource` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Node\Check-TextClipSingleSource.ps1` |
| `Wiring` | full | 0 | `Frame\PowerShellScript\Modules\Node\Check-Wiring.ps1` |

## Parsing（2 件）

| Gate | Tier | Expect | 脚本 |
|---|---|---|---|
| `KeyGrammar` | full | 0 | `Frame\PowerShellScript\Modules\Parsing\Check-KeyGrammar.ps1` |
| `RecognizerVocabulary` | full | 0 | `Frame\PowerShellScript\Modules\Parsing\Check-RecognizerVocabulary.ps1` |

## Process（9 件）

| Gate | Tier | Expect | 脚本 |
|---|---|---|---|
| `AnalysisDocGrounding` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Process\Check-AnalysisDocGrounding.ps1` |
| `BatchLineEndings` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Process\Check-BatchLineEndings.ps1` |
| `BatchScope` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Process\Check-BatchScope.ps1` |
| `InvokeEntrypoints` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Process\Check-InvokeEntrypoints.ps1` |
| `PreflightCost` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Process\Check-PreflightCost.ps1` |
| `RunConvergence` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Process\Check-RunConvergence.ps1` |
| `TestDoubleParity` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Process\Check-TestDoubleParity.ps1` |
| `VerifyProportionality` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Process\Check-VerifyProportionality.ps1` |
| `VerifySingleShot` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Process\Check-VerifySingleShot.ps1` |

## Spec（5 件）

| Gate | Tier | Expect | 脚本 |
|---|---|---|---|
| `SpecBoundary` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecBoundary.ps1` |
| `SpecGeneratedBoundary` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecGeneratedBoundary.ps1` |
| `SpecOnly` | full | 0 | `Environment\CentralTest\Tools\Gate\Test-SpecOnlyGate.ps1` |
| `SpecPortLedger` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecPortLedger.ps1` |
| `SpecToolingContract` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecToolingContract.ps1` |

## Usharp（7 件）

| Gate | Tier | Expect | 脚本 |
|---|---|---|---|
| `AgentStorageIsolation` | fast | 0 | `Frame\PowerShellScript\Operation\DSL\Agent\Check-AgentStorageIsolation.ps1` |
| `FsSinkEntry` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Usharp\Check-FsSinkEntry.ps1` |
| `HtmlViewerVocabulary` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Usharp\Check-HtmlViewerVocabulary.ps1` |
| `ToolFaceDeclaration` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Usharp\Check-ToolFaceDeclaration.ps1` |
| `UsharpDocForm` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Usharp\Check-UsharpDocForm.ps1` |
| `UsharpGetParity` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Usharp\Check-UsharpGetParity.ps1` |
| `UsharpJoint` | full | 0 | `Frame\PowerShellScript\Operation\DSL\Usharp\Check-UsharpJoint.ps1` |

<!-- PROTO_AUTO:gateIndex:END -->
