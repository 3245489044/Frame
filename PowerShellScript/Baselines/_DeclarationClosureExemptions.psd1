@{
    # ===== 声明闭合孤儿豁免册（F3）指针基线（单源 = JSON，门禁与运行期共读）=====
    # 守卫 = Operation\DSL\KeySpace\Check-DeclarationClosure.ps1（DC5：册存在 + 事实行合格）
    # 内容载体禁止复制到本文件（否则又是两份清单）；本文件只登记路径与口径。
    Schema = 'declaration-exemptions-register-v1'
    Register = 'Compliance'

    DataFile = @{ Value = 'Frame\PowerShellScript\Baselines\Declarations\declaration-exemptions.json'; Origin = 'Derived'; DueWhen = '豁免收敛（items 归零）后退役本登记' }
    RequiredKeys = @{ Value = @('implId', 'Reason', 'DueWhen'); Origin = 'Derived'; DueWhen = '豁免条目字段口径变更时' }
}