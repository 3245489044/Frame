using System.Threading;
using System.Threading.Tasks;
using Agent.Contract.Scheduling;
using Agent.Core.TaskPool;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Share.StateManagement;
using Share.TaskStatusPool.Loops;
using Share.TaskStatusPool.TaskPool;
using TestInfrastructure;

namespace Agent.Core.Tests;

/// <summary>
/// 零 LLM 执行器 <see cref="RuleTaskExecutor"/> 定向测试（任务池脱离 LLM 的核心解耦验证）：
/// ① 按 SchedulingMode 匹配；② continue 规则 → Success；③ retry 规则 → 预算终止（非 Continue 死循环）；
/// ④ 无配置/无命中 → fail-safe Timeout；⑤ 适配器路由：Config 任务经 RuleTaskExecutor 真实执行、Hybrid 任务无执行器置 Faulted。
/// </summary>
[TestClass]
[TestCategory(TestCategories.Concurrency)]
public class RuleTaskExecutorTests
{
    private const string RulesKey = "scheduler.rules.tp_test";

    private static AgentTaskItem ConfigTask(string rulesKey)
        => new() { UserInput = "rule-task", SchedulingMode = SchedulingMode.Config, SchedulerRulesKey = rulesKey };

    private static async Task<InMemoryConfigStore> StoreAsync(string? rulesJson, string key = RulesKey)
    {
        var store = new InMemoryConfigStore();
        if (rulesJson is not null) await store.SetAsync(key, rulesJson);
        return store;
    }

    [TestMethod]
    public void RuleTaskExecutor_MatchesOnlyConfigMode()
    {
        var executor = new RuleTaskExecutor(new InMemoryConfigStore());
        Assert.AreEqual("rule", executor.Name);
        Assert.IsTrue(executor.CanExecute(ConfigTask(RulesKey)));
        Assert.IsFalse(executor.CanExecute(new AgentTaskItem { SchedulingMode = SchedulingMode.Hybrid }));
    }

    [TestMethod]
    public async Task RuleTaskExecutor_ContinueRule_Success()
    {
        var store = await StoreAsync("{\"rules\":[{\"action\":\"continue\"}]}");
        var executor = new RuleTaskExecutor(store);

        var result = await executor.ExecuteAsync(ConfigTask(RulesKey), "t1", CancellationToken.None);

        Assert.AreEqual(LoopDecision.Success, result.FinalDecision, "continue 规则应确定性执行成功");
        Assert.IsTrue(result.Rounds.Count >= 1);
        Assert.IsTrue(result.TotalDuration >= System.TimeSpan.Zero);
    }

    [TestMethod]
    public async Task RuleTaskExecutor_RetryRule_Terminates()
    {
        var store = await StoreAsync("{\"rules\":[{\"action\":\"retry\"}]}");
        var executor = new RuleTaskExecutor(store);

        var result = await executor.ExecuteAsync(ConfigTask(RulesKey), "t2", CancellationToken.None);

        // retry 规则 → 每轮 Ok=false 推进预算 → 熔断/超限终止，绝不无限 continue。
        Assert.AreNotEqual(LoopDecision.Continue, result.FinalDecision);
        Assert.IsTrue(result.Rounds.Count >= 1);
    }

    [TestMethod]
    public async Task RuleTaskExecutor_MissingRule_FailSafeTerminates()
    {
        var store = await StoreAsync(null); // 无规则
        var executor = new RuleTaskExecutor(store);

        var result = await executor.ExecuteAsync(ConfigTask(RulesKey), "t3", CancellationToken.None);

        // 无命中规则 → 每轮失败推进预算 → 熔断/超限终止（fail-safe：绝不无限 continue、不抛异常）。
        Assert.AreNotEqual(LoopDecision.Continue, result.FinalDecision);
        Assert.IsTrue(result.Rounds.Count >= 1);
    }

    [TestMethod]
    public async Task RuleTaskExecutor_NullStore_FailSafe()
    {
        var executor = new RuleTaskExecutor(null);
        var result = await executor.ExecuteAsync(ConfigTask(RulesKey), "t4", CancellationToken.None);
        Assert.AreNotEqual(LoopDecision.Continue, result.FinalDecision, "null ur_config 应 fail-safe 终止");
    }

