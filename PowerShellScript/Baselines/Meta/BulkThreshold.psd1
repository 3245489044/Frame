@{
    # ===== BulkThreshold 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-BulkThreshold.ps1）=====
    # 语义：存在变比率计算与 BulkReplace 分支（双原语），阈值来自配置（禁硬编码在判定处）。
    Schema = 'gate-baseline-v1'
    Gate   = 'BulkThreshold'

    BulkContract = @{
        Value = @(
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\Kernel\BulkThresholdPolicy.cs';
               Must = @('ChangeRatio', 'BulkReplace', 'bulkNodeThreshold', 'bulkRatioThreshold') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\UiWriteRuntimeOptions.cs';
               Must = @('BulkNodeThreshold', 'BulkRatioThreshold', 'BulkReplaceEnabled') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\HtmlBackendRuntime.SurfaceWrite.Prepare.cs';
               Must = @('BulkThresholdPolicy.Decide', 'op = "bulk"') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Assets\HtmlAssetRegistry.cs';
               Must = @('function bulk', 'bulk:bulk') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\DomOps.cs';
               Must = @('Bulk = "bulk"') }
        )
        Origin = 'Derived'
        DueWhen = ''
    }
}
