# 门禁索引（生成产物，禁手改）

> 由 `Operation\DSL\Tooling\Build-GateIndex.ps1` 从 `Operation\DSL\Registry\GateManifests.psd1` 的 `Domain` / `Family` / `Form` 字段编译生成。
> 新增/改域/改名后重跑生成器（幂等）；守卫 = `Operation\DSL\Meta\Check-GateTaxonomy.ps1`。

<!-- PROTO_AUTO:gateIndex:BEGIN -->

## Agent（84 件）

| Gate | Tier | Expect | Family | Form | 脚本 |
|---|---|---|---|---|---|
| `ActionCatalogSingleSource` | fast | 0 | Contract | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-ActionCatalogSingleSource.ps1` |
| `ActorBindingSingleWriter` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-ActorBindingSingleWriter.ps1` |
| `ActorInvocationSingleSource` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-ActorInvocationSingleSource.ps1` |
| `ActorObservationEmptyReason` | fast | 0 | Observability | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Observability\Check-ActorObservationEmptyReason.ps1` |
| `ActorObservationSurface` | fast | 0 | Observability | Consistency | `Frame\PowerShellScript\Operation\DSL\Agent\Observability\Check-ActorObservationSurface.ps1` |
| `AgentBuildPoint` | full | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-AgentBuildPoint.ps1` |
| `AgentDataSeparation` | full | 0 | Boundary | Consistency | `Frame\PowerShellScript\Operation\DSL\Agent\Boundary\Check-AgentDataSeparation.ps1` |
| `AgentDecisionCoreBoundary` | full | 0 | Boundary | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Boundary\Check-AgentDecisionCoreBoundary.ps1` |
| `AgentDefaultDispatch` | fast | 0 | Declaration | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Declaration\Check-AgentDefaultDispatch.ps1` |
| `AgentModulesFrameworkLayout` | fast | 0 | Boundary | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Boundary\Check-AgentModulesFrameworkLayout.ps1` |
| `AgentProcessSurface` | full | 0 | Contract | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-AgentProcessSurface.ps1` |
| `AgentRuntimeSlots` | full | 0 | Runtime | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-AgentRuntimeSlots.ps1` |
| `AgentRuntimeUIConfig` | full | 0 | Presentation | Consistency | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-AgentRuntimeUIConfig.ps1` |
| `AgentSpec` | fast | 0 | Declaration | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Declaration\Check-AgentSpec.ps1` |
| `CanonicalForm` | fast | 0 | Contract | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-CanonicalForm.ps1` |
| `CanonReplay` | fast | 0 | Meta | Consistency | `Frame\PowerShellScript\Operation\DSL\Agent\Meta\Check-CanonReplay.ps1` |
| `CircuitSemantics` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-CircuitSemantics.ps1` |
| `ContextGeometryBoundary` | fast | 0 | Boundary | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Boundary\Check-ContextGeometryBoundary.ps1` |
| `ContextGeometryConvergence` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-ContextGeometryConvergence.ps1` |
| `ContextGeometryPrefixPurity` | fast | 0 | Runtime | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-ContextGeometryPrefixPurity.ps1` |
| `DbRenderPurity` | fast | 0 | Presentation | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-DbRenderPurity.ps1` |
| `DbToolbarDictAlloc` | fast | 0 | Presentation | Ratchet | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-DbToolbarDictAlloc.ps1` |
| `DelegateEntry` | fast | 0 | Contract | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-DelegateEntry.ps1` |
| `DetailReadWriteSamePort` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-DetailReadWriteSamePort.ps1` |
| `DomainResolveSingleEntry` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-DomainResolveSingleEntry.ps1` |
| `EchoConstants` | fast | 0 | Observability | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Observability\Check-EchoConstants.ps1` |
| `EffectiveFace` | full | 0 | Runtime | Drift | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-EffectiveFace.ps1` |
| `EffectiveModeSingleSource` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-EffectiveModeSingleSource.ps1` |
| `EntrySingleSource` | full | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-EntrySingleSource.ps1` |
| `ExecutionLaneGate` | fast | 0 | Contract | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-ExecutionLaneGate.ps1` |
| `GatherClosure` | fast | 0 | Runtime | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-GatherClosure.ps1` |
| `GatherDecl` | fast | 0 | Runtime | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-GatherDecl.ps1` |
| `GatherDiagnosticsSingleSource` | fast | 0 | Observability | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Observability\Check-GatherDiagnosticsSingleSource.ps1` |
| `GatherSpec` | fast | 0 | Declaration | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Declaration\Check-GatherSpec.ps1` |
| `GenerationLayout` | full | 0 | Meta | Consistency | `Frame\PowerShellScript\Operation\DSL\Agent\Meta\Check-GenerationLayout.ps1` |
| `GenerationRebuildChain` | full | 0 | Meta | Consistency | `Frame\PowerShellScript\Operation\DSL\Agent\Meta\Check-GenerationRebuildChain.ps1` |
| `HistoryAssemblySurface` | full | 0 | Presentation | Consistency | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-HistoryAssemblySurface.ps1` |
| `HostAccessNoRestate` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-HostAccessNoRestate.ps1` |
| `IdentityTemplatePrefix` | fast | 0 | Declaration | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Declaration\Check-IdentityTemplatePrefix.ps1` |
| `IntentContract` | fast | 0 | Contract | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-IntentContract.ps1` |
| `IntentMappingNoLiteral` | fast | 0 | Contract | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-IntentMappingNoLiteral.ps1` |
| `IntentParity` | fast | 0 | Contract | Consistency | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-IntentParity.ps1` |
| `IntentSupportsProjected` | fast | 0 | Contract | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-IntentSupportsProjected.ps1` |
| `KeyspaceMirror` | fast | 0 | Contract | Drift | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-KeyspaceMirror.ps1` |
| `LaneCatalog` | fast | 0 | Declaration | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Declaration\Check-LaneCatalog.ps1` |
| `LlmRoutingSurface` | full | 0 | Runtime | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-LlmRoutingSurface.ps1` |
| `NodePromptBinding` | full | 0 | Declaration | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Declaration\Check-NodePromptBinding.ps1` |
| `OperationRowCarriesActor` | fast | 0 | Observability | Consistency | `Frame\PowerShellScript\Operation\DSL\Agent\Observability\Check-OperationRowCarriesActor.ps1` |
| `PowerShellHostSingleSource` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-PowerShellHostSingleSource.ps1` |
| `PromptCacheStability` | fast | 0 | Declaration | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Declaration\Check-PromptCacheStability.ps1` |
| `PromptSegments` | fast | 0 | Declaration | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Declaration\Check-PromptSegments.ps1` |
| `PsSkillToolMap` | fast | 0 | Contract | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-PsSkillToolMap.ps1` |
| `ReadinessSingleEntry` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-ReadinessSingleEntry.ps1` |
| `RecordDraftSingleSource` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-RecordDraftSingleSource.ps1` |
| `RecordSingleTextForm` | fast | 0 | Presentation | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-RecordSingleTextForm.ps1` |
| `RefreshFrameworkBoundary` | full | 0 | Boundary | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Boundary\Check-RefreshFrameworkBoundary.ps1` |
| `RenderNoDomainBranch` | fast | 0 | Presentation | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-RenderNoDomainBranch.ps1` |
| `RoundFailureEnvelope` | fast | 0 | Observability | Consistency | `Frame\PowerShellScript\Operation\DSL\Agent\Observability\Check-RoundFailureEnvelope.ps1` |
| `RoundShape` | fast | 0 | Declaration | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Declaration\Check-RoundShape.ps1` |
| `RowIdentityBareKey` | fast | 0 | Runtime | Ratchet | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-RowIdentityBareKey.ps1` |
| `SaveDispatchNoDomainSwitch` | fast | 0 | Runtime | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-SaveDispatchNoDomainSwitch.ps1` |
| `ScriptNoFixedSleep` | fast | 0 | Meta | Ratchet | `Frame\PowerShellScript\Operation\DSL\Agent\Meta\Check-ScriptNoFixedSleep.ps1` |
| `SendDeclarationCoverage` | fast | 0 | Declaration | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Declaration\Check-SendDeclarationCoverage.ps1` |
| `ShareMessagingBoundary` | fast | 0 | Boundary | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Boundary\Check-ShareMessagingBoundary.ps1` |
| `SignalDeclaration` | fast | 0 | Declaration | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Declaration\Check-SignalDeclaration.ps1` |
| `SignalTraceSchema` | fast | 0 | Observability | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Observability\Check-SignalTraceSchema.ps1` |
| `TemplateAccessSingleEntry` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-TemplateAccessSingleEntry.ps1` |
| `ToolCallConvergence` | fast | 0 | Contract | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-ToolCallConvergence.ps1` |
| `TransferKey` | fast | 0 | Contract | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Contract\Check-TransferKey.ps1` |
| `UiAnchorWrites` | fast | 0 | Presentation | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-UiAnchorWrites.ps1` |
| `UiLocDeclaration` | fast | 0 | Presentation | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-UiLocDeclaration.ps1` |
| `UiNoViewBranch` | fast | 0 | Presentation | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-UiNoViewBranch.ps1` |
| `UiReadOutcome` | fast | 0 | Presentation | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-UiReadOutcome.ps1` |
| `UiRegionOwnership` | fast | 0 | Presentation | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-UiRegionOwnership.ps1` |
| `UiRenderEntry` | fast | 0 | Presentation | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-UiRenderEntry.ps1` |
| `UiSelectionExternalization` | full | 0 | Presentation | Consistency | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-UiSelectionExternalization.ps1` |
| `UiTemplateSet` | fast | 0 | Presentation | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-UiTemplateSet.ps1` |
| `UiTextSafe` | fast | 0 | Presentation | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-UiTextSafe.ps1` |
| `UiViewCatalog` | fast | 0 | Presentation | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-UiViewCatalog.ps1` |
| `UiWriteFidelity` | fast | 0 | Presentation | Consistency | `Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-UiWriteFidelity.ps1` |
| `UnknownVisible` | fast | 0 | Observability | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Observability\Check-UnknownVisible.ps1` |
| `VmKernelReferenceBoundary` | full | 0 | Boundary | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Boundary\Check-VmKernelReferenceBoundary.ps1` |
| `WorkflowRegistration` | fast | 0 | Declaration | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Declaration\Check-WorkflowRegistration.ps1` |
| `WriteCapabilitySinglePredicate` | fast | 0 | Runtime | SingleSource | `Frame\PowerShellScript\Operation\DSL\Agent\Runtime\Check-WriteCapabilitySinglePredicate.ps1` |

