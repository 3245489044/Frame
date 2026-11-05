namespace Modules.Sync.Authority;

/// <summary>
/// 可见线索（敌前摇）词表：玩法侧的“前摇种类”，不是协议投影类型。
/// <para>
/// 序号 0..3 与契约投影 <c>Telegraph</c> 字节的冻结取值顺序一致（<c>None/Projectile/Smoke/Melee</c>），
/// 因此投影写出时可直接做 <c>(byte)cue</c>；该一致性属于契约依赖点，SY-00 落地后需与契约枚举对拍。
/// </para>
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
