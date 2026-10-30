@{
    Description = 'UiRegionKind 受控词汇快照（外部件 UiBackend 枚举；仓内无源码，故以基线锁词汇）。声明面 kind 只可取自本表；新增词汇须先改枚举再登记。'
    Kinds = @(
        'Sidebar', 'Feed', 'Board', 'Prompt', 'Nodes', 'Bubbles', 'Composer', 'Overlay'
    )
}
