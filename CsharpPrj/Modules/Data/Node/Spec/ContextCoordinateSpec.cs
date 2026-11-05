namespace Modules.Node.Spec;

/// <summary>
/// 身份坐标绑定（声明面 <c>[Domain] coordinates</c>）：身份占位符 → 键族/轴。
/// <para>
/// 语义：该占位符的取值域由 <c>[KeySpace]</c> 的族/轴声明；运行期经唯一解析器求规范值。
/// 框架不在此处猜族（缺绑定由闸A 报错）。
/// </para>
/// </summary>
/// <param name="Name">身份占位符名。</param>
/// <param name="Family">键族 id。</param>
/// <param name="Axis">族内轴名。</param>
/// <param name="Canonical">规范值姿态（required = 必须解析）。</param>
public sealed record ContextCoordinateSpec(string Name, string Family, string Axis, string Canonical)
{
    /// <summary>坐标名（<c>family/axis</c>；解析器键）。</summary>
    public string CoordinateName => $"{Family}/{Axis}";
}
