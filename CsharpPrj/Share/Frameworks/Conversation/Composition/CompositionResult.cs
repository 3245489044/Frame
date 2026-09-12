using System.Collections.Generic;

namespace Share.Composition;

/// <summary>
/// 组装产物 —— 每次组装统一产出「产物 + 部件集 + 快照」三件套。
/// 文本 / HTML 组装的 Product 为 string；图组装的 Product 为图结果（诊断 / 部分成功语义入 <see cref="Snapshot"/>）。
/// </summary>
/// <typeparam name="T">产物类型。</typeparam>
/// <param name="Product">组装产物。</param>
/// <param name="Parts">参与组装的部件集（写侧留痕，供回放 / 校验）。</param>
/// <param name="Snapshot">组装快照（部件集 + 逐段绑定）。</param>
public sealed record CompositionResult<T>(
    T Product,
    IReadOnlyList<IComponent> Parts,
    CompositionSnapshot Snapshot);
