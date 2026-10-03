@{
    # ===== AgentFrameworkSingleSource 门禁基线（单源；守卫 = Modules\Node\Check-AgentFrameworkSingleSource.ps1）=====
    # 2026-09-29（SR-2a）：由**遗留子根** Modules\Node\Baselines\（同名 psd1）并入规范根；
    #   文件名对齐 U7 契约（`<Gate>.psd1` 与 `Check-<Gate>.ps1` 同名 ⇒ 接线可判）；
    #   裸值一律改**事实行**（Value + Origin + DueWhen）—— 使「每条基线必带 Origin」成为机器判据（U9），
    #   此前裸值可静默通过 U3 ⇒ 外置基线可退化为另一种散落常量。
    Schema = 'gate-baseline-v1'
    Gate   = 'AgentFrameworkSingleSource'
    Source = 'Frame\PowerShellScript\Modules\Node\Check-AgentFrameworkSingleSource.ps1'

    # D1 诊断载体单源（框架层 HierarchyMethod 内 Action<string, object?> 只允许出现在适配器本体）
    DiagnosticsCarrierAllowedFiles = @{
        Value   = @('IDiagnosticsSink.cs')
        Origin  = 'Verdict'
        DueWhen = '重裁触发：出现第二种合法载体（跨 ALC 代理/桥接）时须显式裁决并改本行'
    }

    # D3 键寻址收口：命名空间取值点（NodeOutputKeys.Normalize( / NodeKeys.DefaultWorkflowId）按文件基线（只减不增）
    #   目标 = 全部收敛到 KeyAddressing / NodeOutputKeys / NodeKeys 三处（本表为存量豁免面，随批次核减）
    #   2026-09-29（F-P2-3 收紧）：NodeDomainPort.cs 1 → 0 —— 读侧 loc 改由位置反解端口推导（DeriveLoc），
    #     不再把 NodeKeys.DefaultWorkflowId 当 loc 坐标硬写（该值本身是工作流命名空间，非位置轴坐标）。
    KeyAddressingBaselines = @{
        Value = @{
            'AgentNodeCarriers.cs'                = 1
            'AgentUiLibraryHistoryOps.cs'         = 1
            'HierarchyLevelRefs.cs'               = 1
            'HistoryOpsFunctions.cs'              = 2
            'KeyAddressing.cs'                    = 2
            'McpServerTools.cs'                   = 1
            'NodeDefinition.cs'                   = 1
            'NodeDomainPort.cs'                   = 0
            'NodeExecutionRecordFactory.cs'       = 1
            'NodeOutputKeys.cs'                   = 1
            'NodeOutputLevelProcessor.cs'         = 1
            'NodeOutputStore.cs'                  = 2
            'NodeRoundRuntime.cs'                 = 2
            'NodeStepExecutionService.cs'         = 2
            'NodeTaskLinker.cs'                   = 1
            'ReferenceHierarchyRelationReader.cs' = 1
            'RoundStepInvoker.cs'                 = 1
            'StandardContextBuilder.cs'           = 1
            'StatelessAnswerTaskExecutor.cs'      = 1
            'SyncRelationsBlock.cs'               = 1
            'WorkflowMethodFunctionInvoker.cs'    = 1
        }
        Origin  = 'Debt'
        DueWhen = '键寻址取值点逐文件清零 → 空表（销账后本行删除）'
    }

    # D2 回退姿态：流回退分支（_taskProvisionFlow / _attributionFlow 的 is null / is not null）按文件基线（只减不增）
    #     批次 C 已删两处 legacy 直调委托（HistoryProjector / NodeTaskHistoryService）⇒ 本表所计已退化为
    #     「流入口是否装配」的**入口判空**（其下游为诊断可见化 / Fail，不再是回退写边）；
    #     下一次口径重构应改为按 `legacy:` 委托实质计数（届时本表可清零）。
    FlowFallbackBaselines = @{
        Value = @{
            # 降级态（轮末沉淀流缺位）逐点归属边回退：本轮仅剩此一处（属「路径选择」而非缺失处置），
            # 待 A0 严格形态裁决（SyncRelationsBlock 逐点回退退役 ⇒ 轮末沉淀流为唯一归属边 owner）后清零。
            'SyncRelationsBlock.cs' = 1
        }
        Origin  = 'Debt'
        DueWhen = 'A0 严格形态裁决（SyncRelationsBlock 降级态逐点回退退役）后清零本表'
    }

    # D4 簿记流模板：数据表行数下限（BookkeepingFlowCatalog 登记行）
    BookkeepingFlowRows = @{
        Value   = 4
        Origin  = 'Derived'
        DueWhen = '可由 BookkeepingFlowCatalog 行数复算 ⇒ SR-3 后改由生成器产出'
    }

    # D5 参数读取单源：OpsFunctions 内完整三行 Param 实现形态计数上限（目标 0）
    ParamImplementationMax = @{
        Value   = 0
        Origin  = 'Debt'
        DueWhen = '保持归零（拦存在即可）；取参实现全部经 WorkflowContextParams'
    }

    # D8 簿记流执行器构造点单源（§6 Wiring 切片）：`new OneShotFlowRunner(` 构造点上限（唯一 = BookkeepingFlowAssembly）
    #     缩减切片口径（NodeStore/KeyMappings/HistoryProjector/EnableOutbox/AttributionFlow）不得在装配点重复书写。
    BookkeepingRunnerConstructionMax = @{
        Value   = 1
        Origin  = 'Verdict'
        DueWhen = '重裁触发：出现第二种合法构造入口（跨程序集工厂/测试装配面）时须显式裁决'
    }

    # D9 姿态收口台账（批次 A 建；批次 C 翻转生效）
    #     口径：RequireFlow 装配点 = 显式选定收口姿态（组合根）；AllowLegacyFallback 显式装配点 = 显式迁移期姿态
    #           （不含 FlowPosture 自身的常量定义与调用点的 `??` 缺省值）。
    PostureRequireFlowRequired = @{
        Value   = $true
        Origin  = 'Verdict'
        DueWhen = '重裁触发：若需回退迁移期姿态（AllowLegacyFallback），须显式重裁本项并记理由'
    }
    PostureRequireFlowMin = @{
        Value   = 2
        Origin  = 'Debt'
        DueWhen = '组合根收敛为单入口后 → 1（并重裁下限口径）'
    }

    # D6 正对照：扫描面最小命中数（全 0 = 扫描面失效，非「零违规」）
    ScanSurfaceMinHits = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = '可由扫描面命中数复算（正对照下限，恒 ≥ 1）'
    }

    # D10 上下文组装组合点基线（只减不增；口径见 Check-AgentFrameworkSingleSource.ps1 D10）：
    #     Scope A（node/task/history 读组装）内「归属边枚举（GetTaskNodeKeysAsync）+ 节点输出逐键读（TryGetNodeOutputAsync）」
    #     **组合**的文件数。单一实现 = Agent.Session\Context\ContextProjection.cs（S1 收口；S5 后该组合可整体退役为
    #     声明流 context-projection，届时下调至 0）。
    ContextAssemblyComboBaseline = @{
        Value   = 1
        Origin  = 'Debt'
        DueWhen = 'S5 组装归流落地（context-projection 声明流）后 → 0（销账后本行删除）'
    }

    # D11 运行期本地流 msgId 上限（统一 DiagnosticsCodes + context；目标 0）。
    #     运行期「缺失 / 失败」可见化一律经 DiagnosticsCodes.FlowMissing/FlowFailed + context 标签；
    #     装配期可见化（*_unavailable）与模板基类（BookkeepingFlow_runner_missing）不入本判据面。
    LocalFlowMsgIdMax = @{
        Value   = 0
        Origin  = 'Debt'
        DueWhen = '保持归零（拦存在即可）：禁各调用点自造运行期流诊断 msgId'
    }
}
