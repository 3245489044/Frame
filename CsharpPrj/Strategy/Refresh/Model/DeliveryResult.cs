using System;

namespace Strategy.Refresh.Model
{
    /// <summary>
    /// 写落地结果（L0 契约，零领域）：把「送达」与「内容变化」拆成两个事实。
    /// <para><see cref="Delivered"/> = false ⇒ 未送达，调度器按 at-least-once 置回重试。</para>
    /// <para><see cref="Delivered"/> = true 且 <see cref="ContentChanged"/> = false ⇒ 已送达但内容未变，不推进 baseline / 进度水位。</para>
    /// <para>二者同时为 true ⇒ 推进 baseline + 进度水位。</para>
    /// </summary>
    public sealed record DeliveryResult(
        bool Delivered,
        bool ContentChanged,
        string? Fingerprint,
        Exception? Error = null)
    {
        /// <summary>未送达（可重试）。</summary>
        public static DeliveryResult NotDelivered(Exception? error = null)
            => new(false, false, null, error);

        /// <summary>已送达且内容变化。</summary>
        public static DeliveryResult Changed(string? fingerprint)
            => new(true, true, fingerprint);

        /// <summary>已送达但内容未变。</summary>
        public static DeliveryResult Unchanged(string? fingerprint)
            => new(true, false, fingerprint);
    }
}
