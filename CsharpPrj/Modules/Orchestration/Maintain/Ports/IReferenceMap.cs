#nullable enable

namespace Modules.Maintain.Ports
{
    /// <summary>
    /// 引用地图：描述"数据中哪些字段代表对其他数据键的引用"。
    /// 取代面向具体类型的硬编码字段清单，可插拔扩展——新增引用字段只需登记一对读写访问器。
    /// </summary>
    public interface IReferenceMap<TData>
    {
        IReadOnlyList<ReferenceField<TData>> Fields { get; }
    }

    /// <summary>单个引用字段的位置（读/写访问器）。</summary>
    public readonly record struct ReferenceField<TData>(
        string Name,
        Func<TData, string?> Reader,
        Action<TData, string> Writer);
}