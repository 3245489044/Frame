using System;
using System.Collections.Generic;
using System.Linq;
using ParameterResolver.KeyMapping;
using Share.HierarchySupport;

namespace Operation.HierarchyMethod.Nodes.Workflows
{
    /// <summary>
    /// 通用工作流定义加载器（Nodes/Workflows 层，LLM-free）：把声明式工作流 JSON 反序列化出的节点模型
    /// （<see cref="WorkflowDefinition"/>）沿 start→on_success 主路径提取为线性链 <see cref="RoundSpec"/>。
    /// 纯解析（模型 + 主路径提取 + 成功准则/版本/端口归一），零 Agent 依赖；
    /// I/O（文件读取）、JSON 序列化选项、日志与内置默认骨架由消费方（Agent RoundSpecLoader 薄壳）经委托注入 / 保留。
    /// </summary>
    public static class WorkflowSpecLoader
    {
        /// <summary>从 start 沿 on_success 主路径提取线性链；剥离 rule_match 自闭环（决策节点无 on_success，天然终止）。</summary>
        /// <param name="def">反序列化后的工作流定义（Agent 侧以 ConfigJson.WorkflowOptions 反序列化后传入）。</param>
        /// <param name="logInvalid">无效归因日志委托（reason, workflowId）；null = 不记录。</param>
        /// <param name="logMigrated">旧 Schema 迁移诊断委托（version）；null = 不记录。</param>
        public static RoundSpec? BuildRoundSpec(
            WorkflowDefinition? def,
            Action<string, string?>? logInvalid = null,
            Action<string>? logMigrated = null)
        {
            if (def?.Nodes is null || def.Nodes.Count == 0)
            {
                logInvalid?.Invoke("无 Nodes 声明（反序列化全空：检查 JSON 键命名与 Schema 序列化契约是否匹配）", def?.WorkflowId);
                return null;
            }

            var byId = def.Nodes
                .Where(n => !string.IsNullOrEmpty(n.NodeId))
                .ToDictionary(n => n.NodeId!, StringComparer.Ordinal);

            var start = def.Nodes.FirstOrDefault(n =>
                            string.Equals(n.Symbol, "start", StringComparison.OrdinalIgnoreCase))
                        ?? def.Nodes.FirstOrDefault(n =>
                            string.Equals(n.FuncName, "agent_loop_entry", StringComparison.OrdinalIgnoreCase));
            if (start?.NodeId is null)
            {
                logInvalid?.Invoke("未找到 start 入口节点或 start.NodeId 为空（多字属性 NodeId 反序列化失败：JSON PascalCase 键 vs snake_case 命名策略失配）", def.WorkflowId);
                return null;
            }

            var steps = new List<RoundStep>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? currentId = start.NodeId;
            var guard = 0;

            while (currentId != null && visited.Add(currentId) && guard++ < 64)
            {
                if (!byId.TryGetValue(currentId, out var node)) break;

                // start 桩节点（Symbol=start）本身不入主路径步骤，也无需真实 FuncName（可留空桩）。
                var isStart = string.Equals(node.Symbol, "start", StringComparison.OrdinalIgnoreCase);
                if (!isStart && string.IsNullOrEmpty(node.FuncName)) break;
                if (!isStart)
                {
                    steps.Add(new RoundStep(node.FuncName!,
                        node.ParameterKeys ?? new List<string>(),
                        node.ResultKeys ?? new List<string>(),
                        node.InputMapping,
                        node.OutputMapping,
                        node.NodeId));
                }

                // 决策/终止节点无 on_success → 主路径结束（rule_match 自闭环被自然剥离）
                if (node.NextNodeConfig?.Configs is null ||
                    !node.NextNodeConfig.Configs.TryGetValue("on_success", out var next))
                    break;

                // C1 修复：非 on_success 分支（on_error/on_fail/continue/stop…）被 RoundSpec 线性链丢弃 → 显性告警
                var discarded = node.NextNodeConfig.Configs.Keys
                    .Where(k => !string.Equals(k, "on_success", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (discarded.Count > 0)
                    logInvalid?.Invoke(
                        $"节点 '{node.NodeId}' 声明了非 on_success 分支 [{string.Join(", ", discarded)}]，RoundSpec 线性链仅提取 on_success 主路径，这些分支被丢弃（Usharp [Flow] 声明式工作流可消除此截断）",
                        def.WorkflowId);

                currentId = next;
            }

            // 旧 Schema（Version < 5.0.0）迁移诊断（T7：零异常加载）
            if (!string.IsNullOrWhiteSpace(def.Version) && IsVersionBelow(def.Version, "5.0.0"))
                logMigrated?.Invoke(def.Version);

            if (steps.Count == 0)
            {
                logInvalid?.Invoke("沿 start→on_success 主路径未提取到任何步骤（节点缺失 FuncName/NextNodeConfig 或成环截断）", def.WorkflowId);
                return null;
            }

            return new RoundSpec(def.WorkflowId ?? "agent-round", steps,
                    SafePorts(def.InputPorts), SafePorts(def.OutputPorts), def.GlobalKeyMapping)
            {
                Source = RoundSpecSource.Declared,
                SuccessCriterion = ParseSuccessCriterion(def.SuccessCriterion),
            };
        }

        /// <summary>解析声明式成功准则（字符串 → 枚举；未知/缺省 → DecisionWord，保持主循环语义）。</summary>
        public static RoundSpecSuccessCriterion ParseSuccessCriterion(string? value)
            => string.Equals(value, "run_to_end", StringComparison.OrdinalIgnoreCase)
                ? RoundSpecSuccessCriterion.RunToEnd
                : RoundSpecSuccessCriterion.DecisionWord;

        /// <summary>空/空集合端口归一为 null（零配置路径）。</summary>
        public static IReadOnlyList<PortSpec>? SafePorts(List<PortSpec>? ports)
            => ports is { Count: > 0 } ? ports : null;

        /// <summary>语义化版本比较（解析失败返回 false，不阻断加载）。</summary>
        public static bool IsVersionBelow(string current, string min)
        {
            try
            {
                return Version.Parse(current) < Version.Parse(min);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>声明式工作流定义顶层（agent-loop.json / 会话模板）；由消费方以工作流专用 JsonSerializerOptions 反序列化后交 <see cref="WorkflowSpecLoader.BuildRoundSpec"/>。</summary>
    public sealed class WorkflowDefinition
    {
        public string? WorkflowId { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? SuccessCriterion { get; set; }
        public List<PortSpec>? InputPorts { get; set; }
        public List<PortSpec>? OutputPorts { get; set; }
        public KeyMappingConfig? GlobalKeyMapping { get; set; }
        public List<WorkflowNode>? Nodes { get; set; }
    }

    /// <summary>单个工作流节点。</summary>
    public sealed class WorkflowNode
    {
        public string? Symbol { get; set; }
        public string? NodeId { get; set; }
        public string? FuncName { get; set; }
        public List<string>? ParameterKeys { get; set; }
        public List<string>? ResultKeys { get; set; }
        public KeyMappingConfig? InputMapping { get; set; }
        public KeyMappingConfig? OutputMapping { get; set; }
        public WorkflowNext? NextNodeConfig { get; set; }
    }

    /// <summary>节点的下一边配置（on_success / continue / stop / error）。</summary>
    public sealed class WorkflowNext
    {
        public Dictionary<string, string>? Configs { get; set; }
    }
}

