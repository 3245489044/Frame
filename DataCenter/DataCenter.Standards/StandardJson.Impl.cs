using System.Text.Json;
using DataCenter.Protocol.Standards;
using static Plate.JsonMethod.Utilities.JsonTolerantReader;

namespace DataCenter.Standards;

/// <summary>
/// <c>impl</c> 段读取入口（描述符单源化 B-3b-3 后）：板块外层（kind / id / target / method / reason /
/// legacyKind / productFields）+ 反射族实参 + 组合族（deps / calls / project）读取**全部经描述符引擎**
/// （<see cref="StandardDescriptorReader"/> + <c>StandardReaders</c>）。
/// <para>
/// 边界（防两处各判一遍）：本件只做「<c>kind</c> 归一（必填 / 合法）+ 读取入口切换」；
/// **键闭合由引擎按描述符键集承担**（未知键 fail-fast）；**半规则层**（<c>dep</c> 三形态 / <c>step</c> /
/// <c>valueSource</c> / <c>expr</c> / <c>foreach</c> 等）保留为钩子件；
/// <b>跨项一致性**由语义判据（C7–C13）承担，本件不重复表达。
/// </para>
/// </summary>
public static partial class StandardJson
{
    /// <summary>
    /// 单条端口的 <c>impl</c> → 标准模型（B3 主线）：<c>impl</c> 缺省或为简写字符串时取 kind，
    /// 其余声明经描述符引擎按 kind 分派读取（与内核 <c>PortLedger.Read</c> 同口径）。
    /// </summary>
    internal static PortImplStandard ReadImpl(string portKey, JsonElement spec)
    {
        if (!spec.TryGetProperty("impl", out var impl) || impl.ValueKind != JsonValueKind.Object)
        {
            var text = impl.ValueKind == JsonValueKind.String ? impl.GetString() : null;
            return new PortImplStandard(NormalizeKind(portKey, text));
        }

        NormalizeKind(portKey, Str(impl, "kind"));
        return StandardDescriptorReader.ReadImpl(impl, $"ports.{portKey}.impl");
    }

    /// <summary>
    /// 种类归一（SK-28：**缺省已取消** —— <c>impl.kind</c> 必填，缺即抛；非法即抛）。
    /// 取值域 = 词表唯一源（<c>Kinds</c>，禁手抄 kind）；<c>delegate</c> 属遗留值（只读兼容，新增禁止）。
    /// </summary>
    private static string NormalizeKind(string portKey, string? text)
        => text is null or ""
            ? throw new StandardViolationException(
                $"[port:standard] 端口 '{portKey}' 的 impl.kind 必填（缺省已取消：不得再隐式按 delegate 处理；"
                + $"允许值：{PortImplVocabulary.RenderList(PortImplVocabulary.Kinds)}，"
                + "其中 delegate 为遗留值（新增禁止））")
            : PortImplVocabulary.IsKnownKind(text)
                ? text
                : throw new StandardViolationException(
                    $"[port:standard] 端口 '{portKey}' 的 impl.kind '{text}' 非法（允许值："
                    + $"{PortImplVocabulary.RenderList(PortImplVocabulary.Kinds)}，其中 delegate 为遗留值（新增禁止））");
}
