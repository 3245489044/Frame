@{
    Schema = 'gate-baseline-v1'
    Gate   = 'FaceWhitelist'

    # 判据输入（声明面路径；缺失 ⇒ 判据不可判定，脚本 fail-closed）。
    ProductGraph = @{
        Value  = 'Projects\Plug\Agent\Config\graph.ushar'
        Origin = 'Derived'
    }
}
