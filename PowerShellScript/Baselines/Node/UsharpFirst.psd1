@{
    Schema = 'gate-baseline-v1'
    Gate   = 'UsharpFirst'

    # UF1 字符串规则表 switch 的存量在册（棘轮只降不升；迁声明后删行）。
    RuleTableSwitch = @(
        @{ Value = 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Projection\TaskStateRuleSet.cs'; Origin = 'Debt'; DueWhen = '状态规则已迁声明面，本包装器待收编为纯读取' }
    )

    # UF2 规则表字典的存量在册（渲染装配表，非判定规则；逐项收敛后删行）。
    RuleTableDictionary = @(
        @{ Value = 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Cost\CostViewRenderer.cs'; Origin = 'Debt'; DueWhen = '成本呈现装配表待迁声明' }
        @{ Value = 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Nodes.cs'; Origin = 'Debt'; DueWhen = '节点呈现装配表待迁声明' }
        @{ Value = 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Rendering\ConversationPageRenderer.cs'; Origin = 'Debt'; DueWhen = '渲染模型装配表待迁声明' }
    )

    # UF3 判定上下文魔数（当前实测 0）。
    MagicNumberSites = @(
        @{ Value = '0'; Origin = 'Derived'; DueWhen = '' }
    )

    # UF4 同名 const 异值对（当前实测 0）。
    DuplicateConstantPairs = @(
        @{ Value = '0'; Origin = 'Derived'; DueWhen = '' }
    )
}
