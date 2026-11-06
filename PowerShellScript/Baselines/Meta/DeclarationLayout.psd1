@{
    # ===== DeclarationLayout 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-DeclarationLayout.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'DeclarationLayout'

    # 根级控制面白名单（根级仅允许启动入口 / 布局真源 / schema 与生成物清单）。
    RootControlFiles = @{
        Value = @('agent-config.ushar')
        Origin = 'Derived'
        DueWhen = ''
    }

    # 迁移期根级声明（Batch 2 完成后必须清空；清空即根级只剩控制面）。
    MigrationRootFiles = @{
        Value = @(
            'aggregates.ushar', 'collaboration.ushar', 'context-geometry.ushar',
            'conversation-canon.ushar', 'conversation-graph.ushar', 'graph.ushar',
            'hierarchy.ushar', 'history-loop.ushar', 'history-modes.ushar', 'lanes.ushar',
            'messaging.ushar', 'orchestration-ops.ushar', 'prompt-profiles.ushar',
            'prompt-segments.ushar', 'resource-hierarchy.ushar', 'session-facts.ushar',
            'skills.ushar', 'subagents.ushar', 'tool-face.ushar'
        )
        Origin = 'Debt'
        DueWhen = 'Batch 2 根级分类完成后清空（根级仅控制面）'
    }
}
