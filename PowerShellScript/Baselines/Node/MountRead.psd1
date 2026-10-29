@{
    # 唯一读入口基线（UMF · R1）：只减不增（本批实测：直连读 9）。
    Schema = 'gate-baseline-v1'
    Gate   = 'MountRead'

    DirectGraphReads   = @{ Value = 9; Origin = 'Debt'; DueWhen = 'R3/R4 收口后按实测下调至 0' }
}
