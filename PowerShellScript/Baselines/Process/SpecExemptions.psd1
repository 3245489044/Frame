@{
    Schema = 'spec-exempt-v1'
    # 存量豁免登记册（D-06）：未 inline owner/expires 的既有豁免在册；到期未收敛即门禁 FAIL。新增豁免优先 inline。
    Registry = @{
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\AgentConfigDocumentTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\AgentConfigReconcileTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\AgenticPlanWorkflowReadTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\AgentRunRegionCircuitTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\AgentRunTrunkCircuitTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\AgentStateCenterInjectionTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\AgentTaskOutcomePolicyTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\AgentTaskPoolTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\AgentUiTaskPoolSubscriptionTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\AttemptPolicyTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\BoundedExecutionTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\BoundedSessionGateTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\Cases\RoundAssemblyFrameworkTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\CircuitRegionDeclarationTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\ClineChannelDefaultsTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\ClinePayloadKeysTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\ConfigSchedulingTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\ConversationPipelineTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\ConversationSendRegionCircuitTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\CriterionFrameworkTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\EndToEndConversationLocalTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\EndToEndIntegrationTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\EventSinkTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\FoldRoundArtifactContractTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\HtmlUiAssemblyTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\InvokeInlineProbeTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\LlmRoutingClientToolsTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\LocalLlmHttpIntegrationTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\LocalLlmToolCallTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\Loops\LoopKernelTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\Nodes\GovernanceAssemblyTimingTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\ObservabilityAttachClassificationTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\ObservabilityMessageMergeTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\OutcomeClassifierTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\PathALocalLlmCreateTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\ReactiveTriggerTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\RoundAttributionOutcomeTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\RoundCircuitBackendTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\RuleTaskExecutorTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\RuntimeSurfaceCommitTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\Signal\SignalFrameworkTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\SubAgentIntentResolverTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\TaskCommandHandlerTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\TaskOrchestratorMemberTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\TaskPoolParallelTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\AgentTest\Agent.Core.Tests\TaskRouteResolverTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
        'Environment\CentralTest\CSharpTest\Share.Tsets\StateManagement.Tests\TaskPoolOutcomePolicyTests.cs' = @{ Owner = 'legacy-migration'; Expires = '2026-12-31' }
    }
}