## Build（5 件）

| Gate | Tier | Expect | Family | Form | 脚本 |
|---|---|---|---|---|---|
| `BakeFramework` | full | 0 | — | — | `Projects\BuildFramework\Bake\Check-BakeFramework.ps1` |
| `BinaryContract` | fast | 0 | — | — | `Projects\BuildFramework\Gates\Check-BinaryContract.ps1` |
| `BuildFrameworkModules` | full | 0 | — | — | `Projects\BuildFramework\Gates\Check-BuildFrameworkModules.ps1` |
| `BuildTargetMap` | full | 0 | — | — | `Projects\BuildFramework\Gates\Check-BuildTargetMap.ps1` |
| `DistributionIncrement` | full | 0 | — | — | `Projects\BuildFramework\Gates\Check-DistributionIncrement.ps1` |

## Data（29 件）

| Gate | Tier | Expect | Family | Form | 脚本 |
|---|---|---|---|---|---|
| `BatchStoreSingleSource` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-BatchStoreSingleSource.ps1` |
| `ConversationConversion` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-ConversationConversion.ps1` |
| `ConversationReadSingleSource` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-ConversationReadSingleSource.ps1` |
| `DataCenterGenericity` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-DataCenterGenericity.ps1` |
| `DataContracts` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-DataContracts.ps1` |
| `DataStoreDomainBoundary` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-DataStoreDomainBoundary.ps1` |
| `DualCarrierExclusion` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-DualCarrierExclusion.ps1` |
| `EdgeAsVertex` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-EdgeAsVertex.ps1` |
| `EndpointKeyConsistency` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-EndpointKeyConsistency.ps1` |
| `FactStandard` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-FactStandard.ps1` |
| `FieldLineageClosure` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-FieldLineageClosure.ps1` |
| `FrameworkActivation` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-FrameworkActivation.ps1` |
| `GraphDeclarationParity` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-GraphDeclarationParity.ps1` |
| `GraphIndexDeclaration` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-GraphIndexDeclaration.ps1` |
| `GraphKeySingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-GraphKeySingleSource.ps1` |
| `GraphPathSingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-GraphPathSingleSource.ps1` |
| `GraphSingleSource` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-GraphSingleSource.ps1` |
| `GraphTombstoneRead` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-GraphTombstoneRead.ps1` |
| `ProductIdempotency` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-ProductIdempotency.ps1` |
| `ReadIdentitySingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-ReadIdentitySingleSource.ps1` |
| `RelationGraphParity` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-RelationGraphParity.ps1` |
| `RenderLayerStoreFree` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-RenderLayerStoreFree.ps1` |
| `SelectionContract` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-SelectionContract.ps1` |
| `SessionDuplication` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-SessionDuplication.ps1` |
| `SingleWriter` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-SingleWriter.ps1` |
| `SqliteTablePolicySingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-SqliteTablePolicySingleSource.ps1` |
| `StorageWriteEntries` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-StorageWriteEntries.ps1` |
| `TableConvergenceSingleEntry` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-TableConvergenceSingleEntry.ps1` |
| `TableWriteDeclaration` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Data\Check-TableWriteDeclaration.ps1` |

## Declarations（4 件）

| Gate | Tier | Expect | Family | Form | 脚本 |
|---|---|---|---|---|---|
| `ClinerulesReferences` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Declarations\Check-ClinerulesReferences.ps1` |
| `InitGuards` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Declarations\Check-InitGuards.ps1` |
| `LogMessages` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Declarations\Check-LogMessages.ps1` |
| `RuntimeMirror` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Declarations\Check-RuntimeMirror.ps1` |

## KeySpace（20 件）

| Gate | Tier | Expect | Family | Form | 脚本 |
|---|---|---|---|---|---|
| `DeclarationClosure` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-DeclarationClosure.ps1` |
| `DispatchTraceSchema` | fast | 0 | Observability | Closure | `Frame\PowerShellScript\Operation\DSL\Agent\Observability\Check-DispatchTraceSchema.ps1` |
| `ExecutionOwnership` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-ExecutionOwnership.ps1` |
| `KeyAxisCompleteness` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeyAxisCompleteness.ps1` |
| `KeyCorpusGolden` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeyCorpusGolden.ps1` |
| `KeyDeclNoLogic` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeyDeclNoLogic.ps1` |
| `KeyDeclSingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeyDeclSingleSource.ps1` |
| `KeyDomainStrict` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeyDomainStrict.ps1` |
| `KeyIdentity` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-KeyIdentity.ps1` |
| `KeyOpsClosedSet` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeyOpsClosedSet.ps1` |
| `KeyOwnerUnique` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeyOwnerUnique.ps1` |
| `KeyParseAmbiguity` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeyParseAmbiguity.ps1` |
| `KeyParseSingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeyParseSingleSource.ps1` |
| `KeyPolicy` | data | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-KeyPolicy.ps1` |
| `KeyRoundTrip` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeyRoundTrip.ps1` |
| `KeySpaceCoverage` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeySpaceCoverage.ps1` |
| `KeySurfaceDistinguishable` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeySurfaceDistinguishable.ps1` |
| `KeySurfaceSingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-KeySurfaceSingleSource.ps1` |
| `WireFabric` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-WireFabric.ps1` |
| `WorkFlowMethodKeySemantics` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\KeySpace\Check-WorkFlowMethodKeySemantics.ps1` |

## Meta（23 件）

| Gate | Tier | Expect | Family | Form | 脚本 |
|---|---|---|---|---|---|
| `AntiPatternFamily` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-AntiPatternFamily.ps1` |
| `BulkThreshold` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-BulkThreshold.ps1` |
| `ContractRegistry` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-ContractRegistry.ps1` |
| `DeclaredBundle` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-DeclaredBundle.ps1` |
| `FallbackInventory` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-FallbackInventory.ps1` |
| `FallbackInventoryRate` | fast | 0 | Meta | Ratchet | `Frame\PowerShellScript\Operation\DSL\Agent\Meta\Check-FallbackInventoryRate.ps1` |
| `GateBaselines` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-GateBaselines.ps1` |
| `GateFileIndex` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-GateFileIndex.ps1` |
| `GateRegistry` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-GateRegistry.ps1` |
| `GateTaxonomy` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-GateTaxonomy.ps1` |
| `GeneratedArtifactsDrift` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-GeneratedArtifactsDrift.ps1` |
| `OperationLayerIsolation` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-OperationLayerIsolation.ps1` |
| `PrjBridgeDecoupling` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-BridgeDecoupling.ps1` |
| `PsCapabilityFileOnly` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-PsCapabilityFileOnly.ps1` |
| `RefreshFetch` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-RefreshFetch.ps1` |
| `SectionWrite` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-SectionWrite.ps1` |
| `SemanticCentralization` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-SemanticCentralization.ps1` |
| `UiBudget` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-UiBudget.ps1` |
| `UiIncremental` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-UiIncremental.ps1` |
| `UiSingleWrite` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-UiSingleWrite.ps1` |
| `UiTelemetryFields` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-UiTelemetryFields.ps1` |
| `UiThreadAffinity` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-UiThreadAffinity.ps1` |
| `WriteBudgetNodes` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Meta\Check-WriteBudgetNodes.ps1` |

## Node（36 件）

| Gate | Tier | Expect | Family | Form | 脚本 |
|---|---|---|---|---|---|
| `AgentFrameworkSingleSource` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-AgentFrameworkSingleSource.ps1` |
| `AgentOrchestrationFlow` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-AgentOrchestrationFlow.ps1` |
| `AgentRuntimeUIConfigFreshness` | fast | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-AgentRuntimeUIConfigFreshness.ps1` |
| `DbPageGatherWiring` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-DbPageGatherWiring.ps1` |
| `DomainRosterSingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Node\Check-DomainRosterSingleSource.ps1` |
| `DomainWireSingleSource` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Node\Check-DomainWireSingleSource.ps1` |
| `FreshnessFamilySingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Node\Check-FreshnessFamilySingleSource.ps1` |
| `GatherFieldMapping` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Node\Check-GatherFieldMapping.ps1` |
| `MountCallSites` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-MountCallSites.ps1` |
| `MountDeclaration` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-MountDeclaration.ps1` |
| `MountFailurePosture` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-MountFailurePosture.ps1` |
| `MountRead` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-MountRead.ps1` |
| `MountSingleWriter` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-MountSingleWriter.ps1` |
| `NodeCollectPort` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-NodeCollectPort.ps1` |
| `NodeDeclSingleSource` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-NodeDeclSingleSource.ps1` |
| `NodeEdgeIntegrity` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Node\Check-NodeEdgeIntegrity.ps1` |
| `NodeFlowDomainMatch` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-NodeFlowDomainMatch.ps1` |
| `NodeFramework` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-NodeFramework.ps1` |
| `NodeGather` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-NodeGather.ps1` |
| `NodeHierarchyRefs` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-NodeHierarchyRefs.ps1` |
| `NodeKeyAddress` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-NodeKeyAddress.ps1` |
| `NodeLocConsistency` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-NodeLocConsistency.ps1` |
| `NodeLocPlacement` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-NodeLocPlacement.ps1` |
| `NodePortPurity` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-NodePortPurity.ps1` |
| `NodeReadSingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-NodeReadSingleSource.ps1` |
| `NodeWriteBypass` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-NodeWriteBypass.ps1` |
| `ObservabilitySurface` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-ObservabilitySurface.ps1` |
| `ReferenceExclusion` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-ReferenceExclusion.ps1` |
| `ReferenceVocabulary` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-ReferenceVocabulary.ps1` |
| `RelationGather` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Node\Check-RelationGather.ps1` |
| `RelationPlan` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Node\Check-RelationPlan.ps1` |
| `StoreReadPayloadRecordForm` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Node\Check-StoreReadPayloadRecordForm.ps1` |
| `StoreReadPortSingleTable` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Node\Check-StoreReadPortSingleTable.ps1` |
| `TextClipSingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Node\Check-TextClipSingleSource.ps1` |
| `Wiring` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Node\Check-Wiring.ps1` |
| `WriteSurface` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Node\Check-WriteSurface.ps1` |

## Parsing（2 件）

| Gate | Tier | Expect | Family | Form | 脚本 |
|---|---|---|---|---|---|
| `KeyGrammar` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Parsing\Check-KeyGrammar.ps1` |
| `RecognizerVocabulary` | full | 0 | — | — | `Frame\PowerShellScript\Modules\Parsing\Check-RecognizerVocabulary.ps1` |

