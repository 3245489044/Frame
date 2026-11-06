#nullable enable

using System;
using System.Collections.Generic;

namespace Share.SurfaceModel.Selection;

/// <summary>一次外观选择快照（视图 × 档 × 载体 × 覆盖 + 指纹）。</summary>
/// <param name="ViewKey">请求视图键（产品视图键，不要求等于表面声明键）。</param>
/// <param name="ProfileId">外观档。</param>
/// <param name="CarrierId">载体 id。</param>
/// <param name="Overrides">显式覆盖（最高优先级，参与指纹）。</param>
/// <param name="Fingerprint">选择指纹（缓存/写槽失效依据）。</param>
public sealed record SurfaceSelection(
    string ViewKey,
    string ProfileId,
    string CarrierId,
    IReadOnlyDictionary<string, string> Overrides,
    string Fingerprint);
