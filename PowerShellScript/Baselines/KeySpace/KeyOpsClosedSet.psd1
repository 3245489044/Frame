@{
    # KB2 固有函数闭集基线（单源；棘轮只降不升）
    # Value = 存量债（Origin='Debt' + DueWhen，只降不升）；Whitelist = 非键上下文（结构事实 + Kind/Reason）。
    Schema = 'gate-baseline-v1'
    Gate   = 'KeyOpsClosedSet'
    Value  = @{ Value = @(
        @{ Key = 'Projects/Plug/Agent/src/Agent.Core/Engine/Nodes/Registry/AgentHistoryKeyGrammar.cs|interp'; Count = 1 }
        @{ Key = 'Frame/CsharpPrj/Share/Frameworks/Conversation/ContextBinding/AnchorKey.cs|interp'; Count = 1 }
        @{ Key = 'Frame/CsharpPrj/Share/Frameworks/Conversation/ContextBinding/AnchorKeyRules.cs|starts'; Count = 1 }
        @{ Key = 'Frame/CsharpPrj/Operation/HierarchyMethod/Hierarchy/Dataflow/RunState/GovernanceRoundRegistry.cs|interp'; Count = 2 }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Contract/NodeDomain/DomainKeyOps.cs|starts'; Count = 1 }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Ui/Core/Functions/HistoryOps/AgentUiLibraryHistoryOps.cs|slice'; Count = 1 }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Core/Entry/Plan/WritePlanner.cs|interpDot'; Count = 3 }
    ); Origin = 'Debt'; DueWhen = 'KS6 降基线后剩余项并入 KS7（节点/轮次键声明化）收敛为 0' }

    # 非键白名单（替代「调宽正则」）：每条必须给出 Kind + Reason（结构事实，非执行者叙述）。
    # Kind ∈ {path, css, type-name, tag, log, ui-text, port-contract, identity-token, comparison}
    Whitelist = @{ Value = @(
        # ---- concatLiteral：非键拼接 ----
        @{ Key = 'Frame/CsharpPrj/Modules/Wiring/Runtime/IWireHandlerTable.cs|concatLiteral'; Count = 1; Kind = 'port-contract'; Reason = '接盒端口取数键 BoxId.portName（端口契约名，非键空间寻址）' }
        @{ Key = 'Frame/CsharpPrj/Modules/Wiring/Model/PortSpec.cs|concatLiteral'; Count = 1; Kind = 'port-contract'; Reason = '接盒引用名 Owner.Name' }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Core/Engine/TaskPool/TaskOrchestrator.cs|concatLiteral'; Count = 1; Kind = 'port-contract'; Reason = '配方出口名 __snapshot.snapshot' }
        @{ Key = 'Frame/CsharpPrj/Plate/RoslynMethod/RoslynMethod.Refactoring/StaticStore.cs|concatLiteral'; Count = 2; Kind = 'type-name'; Reason = '反射类型全名' }
        @{ Key = 'Frame/CsharpPrj/Plate/RoslynMethod/RoslynMethod.Core/Utilities/SyntaxInfoTool.cs|concatLiteral'; Count = 1; Kind = 'type-name'; Reason = '语法节点类型全名' }
        @{ Key = 'Frame/CsharpPrj/Strategy/Observability/SpecHost/PortBackedProxy.cs|concatLiteral'; Count = 2; Kind = 'port-contract'; Reason = '可观测端口名 type.method' }
        @{ Key = 'Frame/CsharpPrj/Operation/HierarchyMethod/Nodes/Execution/Artifacts/NodeTags.cs|concatLiteral'; Count = 1; Kind = 'tag'; Reason = '标签 轴.词' }
        @{ Key = 'Frame/CsharpPrj/Modules/Node/Spec/TagVocabulary.cs|concatLiteral'; Count = 1; Kind = 'tag'; Reason = '标签 轴.词' }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Modules/Framework/NodeSpec/NodeTagWriter.cs|concatLiteral'; Count = 1; Kind = 'tag'; Reason = '标签 轴.词' }
        @{ Key = 'Frame/CsharpPrj/Operation/DataStoreDriven/Core/Shared/ZoneNodeAdapter.cs|concatLiteral'; Count = 1; Kind = 'path'; Reason = '存储 checkpoint 名 ns.id' }
        @{ Key = 'Frame/CsharpPrj/Plate/DataManage/StorageFramework/Sqlite/SqliteWriteOwnerRegistry.cs|concatLiteral'; Count = 1; Kind = 'type-name'; Reason = '写者登记用 类型.方法名' }
        @{ Key = 'Frame/CsharpPrj/Modules/LogicComposition/Conversion/Hosting/LogicalFileWriter.cs|concatLiteral'; Count = 1; Kind = 'path'; Reason = '临时文件名 target.guid.write.tmp' }
        @{ Key = 'Frame/CsharpPrj/BasicMethod/FrameMethod/DictionaryFrame.cs|concatLiteral'; Count = 1; Kind = 'path'; Reason = '字典遍历路径拼装' }
        @{ Key = 'Frame/CsharpPrj/Plate/HtmlViewer/Html/Bridge/BridgeModuleManifest.cs|concatLiteral'; Count = 1; Kind = 'css'; Reason = 'JS 全局名拼接' }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Contract/Usharp/UsharpPathTree.cs|concatLiteral'; Count = 1; Kind = 'path'; Reason = 'USharp 声明路径树（非键空间）' }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Session/Projection/ActorObservationContracts.cs|concatLiteral'; Count = 2; Kind = 'ui-text'; Reason = '执行体行键（UI 选择态/刷新键，不参与寻址，无族声明）' }
        # ---- splitDot：非键点号切分（JSON 路径 / 字典路径 / 命名空间） ----
        @{ Key = 'Frame/CsharpPrj/Modules/LogicComposition/Conversion/Validation/Rules/FlatFactProjection.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '规则字段路径切分' }
        @{ Key = 'Frame/CsharpPrj/Modules/LogicComposition/Conversion/Validation/Rules/RuleSpecScope.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '规则 spec 路径切分' }
        @{ Key = 'Frame/CsharpPrj/Plate/JsonMethod/Utilities/JsonPathHelper.cs|splitDot'; Count = 1; Kind = 'path'; Reason = 'JSON 路径切分' }
        @{ Key = 'Frame/CsharpPrj/Modules/Parsing/Parsing/JsonPathHelper.cs|splitDot'; Count = 1; Kind = 'path'; Reason = 'JSON 路径切分' }
        @{ Key = 'Frame/CsharpPrj/Operation/BackEndDriven/CommandExecution/CommandSystems.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '命令行命名空间切分' }
        @{ Key = 'Frame/CsharpPrj/BasicMethod/Common/Configuration/GlobalValue.cs|splitDot'; Count = 2; Kind = 'path'; Reason = '配置键路径切分' }
        @{ Key = 'Frame/CsharpPrj/Modules/ParameterResolver/Validation/Rules/Structure/RequiredFieldRule.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '字段路径切分' }
        @{ Key = 'Frame/CsharpPrj/Share/Frameworks/UniversalReference/Reference/Relationship/RelationshipCore.cs|splitDot'; Count = 2; Kind = 'path'; Reason = '关系引用路径切分' }
        @{ Key = 'Frame/CsharpPrj/Plate/DeclarativeLedger.Tools/LedgerForge/SplitCommand.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '台账命令行切分（工具）' }
        @{ Key = 'Frame/CsharpPrj/Plate/RoslynMethod/RoslynMethod.Core/Utilities/UsingManager.cs|splitDot'; Count = 2; Kind = 'type-name'; Reason = '命名空间切分' }
        @{ Key = 'Frame/CsharpPrj/BasicMethod/FrameMethod/DictionaryFrame.cs|splitDot'; Count = 2; Kind = 'path'; Reason = '字典路径切分' }
        @{ Key = 'Frame/CsharpPrj/Modules/ParameterResolver/Utilities/PropertyPathResolver.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '属性路径切分' }
        @{ Key = 'Frame/CsharpPrj/Modules/ExecutionFramework/Session/DataContext.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '上下文数据键路径切分' }
        @{ Key = 'Frame/CsharpPrj/Share/Composition/PlaceholderFramework/Core/ConfigInjector.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '占位符配置路径切分' }
        @{ Key = 'Frame/CsharpPrj/Modules/ParameterResolver/Validation/Rules/JsonTreeWalker.cs|splitDot'; Count = 2; Kind = 'path'; Reason = 'JSON 树路径切分' }
        @{ Key = 'Frame/CsharpPrj/Modules/Node/Instances/InstanceSpecLoader.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '规格字段路径切分' }
        @{ Key = 'Frame/CsharpPrj/Plate/JsonMethod/Utilities/JsonTolerantReader.cs|splitDot'; Count = 1; Kind = 'path'; Reason = 'JSON 路径切分' }
        @{ Key = 'Frame/CsharpPrj/Modules/LogicComposition/Conversion/Validation/Rules/RulePath.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '规则路径切分' }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Modules/Framework/Cost/AgentCostSettings.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '成本配置键路径切分' }
        @{ Key = 'Frame/CsharpPrj/Operation/WorkFlowMethod/Parameters/ParameterValueResolver.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '参数路径切分' }
        @{ Key = 'Frame/CsharpPrj/Share/Frameworks/Diagnostics/ErrorHandling/ProjectSourceResolver.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '源码路径切分' }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Modules/Framework/Messaging/MessageBusCatalog.cs|splitDot'; Count = 1; Kind = 'path'; Reason = '消息总线目录路径切分' }
        @{ Key = 'Frame/CsharpPrj/Modules/Node/Projection/NodeKeyOrdinalComparer.cs|splitDot'; Count = 2; Kind = 'comparison'; Reason = '键序比较器（按段比较，不构造/反解键；非寻址面）' }

        # ---- interp / interpDot：日志与 UI 文案（含 {round}/{seq} 占位，非键） ----
        @{ Key = 'Projects/Plug/Agent/src/Agent.Modules/Framework/Cost/Projection/RunEventProjection.cs|interp'; Count = 2; Kind = 'log'; Reason = '诊断日志 cost_run_event_dropped(scope=…, seq=…)' }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Modules/Framework/Cost/Projection/CostLogMirror.cs|interp'; Count = 2; Kind = 'log'; Reason = '诊断日志 cost_log_mirror_dropped(seq=…)' }
        @{ Key = 'Frame/CsharpPrj/Modules/Node/Materialization/NodeMaterializer.cs|interp'; Count = 1; Kind = 'log'; Reason = '违反诊断文本「轮次 {round} 超前」' }
        @{ Key = 'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlViewerBridge/HtmlStreamRenderer.cs|interp'; Count = 1; Kind = 'ui-text'; Reason = 'UI 文案「第 {round} 轮」' }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Modules/Framework/RoundAttribution/RoundAttributionOpsFunctions.cs|interp'; Count = 1; Kind = 'identity-token'; Reason = '关联 token {taskKey}#r{round}（不参与寻址）' }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Contract/Runtime/Turn/TurnRuntime.cs|interp'; Count = 1; Kind = 'log'; Reason = '心跳字段文本 round={round}' }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Core/Engine/Nodes/Registry/AgentHistoryKeyGrammar.cs|interpDot'; Count = 1; Kind = 'identity-token'; Reason = 'IContainerKeyGrammar.ParentKey 父键（框架契约形态，非落库身份键）' }
    ); Origin = 'Derived'; DueWhen = '' }
}


