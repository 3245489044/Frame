using Modules.KeySpace.Diagnostics;
using Modules.KeySpace.Model;

namespace Modules.KeySpace.Abstractions;

/// <summary>
/// 键空间代数：族 × 面 × 坐标的构造、解析、归一与面间转换唯一入口。
/// <para>面之间只能经坐标转换；兼容模板只读；空身份与歧义均为显式失败，不产出半成品键。</para>
/// </summary>
public interface IKeyAlgebra
{
    /// <summary>按坐标与目标面构造键（缺身份/缺必填占位 ⇒ 抛）。</summary>
    SurfaceKey Build(KeyPoint point, KeySurface surface);

    /// <summary>按坐标与目标面构造键（失败返回诊断，不抛）。</summary>
    bool TryBuild(KeyPoint point, KeySurface surface, out SurfaceKey key, out KeyDiagnostic? diagnostic);

    /// <summary>宽松构造（仅迁移对拍用；缺值以空串代入）。</summary>
    string BuildLenient(KeyPoint point, KeySurface surface);

    /// <summary>解析键到指定面（族提示为空时只接受带族前缀的面）。</summary>
    bool TryParse(
        string? raw,
        KeySurface surface,
        string? familyHint,
        out SurfaceKey key,
        out KeyDiagnostic? diagnostic);

    /// <summary>归一（幂等）：按族提示兼容各面与兼容模板；无法归属 ⇒ 抛。</summary>
    SurfaceKey Normalize(string? raw, string? familyHint);

    /// <summary>归一（失败返回诊断，不抛）。</summary>
    bool TryNormalize(string? raw, string? familyHint, out SurfaceKey key, out KeyDiagnostic? diagnostic);

    /// <summary>面间转换：经坐标重建目标面（同面原样返回）。</summary>
    SurfaceKey Convert(SurfaceKey key, KeySurface target);
}
