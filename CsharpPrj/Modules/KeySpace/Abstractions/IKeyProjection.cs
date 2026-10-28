using System.Collections.Generic;
using Modules.KeySpace.Model;

namespace Modules.KeySpace.Abstractions;

/// <summary>
/// 键面投影唯一入口（GP-1）：同一实体在 Identity / Storage / Endpoint / Display 之间的唯一转换点。
/// <para>
/// 纪律：本接口只做投影，不新增候选表、不做域词判断；候选（只读兼容）形态一律来自声明面
/// <see cref="KeySurfaceSpec.ReadOnlyTemplates"/>。实现由 <c>KeyProjection</c> 提供（零逻辑转发给
/// <see cref="IKeyAlgebra"/>），调用方禁再自建第二套拼接/前缀归一。
/// </para>
/// </summary>
public interface IKeyProjection
{
    /// <summary>把任一声明形态（或裸身份）投影到指定面；不可投影 ⇒ 抛（不产出半成品键）。</summary>
    SurfaceKey Project(string family, string raw, KeySurface surface);

    /// <summary>投影（失败返回 false，不抛）。</summary>
    bool TryProject(string family, string raw, KeySurface surface, out SurfaceKey key);

    /// <summary>
    /// 同一实体在该面的全部可读形态：规范形态在前，只读兼容形态随后（去重保序）。
    /// <para>空输入 ⇒ 空表；不可解析 ⇒ 空表（调用方按「未命中 ≠ 不存在」处理）。</para>
    /// </summary>
    IReadOnlyList<string> Candidates(string family, string raw, KeySurface surface);

    /// <summary>同一性判定（忽略面与兼容形态差异；两端都可归一才判等）。</summary>
    bool SameEntity(string left, string right, string family);
}
