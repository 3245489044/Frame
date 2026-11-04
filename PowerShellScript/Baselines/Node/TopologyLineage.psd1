@{
    Schema = 'gate-baseline-v1'
    Gate   = 'TopologyLineage'

    # 判据输入（声明面 / 生产调用点路径；缺失 ⇒ 判据不可判定）。
    ProductGraph = @{
        Value   = 'Projects\Plug\Agent\Config\graph.ushar'
        Origin  = 'Derived'
        DueWhen = ''
    }

    ProductionFile = @{
        Value   = 'Projects\Plug\Agent\src\Agent.Launcher\Composition\Hosting\Declaration\AgentGraphComposition.cs'
        Origin  = 'Derived'
        DueWhen = ''
    }

    # 血缘边数下限（声明面 derives= 产品数；只升不降：新增派生产品须同步声明血缘）。
    MinLineageEdges = @{
        Value   = '5'
        Origin  = 'Derived'
        DueWhen = ''
    }
}
