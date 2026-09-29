@{
    # ===== NodeKeyAddress 门禁基线（单源；守卫 = Modules\Node\Check-NodeKeyAddress.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'NodeKeyAddress'

    # A7 正对照（防假绿）：位置声明数 ≥ 基线；> 基线时列待消除（新增位置须同步上调）
    # 2026-09-27 实测：5 域 9 位置（conversation 5（含 archive-raw-legacy 兼容读）/ segment 2 / message 1 / process 1 / task-node 1）
    PlacementCount = @{ Value = 9; Origin = 'Derived'; DueWhen = 'SR-3c：位置行由 loc 词集 + 域归属展开生成后，本值改由生成器产出' }
}
