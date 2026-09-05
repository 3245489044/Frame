#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Forms;
using ECS.Bridge;
using FrameworkAbstractions.ContainerAbstractions;
using Modules.InternalStorage.Extensions;
using TConfig;
using TConfig.ContextFrame.Abstractions;

namespace FrontEndDriven.Container;

/// <summary>
/// 容器聚拢点（ContainerRegistry）——容器事实源统一为 1 的枢纽（SingleImplementation）。
/// 承载 scope ↔ hostKey ↔ entityKey ↔ controlName ↔ role ↔ MountState 六元组：
///   - scope↔hostKey：直接落 IStorageCell 双向映射（原 HostBindingStore 内部化，2026-08-05 收敛）；
///   - entityKey/controlName/role/mountState：本 Registry 内存记录（注册边界归一一次）；
///   - 实际控件：经 <see cref="ControlPool"/> 按 hostKey 解析（唯一控件事实源）。
/// 普通控件索引见 <see cref="ControlIndexRegistry"/>（原 ContainerRegistry[ControlIndex]，仅承载非容器角色控件）。
/// </summary>
public sealed class ContainerRegistry : IDisposable
{
    // 原 HostBindingStore 内部常量（HostBindingStore 类删除后内联；键空间保持兼容旧持久化数据）
    private const string HostBindingMap = "host_binding";
    private const string HostBindingSourcePrefix = "host_binding:source:";

    private readonly IStorageCell _cell;
    private readonly ControlPool? _controlPool;
    private readonly Func<string, System.Windows.Forms.Control?>? _externalHostResolver;
    private readonly Func<string, System.Windows.Forms.Control?>? _visibleTreeResolver;
    private readonly ConcurrentDictionary<string, Entry> _byHostKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _byScope = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _byEntityKey = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    private sealed record Entry(
        ContainerId Id,
        MountState State,
        string Scope);

    /// <summary>
    /// </summary>
    /// <param name="cell">统一存储后端（scope↔hostKey 绑定落 Session 层双向映射）。</param>
    /// <param name="controlPool">控件池（唯一控件事实源）。</param>
    /// <param name="externalHostResolver">外部宿主直绑解析（如 root 容器，仅注册边界特殊点；优先于 ControlPool）。</param>
    /// <param name="visibleTreeResolver">
    /// 可见树宿主解析兜底（S4 统一寻址口径）：<see cref="_controlPool"/> 按 hostKey 未命中时，
    /// 回退到真实可见树递归解析宿主控件（消除「未找到宿主」的假 FAIL——宿主名映射被覆盖时 Control 仍可命中）。
    /// </param>
    public ContainerRegistry(
        IStorageCell cell,
        ControlPool? controlPool = null,
        Func<string, System.Windows.Forms.Control?>? externalHostResolver = null,
        Func<string, System.Windows.Forms.Control?>? visibleTreeResolver = null)
    {
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
        _controlPool = controlPool;
        _externalHostResolver = externalHostResolver;
        _visibleTreeResolver = visibleTreeResolver;
    }

    /// <summary>注册容器（scope↔hostKey 落 IStorageCell 双向映射；其余落入内存记录）。</summary>
    public ContainerHandle Register(ContainerRegistration reg, string source = "code-registered")
    {
        ArgumentNullException.ThrowIfNull(reg);
        if (string.IsNullOrWhiteSpace(reg.Scope)) throw new ArgumentException("作用域不能为空", nameof(reg));
        if (string.IsNullOrWhiteSpace(reg.HostKey)) throw new ArgumentException("宿主标识不能为空", nameof(reg));

        var id = new ContainerId
        {
            ScopeId = reg.Scope,
            HostKey = reg.HostKey,
            EntityKey = reg.EntityKey ?? string.Empty,
            ControlName = string.IsNullOrWhiteSpace(reg.ControlName) ? reg.HostKey : reg.ControlName,
            Role = reg.Role,
        };

        // scope↔hostKey 落地到 IStorageCell 双向映射（原 HostBindingStore.Bind 内联）
        BindScopeHost(id.ScopeId, id.HostKey, source);
        _byHostKey[id.HostKey] = new Entry(id, MountState.Registered, id.ScopeId);
        _byScope[id.ScopeId] = id.HostKey;
        if (!string.IsNullOrEmpty(id.EntityKey))
            _byEntityKey[id.EntityKey] = id.HostKey;

        return ToHandle(id, MountState.Registered);
    }