## Process（25 件）

| Gate | Tier | Expect | Family | Form | 脚本 |
|---|---|---|---|---|---|
| `AgentSemanticsLeak` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-AgentSemanticsLeak.ps1` |
| `AnalysisDocGrounding` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-AnalysisDocGrounding.ps1` |
| `BatchLineEndings` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-BatchLineEndings.ps1` |
| `BatchScope` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-BatchScope.ps1` |
| `ContainerSemantics` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-ContainerSemantics.ps1` |
| `DeclarationCapability` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-DeclarationCapability.ps1` |
| `DimensionReduction` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-DimensionReduction.ps1` |
| `EntityExistenceSingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-EntityExistenceSingleSource.ps1` |
| `FragmentBoundary` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-FragmentBoundary.ps1` |
| `FrameFactCoverage` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-FrameFactCoverage.ps1` |
| `HistorySemantics` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-HistorySemantics.ps1` |
| `InvokeEntrypoints` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-InvokeEntrypoints.ps1` |
| `JudgmentParity` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-JudgmentParity.ps1` |
| `JudgmentPoints` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-JudgmentPoints.ps1` |
| `OrchestrationKernel` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-OrchestrationKernel.ps1` |
| `PreflightCost` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-PreflightCost.ps1` |
| `ReadModelSingleSource` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-ReadModelSingleSource.ps1` |
| `RenderNoStringConcat` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-RenderNoStringConcat.ps1` |
| `RunConvergence` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-RunConvergence.ps1` |
| `SpecExempt` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-SpecExempt.ps1` |
| `TestDoubleParity` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-TestDoubleParity.ps1` |
| `TextKernelPurity` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-TextKernelPurity.ps1` |
| `UiSingleEntry` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-UiSingleEntry.ps1` |
| `VerifyProportionality` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-VerifyProportionality.ps1` |
| `VerifySingleShot` | fast | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Process\Check-VerifySingleShot.ps1` |

