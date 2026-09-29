@{
    # ===== 内联数值基线清册（F-Debt/F-Derived 待迁移面；守卫 = Operation\DSL\Check-GateBaselines.ps1）=====
    # 语义：**清册外**出现内联数值基线 ⇒ 违规（拦新增）；在册者按 Origin 分类逐件迁移（只减不增）。
    # 口径：Get-InlineBaselineRegex（GateBaseline.psm1 唯一定义）——匹配「名字含 Baseline 的变量 = 十进制字面量」。
    # 首次实测 = 2026-09-29（SR-1 批）。
    Schema   = 'inline-baseline-register-v1'
    Measured = '2026-09-29'
    Canonical = 'Origin=Derived 的基线**必须外置**到 Baselines\<Gate>.psd1；本表只承接存量与 Debt。'
    # 口径演进（实测）：首次用**宽口径**正则得 13 件；SR-W1e 用基线的**严格口径**
    #   （Get-InlineBaselineRegex：仅「名字含 Baseline 的变量 = 十进制字面量」）实测 =
    #   **扫描 39 件 / 命中 15 处 / 9 件 / 0 违规**。差额 4 件为零命中项（U6 报 [可销账] 后再删，只减不增）。

    # 终端态（2026-09-29 SR-1b/SR-1e 实测）：**全部归零 ⇒ 本表清空**。
    #   `GateBaselines -ShowDetail` 实测：内联基线命中 **0 处 / 0 件**；外置基线 **12 件**。
    #   新增内联基线：优先外置到 Baselines\<Gate>.psd1（Origin=Derived）；确属存量债务才登记本表（Origin=Debt + DueWhen）。
    # 历史（已销账）：宽口径曾入 13 件 → 严格口径实测 9 件（+ NodeKeyAddress）→ 全部迁移；
    #   其中 2 件零命中（ContentSingleSource / SessionDuplication）属**第二形态**（内联目标表 `Target = <n>`，宽口径 31 处）另记 SR-1c。
    Entries = @()
}
