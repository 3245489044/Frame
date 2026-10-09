using System.Text.Json;
using DataCenter.Protocol.Standards;
using DataCenter.Protocol.Standards.Descriptors;

namespace DataCenter.Standards;

/// <summary>
/// 描述符驱动的通用读取器门面（批 B-3b 两段式：与手写读取器对拍用）。
/// <para>常规层由引擎结构读取；半规则层（<c>step</c> 四形态 / <c>valueSource</c> 多形态 / <c>expr</c> 子层 / 派生位等）
/// 由清单（SSD）声明的 <c>Reader</c> id 经 <c>StandardReaders</c> 注册表接管——**接线单源 = SSD**，无独立钩子件。</para>
/// </summary>
public static class StandardDescriptorReader
{
    /// <summary>由描述符读取 <c>impl</c> 层 → <see cref="PortImplStandard"/>（常规层）；半规则成员由清单声明的 reader 接管。</summary>
    public static PortImplStandard ReadImpl(JsonElement impl)
        => ReadImpl(impl, "(descriptor)");

    /// <summary>由描述符读取 <c>impl</c> 层（<paramref name="context"/> = 键路径，如 <c>ports.A.B.impl</c>；供键闭包诊断）。</summary>
    internal static PortImplStandard ReadImpl(JsonElement impl, string context)
        => (PortImplStandard)StandardReaderEngine.ReadLayer(StandardDescriptors.Impl, impl, context);

    /// <summary>按层名读取（通用入口；层未登记即抛；<paramref name="context"/> = 键路径）。</summary>
    internal static object Read(string layer, JsonElement obj, string context)
    {
        var descriptor = StandardDescriptors.All.FirstOrDefault(item => item.Layer == layer)
            ?? throw new StandardViolationException($"描述符层 '{layer}' 未登记");
        return StandardReaderEngine.ReadLayer(descriptor, obj, context);
    }

    /// <summary>按层名读取（通用入口；层未登记即抛）。</summary>
    public static object Read(string layer, JsonElement obj)
        => Read(layer, obj, layer);
}
