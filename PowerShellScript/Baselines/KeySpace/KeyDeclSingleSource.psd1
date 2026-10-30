@{
    # KB2 键形字面量单源基线（单源；棘轮只降不升）
    Schema = 'gate-baseline-v1'
    Gate   = 'KeyDeclSingleSource'

    Whitelist = @{ Value = @(
        'Projects/Plug/Agent/src/Agent.Contract/NodeDomain/ProcessKeys.cs'
        'Frame/CsharpPrj/Modules/Node/Identity/HierarchyKeyFamilies.cs'
    ); Origin = 'Derived'; DueWhen = '' }

    Value = @{ Value = @(
        @{ Key = 'Projects/Plug/Agent/src/Agent.Core/Engine/Nodes/Registry/AgentHistoryKeyGrammar.cs'; Count = 1 }
        @{ Key = 'Frame/CsharpPrj/Modules/Node/Identity/NodeStoreKeyGrammars.cs'; Count = 6 }
    ); Origin = 'Debt'; DueWhen = 'KS6 降基线后剩余项并入 KS7（历史键/节点库键声明化）收敛为 0' }
}
