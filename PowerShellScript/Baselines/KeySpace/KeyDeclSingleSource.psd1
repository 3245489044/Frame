@{
    # KB2 键形字面量单源基线（单源；棘轮只降不升）
    Schema = 'gate-baseline-v1'
    Gate   = 'KeyDeclSingleSource'

    Whitelist = @{ Value = @(
        'Projects/Plug/Agent/src/Agent.Contract/NodeDomain/DomainKeyFamilies.cs'
        'Projects/Plug/Agent/src/Agent.Contract/NodeDomain/ProcessKeys.cs'
        'Frame/CsharpPrj/Modules/Node/Identity/HierarchyKeyFamilies.cs'
    ); Origin = 'Derived'; DueWhen = '' }

    Value = @{ Value = @(); Origin = 'Debt'; DueWhen = 'KB4/KB6 收敛到 0' }
}
