using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 装配视图：多槽位容器 + 必需槽位对账。
    /// <para>通用件：零领域类型（槽键与值一律由调用方注入）。</para>
    /// </summary>
    public sealed class AssemblySlotView
    {
        private readonly Dictionary<string, object?> _slots = new(StringComparer.Ordinal);
        private readonly List<SlotRequirement> _requirements = new();

        /// <summary>登记槽位（不参与必需性对账）。</summary>
        public AssemblySlotView Add(string slot, object? value)
        {
            _slots[slot] = value;
            return this;
        }

        /// <summary>登记槽位并声明为必需（值 <c>null</c> 即缺失）。</summary>
        public AssemblySlotView Require(string slot, object? value, string? missingReason = null)
        {
            _slots[slot] = value;
            _requirements.Add(new SlotRequirement(slot, value is not null, missingReason));
            return this;
        }

        /// <summary>追加对账声明（批量；不覆盖槽值）。</summary>
        public AssemblySlotView Require(IEnumerable<SlotRequirement> requirements)
        {
            ArgumentNullException.ThrowIfNull(requirements);
            foreach (var requirement in requirements) _requirements.Add(requirement);
            return this;
        }

        /// <summary>取槽值（未登记 → null）。</summary>
        public object? Slot(string slot) => _slots.TryGetValue(slot, out var value) ? value : null;

        /// <summary>取槽值（类型不符 / 未登记 → null）。</summary>
        public T? Slot<T>(string slot) where T : class => Slot(slot) as T;

        /// <summary>未达成的必需槽位清单（按声明序）。</summary>
        public IReadOnlyList<string> Missing
            => _requirements.Where(r => !r.Present).Select(r => r.Slot).ToArray();

        /// <summary>必需槽位是否全部达成。</summary>
        public bool IsComplete => Missing.Count == 0;

        /// <summary>缺失 → <see cref="AssemblyIncompleteException"/>。</summary>
        public void EnsureComplete()
        {
            if (IncompleteOf(_requirements) is { } incomplete) throw incomplete;
        }

        /// <summary>
        /// 按声明对账（不保留视图）：缺失项 → <see cref="AssemblyIncompleteException"/>；全部具备 → null。
        /// <para>唯一对账实现：实例视图与「仅需对账」的调用方（如领域异常载体）共用本方法。</para>
        /// </summary>
        public static AssemblyIncompleteException? IncompleteOf(IEnumerable<SlotRequirement> requirements)
        {
            ArgumentNullException.ThrowIfNull(requirements);

            var missing = new List<string>();
            var reasons = new List<string>();
            foreach (var requirement in requirements)
            {
                if (requirement.Present) continue;
                missing.Add(requirement.Slot);
                if (!string.IsNullOrEmpty(requirement.MissingReason)) reasons.Add(requirement.MissingReason!);
            }

            return missing.Count == 0 ? null : new AssemblyIncompleteException(missing, reasons);
        }
    }
}
