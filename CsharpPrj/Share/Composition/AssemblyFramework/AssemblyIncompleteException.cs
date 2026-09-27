using System;
using System.Collections.Generic;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 必需槽位缺失（装配目标态未达成）。
    /// <para>通用件：清单与原因均由调用方（域侧）提供，本类型只承载对账结果。</para>
    /// </summary>
    public sealed class AssemblyIncompleteException : Exception
    {
        public AssemblyIncompleteException(
            IReadOnlyList<string> missingSlots, IReadOnlyList<string> reasons, string? message = null)
            : base(message ?? BuildMessage(missingSlots, reasons))
        {
            MissingSlots = missingSlots ?? Array.Empty<string>();
            Reasons = reasons ?? Array.Empty<string>();
        }

        /// <summary>未达成的必需槽位清单（按声明序）。</summary>
        public IReadOnlyList<string> MissingSlots { get; }

        /// <summary>各缺失项的逐项原因（与 <see cref="MissingSlots"/> 同源产生）。</summary>
        public IReadOnlyList<string> Reasons { get; }

        private static string BuildMessage(IReadOnlyList<string> missingSlots, IReadOnlyList<string> reasons)
            => $"装配目标态未达成：必需槽位缺失 = {string.Join(", ", missingSlots ?? Array.Empty<string>())}。" +
               string.Join(" ", reasons ?? Array.Empty<string>());
    }

    /// <summary>槽位需求声明（域侧构造；缺失原因文案由域侧提供）。</summary>
    /// <param name="Slot">槽位名（诊断/清单展示口径）。</param>
    /// <param name="Present">该槽位是否已达成（<c>false</c> 即缺失）。</param>
    /// <param name="MissingReason">缺失原因（<see cref="Present"/> 为真时忽略）。</param>
    public readonly record struct SlotRequirement(string Slot, bool Present, string? MissingReason);
}
