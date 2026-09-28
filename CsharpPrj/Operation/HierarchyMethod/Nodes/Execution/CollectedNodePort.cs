namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 收集式输入端口（<see cref="NodeExecutionKeys.CollectNodePorts"/>）的整包条目：携带**溯源**（Node + Port）。
/// <para>为何必须携带溯源而非裸键值对：源键 <c>{node}.{port}</c> 中的节点键自身可含点
/// （如 <c>session.sess-1.msg.1</c>），从键串反解 node/port 不可靠 ⇒ 读侧投影（按节点前缀裁剪 / 排序 /
/// 端口命名）只能以本记录的结构化字段为准。</para>
/// </summary>
/// <param name="Key">源键（qualified <c>{node}.{port}</c> 或裸 <c>{port}</c>）。</param>
/// <param name="Node">产出该端口的节点键。</param>
/// <param name="Port">端口名。</param>
/// <param name="Value">端口值（非 null；空端口按既有规则不合入）。</param>
public sealed record CollectedNodePort(string Key, string Node, string Port, object? Value);
