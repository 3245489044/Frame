using System;

namespace Share.HierarchySupport
{
    /// <summary>
    /// 数据契约 —— 统一数据传递标准的数据项声明（单一事实源）。
    /// 每个跨边界数据项以一条契约注册：键（唯一）+ 数值类型 + 方向 + 版本 + 生产 / 消费方 + 作用域。
    /// 定死的是契约形态（本 record 的文法）；不定死的是实例（经注册表扩展、版本化演进）。
    /// </summary>
    public sealed record DataContract(
        string Key,
        Type ValueType,
        DataDirection Direction,
        int SchemaVersion = 1,
        string? Producer = null,
        string? Consumer = null,
        DataScope Scope = DataScope.Round,
        bool Persist = true)
    {
        /// <summary>值是否可接受（null 或类型可赋值）。</summary>
        public bool Accepts(object? value)
            => value is null || ValueType.IsInstanceOfType(value);
    }
}
