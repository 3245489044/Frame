using UniversalWorkflowFramework.StorageFramework.Batch;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 域级**行端口**契约（「域 → 行」的读写删唯一接缝）：一行 =（物理落点，键，文档文本）。
/// <para>
/// 语义：本端口只做「键 + 文档」原语；**域不变式**（任务状态机 / 历史整体性 / 图边一致性）
/// 留在域适配器（校验器 / 映射器 / 写后动作委托），不进入本端口。
/// </para>
/// <para>
/// 纪律（行写通用化 D1–D7）：① fail-closed（未声明可写不调用）；② 逐项 <see cref="BatchItemOutcome"/>
/// 可见化（禁整批静默）；③ 键不可改（读-合并-写）；④ 写经库级单写通道；⑤ 批量唯一构建点（不旁路 <c>IBatchStore</c> 自拼 SQL）。
/// </para>
/// </summary>
public interface IStoreRowPort
{
    /// <summary>物理落点（SQLite 表名 / LiteDB 集合名）。</summary>
    string Place { get; }

    /// <summary>键列名（KvUsharp 恒 <c>[NodeKey]</c>；图域 = 资源 id 列）。</summary>
    string KeyColumn { get; }

    /// <summary>详读单行（编辑框初值）；不存在 ⇒ <c>false</c>。</summary>
    /// <param name="key">真键（行键）。</param>
    /// <param name="docText">文档文本（真源；如 USharp <c>[Record]</c> 文本）。</param>
    bool TryRead(string key, out string docText);

    /// <summary>行级 upsert（幂等覆写；主键不可改，越界 ⇒ <c>Rejected</c> 可见化）。</summary>
    /// <param name="key">真键。</param>
    /// <param name="docText">文档文本。</param>
    BatchItemOutcome Upsert(string key, string docText);

    /// <summary>行级删除（逐项结果，不静默）。</summary>
    /// <param name="key">真键。</param>
    BatchItemOutcome Delete(string key);
}
