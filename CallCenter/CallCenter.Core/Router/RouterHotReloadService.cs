using CallCenter.Core.Cache;
using CallCenter.Core.Discovery;
using CallCenter.Core.Tracks;

namespace CallCenter.Core.Router;

/// <summary>一次热重载已生效的事件数据。</summary>
public sealed record RouterHotReloadedEventArgs(string ContentFingerprint, int ToolCount);

/// <summary>
/// P4-a 常驻宿主（Host CLI --watch / Agent GUI 组合根）共享的「function-libraries.json → 热换 C# 轨」监视服务
/// （SingleImplementation / TH-12，收敛 Host Program.cs 内静态 watch 编排，不再每宿主各自实现）。
/// <para>
/// <b>热换载体</b>：对 <see cref="ToolRouterReference"/> 做<b>原子换 router 快照</b>——每次内容变化以
/// <see cref="ToolRouterFactory.BuildCSharpRouterFromJsonWithFactory"/> 构建<b>全新</b> ToolRouter（含最新 CSharpTrack）→
/// <see cref="ToolRouterReference.Replace"/> 换当前引用。读取方每次拿到完整一致快照，不原位改共享字典
/// （GUI 并发宿主折叠算子读 <c>Tools</c> 时不与后台热换竞态，见 P4-a）。旧 router 在途调用由持有其实例的读取方继续完成。
/// </para>
/// <para>
/// <b>指纹与同源</b>：先一次读入字节快照，<see cref="ContentHash.Compute(byte[])"/> 与 <see cref="CSharpFunctionLibrarySource.FromJson"/>
/// 解析同源该快照（C5），避免二次打开读到中间态；内容指纹未变 → 跳过；重发现为空 / 解析失败 → 跳过且不破坏当前工具面。
/// </para>
/// </summary>
public sealed class RouterHotReloadService : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(400);

    private readonly ToolRouterReference _reference;
    private readonly string _configPath;
    private readonly Func<CSharpFunctionLibrarySource, ICSharpDelegateInvoker?>? _invokerFactory;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private FileSystemWatcher? _watcher;
    private string? _appliedFingerprint;
    private bool _started;
    private bool _disposed;

    /// <param name="reference">热换目标（原子换 router 快照的持有器）；读取方（如 <c>CallCenterFoldToolProvider</c>）经其取当前。</param>
    /// <param name="configPath">function-libraries.json 清单路径。</param>
    /// <param name="invokerFactory">每轮重发现的源 → 执行后端（可为 null = CSharpTrack 缺省 Direct）。</param>
    public RouterHotReloadService(
        ToolRouterReference reference,
        string configPath,
        Func<CSharpFunctionLibrarySource, ICSharpDelegateInvoker?>? invokerFactory = null)
    {
        _reference = reference ?? throw new ArgumentNullException(nameof(reference));
        _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
        _invokerFactory = invokerFactory;
    }

    /// <summary>热换目标引用（供装配方把同一引用交给读取工具面）。</summary>
    public ToolRouterReference Reference => _reference;

    /// <summary>是否已开始文件监视。</summary>
    public bool IsWatching => _watcher is { EnableRaisingEvents: true };

    /// <summary>内容变更且热换已生效时触发（观测：宿主据此记「热重载 C#Track」日志）。</summary>
    public event EventHandler<RouterHotReloadedEventArgs>? Changed;

    /// <summary>开始文件监视（默认先做一次初始装载，使引用立即指向当前清单内容）。幂等。</summary>
    public void Start(bool loadInitial = true)
    {
        if (loadInitial)
            ReloadNow();

        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;

            var dir = Path.GetDirectoryName(_configPath);
            if (string.IsNullOrEmpty(dir)) return;

            var watcher = new FileSystemWatcher(dir, Path.GetFileName(_configPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            _watcher = watcher;
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
        => _ = Task.Run(() => DebouncedReloadAsync(_cts.Token));

    private async Task DebouncedReloadAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(DebounceDelay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ReloadNow();
    }

    /// <summary>
    /// 确定性重载（供测试与初始装载；FileSystemWatcher 触发亦最终落到本方法）：读字节快照 → 指纹去重 →
    /// 重发现构建全新 router → 原子换引用。内容未变 / 重发现为空 / 解析失败返回 null（不破坏当前快照）；
    /// 成功换引用返回所应用的内容指纹。
    /// </summary>
    public string? ReloadNow()
    {
        lock (_gate)
        {
            string fingerprint;
            string json;
            try
            {
                var bytes = File.ReadAllBytes(_configPath);
                if (bytes.Length == 0) return null;
                fingerprint = ContentHash.Compute(bytes);
                json = System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }

            if (_appliedFingerprint == fingerprint) return null;

            ToolRouter next;
            try
            {
                next = ToolRouterFactory.BuildCSharpRouterFromJsonWithFactory(json, _invokerFactory);
            }
            catch
            {
                // 解析失败不破坏当前快照；也不更新指纹（下轮同内容重试，不吞）。
                return null;
            }

            // 有效但重发现为空：跳过替换（保留当前工具面），记录指纹避免重复构建。
            if (next.Tools.Count == 0)
            {
                _appliedFingerprint = fingerprint;
                return null;
            }

            _reference.Replace(next);
            _appliedFingerprint = fingerprint;
            Changed?.Invoke(this, new RouterHotReloadedEventArgs(fingerprint, next.Tools.Count));
            return fingerprint;
        }
    }

    /// <summary>停止监视并释放（幂等）。</summary>
    public void Dispose()
    {
        FileSystemWatcher? watcher;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            watcher = _watcher;
            _watcher = null;
        }

        _cts.Dispose();
        if (watcher is not null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnFileEvent;
            watcher.Created -= OnFileEvent;
            watcher.Dispose();
        }
    }
}
