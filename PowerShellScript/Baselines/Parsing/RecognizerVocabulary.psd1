@{
    # ===== RecognizerVocabulary 门禁基线（单源；守卫 = Modules\Parsing\Check-RecognizerVocabulary.ps1）=====
    # 2026-09-27（K3/A 批）：kind 18 → 24（+ user-message / assistant-message / attachment 及词表同步）；
    #                        rule 15 → 38（快照逐消息规则 +6）。
    Schema = 'gate-baseline-v1'
    Gate   = 'RecognizerVocabulary'

    # kind 词表条目数 / 规则条目数（下限）：两值均可由 Config\agents\cline\recognizers.json 机械复算
    # 2026-10-29（P1-6）：kind 24 → 27（校正现存漂移：实际 27 kinds；规则 38 不变）。
    KindCount = @{ Value = 27; Origin = 'Derived'; DueWhen = 'SR-3b：词表产物化后改由生成器产出（RecognizedKinds.cs + 本基线同源）' }
    RuleCount = @{ Value = 38; Origin = 'Derived'; DueWhen = 'SR-3b：同上（由 recognizers.json 规则数复算）' }
}
