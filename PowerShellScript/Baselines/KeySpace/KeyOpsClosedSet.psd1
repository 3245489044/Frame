@{
    # KB2 固有函数闭集基线（单源；棘轮只降不升）
    Schema = 'gate-baseline-v1'
    Gate   = 'KeyOpsClosedSet'
    Value  = @{ Value = @(
        @{ Key = 'Projects/Plug/Agent/src/Agent.Modules/Framework/Cost/Projection/RunEventProjection.cs|interp'; Count = 1 }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Core/Cost/Nodes/CostNodeKeys.cs|interp'; Count = 5 }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Modules/Framework/Cost/Projection/CostLogMirror.cs|interp'; Count = 1 }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Core/Engine/Nodes/Registry/AgentHistoryKeyGrammar.cs|interp'; Count = 1 }
        @{ Key = 'Frame/CsharpPrj/Share/Frameworks/Conversation/ContextBinding/AnchorKey.cs|interp'; Count = 1 }
        @{ Key = 'Frame/CsharpPrj/Operation/HierarchyMethod/Hierarchy/Dataflow/RunState/GovernanceRoundRegistry.cs|interp'; Count = 2 }
        @{ Key = 'Frame/CsharpPrj/Modules/Node/Materialization/NodeMaterializer.cs|interp'; Count = 1 }
        @{ Key = 'Frame/CsharpPrj/Share/Frameworks/Conversation/ContextBinding/AnchorKeyRules.cs|starts'; Count = 1 }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Contract/NodeDomain/DomainKeyOps.cs|starts'; Count = 1 }
        @{ Key = 'Projects/Plug/Agent/src/Agent.Ui/Core/Functions/HistoryOps/AgentUiLibraryHistoryOps.cs|slice'; Count = 1 }
        @{ Key = 'Frame/CsharpPrj/Operation/FrontEndDriven/HtmlViewerBridge/HtmlStreamRenderer.cs|interp'; Count = 1 }
    ); Origin = 'Debt'; DueWhen = 'KP2–KP6 逐域收敛后降为 0' }
}
