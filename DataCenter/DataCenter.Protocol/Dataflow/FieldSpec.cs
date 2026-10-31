namespace DataCenter.Protocol.Dataflow;

/// <summary>
/// 字段契约（数据产品 in/out 的最小声明单位）—— 只承载「名 / 类型 / 是否必填」三元，
/// 不含任何取值逻辑（取值属运行期 provenance，不属声明面）。
/// <para>
/// 字段名唯一性与端点闭合由装载期判据（C16/C17 + <c>Check-FieldLineageClosure</c>）裁决，
/// 本记录不承担校验职责（契约层零实现）。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §3.4。
/// </summary>
/// <param name="Name">字段名（产品内唯一；形态 <c>[A-Za-z_][A-Za-z0-9_.]*</c>）。</param>
/// <param name="Type">字段类型名（开放词；默认 <c>any</c>，不做类型系统约束）。</param>
/// <param name="Required">是否必填（缺省 true：产物字段默认是对外承诺）。</param>
public sealed record FieldSpec(string Name, string Type = "any", bool Required = true);
