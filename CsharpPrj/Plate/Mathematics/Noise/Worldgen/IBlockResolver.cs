namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 方块角色解析契约：Frame 世界生成只说「角色名」（grass/log/coal_ore…），由上层把它映射成不透明方块 ID。
/// <ul>
///   <li>回退策略由调用方注入，Frame 不预设任何名称别名。</li>
///   <li>缺失角色必须可观测：解析失败或命中回退时，角色名进入 <see cref="MissingRoles"/>，禁止静默。</li>
/// </ul>
/// </summary>
public interface IBlockResolver
{
    /// <summary>角色名 → 不透明方块 ID（true 表示已解析，可能来自回退链）。</summary>
    bool TryResolve(string role, out ushort blockId);

    /// <summary>缺失/回退过的角色名（稳定排序、可重复查询）。</summary>
    IReadOnlyList<string> MissingRoles { get; }
}
