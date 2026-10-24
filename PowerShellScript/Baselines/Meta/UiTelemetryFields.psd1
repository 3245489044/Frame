@{
    # ===== UiTelemetryFields 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-UiTelemetryFields.ps1）=====
    # 语义：每节遥测含 nodes/bytes/jsMs/outcome；每拍含 batchNodes/lane/queueMs/droppedSections；msgId 已注册。
    Schema = 'gate-baseline-v1'
    Gate   = 'UiTelemetryFields'

    TelemetryContract = @{
        Value = @(
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\Kernel\SectionTelemetry.cs';
               Must = @('nodes=', 'bytes=', 'jsMs=', 'outcome=', 'batchNodes=', 'droppedSections=', 'degradeReason=') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\UiWriteMetrics.cs';
               Must = @('BeatNodes', 'SectionsBulk', 'DroppedSections', 'batchNodes=') },
            @{ File = 'Projects\FileDriven\FileDriven.Execution\Diagnostics\execution_log_messages.json';
               Must = @('"UiWriteMetrics"', '"UiWriteSection"') }
        )
        Origin = 'Derived'
        DueWhen = ''
    }
}
