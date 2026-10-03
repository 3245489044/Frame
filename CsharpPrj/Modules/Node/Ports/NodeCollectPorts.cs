namespace Modules.Node.Ports;

/// <summary>
/// 收集式输入端口（<see cref="NodeCollectPorts.Token"/>）的整包条目：携带<b>溯源</b>（Node + Port）。
/// <para>为何必须携带溯源而非裸键值对：源键 <c>{node}.{port}</c> 中的节点键自身可含点
/// （如 <c>session.sess-1.msg.1</c>），从键串反解 node/port 不可靠 ⇒ 读侧投影（按节点前缀裁剪 /
/// 排序 / 端口命名）只能以本记录的结构化字段为准。</para>
/// </summary>
/// <param name="Key">源键（qualified <c>{node}.{port}</c> 或裸 <c>{port}</c>）。</param>
/// <param name="Node">产出该端口的节点键。</param>
/// <param name="Port">端口名。</param>
/// <param name="Value">端口值（非 null；空端口按既有规则不合入）。</param>
public sealed record CollectedNodePort(string Key, string Node, string Port, object? Value);

/// <summary>
/// 收集式输入端口 token 约定（键空间的「整包」扩展，唯一非具名端口）：声明本端口的步骤把
/// <b>本作用域全部节点输出源键</b>作为整包入参接收。
/// <para>收集范围 = 有 provenance 的节点输出源键（<c>{node}.{port}</c> 恒有 + 裸 <c>{port}</c> 先到者胜）；
/// 数据空间兜底键不入包（防 vars 全量键污染包）。整包不是键 ⇒ 不查数据空间、不入 KeyMapping 目标集。</para>
/// <para>装配 / 执行侧的识别与跳过点一律经 <see cref="IsCollectToken"/> 判定（白名单单一来源，禁字面量散落）。</para>
/// </summary>
public static class NodeCollectPorts
{
    /// <summary>收集式输入端口保留 token（<c>[Node] inports=</c> 的唯一非具名端口）。</summary>
    public const string Token = "nodes.*";

    /// <summary>是否为收集式输入端口 token。</summary>
    public static bool IsCollectToken(string? key)
        => string.Equals(key, Token, StringComparison.Ordinal);
}
