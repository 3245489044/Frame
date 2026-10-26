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
        @{ Key = 'Projects/Plug/Agent/src/Agent.Core/Cost/Nodes/CostNodeKeys.cs'; Count = 3 }
    ); Origin = 'Debt'; DueWhen = 'KP2–KP6 逐域收敛后降为 0' }
}
