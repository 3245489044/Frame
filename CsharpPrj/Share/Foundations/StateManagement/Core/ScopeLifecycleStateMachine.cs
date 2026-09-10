using System.Collections.Concurrent;

namespace Share.StateManagement
{
    /// <summary>
    /// 作用域生命周期状态机——纯逻辑（无 UI 类型）。
    /// 转移（§4.4）：Empty→Declared→Loading→Loaded / Loading→Failed（失败终态，保留 LastError）/
    /// Failed→Loading（重试须完整事务）/ Loaded→Destroying→Destroyed→(移除→Empty 语义)。
    /// LoadAsync 的渲染段委托给注入的 Func（P3 测试桩；P4 由 ScopeLifecycleAdapter 提供真实渲染）。
    /// 幂等短路（R6）：仅当当前状态为 Loaded（成功态）且同 ConfigPath + ConfigVersion>0 + !forceReload
    /// → 直接成功（不重渲染）。Loading/Failed 状态一律走完整事务——失败不得被短路吞掉（E1 修复）。
    /// </summary>
    public sealed class ScopeLifecycleStateMachine : IScopeLifecycle
    {
        private readonly Func<string, string, bool, Task<LoadResult>> _renderDelegate;
        private readonly Action<string>? _destroyDelegate;
        private readonly ConcurrentDictionary<string, ScopeLifecycleInfo> _states = new(StringComparer.Ordinal);

        public event Action<ScopeLifecycleInfo>? ScopeStateChanged;

        public ScopeLifecycleStateMachine(
            Func<string, string, bool, Task<LoadResult>> renderDelegate,
            Action<string>? destroyDelegate = null)
        {
            _renderDelegate = renderDelegate ?? throw new ArgumentNullException(nameof(renderDelegate));
            _destroyDelegate = destroyDelegate;
        }

        // ── IScopeLifecycle ──

        public async Task<LoadResult> LoadAsync(string scope, string configPath, bool forceReload = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

            // R6 幂等短路：仅当当前状态为 Loaded（成功态）才短路；Loading/Failed 必须走完整事务，
            // 避免「上次失败残留 ConfigVersion>0」被短路误判为成功（E1 修复：失败不得被吞）。
            var current = GetState(scope);
            if (!forceReload && current is { State: ScopeState.Loaded, ConfigPath: not null } &&
                current.ConfigVersion > 0 &&
                string.Equals(current.ConfigPath, configPath, StringComparison.OrdinalIgnoreCase))
            {
                return new LoadResult(scope, true, current.ConfigVersion, current.NodeCount);
            }

            // Empty/Declared/Loaded(重载) → Loading
            var loading = current is null
                ? new ScopeLifecycleInfo(scope, ScopeState.Loading, 0, configPath, 0, null, DateTime.UtcNow)
                : current with { State = ScopeState.Loading, ConfigPath = configPath, LastError = null, UpdatedAtUtc = DateTime.UtcNow };
            SetState(loading);

            // 渲染段委托（P3 测试桩 / P4 Provider）
            LoadResult result;
            try
            {
                result = await _renderDelegate(scope, configPath, forceReload).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = new LoadResult(scope, false, 0, 0, ex.Message);
            }

            if (result.Succeeded)
            {
                var loaded = new ScopeLifecycleInfo(
                    scope,
                    ScopeState.Loaded,
                    (current?.ConfigVersion ?? 0) + 1,
                    configPath,
                    result.NodeCount,
                    null,
                    DateTime.UtcNow);
                SetState(loaded);
                return result with { ConfigVersion = loaded.ConfigVersion };
            }

            // 失败：落 Failed 终态（保留 ConfigVersion 供诊断，LastError 可见）。
            // 重试时须从 Failed → Loading 走完整事务（幂等短路已排除 Failed，E1 修复）。
            var failed = new ScopeLifecycleInfo(
                scope,
                ScopeState.Failed,
                current?.ConfigVersion ?? 0,
                configPath,
                result.NodeCount,
                result.Error ?? "Load failed",
                DateTime.UtcNow);
            SetState(failed);
            return result;
        }

        public Task DestroyAsync(string scope)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);

            var current = GetState(scope);
            var destroying = current is null
                ? new ScopeLifecycleInfo(scope, ScopeState.Destroying, 0, null, 0, null, DateTime.UtcNow)
                : current with { State = ScopeState.Destroying, UpdatedAtUtc = DateTime.UtcNow };
            SetState(destroying);

            // 销毁委托（P3 测试桩 / P4 经 CompositionLifecycleAdapter.ScopeDestroyed 对接）
            _destroyDelegate?.Invoke(scope);

            // Destroying → Destroyed → 移除（Empty 语义）
            var destroyed = destroying with { State = ScopeState.Destroyed, UpdatedAtUtc = DateTime.UtcNow };
            SetState(destroyed);
            _states.TryRemove(scope, out _);

            return Task.CompletedTask;
        }

        public ScopeLifecycleInfo? GetState(string scope)
            => _states.TryGetValue(scope, out var state) ? state : null;

        public IReadOnlyCollection<string> GetScopes()
            => _states.Keys.ToArray();

        // ── 内部：状态落定 + 事件 ──

        private void SetState(ScopeLifecycleInfo info)
        {
            _states[info.Scope] = info;
            ScopeStateChanged?.Invoke(info);
        }
    }
}