    /// <summary>注册（scope 已知但实体/控件名归一）。</summary>
    public ContainerHandle Register(
        string scope, string hostKey,
        TConfig.ECS.Entity entity = default,
        string? controlName = null,
        ContainerRole role = ContainerRole.Other,
        string source = "code-registered")
    {
        return Register(new ContainerRegistration
        {
            Scope = scope,
            HostKey = hostKey,
            EntityKey = entity.IsValid ? entity.ToString() : null,
            ControlName = controlName,
            Role = role,
        }, source);
    }

    public void Unregister(ContainerId id)
    {
        if (!_byHostKey.TryRemove(id.HostKey, out _)) return;
        _byScope.TryRemove(id.ScopeId, out _);
        if (!string.IsNullOrEmpty(id.EntityKey)) _byEntityKey.TryRemove(id.EntityKey, out _);
        UnbindScopeHost(id.ScopeId);
    }

    // ── 挂载事实写入（唯一写入点来自 WinFormApplier / EcsGate，S4 接入） ──

    /// <summary>标记挂载成功（WinFormApplier.ApplyRelations 挂载成功后调用）。</summary>
    public void MarkMounted(ContainerId id, System.Windows.Forms.Control? control)
    {
        if (_byHostKey.TryGetValue(id.HostKey, out var entry))
            _byHostKey[id.HostKey] = entry with { State = MountState.Mounted };
    }

    /// <summary>按宿主标识标记挂载成功（仅当该宿主是已登记容器时生效）。</summary>
    public void MarkMounted(string hostKey)
    {
        if (!string.IsNullOrWhiteSpace(hostKey) && _byHostKey.TryGetValue(hostKey, out var entry))
            _byHostKey[hostKey] = entry with { State = MountState.Mounted };
    }

    /// <summary>标记卸载（EcsGate.DestroyScopeEntities 销毁作用域容器时调用）。</summary>
    public void MarkUnmounted(ContainerId id)
    {
        if (_byHostKey.TryGetValue(id.HostKey, out var entry))
            _byHostKey[id.HostKey] = entry with { State = MountState.Orphaned };
    }

    /// <summary>按宿主标识标记卸载（仅当该宿主是已登记容器时生效）。</summary>
    public void MarkUnmounted(string hostKey)
    {
        if (!string.IsNullOrWhiteSpace(hostKey) && _byHostKey.TryGetValue(hostKey, out var entry))
            _byHostKey[hostKey] = entry with { State = MountState.Orphaned };
    }

    // ── 唯一解析出口（供前后端共用） ──

    /// <summary>
    /// 统一宿主寻址（唯一实现）：scope 优先，hostKey 反查兜底。
    /// 收敛 HostMountService.ResolveHost / ContainerAccessPortAdapter.ResolveHostByKeyOrScope 的重复反查。
    /// </summary>
    public ContainerHandle? ResolveByScopeOrHostKey(string scopeOrHostKey)
    {
        if (string.IsNullOrWhiteSpace(scopeOrHostKey)) return null;

        var handle = ResolveByScope(scopeOrHostKey);
        if (handle != null) return handle;

        var scopeReverse = ResolveScopeByHostKey(scopeOrHostKey);
        return !string.IsNullOrEmpty(scopeReverse) ? ResolveByScope(scopeReverse) : null;
    }

