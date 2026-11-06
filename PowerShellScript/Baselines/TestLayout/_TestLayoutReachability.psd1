@{
    # ===== TestLayout 可达性基线（登记册；`_` 前缀 ⇒ 不进入单件门禁基线判据）=====
    # 语义：文件级**投影**的存量缺口计数（只减不增）。不是「每个源码文件必须有测试」——
    #   测试侧 = 无任何证据（L1/L2/L3）的载体文件数；
    #   源码侧 = 无任何测试投影的可测源码文件数（豁免/生成/接口/枚举不计）。
    # 派生口径：Environment\CentralTest\TestRunner\TestLayout.psm1::Get-TestLayoutFileMap().Stats
    # 消费方：Tools\Gate\TestLayout\Check-TestFileOwned.ps1 / Check-TestSourceReachable.ps1
    # 纪律：新增缺口由「增量检查」直接判红；本表只用于冻结存量，**禁上调**（DueWhen 到期须复核）。
    Schema  = 'test-layout-reachability-v1'
    Derived = '2026-11-06'

    # 实测（2026-11-06，全仓）：载体 1073 / 辅助 202 / 无主 154；可测源码 6017 / 已覆盖 1914 / 未覆盖 4103
    TestUnowned = @{ Value = 153; Origin = 'Debt'; DueWhen = '无主测试文件逐项补 covers 或删除后下降' }
    SourceUncovered = @{ Value = 4103; Origin = 'Debt'; DueWhen = '按组补测试 / 确认结构豁免后下降' }
    Reference = @{
        Carriers    = 1073
        Helpers     = 202
        Testable    = 6017
        Exempt      = 687
        Covered     = 1914
        L1          = 405
        L2          = 917
        L3          = 0
    }
}
