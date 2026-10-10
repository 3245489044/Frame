namespace Plate.DslMethod.Usharp;

/// <summary>
/// USharp 记录字段路径常量（**中立单源**）：表达式索引 <c>usharp_get([Doc],'$.x')</c> 与查询/排序/CAS
/// 表达式**同源**生成，消除魔法字符串漂移。
/// <para>
/// 路径语义 ≡ JSON 路径（<c>$.field[.sub]</c>），取值经中立 <see cref="UsharpGet"/>；
/// 本类位于中立库，存储层可引用（无 <c>Usharp.*</c> 依赖）。
/// </para>
/// </summary>
public static class UsharpRecordPaths
{
    public const string ThreadId = "$.ThreadId";
    public const string SessionId = "$.SessionId";
    public const string Status = "$.Status";
    public const string NodeType = "$.NodeType";
    public const string Priority = "$.Priority";
    public const string MaxRetries = "$.MaxRetries";
    public const string CreatedAt = "$.CreatedAt";
    public const string UpdatedAt = "$.UpdatedAt";
    public const string EnvelopeCreatedAt = "$.__createdAt";
    public const string EnvelopeUpdatedAt = "$.__updatedAt";
}
