#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Html;

/// <summary>
/// 文档脚本生命周期宿主——把「桥脚本幂等注入 + 就绪确认 + 导航后重注入」从 HtmlViewer 方法体收敛为统一宿主：
/// 桥脚本依赖活 document（WebView2 的 document-created 注入对 NavigateToString 不生效），故每次导航后须在此补注入。
/// 桥脚本自带 <c>__domBridgeInstalled</c> 幂等守卫，对同一 document 重复执行不挂两遍监听器。
/// 本类不持有 CoreWebView2 / UI 线程语义——原始执行与就绪查询都经 <see cref="IHtmlChannel"/> 统一收敛。
/// </summary>
public sealed class HtmlScriptHost
{
    private static readonly IHtmlAssetProvider DefaultAssets = HtmlAssetRegistry.Default;

    private readonly IHtmlChannel _channel;
    private readonly HtmlScriptExecutor _executor;
    private readonly IHtmlAssetProvider _assets;
    private readonly int _retryAttempts;
    private readonly int _retryDelayMs;

    public HtmlScriptHost(
        IHtmlChannel channel,
        HtmlScriptExecutor executor,
        IHtmlAssetProvider? assets = null,
        int retryAttempts = 3,
        int retryDelayMs = 50)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _assets = assets ?? DefaultAssets;
        _retryAttempts = Math.Max(1, retryAttempts);
        _retryDelayMs = Math.Max(0, retryDelayMs);
    }

    /// <summary>桥链路结构化诊断出口（category, message 两元；宿主接线到日志门面，未接线为空操作）。</summary>
    public event Action<string, string>? Diagnostics;

    private void Report(string category, string message) => Diagnostics?.Invoke(category, message);

    /// <summary>文档级行为脚本清单（点击桥 + 通用轮询/刷新桥），各自带 document 级幂等守卫；导航后随 <see cref="EnsureBridgeLoadedAsync"/> 统一注入。</summary>
    private static readonly string[] DocumentScripts =
    {
        HtmlAssetKeys.BridgeScript,
        HtmlAssetKeys.RefreshBridgeScript,
    };

    /// <summary>当前活 document 是否已注入 __domBridge（点击桥随其就位）；临时性失败（document 未就绪/被导航替换）返回 false，不抛。</summary>
    public Task<bool> IsBridgeReadyAsync(CancellationToken ct = default)
        => _channel.IsBridgeReadyAsync(ct);

    /// <summary>
    /// 确保文档行为脚本就绪（P4 注入自愈）：先查桥就绪，未就绪则幂等注入全部文档脚本（点击桥 + 轮询桥）并回读确认；时限内重试。
    /// 返回 true=桥就绪（轮询桥亦已随注入）；false=注入失败（已附带 document 诊断，区分「未就绪」vs「脚本执行异常」vs「chrome.webview 缺失」）。
    /// </summary>
    public async Task<bool> EnsureBridgeLoadedAsync(CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= _retryAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (await _channel.IsBridgeReadyAsync(ct).ConfigureAwait(false))
                return true;
            try
            {
                // 注入全部文档级行为脚本（点击桥 + 轮询桥），各自带 document 级幂等守卫；脚本独立、互不干扰。
                foreach (var key in DocumentScripts)
                {
                    ct.ThrowIfCancellationRequested();
                    await _channel.ExecuteScriptAsync(_assets.GetScript(key), ct).ConfigureAwait(false);
                }
                if (await _channel.IsBridgeReadyAsync(ct).ConfigureAwait(false))
                    return true;
                Report("HtmlChannel_inject_bridge",
                    $"文档脚本注入后 __domBridgeInstalled 仍 false, ExecuteScriptAsync 返回待诊");
            }
            catch (Exception ex)
            {
                Report("HtmlChannel_inject_bridge",
                    $"桥注入尝试 {attempt}/{_retryAttempts} 失败: {ex.GetType().Name}: {ex.Message}");
            }
            await Task.Delay(_retryDelayMs, ct).ConfigureAwait(false);
        }
        Report("HtmlChannel_inject_bridge", await _executor.GetBridgeDiagnosisAsync(ct).ConfigureAwait(false));
        return false;
    }
}