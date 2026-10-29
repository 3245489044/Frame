@{
    # 失败姿态基线（UMF · R1）：旧写回退 / C# 镜像用量只减不增。
    Schema = 'gate-baseline-v1'
    Gate   = 'MountFailurePosture'

    Facts = @{
        LegacyFallbacks      = @{ Value = 20; Origin = 'Debt'; DueWhen = 'R5 收口后归零' }
        DefaultBindingsUsage = @{ Value = 1;  Origin = 'Debt'; DueWhen = 'R6 判红：生产禁用 MountBindingCatalog.Default' }
    }
}
