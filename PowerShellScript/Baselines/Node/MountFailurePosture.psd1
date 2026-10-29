@{
    # 失败姿态基线（UMF · R1/R6）：旧写回退 / C# 镜像用量 → 0（本批实测均 0）。
    Schema = 'gate-baseline-v1'
    Gate   = 'MountFailurePosture'

    LegacyFallbacks      = @{ Value = 0; Origin = 'Derived'; DueWhen = '保持 0（旧写回退只减不增）' }
    DefaultBindingsUsage = @{ Value = 0; Origin = 'Derived'; DueWhen = '保持 0（生产禁用 MountBindingCatalog.Default）' }
}
