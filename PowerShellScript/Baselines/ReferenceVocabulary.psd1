@{
    # ===== ReferenceVocabulary 门禁基线（单源；守卫 = Modules\Node\Check-ReferenceVocabulary.ps1）=====
    # 端点类型（node/keymapping 经 NodeTypes 单源；task/session/history/workflow）同为单源定义，但因其名过短易误判，
    # 不入本判据的词表（其唯一性由单源文件本身 + 消费面均为常量引用保证）。
    Schema = 'gate-baseline-v1'
    Gate   = 'ReferenceVocabulary'

    # 单源字面量命中数基线（只减不增）
    SingleSourceLiteral = @{ Value = 13; Origin = 'Debt'; DueWhen = '词表收敛（SR-4 同形字面量判据）落地后 → 下调' }
}
