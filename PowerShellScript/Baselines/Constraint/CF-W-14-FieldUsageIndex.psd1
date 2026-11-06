@{
    Schema = 'gate-baseline-v1'
    Gate = 'CF-W-14-FieldUsageIndex'
    Source = 'Environment\CentralTest\Tools\Gate\Constraint\Check-CF-W-14-FieldUsageIndex.ps1'

    # 正对照下限：索引确实覆盖当前扫描面（源树数）。扫描面扩面须同步重基线（一次性诚实重基线）。
    ScanFiles = @{ Value = 413; Origin = 'Derived'; DueWhen = '' }

    # 棘轮：符号为 null 的成员访问中，成员名属于配置字段的计数（缺引用装配的间接后果）。
    UnresolvedReceivers = @{
        Value = 317
        Origin = 'Debt'
        DueWhen = '逐项销账：优先补齐缺失引用装配/归属可静态确认的接收者；CW-04/10/13 接入索引时随消费面收敛，只减不增'
    }
}