    [TestMethod]
    public async Task RuleTaskExecutor_SingleRootHarvest_UpdatesItem()
    {
        var store = await StoreAsync("{\"rules\":[{\"action\":\"retry\"}]}");
        var executor = new RuleTaskExecutor(store);
        var item = ConfigTask(RulesKey);

        await executor.ExecuteAsync(item, "t5", CancellationToken.None);

        // 单根收割（AX.StateRootSingleSource）：确定性核心（Budget/Token/Flag）由轮内 RoundLifecycle 经共享引用直写根；
        // Round/LastEval/Scope 由 RoundLoopDriver 收割回根 —— 执行后 item 即为完整快照，无需再解包 AxiomContext 读取。
        Assert.IsTrue(item.Round >= 1, "执行后 item.Round 应被收割回根");
        Assert.AreNotEqual(EvalOutcome.None, item.LastEval, "执行后 item.LastEval 应被更新");
        Assert.IsTrue(item.Token.Value > 0 || item.Budget.Level > 0, "retry 规则应推进预算/token（共享引用直写根）");
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AgentTaskPoolAdapter_RoutesConfigToRuleExecutor()
    {
        var store = await StoreAsync("{\"rules\":[{\"action\":\"continue\"}]}");
        var adapter = new AgentTaskPoolAdapter(
            new InMemoryTaskPoolPersistence<AgentTaskItem>(),
            new ITaskExecutor[] { new RuleTaskExecutor(store) });
        adapter.Start();
        try
        {
            var item = ConfigTask(RulesKey);
            item.TaskKey = "cfg";
            await adapter.EnqueueAsync(item, CancellationToken.None);

            // 轮询等待消费循环完成（Config 任务应经 RuleTaskExecutor 真实执行到 Completed）。
            var status = await WaitForTerminalAsync(adapter, item.TaskKey, CancellationToken.None);
            Assert.AreEqual(TaskExecutionStatus.Completed, status, "Config 任务应经零 LLM 执行器真实执行完成");
        }
        finally { await adapter.DisposeAsync(); }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AgentTaskPoolAdapter_HybridWithoutExecutor_Faulted()
    {
        // 仅注册 RuleTaskExecutor → Hybrid 任务无可执行器 → 置 Faulted（fail-safe，不整池崩溃）。
        var store = await StoreAsync("{\"rules\":[{\"action\":\"continue\"}]}");
        var adapter = new AgentTaskPoolAdapter(
            new InMemoryTaskPoolPersistence<AgentTaskItem>(),
            new ITaskExecutor[] { new RuleTaskExecutor(store) });
        adapter.Start();
        try
        {
            var taskKey = "hyb";
            await adapter.EnqueueAsync(new AgentTaskItem { UserInput = "x", SchedulingMode = SchedulingMode.Hybrid, TaskKey = taskKey }, CancellationToken.None);

            var status = await WaitForTerminalAsync(adapter, taskKey, CancellationToken.None);
            Assert.AreEqual(TaskExecutionStatus.Faulted, status, "无可用执行器的任务应置 Faulted");
        }
        finally { await adapter.DisposeAsync(); }
    }

    private static async Task<TaskExecutionStatus> WaitForTerminalAsync(AgentTaskPoolAdapter adapter, string taskKey, CancellationToken ct)
    {
        TaskExecutionStatus? terminal = null;
        await AsyncTestHelper.WaitUntilOrThrowAsync(async () =>
        {
            var status = await adapter.GetStatusAsync(taskKey, ct);
            if (status is TaskExecutionStatus.NotStarted or TaskExecutionStatus.Running)
                return false;
            terminal = status;
            return true;
        }, timeoutMs: 5000, pollIntervalMs: 25,
            description: $"任务 {taskKey} 未在超时内到达终态");
        return terminal ?? TaskExecutionStatus.Faulted;
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task AgentTaskPoolAdapter_EnqueueAndWait_ReturnsResult_ZeroLlm()
    {
        var store = await StoreAsync("{\"rules\":[{\"action\":\"continue\"}]}");
        var adapter = new AgentTaskPoolAdapter(
            new InMemoryTaskPoolPersistence<AgentTaskItem>(),
            new ITaskExecutor[] { new RuleTaskExecutor(store) });
        try
        {
            var item = ConfigTask(RulesKey);
            item.TaskKey = "wait-cfg";
            // EnqueueAndWaitAsync 内部自动启动消费循环（不手动 Start），并取回 AxiomLoopResult。
            var result = await adapter.EnqueueAndWaitAsync(item, CancellationToken.None);
            Assert.IsNotNull(result);
            Assert.AreEqual(LoopDecision.Success, result.FinalDecision, "零 LLM 规则任务应经任务池执行并取回结果");
        }
        finally { await adapter.DisposeAsync(); }
    }
}
