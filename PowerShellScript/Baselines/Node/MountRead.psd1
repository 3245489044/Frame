@{
    # 唯一读入口基线（UMF · R1）。Origin=Debt 行必须给 DueWhen（Check-GateBaselines U3）。
    Schema = 'gate-baseline-v1'
    Gate   = 'MountRead'

    Facts = @{
        DirectGraphReads      = @{ Value = 120; Origin = 'Debt'; DueWhen = 'R3/R4 收口后按实测下调至 0' }
        ProjectionRegistry    = @{ Value = 5;   Origin = 'Derived'; DueWhen = '保持（HistoryNodes/TaskNodes/HistoryTasks/TaskTree/SessionNodes）' }
    }

    # 逐文件棘轮（可选）：登记后按文件只减不增；未登记 = 仅总量棘轮（报告态）。
    Files = @{}
}
