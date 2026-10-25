@{
    Schema = 'gate-baseline-v1'
    Gate   = 'DimensionReduction'
    # 计数事实 = 源码静态计数（git 索引）；Debt = 目标未达，DueWhen 给出销账条件。
    FormConversions    = @{ Value = 4;    Origin = 'Debt'; DueWhen = '平面化 P3 完成时收敛到 TargetFormConversions' }
    FormSmugglingSites = @{ Value = 5; Origin = 'Debt'; DueWhen = '平面化 P2 完成时收敛到 TargetFormSmugglingSites' }
    WritePrimitives    = @{ Value = 2;    Origin = 'Debt'; DueWhen = '平面化 P3 完成时收敛到 TargetWritePrimitives' }
    LedgerWriters      = @{ Value = 2;      Origin = 'Debt'; DueWhen = '平面化 P3 完成时收敛到 TargetLedgerWriters' }
    EvidenceFloor      = @{ Value = 34;           Origin = 'Derived'; DueWhen = '' }
    TargetFormConversions    = @{ Value = 2; Origin = 'Derived'; DueWhen = '' }
    TargetFormSmugglingSites = @{ Value = 0; Origin = 'Derived'; DueWhen = '' }
    TargetWritePrimitives    = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    TargetLedgerWriters      = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
}
