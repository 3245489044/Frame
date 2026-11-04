@{
    Schema = 'gate-baseline-v1'
    Gate   = 'NoRoleAttribution'

    # 归属判定黑名单（`Role ==/!= ConversationRole.*`）的显式豁免清单（棘轮只降不升）。
    # 当前无豁免（Value 空 = 无）：B4 已把 History feed 切到通道白名单。
    Allow = @(
        @{ Value = ''; Origin = 'Derived'; DueWhen = '' }
    )
}
