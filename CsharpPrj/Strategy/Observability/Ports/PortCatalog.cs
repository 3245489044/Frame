using System.Collections.Concurrent;
using Strategy.Observability.Boundary;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>内存端口目录实现（E10）：登记即装饰（记录 + 可选观测），解析即校验。</summary>
public sealed class PortCatalog : IPortCatalog
{
    private readonly ConcurrentDictionary<string, PortHandle> _handles = new(StringComparer.Ordinal);
    private readonly IBoundaryContractRegistry? _contracts;
    private readonly Func<string, IPortBinding, IPortBinding>? _decorator;

    /// <summary>
    /// <paramref name="contracts"/>：可选契约注册表（给出时登记同步播种，重复登记即抛）；
    /// <paramref name="decorator"/>：绑定装饰器（装配期统一埋点入口，如 <see cref="ObservedPortDecorator"/>）。
    /// </summary>
    public PortCatalog(
        IBoundaryContractRegistry? contracts = null,
        CallLog? log = null,
        Func<string, IPortBinding, IPortBinding>? decorator = null)
    {
        _contracts = contracts;
        _decorator = decorator;
        Log = log ?? new CallLog();
    }

    public CallLog Log { get; }

    public IReadOnlyList<BoundaryContract> Contracts
        => _handles.Values.Select(h => h.Contract).OrderBy(c => c.Key, StringComparer.Ordinal).ToArray();

    public PortHandle Register(BoundaryContract contract, IPortBinding binding)
    {
        PortGuard.Required(contract, nameof(contract));
        PortGuard.Required(binding, nameof(binding));
        EnsurePortKind(contract);

        var handle = new PortHandle(contract, Decorate(contract.Key, binding));
        if (!_handles.TryAdd(contract.Key, handle))
            throw new DuplicateBoundaryException(contract.Key);

        try
        {
            _contracts?.Register(contract);
        }
        catch
        {
            // 契约面登记失败（同键重复 / 校验不通过）→ 回滚句柄：两面保持一致，不留“有句柄无契约”的悬挂态。
            _handles.TryRemove(contract.Key, out _);
            throw;
        }

        return handle;
    }

    public bool TryRegister(BoundaryContract contract, IPortBinding binding)
    {
        PortGuard.Required(contract, nameof(contract));
        PortGuard.Required(binding, nameof(binding));
        EnsurePortKind(contract);

        if (!_handles.TryAdd(contract.Key, new PortHandle(contract, Decorate(contract.Key, binding)))) return false;

        try
        {
            _contracts?.TryRegister(contract);
        }
        catch
        {
            // 契约面登记失败（校验不通过）→ 与 Register 同补偿：回滚句柄，不留“有句柄无契约”的悬挂态。
            _handles.TryRemove(contract.Key, out _);
            throw;
        }

        return true;
    }

    public PortHandle Resolve(string portKey)
        => TryResolve(portKey, out var handle)
            ? handle
            : throw new PortNotRegisteredException(portKey, _handles.Keys.OrderBy(k => k, StringComparer.Ordinal));

    public bool TryResolve(string portKey, out PortHandle handle)
    {
        if (string.IsNullOrWhiteSpace(portKey)) throw new ArgumentException("端口键不能为空", nameof(portKey));
        return _handles.TryGetValue(portKey, out handle!);
    }

    public void Replace(string portKey, IPortBinding binding)
    {
        PortGuard.Required(binding, nameof(binding));
        if (string.IsNullOrWhiteSpace(portKey)) throw new ArgumentException("端口键不能为空", nameof(portKey));
        if (!_handles.TryGetValue(portKey, out var handle))
            throw new PortNotRegisteredException(portKey, _handles.Keys.OrderBy(k => k, StringComparer.Ordinal));
        handle.Binding = Decorate(portKey, binding);
    }

    /// <summary>同步调用入口：解析 + 记录 + 调用（记录必发生，无论绑定为何）。</summary>
    public object? Invoke(string portKey, IPortInput input, CancellationToken ct = default)
        => Resolve(portKey).Binding.InvokeAsync(input, ct).AsTask().GetAwaiter().GetResult();

    /// <summary>异步调用入口（通用驱动层用）。</summary>
    public async ValueTask<object?> InvokeAsync(string portKey, IPortInput input, CancellationToken ct = default)
        => await Resolve(portKey).Binding.InvokeAsync(input, ct).ConfigureAwait(false);

    /// <summary>清空登记与调用记录（测试隔离用）。</summary>
    public void Clear()
    {
        _handles.Clear();
        Log.Clear();
    }

    private static void EnsurePortKind(BoundaryContract contract)
    {
        if (contract.Kind != BoundaryKind.Port)
            throw new BoundaryContractViolationException(contract.Key, "端口目录只接受 Port 类边界契约");
    }

    /// <summary>
    /// 记录（最外层，保证每次调用恰好记一条）→ 可选观测装饰器 → 实际绑定。
    /// 同时作为 <see cref="IPortCatalog.Decorate"/> 的实现：**无契约登记点**调用（内联 invocation）
    /// 经此取得记录装饰，与 <see cref="Register"/> 同源（账目单一写入，禁第二份记录实现）。
    /// </summary>
    public IPortBinding Decorate(string portKey, IPortBinding binding)
    {
        ArgCheck(portKey, nameof(portKey));
        PortGuard.Required(binding, nameof(binding));
        var observed = _decorator is null ? binding : _decorator(portKey, binding);
        return new RecordingBinding(portKey, observed, Log);
    }

    private static void ArgCheck(string portKey, string name)
    {
        if (string.IsNullOrWhiteSpace(portKey)) throw new ArgumentException("端口键不能为空", name);
    }

    /// <summary>记录型装饰：写 <see cref="CallLog"/> 后交内层；异常照记（errorClass 记异常类型名）。</summary>
    private sealed class RecordingBinding : IPortBinding
    {
        private readonly IPortBinding _inner;
        private readonly CallLog _log;

        public RecordingBinding(string portKey, IPortBinding inner, CallLog log)
        {
            PortKey = portKey;
            _inner = inner;
            _log = log;
        }

        /// <summary>端口键（诊断用）。</summary>
        public string PortKey { get; }

        public async ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
        {
            var start = DateTimeOffset.UtcNow;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = await _inner.InvokeAsync(input, ct).ConfigureAwait(false);
                watch.Stop();
                Record(input, succeeded: true, errorClass: null, watch.Elapsed.TotalMilliseconds, result, start);
                return result;
            }
            catch (Exception ex)
            {
                watch.Stop();
                Record(input, succeeded: false, errorClass: ex.GetType().Name, watch.Elapsed.TotalMilliseconds, null, start);
                throw;
            }
        }

        private void Record(IPortInput input, bool succeeded, string? errorClass, double elapsedMs, object? result, DateTimeOffset at)
            => _log.Record(new PortCallView(
                input.PortKey, PortCallView.MethodOf(input.PortKey), input.Args, input.ArgsHash, input.Src,
                succeeded, errorClass, elapsedMs, result, at));
    }
}
