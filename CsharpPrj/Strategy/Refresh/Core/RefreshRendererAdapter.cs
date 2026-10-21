using System;
using System.Threading;
using System.Threading.Tasks;

using Strategy.Refresh.Model;
using Strategy.Refresh.Ports;

namespace Strategy.Refresh.Core
{
    /// <summary>
    /// <see cref="IRefreshRenderer"/> 通用适配器（委托式，零 HTML / 零领域类型）：
    /// 收拢「取载荷 → 渲染 → 空产物 ⇒ Unchanged / 非空 ⇒ Changed + 指纹 + 进度令牌」的同构样板。
    /// <para>
    /// 载荷为泛型（对象/文本皆可），空判据与指纹由调用方注入；框架不解释产物语义，
    /// 领域侧只需提供渲染委托，不再各写一份 RenderAsync 骨架。
    /// </para>
    /// </summary>
    /// <typeparam name="TPayload">刷新产物载荷（引用类型）。</typeparam>
    public sealed class RefreshRendererAdapter<TPayload> : IRefreshRenderer
        where TPayload : class
    {
        private readonly Func<RefreshRequest, CancellationToken, Task<TPayload?>> _render;
        private readonly Func<TPayload, string> _fingerprintOf;
        private readonly Func<TPayload, bool> _isEmpty;
        private readonly string _emptyFingerprint;

        /// <param name="slotKey">槽键（框架内唯一）。</param>
        /// <param name="render">渲染委托（返回 null ⇒ 空产物）。</param>
        /// <param name="fingerprintOf">非空载荷的内容指纹（与刷新框架同一口径）。</param>
        /// <param name="isEmpty">空产物判据；null ⇒ 仅 null 视为空。</param>
        /// <param name="emptyFingerprint">空产物指纹（Unchanged 结论携带）。</param>
        public RefreshRendererAdapter(
            string slotKey,
            Func<RefreshRequest, CancellationToken, Task<TPayload?>> render,
            Func<TPayload, string> fingerprintOf,
            Func<TPayload, bool>? isEmpty = null,
            string emptyFingerprint = "empty")
        {
            if (string.IsNullOrWhiteSpace(slotKey))
                throw new ArgumentException("槽键不得为空。", nameof(slotKey));

            SlotKey = slotKey;
            _render = render ?? throw new ArgumentNullException(nameof(render));
            _fingerprintOf = fingerprintOf ?? throw new ArgumentNullException(nameof(fingerprintOf));
            _isEmpty = isEmpty ?? (static _ => false);
            _emptyFingerprint = emptyFingerprint;
        }

        /// <inheritdoc />
        public string SlotKey { get; }

        /// <inheritdoc />
        public async Task<RefreshArtifact> RenderAsync(RefreshRequest request, CancellationToken ct = default)
        {
            var payload = await _render(request, ct).ConfigureAwait(false);
            if (payload is null || _isEmpty(payload))
                return RefreshArtifact.Unchanged(_emptyFingerprint);

            var fingerprint = _fingerprintOf(payload);
            return RefreshArtifact.Changed(payload, fingerprint, progressToken: fingerprint);
        }
    }
}