    public ContainerHandle? Resolve(ContainerId id)
        => ResolveByHostKey(id.HostKey);

    public ContainerHandle? ResolveByScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return null;
        return _byScope.TryGetValue(scope, out var hostKey) ? ResolveByHostKey(hostKey) : null;
    }

    public ContainerHandle? ResolveByHostKey(string hostKey)
    {
        if (string.IsNullOrWhiteSpace(hostKey)) return null;
        if (!_byHostKey.TryGetValue(hostKey, out var entry)) return null;
        return ToHandle(entry.Id, entry.State);
    }

    public ContainerId? FindIdByScope(string scope)
        => _byScope.TryGetValue(scope ?? string.Empty, out var hk)
            && _byHostKey.TryGetValue(hk, out var e) ? e.Id : null;

    /// <summary>按宿主标识反查作用域（替代硬编码映射）。</summary>
    public string? ResolveScopeByHostKey(string hostKey)
    {
        if (string.IsNullOrWhiteSpace(hostKey)) return null;
        return _byHostKey.TryGetValue(hostKey, out var e) ? e.Scope : null;
    }

    public IReadOnlyList<ContainerHandle> GetAll()
    {
        var list = new List<ContainerHandle>(_byHostKey.Count);
        foreach (var entry in _byHostKey.Values)
            list.Add(ToHandle(entry.Id, entry.State));
        return list;
    }

    public MountState GetMountState(ContainerId id)
        => _byHostKey.TryGetValue(id.HostKey, out var e) ? e.State : MountState.Orphaned;

    public bool IsContainerControlName(string hostKeyOrName)
        => _byHostKey.ContainsKey(hostKeyOrName ?? string.Empty);

    // ── 清理 ──

    /// <summary>
    /// 按作用域注销（StateCenter scope 销毁时调用；解除 scope↔hostKey 绑定并移除内存记录）。
    /// </summary>
    public void UnregisterScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return;

        if (_byScope.TryRemove(scope, out var hostKey))
        {
            UnbindScopeHost(scope);
            _byHostKey.TryRemove(hostKey, out _);
            foreach (var kvp in _byEntityKey.Where(k => string.Equals(k.Value, hostKey, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _byEntityKey.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void Clear()
    {
        foreach (var entry in _byHostKey.Values)
            UnbindScopeHost(entry.Scope);
        _byHostKey.Clear();
        _byScope.Clear();
        _byEntityKey.Clear();
    }

    // ── scope↔hostKey 绑定（原 HostBindingStore 内部化：IStorageCell 双向映射 + 来源标记） ──

    private void BindScopeHost(string scope, string hostKey, string source)
    {
        _cell.MapForward<string, string>(HostBindingMap, scope, hostKey);
        _cell.Set(HostBindingSourcePrefix + scope, source, StorageLayer.Session);
    }

    private void UnbindScopeHost(string scope)
    {
        _cell.Unmap<string, string>(HostBindingMap, scope);
        _cell.Remove(HostBindingSourcePrefix + scope, StorageLayer.Session);
    }

    private ContainerHandle ToHandle(ContainerId id, MountState state)
    {
        // 寻址链（S4 统一口径）：外部直绑 → ControlPool（唯一控件事实源）→ 可见树解析兜底，消除「未找到宿主」假 FAIL。
        var control = _externalHostResolver?.Invoke(id.HostKey)
            ?? _controlPool?.GetControl(id.HostKey)
            ?? _visibleTreeResolver?.Invoke(id.HostKey);
        return new ContainerHandle
        {
            Id = id,
            Control = control,
            State = state,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
    }
}

/// <summary>容器注册参数（ContainerRegistration）。</summary>
public sealed record ContainerRegistration
{
    public required string Scope { get; init; }
    public required string HostKey { get; init; }
    public string? EntityKey { get; init; }
    public string? ControlName { get; init; }
    public ContainerRole Role { get; init; } = ContainerRole.Other;
}

