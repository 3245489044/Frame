using Strategy.Refresh.Diagnostics;
using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Core
{
    /// <summary>
    /// 刷新注册表（L1）：登记源（<see cref="IRefreshSource"/> + <see cref="RefreshDescriptor"/>）与渲染器
    /// （<see cref="IRefreshRenderer"/>），并作为「已注册面」的唯一查询处。
    /// <para><b>登记语义</b>：同键同实例 ⇒ 幂等（no-op，返回 false）；同键异实例 ⇒ 替换（旧源先停，
    /// 用于「换池重订」）；未注册查询 ⇒ <see cref="RefreshNotRegisteredException"/>（调用边界抛，不静默回落）。</para>
    /// <para>零领域：源 / 渲染器的语义由上层实现，本表只按不透明键索引。</para>
    /// </summary>
    public sealed class RefreshRegistry
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _sources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IRefreshRenderer> _renderers = new(StringComparer.Ordinal);

        private sealed record Entry(RefreshDescriptor Descriptor, IRefreshSource Source);

        /// <summary>已登记源描述符快照（按标识升序，稳定输出）。</summary>
        public IReadOnlyList<RefreshDescriptor> SourceDescriptors
        {
            get { lock (_gate) return _sources.Values.Select(static e => e.Descriptor).OrderBy(static d => d.SourceId, StringComparer.Ordinal).ToArray(); }
        }

        /// <summary>已登记渲染器槽键快照（升序，稳定输出）。</summary>
        public IReadOnlyList<string> RendererSlotKeys
        {
            get { lock (_gate) return _renderers.Keys.OrderBy(static k => k, StringComparer.Ordinal).ToArray(); }
        }

        /// <summary>已登记源实例快照。</summary>
        public IReadOnlyList<IRefreshSource> Sources()
        {
            lock (_gate) return _sources.Values.Select(static e => e.Source).ToArray();
        }

        /// <summary>
        /// 登记一个刷新源。返回 true = 发生了实际登记（新增或替换）；false = 幂等重复（同实例）。
        /// <para>替换时旧源会被 <see cref="IRefreshSource.Stop"/>（调用方须先退订旧源事件）。</para>
        /// </summary>
        public bool RegisterSource(RefreshDescriptor descriptor, IRefreshSource source)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentNullException.ThrowIfNull(source);
            if (string.IsNullOrWhiteSpace(descriptor.SourceId))
                throw new RefreshRegistrationException("RefreshDescriptor.SourceId 不得为空");

            IRefreshSource? replaced = null;
            lock (_gate)
            {
                if (_sources.TryGetValue(descriptor.SourceId, out var exist))
                {
                    if (ReferenceEquals(exist.Source, source)) return false; // 幂等
                    replaced = exist.Source;
                }
                _sources[descriptor.SourceId] = new Entry(descriptor, source);
            }
            replaced?.Stop();
            return true;
        }

        /// <summary>注销一个刷新源（按实例；幂等）。返回 true = 实际移除。</summary>
        public bool UnregisterSource(IRefreshSource source)
        {
            if (source is null) return false;
            lock (_gate)
            {
                string? key = null;
                foreach (var kv in _sources)
                {
                    if (ReferenceEquals(kv.Value.Source, source)) { key = kv.Key; break; }
                }
                return key is not null && _sources.Remove(key);
            }
        }

        /// <summary>登记一个渲染器。返回 true = 新增；false = 同实例幂等重复。</summary>
        public bool RegisterRenderer(IRefreshRenderer renderer)
        {
            ArgumentNullException.ThrowIfNull(renderer);
            if (string.IsNullOrWhiteSpace(renderer.SlotKey))
                throw new RefreshRegistrationException("IRefreshRenderer.SlotKey 不得为空");
            lock (_gate)
            {
                if (_renderers.TryGetValue(renderer.SlotKey, out var exist) && ReferenceEquals(exist, renderer))
                    return false;
                _renderers[renderer.SlotKey] = renderer;
                return true;
            }
        }

        /// <summary>按槽键取渲染器；未注册 ⇒ <see cref="RefreshNotRegisteredException"/>。</summary>
        public IRefreshRenderer Renderer(string slotKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(slotKey);
            lock (_gate)
            {
                if (_renderers.TryGetValue(slotKey, out var renderer)) return renderer;
            }
            throw new RefreshNotRegisteredException($"未登记刷新渲染器：slotKey=\"{slotKey}\"");
        }

        /// <summary>按槽键尝试取渲染器（不抛）。</summary>
        public bool TryGetRenderer(string slotKey, out IRefreshRenderer renderer)
        {
            renderer = null!;
            if (string.IsNullOrWhiteSpace(slotKey)) return false;
            lock (_gate) return _renderers.TryGetValue(slotKey, out renderer!);
        }
    }
}
