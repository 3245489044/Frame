@{
    # ===== GraphDeclarationDrift 门禁基线（单源；守卫 = Operation\DSL\Node\Check-GraphDeclarationDrift.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'GraphDeclarationDrift'

    # 判据输入（声明面路径；缺失 ⇒ 判据不可判定，脚本 fail-closed）。
    ProductGraph = @{
        Value  = 'Projects\Plug\Agent\Config\graph.ushar'
        Origin = 'Derived'
    }
    GatherDeclaration = @{
        Value  = 'Projects\Plug\Agent\Config\nodes\node-gather.ushar'
        Origin = 'Derived'
    }
    RegionDeclaration = @{
        Value  = 'Projects\Plug\Agent\Config\ui\ui-regions.json'
        Origin = 'Derived'
    }

    # GD3 棘轮（只降不升）：视图内容域尚未与 [Gather] 段同名。
    # 2026-11-04 D2：三视图内容域已与 [Gather] 段同名（session-view / task-node / task-nodes）⇒ 已销账。
    # 新增缺口一律判红；保留空槽位（事实行形态）。
    UnreachableViewDomains = @(
        @{ Value = ''; Origin = 'Derived'; DueWhen = '' }
    )
}
