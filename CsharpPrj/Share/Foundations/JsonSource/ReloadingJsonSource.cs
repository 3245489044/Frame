#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace Share.JsonSource;

/// <summary>
/// 单文件 JSON 热重载源。
/// <para>统一口径（同时覆盖 DecisionRules 的两处实现）：</para>
/// <list type="bullet">
/// <item>按 <b>mtime</b> 判定变化（不用内容哈希），未变化直接返回缓存。</item>
/// <item>每次变化只 <b>读盘一次</b>，解析与校验都基于同一份文本。</item>
/// <item>解析/读取失败 <b>保留旧版</b> 并上报，不抛异常；失败也记录 mtime，避免每次调用重读风暴。</item>
/// <item>文件缺失不抛：有旧值返回旧值，无旧值返回 null。</item>
/// <item>框架不决定校验规则，只把文本交给调用方注入的解析器。</item>
/// </list>
/// </summary>
public sealed class ReloadingJsonSource<T> where T : class
{
    private readonly string _path;
    private readonly ReloadSlot<T> _slot;

    /// <param name="path">JSON 文件路径。</param>
    /// <param name="parse">解析委托（返回成功值或错误列表，禁止抛异常）。</param>
    /// <param name="reloadOnChange">是否按 mtime 热重载；false = 仅首次加载。</param>
    /// <param name="onError">失败上报回调（可空）；回调在锁外调用。</param>
    public ReloadingJsonSource(
        string path,
        Func<string, JsonParseResult<T>> parse,
        bool reloadOnChange = true,
        Action<JsonSourceFailure>? onError = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path 不得为空。", nameof(path));
        ArgumentNullException.ThrowIfNull(parse);
        _path = path;
        _slot = new ReloadSlot<T>(parse, reloadOnChange, onError);
    }

    /// <summary>文件路径。</summary>
    public string Path => _path;

    /// <summary>是否至少成功加载过一次。</summary>
    public bool IsLoaded => _slot.IsLoaded;

    /// <summary>最近一次失败原因（成功后清空）。</summary>
    public IReadOnlyList<string> LastErrors => _slot.LastErrors;

    /// <summary>最近一次成功加载时的 mtime（UTC）。</summary>
    public DateTime? LastWriteTimeUtc => _slot.LastWriteTimeUtc;

    /// <summary>读取（必要时重载）；永不因 IO/解析失败抛出。</summary>
    public T? Load() => _slot.Load(_path, null);

    /// <summary>强制下次读取时重新读盘（内容错误修复后无需等待 mtime 变化）。</summary>
    public void Invalidate() => _slot.Invalidate();
}

/// <summary>
/// 多键 JSON 热重载源（每个键一个独立文件/独立缓存）。
/// <para>与 <see cref="ReloadingJsonSource{T}"/> 共用同一 mtime 引擎，语义完全一致；
/// 键之间互不影响（一个键失败不会污染其它键的旧值）。</para>
/// </summary>
public sealed class ReloadingJsonSourceSet<T> where T : class
{
    private readonly Func<string, string> _pathFor;
    private readonly Func<string, JsonParseResult<T>> _parse;
    private readonly bool _reloadOnChange;
    private readonly Action<JsonSourceFailure>? _onError;
    private readonly Dictionary<string, ReloadSlot<T>> _slots = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <param name="pathFor">键 → 文件路径。</param>
    /// <param name="parse">解析委托（返回成功值或错误列表，禁止抛异常）。</param>
    /// <param name="reloadOnChange">是否按 mtime 热重载。</param>
    /// <param name="onError">失败上报回调（可空）；回调在锁外调用。</param>
    public ReloadingJsonSourceSet(
        Func<string, string> pathFor,
        Func<string, JsonParseResult<T>> parse,
        bool reloadOnChange = true,
        Action<JsonSourceFailure>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(pathFor);
        ArgumentNullException.ThrowIfNull(parse);
        _pathFor = pathFor;
        _parse = parse;
        _reloadOnChange = reloadOnChange;
        _onError = onError;
    }

    /// <summary>已建立缓存的键（快照）。</summary>
    public IReadOnlyList<string> Keys
    {
        get { lock (_gate) return Array.AsReadOnly(new List<string>(_slots.Keys).ToArray()); }
    }

    /// <summary>读取指定键；永不因 IO/解析失败抛出。</summary>
    public T? Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("key 不得为空。", nameof(key));

