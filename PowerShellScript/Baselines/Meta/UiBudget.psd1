@{
    # ===== UiBudget 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-UiBudget.ps1）=====
    # 语义：Append/Trim 动词四层闭合（契约 / 协调器 / 原语 / JS）+ 刷新分层（Tick 只产叶子）+ 气泡段流契约。
    #   帧预算阈值（ops/bytes/ms + degradeReason）随后续批扩项。
    Schema = 'gate-baseline-v1'
    Gate   = 'UiBudget'

    VerbClosureLayers = @{
        Value = @(
            'Frame\CsharpPrj\Plate\HtmlViewer\Abstractions\UiWritePlan.cs',
            'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\HtmlBackendRuntime.PlanWrite.cs',
            'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\DomOps.cs',
            'Frame\CsharpPrj\Plate\HtmlViewer\Html\Assets\HtmlAssetRegistry.cs'
        )
        Origin = 'Derived'
        DueWhen = ''
    }

    # B6：Tick 只产叶子（唯一产地 = AsTick；其函数体内禁出现区域/树/流动词）。
    TickPath = @{
        Value = @{
            Pipeline = 'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge\RegionRenderPipeline.cs'
            EntryToken = 'private static RenderOutput AsTick'
            TickToken = 'UiWriteSource.Tick'
            LeafToken = 'UiWriteKind.Leaf'
            ForbiddenTokens = @('UiWritePlan.Trees(', 'UiWritePlan.AppendMany(', 'UiWriteKind.Region')
            WindowRadius = 600
        }
        Origin = 'Derived'
        DueWhen = ''
    }

    # B7：气泡段流契约四层（计划方向枚举 / JS 方向分支 / 段键前缀 / 渲染器分段入口）。
    StreamSectionContract = @{
        Value = @(
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Abstractions\UiWritePlan.cs';
               Must = @('enum UiStreamDirection', 'OldestFirst', 'UiStreamAppend') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Assets\HtmlAssetRegistry.cs';
               Must = @('direction', 'oldestFirst') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Render\HtmlPageRegionLayout.cs';
               Must = @('StreamSectionPrefix', 'bubbles:') },
            @{ File = 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationPageRenderer.cs';
               Must = @('RenderSections(', 'WrapStreamSection(') }
        )
        Origin = 'Derived'
        DueWhen = ''
    }

    # B5：帧预算 + 指标 + 灰度开关（单源载体：选项 / 指标 / 协调器降级入口）。
    # S2 起：预算主维度 = 节点数（MaxNodesPerBeat），MaxOpsPerFlush 降为辅助判据。
    WriteRuntimeContract = @{
        Value = @(
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\UiWriteRuntimeOptions.cs';
               Must = @('FrameBudget', 'InteractiveFastPath', 'MaxOpsPerFlush', 'MaxBytesPerFlush', 'MaxNodesPerBeat') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\UiWriteMetrics.cs';
               Must = @('class UiWriteMetrics', 'CoalescedOps', 'DroppedOps', 'LastDegradeReason', 'ApplyLatencyP95Ms', 'StreamLagMs') },
            @{ File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\HtmlBackendRuntime.PlanWrite.cs';
               Must = @('ApplyFrameBudget', 'OnDropped', 'ReportMetricsIfDue', 'InteractiveFastPath') }
        )
        Origin = 'Derived'
        DueWhen = ''
    }
}
