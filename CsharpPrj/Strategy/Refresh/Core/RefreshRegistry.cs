using System;

using Strategy.Refresh.Diagnostics;
using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Core
{
    /// <summary>
    /// 刷新注册表（L1）：登记源（<see cref="IRefreshSource"/> + <see cref="RefreshDescriptor"/>）与渲染器
    /// （<see cref="IRefreshRenderer"/>），是「已注册面」与「登记语义」的**唯一真源**。
    /// <para><b>登记语义</b>：同键同实例 ⇒ 幂等（no-op，返回 false）；同键异实例 ⇒ 替换（旧源先退订并停，
    /// 用于「换池重订」）；未注册查询 ⇒ <see cref="RefreshNotRegisteredException"/>（调用边界抛，不静默回落）。</para>
    /// <para><b>事件接线</b>：构造时注入 <c>onChanged</c>（源变化 → 调度器标脏），登记即完成订阅、注销即退订；
    /// 调度器不再保存第二份「已登记源」清单，避免双份真源与两套重复语义。</para>
    /// <para>零领域：源 / 渲染器的语义由上层实现，本表只按不透明键索引。</para>
    /// </summary>
    public sealed class RefreshRegistry
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _sources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IRefreshRenderer> _renderers = new(StringComparer.Ordinal);
        private readonly Action<string?, RefreshPriority>? _onChanged;

        /// <param name="onChanged">源变化接线（scope, priority）⇒ 调度器标脏；null = 只登记不接线。</param>
        public RefreshRegistry(Action<string?, RefreshPriority>? onChanged = null) => _onChanged = onChanged;

        private sealed record Entry(RefreshDescriptor Descriptor, IRefreshSource Source, Action<string?> Handler);

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
        /// 登记一个刷新源（唯一登记入口）。返回 true = 发生了实际登记（新增或替换）；false = 幂等重复（同实例）。
        /// <para>替换时旧源会先退订事件并 <see cref="IRefreshSource.Stop"/>；新源在此完成事件接线（是否 Start 由编排面决定）。</para>
        /// </summary>
        public bool RegisterSource(RefreshDescriptor descriptor, IRefreshSource source)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentNullException.ThrowIfNull(source);
            if (string.IsNullOrWhiteSpace(descriptor.SourceId))
                throw new RefreshRegistrationException("RefreshDescriptor.SourceId 不得为空");

            var handler = new Action<string?>(scope => _onChanged?.Invoke(scope, source.Priority));
            Entry? replaced = null;
            lock (_gate)
            {
                if (_sources.TryGetValue(descriptor.SourceId, out var exist))
                {
                    if (ReferenceEquals(exist.Source, source)) return false; // 幂等
                    replaced = exist;
                }
                _sources[descriptor.SourceId] = new Entry(descriptor, source, handler);
            }

            if (replaced is not null)
            {
                replaced.Source.Changed -= replaced.Handler;
                replaced.Source.Stop();
            }
            if (_onChanged is not null) source.Changed += handler;
            return true;
        }

        /// <summary>注销一个刷新源（按实例；幂等）。返回 true = 实际移除（同时退订并停止该源）。</summary>
        public bool UnregisterSource(IRefreshSource source)
        {
            if (source is null) return false;
            Entry? removed = null;
            lock (_gate)
            {
                string? key = null;
                foreach (var kv in _sources)
                {
                    if (ReferenceEquals(kv.Value.Source, source)) { key = kv.Key; break; }
                }
                if (key is not null)
                {
                    removed = _sources[key];
                    _sources.Remove(key);
                }
            }
            if (removed is null) return false;

            removed.Source.Changed -= removed.Handler;
            removed.Source.Stop();
            return true;
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
