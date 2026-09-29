@{
    # ===== 键文法门禁基线（纯数据单源；守卫 = Modules\Parsing\Check-KeyGrammar.ps1）=====
    # 迁移自 Check-KeyGrammar.ps1 内联常量（2026-09-29 SR-1：基线外置，脚本零数值基线）。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'gate-baseline-v1'
    Gate   = 'KeyGrammar'
    Source = 'Frame\PowerShellScript\Modules\Parsing\Check-KeyGrammar.ps1'

    # 键文法单源白名单（内核 + 各域文法单源文件）；白名单外命中字面量即违规。
    # 2026-09-29（DS-P1）：五族键模板单源上移层 2 = Modules\Node\Identity\NodeStoreKeyGrammars.cs（D1-② 收编）；
    #   旧件（NodeKeyPrefix / SessionMessageKeys）保留为**无字面量别名门面**，故基线命中数不变（只减不增）。
    GrammarWhitelist = @{
        Value = @(
            'Modules\Parsing\Keys\'
            'Modules\Node\Identity\NodeStoreKeyGrammars.cs'
            'Abstractions\SessionMessageKeys.cs'
            'Abstractions\NodeKeyPrefix.cs'
            'Abstractions\KeyMappingKeys.cs'
            'Abstractions\RelationDeclaration.cs'
            'Ids\FactIdGrammar.cs'
            'Ids\FactIdMigration.cs'
            'NodeDomain\ProcessKeys.cs'
            'Instances\InstanceRegistry.cs'
        )
        Origin  = 'Verdict'
        DueWhen = '重裁触发：D-SR-1（完全依赖倒置）落地后键族单源面迁至应用侧注册面 ⇒ 本表改读注册面产物'
    }

    # K1 键族字面量落点文件数基线（只减不增）；白名单外命中恒须为 0。
    # 行形态统一 = @{ Value; Origin; DueWhen }（U11 强制；2026-09-30 SR-2a2 由 `Baseline` 归一为 `Value`）。
    KeyFamilyLiterals = @(
        @{ Id = 'K1a'; Literal = '"#R"';             Value = 1; Origin = 'Debt'; DueWhen = '实例 id 前缀收编进键文法内核后 → 0' }
        @{ Id = 'K1b'; Literal = '"proc."';          Value = 2; Origin = 'Debt'; DueWhen = 'D-SR-1（① 完全依赖倒置）落地后 → 0（白名单外恒 0）' }
        @{ Id = 'K1c'; Literal = '"cline.session."'; Value = 0; Origin = 'Derived'; DueWhen = '' }
        @{ Id = 'K1d'; Literal = '".round."';        Value = 0; Origin = 'Derived'; DueWhen = '' }
        @{ Id = 'K1e'; Literal = '"keymapping."';    Value = 1; Origin = 'Debt'; DueWhen = '键模板全量产物化（SR-3a）后 → 0（落点改读生成物）' }
        @{ Id = 'K1f'; Literal = '"checkpoint."';    Value = 1; Origin = 'Debt'; DueWhen = '键模板全量产物化（SR-3a）后 → 0（落点改读生成物）' }
        @{ Id = 'K1g'; Literal = '"relations."';     Value = 1; Origin = 'Debt'; DueWhen = '键模板全量产物化（SR-3a）后 → 0（落点改读生成物）' }
    )

    # K2 键面手写切片（文件名含 Key/Keys 的 `Substring(`）落点文件数基线。
    KeySlice = @{ Value = 5; Origin = 'Debt'; DueWhen = '键面手写切片全部改经键文法内核反解后 → 0' }

    # K3 就地键序（`OrderBy(...ParseRound/ParseSeq...)`）命中数基线；已归零 ⇒ 拦存在而非拦新增。
    InPlaceOrder = @{ Value = 0; Origin = 'Derived'; DueWhen = '' }

    # K4 键面自建比较器（`Comparer<string>.Create`）落点文件数基线。
    OwnComparer = @{ Value = 1; Origin = 'Debt'; DueWhen = '比较器收编进键文法内核后 → 0' }

    # K5 正对照下限：键文法内核 `KeyGrammar.Create(` 定义点数（防文法被删/未注册而门禁恒绿）。
    GrammarDefs = @{ Value = 6; Origin = 'Debt'; DueWhen = 'SR-3a 键族文法产物化后上调为**族数**（生成物驱动，≥ 当前 6）' }

    # K6 正对照：声明绑定用例在位（声明模板 ↔ 键文法 的绑定点）。
    BindingTest = @{
        Value  = 'Environment\CentralTest\CSharpTest\Modules.Tests\Parsing.Keys.Tests\Parity\DeclarationBindingTests.cs'
        Origin = 'Derived'
        DueWhen = ''
    }
}
