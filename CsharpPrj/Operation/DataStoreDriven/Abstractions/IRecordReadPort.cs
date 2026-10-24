using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 统一行视图（读 / 写 / 删 / 详情同一行身份）。
/// </summary>
/// <param name="Domain">域键（行身份第一段；权威 = 域目录）。</param>
/// <param name="Key">真键（行身份第二段）。</param>
/// <param name="DisplayKey">展示键（缺省 = <paramref name="Key"/>）。</param>
/// <param name="Fields">呈现字段（不含物理列名；缺省空表）。</param>
/// <param name="PayloadJson">规范化载荷（与写回同物：USharp 记录体文本）。</param>
/// <param name="Origin">来源（Live / Snapshot / Derived）。</param>
public sealed record RecordView(
    string Domain,
    string Key,
    string DisplayKey,
    IReadOnlyDictionary<string, string> Fields,
    string PayloadJson,
    string Origin);

/// <summary>
/// 行读端口（以「domain + key」为唯一入口；渲染层不再直接持有 store 端口）。
/// <para>未命中 ⇒ <c>null</c>（禁伪造空行）；域未装配 / 无法列举 ⇒ 抛（fail-closed，禁静默空集）。</para>
/// </summary>
public interface IRecordReadPort
{
    /// <summary>读单行（详情 / 行内编辑初值）。</summary>
    Task<RecordView?> ReadAsync(string domain, string key, CancellationToken ct = default);

    /// <summary>按域列举（可带键前缀；<paramref name="limit"/> 为硬上界）。</summary>
    Task<IReadOnlyList<RecordView>> ReadManyAsync(string domain, string? keyPrefix, int limit, CancellationToken ct = default);
}
