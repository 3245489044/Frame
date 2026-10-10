@{
    # ===== VerifySingleShot 门禁基线（单源；守卫 = Operation\DSL\Check-VerifySingleShot.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'VerifySingleShot'

    # 正对照下限：Frame\Invoke\*.ps1 入口件数 ≥ 此值（入口目录被掏空 ⇒ 判据无标的假绿）
    EntryCount = @{ Value = 5; Origin = 'Derived'; DueWhen = '可由 Frame\Invoke\*.ps1 件数复算 ⇒ 后续改由生成器产出' }
}
