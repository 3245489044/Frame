@{
    # ===== 门禁耗时基线（非单件判据；`_` 前缀 ⇒ 不进入 GateBaselines 单件清册）=====
    # 语义（R5）：逐件毫秒基线 + 慢件预算；超过警告比 → WARN，超过失败比 → FAIL。
    #   · 基线按 profile 分开（serial / parallel4），因为并发下的 wall time 不可横比；
    #   · 只降不升：Update-GateTimingBaseline.ps1 默认只补缺/降值，升高须显式 -AllowIncrease；
    #   · 事件行形态 = @{ Value; Origin; DueWhen }（与 GateBaselines U9/U11 对齐）。
    Schema = 'gate-timing-v1'

    Tiers = @{
        serial = @{
        }
        parallel4 = @{
            'AgentBuildPoint' = @{ Value = 1013; Origin = 'Derived'; DueWhen = '' }
            'AgentProcessSurface' = @{ Value = 4779; Origin = 'Derived'; DueWhen = '' }
            'AgentStorageIsolation' = @{ Value = 571; Origin = 'Derived'; DueWhen = '' }
            'BatchScope' = @{ Value = 1391; Origin = 'Derived'; DueWhen = '' }
            'EffectiveFace' = @{ Value = 679; Origin = 'Derived'; DueWhen = '' }
            'FsSinkEntry' = @{ Value = 3729; Origin = 'Derived'; DueWhen = '' }
            'GateBaselines' = @{ Value = 3625; Origin = 'Derived'; DueWhen = '' }
            'GateFileIndex' = @{ Value = 1191; Origin = 'Derived'; DueWhen = '' }
            'GateRegistry' = @{ Value = 568; Origin = 'Derived'; DueWhen = '' }
            'InvokeEntrypoints' = @{ Value = 881; Origin = 'Derived'; DueWhen = '' }
            'KeyGrammar' = @{ Value = 151138; Origin = 'Derived'; DueWhen = '' }
            'NodeWriteBypass' = @{ Value = 880; Origin = 'Derived'; DueWhen = '' }
            'UsharpGetParity' = @{ Value = 4326; Origin = 'Derived'; DueWhen = '' }
            'VerifyProportionality' = @{ Value = 7462; Origin = 'Derived'; DueWhen = '' }
            'VerifySingleShot' = @{ Value = 585; Origin = 'Derived'; DueWhen = '' }
        }
    }
}