        ReloadSlot<T> slot;
        lock (_gate)
        {
            if (!_slots.TryGetValue(key, out slot!))
            {
                slot = new ReloadSlot<T>(_parse, _reloadOnChange, _onError);
                _slots[key] = slot;
            }
        }

        string path;
        try
        {
            path = _pathFor(key);
        }
        catch (Exception ex)
        {
            _onError?.Invoke(new JsonSourceFailure(string.Empty, key, new[] { ex.Message }));
            return slot.Peek();
        }

        return slot.Load(path, key);
    }

    /// <summary>该键是否至少成功加载过一次。</summary>
    public bool IsLoaded(string key)
    {
        lock (_gate) return _slots.TryGetValue(key, out var slot) && slot.IsLoaded;
    }

    /// <summary>最近一次失败原因（该键）。</summary>
    public IReadOnlyList<string> LastErrors(string key)
    {
        lock (_gate) return _slots.TryGetValue(key, out var slot) ? slot.LastErrors : Array.Empty<string>();
    }

    /// <summary>强制该键下次读取时重新读盘。</summary>
    public void Invalidate(string key)
    {
        lock (_gate)
        {
            if (_slots.TryGetValue(key, out var slot)) slot.Invalidate();
        }
    }

    /// <summary>强制所有键下次读取时重新读盘。</summary>
    public void InvalidateAll()
    {
        lock (_gate)
        {
            foreach (var slot in _slots.Values) slot.Invalidate();
        }
    }
}

/// <summary>mtime + 保留旧版 的共用引擎（内部类型，不对外暴露语义分叉）。</summary>
internal sealed class ReloadSlot<T> where T : class
{
    private readonly object _gate = new();
    private readonly Func<string, JsonParseResult<T>> _parse;
    private readonly bool _reloadOnChange;
    private readonly Action<JsonSourceFailure>? _onError;

    private bool _loaded;
    private bool _force;
    private bool _missingReported;
    private T? _value;
    private DateTime _lastWrite;
    private IReadOnlyList<string> _errors = Array.Empty<string>();

    public ReloadSlot(Func<string, JsonParseResult<T>> parse, bool reloadOnChange, Action<JsonSourceFailure>? onError)
    {
        _parse = parse;
        _reloadOnChange = reloadOnChange;
        _onError = onError;
    }

    public bool IsLoaded
    {
        get { lock (_gate) return _loaded; }
    }

    public IReadOnlyList<string> LastErrors
    {
        get { lock (_gate) return _errors; }
    }

    public DateTime? LastWriteTimeUtc
    {
        get { lock (_gate) return _loaded || _errors.Count > 0 ? _lastWrite : null; }
    }

    public T? Peek()
    {
        lock (_gate) return _loaded ? _value : null;
    }

    public void Invalidate()
    {
        lock (_gate) _force = true;
    }

    public T? Load(string path, string? key)
    {
        JsonSourceFailure? failure;
        T? result;

        lock (_gate)
        {
            failure = null;
            try
            {
                if (!File.Exists(path))
                {
                    if (!_missingReported)
                    {
                        _missingReported = true;
                        _errors = new[] { $"文件不存在：{path}" };
                        failure = new JsonSourceFailure(path, key, _errors);
                    }

                    result = _loaded ? _value : null;
                }
                else
                {
                    _missingReported = false;
                    var write = File.GetLastWriteTimeUtc(path);

                    if (_loaded && !_force && (!_reloadOnChange || write == _lastWrite))
                    {
                        result = _value;
                    }
                    else
                    {
                        var text = File.ReadAllText(path);
                        var parsed = Parse(text);
                        _force = false;
                        _lastWrite = write;

                        if (parsed.Success)
                        {
                            _value = parsed.Value;
                            _loaded = true;
                            _errors = Array.Empty<string>();
                        }
                        else
                        {
                            _errors = parsed.Errors;
                            failure = new JsonSourceFailure(path, key, _errors);
                        }

                        result = _loaded ? _value : null;
                    }
                }
            }
            catch (Exception ex)
            {
                _errors = new[] { ex.Message };
                failure = new JsonSourceFailure(path, key, _errors);
                result = _loaded ? _value : null;
            }
        }

        if (failure is not null) _onError?.Invoke(failure);
        return result;
    }

    private JsonParseResult<T> Parse(string text)
    {
        try
        {
            return _parse(text);
        }
        catch (Exception ex)
        {
            return JsonParseResult<T>.Fail(ex.Message);
        }
    }
}
