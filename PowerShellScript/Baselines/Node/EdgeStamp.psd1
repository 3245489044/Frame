@{
    Schema = 'gate-baseline-v1'
    Gate   = 'EdgeStamp'

    # 判据输入（回填工具导出的结构事实报告；缺失 ⇒ 判据不可判定，脚本 fail-closed）。
    ReportPath = @{
        Value   = 'Data\Tests\edge-stamp\latest.json'
        Origin  = 'Derived'
        DueWhen = ''
    }
}
