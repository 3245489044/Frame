using MathematicalAlgorithms.Core;
using MathematicalAlgorithms.Spatial;

namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 固定顺序的世界生成流水线：Stage 唯一且升序，从 <see cref="WorldVoxel.None"/> 开始逐节改写。
/// <ul>
///   <li>覆盖关系显式：每个 section 在自己的 <see cref="IWorldSection.Apply"/> 里判断输入材质。</li>
///   <li>垂直范围由各节 <see cref="IWorldSection.HeightSpanFor"/> 并集聚合，不在这里猜深度。</li>
///   <li>构造即校验：空 / Stage 重复 / 缺 Stage 0 ⇒ 抛。</li>
/// </ul>
/// </summary>
public sealed class WorldPipeline : IHeightSpanSource
{
    private readonly IWorldSection[] _sections;

    public WorldPipeline(IEnumerable<IWorldSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        _sections = sections.OrderBy(section => section.Stage).ToArray();
        if (_sections.Length == 0)
            throw new ArgumentException("世界生成流水线不能为空", nameof(sections));

        for (int i = 0; i < _sections.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(_sections[i].Name))
                throw new ArgumentException("section 名不能为空", nameof(sections));
            if (i > 0 && _sections[i].Stage == _sections[i - 1].Stage)
                throw new ArgumentException(
                    $"section 阶段 {_sections[i].Stage} 重复（'{_sections[i - 1].Name}' 与 '{_sections[i].Name}'）",
                    nameof(sections));
        }

        if (_sections[0].Stage != WorldSectionStages.Terrain)
            throw new ArgumentException(
                $"流水线必须从地表节（Stage {WorldSectionStages.Terrain}）开始，实际为 {_sections[0].Stage}",
                nameof(sections));

        var hash = SignatureHash.Begin();
        foreach (var section in _sections)
        {
            hash = SignatureHash.Mix(hash, section.Stage);
            hash = SignatureHash.Mix(hash, section.Signature);
        }

        Signature = hash;
    }

    /// <summary>按 Stage 升序排列的 section（只读）。</summary>
    public IReadOnlyList<IWorldSection> Sections => _sections;

    public long Signature { get; }

    /// <summary>各节垂直影响范围的并集（cm）。</summary>
    public HeightSpan HeightSpanFor(int groundMinCm, int groundMaxCm, int seaLevelCm)
    {
        var span = HeightSpan.Empty;
        foreach (var section in _sections)
            span = span.Union(section.HeightSpanFor(groundMinCm, groundMaxCm, seaLevelCm));
        return span;
    }

    /// <summary>单列（ground 已知）的垂直影响范围。</summary>
    public HeightSpan ColumnSpan(int groundYCm, int seaLevelCm) =>
        HeightSpanFor(groundYCm, groundYCm, seaLevelCm);

    /// <summary>从 None 起逐节应用，返回最终内容。</summary>
    public WorldVoxel Apply(in WorldSectionInput input)
    {
        var current = WorldVoxel.None;
        var probe = input with { Current = current };

        foreach (var section in _sections)
        {
            if (section.Apply(probe, out var next))
            {
                current = next;
                probe = input with { Current = current };
            }
        }

        return current;
    }
}
