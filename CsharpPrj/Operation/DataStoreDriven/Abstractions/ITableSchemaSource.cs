namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 表结构声明源端口（数据层「表即声明」）：按逻辑表 id 提供列 DDL 片段。
/// 实现 = USharp <c>[Table]</c> 声明投影（上层适配层 <c>Usharp.Adapters.Declarations</c>）；
/// 存储层仅依赖本端口（零 Usharp 知识），由组合根注入。
/// </summary>
public interface ITableSchemaSource
{
    /// <summary>
    /// 取逻辑表的列 DDL 片段（如 <c>"[Id] INTEGER PRIMARY KEY AUTOINCREMENT"</c>）；
    /// 未声明 ⇒ <c>null</c>（调用方回落手写 DDL，零行为变更）。
    /// </summary>
    IReadOnlyList<string>? GetColumns(string tableId);
}
