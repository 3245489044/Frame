namespace Plate.DslMethod.Abstractions;

/// <summary>公理层级（对齐 clinerules AxiomHierarchyStructure LAYER_ORDER=[L0,L0.5,L1,L2,L3]）。
/// 枚举成员不能含点，L0.5 以 L0_5 表示；ToFolder 映射磁盘安全目录名（L05）。</summary>
public enum AxiomLevel
{
    L0,
    L0_5,
    L1,
    L2,
    L3,
}

/// <summary>公理层级的目录名 / 展示名 / 解析辅助。</summary>
public static class AxiomLevelExtensions
{
    /// <summary>磁盘安全目录名（L0.5 → L05）。</summary>
    public static string ToFolder(this AxiomLevel level) => LevelInfo(level).Folder;

    /// <summary>展示名（保留句点）。</summary>
    public static string ToDisplay(this AxiomLevel level) => LevelInfo(level).Display;

    public static AxiomLevel Parse(string s) =>
        (s?.Trim() ?? "") switch
        {
            "L0" => AxiomLevel.L0,
            "L0.5" or "L05" or "L0_5" => AxiomLevel.L0_5,
            "L1" => AxiomLevel.L1,
            "L2" => AxiomLevel.L2,
            "L3" => AxiomLevel.L3,
            _ => throw new ArgumentOutOfRangeException(nameof(s), s, "未知公理层级"),
        };

    public static bool TryParse(string? s, out AxiomLevel level)
    {
        try
        {
            level = Parse(s ?? "");
            return true;
        }
        catch
        {
            level = default;
            return false;
        }
    }

    /// <summary>单表驱动：目录名 + 展示名（消除并行 switch）。非法枚举值直接抛错，不静默落 L2。</summary>
    private static (string Folder, string Display) LevelInfo(AxiomLevel level) => level switch
    {
        AxiomLevel.L0 => ("L0", "L0"),
        AxiomLevel.L0_5 => ("L05", "L0.5"),
        AxiomLevel.L1 => ("L1", "L1"),
        AxiomLevel.L2 => ("L2", "L2"),
        AxiomLevel.L3 => ("L3", "L3"),
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "未知公理层级"),
    };
}
