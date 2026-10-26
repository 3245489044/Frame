@{
    # ===== ReadModelOnly 基线（Turn 读模型唯一读面）=====
    # 语义：Turn 事实（在途/阶段/轮次/耗时/空态原因）只允许经 ITurnReadModel / TurnReadModel 读取；
    # 禁止在 Turn 渲染面重新引入「任务池/运行态字段直判」（即原缺陷的镜像读法）。
    # 消费方 = Operation\DSL\Agent\Check-ReadModelOnly.ps1（门禁名 ReadModelOnly）
    Schema = 'read-model-only-v1'

    # 扫描面（仓库相对；只能缩不能放）
    ScanRoots = @(
        'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationViewService.cs',
        'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\HistoryBoardModel.cs'
    )

    # 允许的 Turn 读面（正向锚点；命中 = 合规）
    AllowedReadFace = @('TurnReadModel.Shared', 'ITurnReadModel')

    # 禁止形态（结构事实；任一命中即违规）
    ForbiddenPatterns = @(
        @{ Id = 'pool-active'; Pattern = 'pool\.Active|AgentTaskPool[^;]*\.Active\b'; Reason = '任务池激活位直判在途（镜像读法）' }
        @{ Id = 'fixed-round'; Pattern = 'Fixed\.Round'; Reason = '固定层轮次直判（应由 TurnCard.Round 供给）' }
        @{ Id = 'store-table'; Pattern = 'ur_tasks|ur_histories'; Reason = '视图直读存储表' }
    )

    # 棘轮：命中数只减不增（当前允许的存量命中 = 0）
    MaxHits = 0
}
