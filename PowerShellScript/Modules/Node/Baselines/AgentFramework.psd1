@{
    # ===== 门禁基线（纯数据；变更须给出理由并同步文档）=====
    # 本文件为 AgentFrameworkSingleSource 门禁的**基线单源**（禁把基线写回脚本常量）。

    # D1 诊断载体单源（框架层 HierarchyMethod 内 Action<string, object?> 只允许出现在适配器本体）
    DiagnosticsCarrierAllowedFiles = @('IDiagnosticsSink.cs')

    # D3 键寻址收口：命名空间取值点（NodeOutputKeys.Normalize( / NodeKeys.DefaultWorkflowId）按文件基线（只减不增）
    #     目标 = 全部收敛到 KeyAddressing / NodeOutputKeys / NodeKeys 三处（本表为存量豁免面，随批次核减）
    KeyAddressingBaselines = @{
        'AgentNodeCarriers.cs'                     = 1
        'AgentUiLibraryHistoryOps.cs'              = 1
        'HierarchyLevelRefs.cs'                    = 1
        'HistoryOpsFunctions.cs'                   = 2
        'KeyAddressing.cs'                         = 2
        'McpServerTools.cs'                        = 1
        'NodeDefinition.cs'                        = 1
        'NodeDomainPort.cs'                        = 1
        'NodeExecutionRecordFactory.cs'            = 1
        'NodeOutputKeys.cs'                        = 1
        'NodeOutputLevelProcessor.cs'              = 1
        'NodeOutputStore.cs'                       = 2
        'NodeRoundRuntime.cs'                      = 2
        'NodeStepExecutionService.cs'              = 2
        'NodeTaskLinker.cs'                        = 1
        'ReferenceHierarchyRelationReader.cs'      = 1
        'RoundStepInvoker.cs'                      = 1
        'StandardContextBuilder.cs'                = 1
        'StatelessAnswerTaskExecutor.cs'           = 1
        'SyncRelationsBlock.cs'                    = 1
        'WorkflowMethodFunctionInvoker.cs'         = 1
    }

    # D2 回退姿态：流回退分支（_taskProvisionFlow / _attributionFlow 的 is null / is not null）按文件基线（只减不增）
    #     批次 C 已删两处 legacy 直调委托（HistoryProjector / NodeTaskHistoryService）⇒ 本表所计已退化为
    #     「流入口是否装配」的**入口判空**（其下游为诊断可见化 / Fail，不再是回退写边）；
    #     下一次口径重构应改为按 `legacy:` 委托实质计数（届时本表可清零）。
    FlowFallbackBaselines = @{
        'HistoryProjector.cs'                       = 2
        'NodeTaskHistoryService.cs'                 = 2
        'NodeStepExecutionService.cs'               = 1
        'SyncRelationsBlock.cs'                     = 2
        'HierarchyManager.cs'                       = 1
        'TaskOrchestrator.cs'                       = 2
    }

    # D4 簿记流模板：数据表行数下限（BookkeepingFlowCatalog 登记行）
    BookkeepingFlowRows = 4

    # D5 参数读取单源：OpsFunctions 内完整三行 Param 实现形态计数上限（目标 0）
    ParamImplementationMax = 0

    # D8 簿记流执行器构造点单源（§6 Wiring 切片）：`new OneShotFlowRunner(` 构造点上限（唯一 = BookkeepingFlowAssembly）
    #     缩减切片口径（NodeStore/KeyMappings/HistoryProjector/EnableOutbox/AttributionFlow）不得在装配点重复书写
    BookkeepingRunnerConstructionMax = 1

    # D9 姿态收口台账（批次 A 建；批次 C 翻转生效）
    #     口径：RequireFlow 装配点 = 显式选定收口姿态（组合根）；AllowLegacyFallback 显式装配点 = 显式选定迁移期姿态
    #           （不含 FlowPosture 自身的常量定义与调用点的 `??` 缺省值）。
    #     批次 C 已把组合根（AgentOrchestrator 主循环 / ConversationHosting 会话腿）翻到 RequireFlow ⇒
    #     PostureRequireFlowRequired = $true（RequireFlow 装配点 < PostureRequireFlowMin 即违规）。
    PostureRequireFlowRequired = $true
    PostureRequireFlowMin      = 2

    # D6 正对照：扫描面最小命中数（全 0 = 扫描面失效，非「零违规」）
    ScanSurfaceMinHits = 1

    # D10 上下文组装组合点基线（只减不增；口径见 Check-AgentFrameworkSingleSource.ps1 D10）：
    #     Scope A（node/task/history 读组装）内「归属边枚举（GetTaskNodeKeysAsync）+ 节点输出逐键读（TryGetNodeOutputAsync）」
    #     **组合**的文件数。单一实现 = Agent.Session\Context\ContextProjection.cs（S1 收口；S5 后该组合可整体退役为
    #     声明流 context-projection，届时下调至 0）。
    ContextAssemblyComboBaseline = 1
}
