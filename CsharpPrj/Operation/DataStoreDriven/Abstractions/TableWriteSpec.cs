using System.Collections.Generic;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 表**写面声明**读入模型（数据层「表可写性」）：与 <c>[Table]</c> 结构声明同处解析，零领域字面量。
/// <para>
/// 缺省 **fail-closed**：未声明 <c>write:</c> ⇒ <see cref="Editable"/>=false（不可编辑 / 不可批量改写），
/// 且须给出 <see cref="Reason"/> 可见化原因（声明完备性判据）。
/// </para>
/// </summary>
/// <param name="Table">逻辑表 / 容器名。</param>
/// <param name="Editable">是否可写（缺省 false）。</param>
/// <param name="Editor">编辑器形态（<c>record</c>=整记录 / <c>fields</c>=字段表）。</param>
/// <param name="KeyImmutable">键不可改（缺省 true ⇒ 读-合并-写）。</param>
/// <param name="WritableFields">可写字段集（<c>editor=fields</c> 时）。</param>
/// <param name="AllowTypes">类别白名单（如 ur_nodes 的 nodeType）。</param>
/// <param name="AfterWrite">写后动作（<c>none</c> | <c>notify:runtime</c> | <c>cascade:history</c>）。</param>
/// <param name="Reason"><see cref="Editable"/>=false 时**必填**的可见化原因。</param>
public sealed record TableWriteSpec(
    string Table,
    bool Editable,
    string? Editor,
    bool KeyImmutable,
    IReadOnlyList<string>? WritableFields,
    IReadOnlyList<string>? AllowTypes,
    string? AfterWrite,
    string? Reason)
{
    /// <summary>不可写规格（缺省态；<paramref name="reason"/> 为可见化原因）。</summary>
    public static TableWriteSpec NotWritable(string table, string? reason)
        => new(table, Editable: false, Editor: null, KeyImmutable: true,
            WritableFields: null, AllowTypes: null, AfterWrite: null, Reason: reason);

    /// <summary>
    /// 声明完备性：<c>Editable=true</c> ⇒ 有编辑器规格；<c>Editable=false</c> ⇒ 有 reason。
    /// 不合格 ⇒ 声明残缺（门禁红；判据见 <c>Check-TableWriteDeclaration.ps1</c>）。
    /// </summary>
    public bool WellFormed
        => Editable
            ? !string.IsNullOrWhiteSpace(Editor)
            : !string.IsNullOrWhiteSpace(Reason);
}
