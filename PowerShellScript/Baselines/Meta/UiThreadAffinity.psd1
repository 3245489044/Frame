@{
    # ===== UiThreadAffinity 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-UiThreadAffinity.ps1）=====
    # 语义：WebView2 线程亲和的**结构判据载体**（源码文本 / 行号序 / 消息模板），非执行者叙述。
    #   根因见 Docs\analysis\2026\10\10.25\2026-10-25-HtmlViewer首帧渲染跨线程失败-根因与修复施工单.md §二/§三。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'gate-baseline-v1'
    Gate   = 'UiThreadAffinity'

    LifecycleHostFile  = @{ Value = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Transport\WebViewLifecycleHost.cs'; Origin = 'Derived'; DueWhen = '' }
    CoreHostFile       = @{ Value = 'Frame\CsharpPrj\Plate\HtmlViewer\HtmlViewer.cs'; Origin = 'Derived'; DueWhen = '' }
    SinkFile           = @{ Value = 'Projects\FileDriven\FileDriven.Execution\Diagnostics\LogHtmlViewSink.cs'; Origin = 'Derived'; DueWhen = '' }
    MessageCatalogFile = @{ Value = 'Projects\FileDriven\FileDriven.Execution\Diagnostics\execution_log_messages.json'; Origin = 'Derived'; DueWhen = '' }
    MarshalerDir       = @{ Value = 'Frame\CsharpPrj\Plate\HtmlViewer'; Origin = 'Derived'; DueWhen = '' }

    # IWebViewCore 四个成员（Core / EnsureCoreAsync / NavigateToHtmlDocument / DefaultBackgroundColor）各一处守卫。
    MinCoreGuards            = @{ Value = 4; Origin = 'Derived'; DueWhen = '' }
    MarshalerDefinitionCount = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    MinStreakRefs            = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
}