## Spec（5 件）

| Gate | Tier | Expect | Family | Form | 脚本 |
|---|---|---|---|---|---|
| `SpecBoundary` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecBoundary.ps1` |
| `SpecGeneratedBoundary` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecGeneratedBoundary.ps1` |
| `SpecOnly` | full | 0 | — | — | `Environment\CentralTest\Tools\Gate\Test-SpecOnlyGate.ps1` |
| `SpecPortLedger` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecPortLedger.ps1` |
| `SpecToolingContract` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Spec\Check-SpecToolingContract.ps1` |

## Usharp（7 件）

| Gate | Tier | Expect | Family | Form | 脚本 |
|---|---|---|---|---|---|
| `AgentStorageIsolation` | fast | 0 | Boundary | Boundary | `Frame\PowerShellScript\Operation\DSL\Agent\Boundary\Check-AgentStorageIsolation.ps1` |
| `FsSinkEntry` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Usharp\Check-FsSinkEntry.ps1` |
| `HtmlViewerVocabulary` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Usharp\Check-HtmlViewerVocabulary.ps1` |
| `ToolFaceDeclaration` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Usharp\Check-ToolFaceDeclaration.ps1` |
| `UsharpDocForm` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Usharp\Check-UsharpDocForm.ps1` |
| `UsharpGetParity` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Usharp\Check-UsharpGetParity.ps1` |
| `UsharpJoint` | full | 0 | — | — | `Frame\PowerShellScript\Operation\DSL\Usharp\Check-UsharpJoint.ps1` |

<!-- PROTO_AUTO:gateIndex:END -->
