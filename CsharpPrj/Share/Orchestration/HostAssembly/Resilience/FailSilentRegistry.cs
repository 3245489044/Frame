using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.HostAssembly.Resilience;

/// <summary>
/// 可开关可回退的注册器（Fail-Silent with Observable Degrade）—— 统一「可选模块装配失败不阻断宿主，
/// 但聚合降级清单让宿主可感知」的骨架，消除各组合根散落的手写 try/catch。
/// 语义：整体装配继续执行，失败模块仅记入降级清单并触发诊断回调。
/// </summary>
public sealed class FailSilentRegistry
{
    private readonly List<ResilientRegistration> _registrations = new();
    private readonly List<string> _degraded = new();
    private readonly object _gate = new();

    /// <summary>登记一个弹性注册项（异步）。</summary>
    public FailSilentRegistry Register(string moduleName, Func<CancellationToken, Task> register,
        Action<string, Exception>? onDegrade = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentNullException.ThrowIfNull(register);
        lock (_gate) _registrations.Add(new ResilientRegistration(moduleName, register, onDegrade));
        return this;
    }

    /// <summary>登记一个弹性注册项（同步便捷重载）。</summary>
    public FailSilentRegistry Register(string moduleName, Action register,
        Action<string, Exception>? onDegrade = null)
    {
        ArgumentNullException.ThrowIfNull(register);
        return Register(moduleName, _ => { register(); return Task.CompletedTask; }, onDegrade);
    }

    /// <summary>依次执行全部注册项；返回本次新增的降级模块清单。不抛出（各失败被吸收为降级）。</summary>
    public async Task<IReadOnlyList<string>> RunAllAsync(CancellationToken ct = default)
    {
        ResilientRegistration[] snapshot;
        lock (_gate) snapshot = _registrations.ToArray();

        var added = new List<string>();
        foreach (var r in snapshot)
        {
            try
            {
                await r.Register(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // 主动取消不降级，直接上抛
            }
            catch (Exception ex)
            {
                lock (_gate) _degraded.Add(r.ModuleName);
                added.Add(r.ModuleName);
                r.OnDegrade?.Invoke(r.ModuleName, ex);
            }
        }
        return added;
    }

    /// <summary>累计降级模块清单（含历次执行）。</summary>
    public IReadOnlyList<string> DegradedModules
    {
        get { lock (_gate) return _degraded.ToArray(); }
    }
}

