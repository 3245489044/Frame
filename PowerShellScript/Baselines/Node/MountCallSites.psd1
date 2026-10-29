@{
    # 调用点/批量/仲裁窗口基线（UMF · R1）。数值 = 债务上限（只减不增）。
    Schema = 'gate-baseline-v1'
    Gate   = 'MountCallSites'

    MountCallSites = @{
        node_to_task       = @{ Value = 12; Origin = 'Debt'; DueWhen = 'R2/R3 后收敛到 1（轮末）+ 显式命令入口' }
        task_to_history    = @{ Value = 8;  Origin = 'Debt'; DueWhen = 'R5 后收敛到 1' }
        message_to_session = @{ Value = 6;  Origin = 'Derived'; DueWhen = '会话投影切换后保持' }
        task_tree          = @{ Value = 4;  Origin = 'Derived'; DueWhen = '保持' }
    }

    Facts = @{
        BatchInLoop  = @{ Value = 0; Origin = 'Derived'; DueWhen = '保持 0（批量必须一次 MountRequest.Targets）' }
        MissingWindow = @{ Value = 0; Origin = 'Derived'; DueWhen = '保持 0（自动触发须给 Scope.CorrelationId）' }
    }
}
