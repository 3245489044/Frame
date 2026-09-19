namespace Strategy.Observability.Ports;

/// <summary>
/// 求值上下文（R3-1）：**引擎自有**的作用域 / 定位 / 留痕设施的承载点。
/// <para>
/// 本批只落槽位与接口（<see cref="InstructionIndex"/> / <see cref="OutSlot"/> 由引擎在指令分派前置位；
/// <see cref="Notes"/> / <see cref="Note"/> 供 R3-3 承接脚本族的 <c>⊥</c> 留痕，本批**无写入点**，不提前引入未用设施）。
/// </para>
/// <para>
/// 诊断口径（RF-5）：异常消息保持 <c>[port:bind] 端口 'X' 的 …</c> 前缀与既有措辞逐字不变；
/// 定位信息（指令序号 / 槽名）经本上下文暴露，供 R3-3 起新增算子构造消息时使用。
/// </para>
/// </summary>
public sealed class EvalContext
{
    private readonly List<string> _notes = new();

    /// <summary>按端口键构造（<see cref="PortKey"/> = 诊断定位根）。</summary>
    public EvalContext(string portKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portKey);
        PortKey = portKey;
    }

    /// <summary>端口键（不透明字符串）。</summary>
    public string PortKey { get; }

    /// <summary>当前指令序号（引擎在分派每条指令前置位；初始 -1 = 未进入指令序列）。</summary>
    public int InstructionIndex { get; internal set; } = -1;

    /// <summary>当前指令写槽名（空 = 直出）。</summary>
    public string? OutSlot { get; internal set; }

    /// <summary>留痕面（`⊥`：缺槽 / 解析失败不静默；R3-3 承接 <c>LastNotes</c>）。</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>追加留痕（R3-3 起的写入入口）。</summary>
    public void Note(string note) => _notes.Add(note);

    /// <summary>
    /// 整体替换留痕面（步骤执行单源按调用回传；⊥ 记录不跨调用串台 —— 并发调用下各自独立）。
    /// </summary>
    internal void ReplaceNotes(IReadOnlyList<string> notes)
    {
        _notes.Clear();
        _notes.AddRange(notes);
    }
}
