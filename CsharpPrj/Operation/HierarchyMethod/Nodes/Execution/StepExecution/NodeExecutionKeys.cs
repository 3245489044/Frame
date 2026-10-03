using System;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 中立节点编排键名约定（与 Agent 侧 RoundKeys 对齐）：编排器据此从数据空间读取决策/执行结果，不硬编码 Agent 类型。
/// 框架只定义键命名约定，具体语义由 Agent 投影器/策略按需解释。
/// </summary>
public static class NodeExecutionKeys
{
    /// <summary>决策词键（rule_match 产出，Agent RoundKeys.Decision）。</summary>
    public const string Decision = "decision";

    /// <summary>
    /// 收集式输入端口保留 token（<c>[Node] inports=</c> 的唯一非具名端口）：声明本端口的步骤把**本作用域全部节点输出源键**
    /// 作为整包入参接收 —— key 驱动组装的唯一扩展点（落地于 <c>NodeStepExecutionService.ResolveInputsAsync</c>）。
    /// <para>收集范围 = 有 provenance 的节点输出源键（<c>{node}.{port}</c> 恒有 + 裸 <c>{port}</c> 先到者胜）；
    /// 数据空间兜底键**不入包**（防 vars 全量键污染包）。整包不是键 ⇒ 不查数据空间、不入 KeyMapping 目标集。</para>
    /// <para>整包值 = <see cref="Modules.Node.Ports.CollectedNodePort"/> 序列（携带溯源，禁从键串反解 node/port）；</para>
    /// <para>整包不产 provenance（无单一来源）⇒ 不参与 NodeToNode 组合边推导。</para>
    /// </summary>
    // 权威: Modules.Node.Ports.NodeCollectPorts.Token（E2 提取；本处兼容转发，保既有符号名与门禁面）
    public const string CollectNodePorts = Modules.Node.Ports.NodeCollectPorts.Token;

    /// <summary>是否为收集式输入端口 token（白名单判定**单一来源**：一切识别/跳过点一律经本方法，禁字面量散落）。</summary>
    public static bool IsCollectToken(string? key)
        => Modules.Node.Ports.NodeCollectPorts.IsCollectToken(key);
}
