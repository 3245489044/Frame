
using UniversalWorkflowFramework.BranchManagement.Models;

namespace BasicMethod.Tests.UniversalReference;

public static class UniversalReferenceTestDataFactory
{
    public static BranchGraph CreateUserRegistrationBranch() => new()
    {
        Name = "用户注册流程",
        Description = "用户注册的完整工作流分支",
        Nodes = new List<BranchNode>
        {
            new() { NodeId = "start", Type = NodeType.Start, Name = "开始" },
            new() { NodeId = "validate", Type = NodeType.Action, Name = "验证输入" },
            new() { NodeId = "decision", Type = NodeType.Decision, Name = "验证结果" },
            new() { NodeId = "save", Type = NodeType.Action, Name = "保存用户" },
            new() { NodeId = "notify", Type = NodeType.Action, Name = "发送通知" },
            new() { NodeId = "end", Type = NodeType.End, Name = "结束" }
        },
        Edges = new List<BranchEdge>
        {
            new() { EdgeId = "e1", SourceNodeId = "start", TargetNodeId = "validate", Type = EdgeType.Normal },
            new() { EdgeId = "e2", SourceNodeId = "validate", TargetNodeId = "decision", Type = EdgeType.Normal },
            new() { EdgeId = "e3", SourceNodeId = "decision", TargetNodeId = "save", Type = EdgeType.Conditional, Data = { { "condition", "valid" } } },
            new() { EdgeId = "e4", SourceNodeId = "save", TargetNodeId = "notify", Type = EdgeType.Normal },
            new() { EdgeId = "e5", SourceNodeId = "notify", TargetNodeId = "end", Type = EdgeType.Normal }
        }
    };

    public static BranchGraph CreateOrderProcessingBranch() => new()
    {
        Name = "订单处理流程",
        Description = "订单处理的完整工作流分支",
        Nodes = new List<BranchNode>
        {
            new() { NodeId = "order_start", Type = NodeType.Start, Name = "订单开始" },
            new() { NodeId = "check_inventory", Type = NodeType.Action, Name = "检查库存" },
            new() { NodeId = "inventory_decision", Type = NodeType.Decision, Name = "库存判断" },
            new() { NodeId = "process_payment", Type = NodeType.Action, Name = "处理支付" },
            new() { NodeId = "ship_order", Type = NodeType.Action, Name = "发货" },
            new() { NodeId = "order_end", Type = NodeType.End, Name = "订单结束" }
        },
        Edges = new List<BranchEdge>
        {
            new() { EdgeId = "oe1", SourceNodeId = "order_start", TargetNodeId = "check_inventory", Type = EdgeType.Normal },
            new() { EdgeId = "oe2", SourceNodeId = "check_inventory", TargetNodeId = "inventory_decision", Type = EdgeType.Normal },
            new() { EdgeId = "oe3", SourceNodeId = "inventory_decision", TargetNodeId = "process_payment", Type = EdgeType.Conditional, Data = { { "condition", "in_stock" } } },
            new() { EdgeId = "oe4", SourceNodeId = "process_payment", TargetNodeId = "ship_order", Type = EdgeType.Normal },
            new() { EdgeId = "oe5", SourceNodeId = "ship_order", TargetNodeId = "order_end", Type = EdgeType.Normal }
        }
    };

    public static Dictionary<string, object> CreateMetadata(string name, string? version = null) =>
        new()
        {
            { "name", name },
            { "version", version ?? "1.0.0" },
            { "createdAt", DateTime.UtcNow }
        };
}
