namespace Modules.Sync.Contracts;

/// <summary>
/// 可见线索词表（契约面）：序号与投影 <c>Telegraph</c> 字节的冻结取值一一对应。
/// <para>写投影时可直接 <c>(byte)cue</c>；<b>禁止</b>在别处另建同义枚举。</para>
/// <para>MVP 的脚本化敌人只产生 <see cref="Projectile"/>；<see cref="Smoke"/>/<see cref="Melee"/> 是保留词，不发射。</para>
/// </summary>
public enum TelegraphCue : byte
{
    /// <summary>无前摇。</summary>
    None = 0,

    /// <summary>远程技能前摇。</summary>
    Projectile = 1,

    /// <summary>烟雾类前摇（MVP 不发射，保留词）。</summary>
    Smoke = 2,

    /// <summary>近战前摇（MVP 不发射，保留词）。</summary>
    Melee = 3,
}
