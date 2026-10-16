namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 表**写面声明源**端口（数据层「表可写性真源」）：按逻辑表 id / 容器名提供 <see cref="TableWriteSpec"/>。
/// 实现 = USharp <c>[Table] ... write:</c> 声明投影（上层适配层 <c>Usharp.Adapters.Declarations</c>）；
/// 存储层仅依赖本端口（零 Usharp 知识），由组合根注入。
/// <para>缺件 / 未声明 ⇒ <c>null</c>（调用方按 fail-closed 视作不可写）。</para>
/// </summary>
public interface ITableWriteSpecSource
{
    /// <summary>取逻辑表的写面声明；未声明 ⇒ <c>null</c>。</summary>
    /// <param name="tableId">逻辑表 id 或容器名。</param>
    TableWriteSpec? GetWriteSpec(string tableId);
}
