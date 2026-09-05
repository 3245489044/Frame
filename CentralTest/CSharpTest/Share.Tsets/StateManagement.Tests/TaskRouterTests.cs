using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.TaskStatusPool.Routing;

namespace Share.StateManagement.Tests;

/// <summary>
/// 任务路由核心单测（canonical 于 Share.TaskStatusPool.Routing.TaskRouter，SingleImplementation / TH-12）。
/// 覆盖：后台规则优先、自主标记回退、无规则无自主的纯确定性降级。
/// </summary>
[TestClass]
public class TaskRouterTests
{
    private sealed record TaskItem(string Name);

    private sealed class RuleBackend : IRuleBackend<TaskItem>
    {
        private readonly string? _action;
        public RuleBackend(string? action = null) => _action = action;
        public RuleResolution? TryResolve(TaskItem item)
            => _action is null ? null : new RuleResolution(_action, target: item.Name);
    }

    private sealed class Autonomy(bool requiresLlm) : ITaskAutonomy
    {
        public bool RequiresLlm { get; } = requiresLlm;
    }

    [TestMethod]
    public void Route_RuleHit_PrecedesAutonomy()
    {
        var d = TaskRouter.Route(new TaskItem("t"), new Autonomy(true), new RuleBackend("rule"));
        Assert.AreEqual(ExecutionDisposition.RuleDriven, d.Disposition);
        Assert.IsNotNull(d.Rule);
        Assert.AreEqual("rule", d.Rule!.Action);
    }

    [TestMethod]
    public void Route_NoRule_AutonomyRequiresLlm_LlmDriven()
    {
        var d = TaskRouter.Route(new TaskItem("t"), new Autonomy(true), new RuleBackend());
        Assert.AreEqual(ExecutionDisposition.LlmDriven, d.Disposition);
        Assert.IsNull(d.Rule);
    }

    [TestMethod]
    public void Route_NoRule_NonAutonomous_RuleDrivenPureDeterministic()
    {
        var d = TaskRouter.Route(new TaskItem("t"), new Autonomy(false), new RuleBackend());
        Assert.AreEqual(ExecutionDisposition.RuleDriven, d.Disposition);
        Assert.IsNull(d.Rule);
    }

    [TestMethod]
    public void Route_NullAutonomy_DefaultsNonAutonomous()
    {
        var d = TaskRouter.Route(new TaskItem("t"), autonomy: null, ruleBackend: new RuleBackend());
        Assert.AreEqual(ExecutionDisposition.RuleDriven, d.Disposition);
    }

    [TestMethod]
    public void Route_NullRuleBackend_FallsBackToAutonomy()
    {
        Assert.AreEqual(ExecutionDisposition.LlmDriven, TaskRouter.Route(new TaskItem("t"), new Autonomy(true)).Disposition);
        Assert.AreEqual(ExecutionDisposition.RuleDriven, TaskRouter.Route(new TaskItem("t"), new Autonomy(false)).Disposition);
    }
}
