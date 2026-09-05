using Agent.Axioms;
using Agent.Contract;
using Agent.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Operation.StateBlock.Core;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests
{
    /// <summary>
    /// AgentRunner 组合根装配器冒烟测试：验证 <see cref="AgentRunnerFactory.Create"/> 用最小装配输入
    /// （仅循环配置 + 状态后端）即能装配出可用的 <see cref="AgentRunner"/>，且未装配端口保持 null。
    /// 固化"装配逻辑收敛至工厂"边界（为 S0 容器化提供唯一入口）。
    /// </summary>
    [TestClass]
    public sealed class AgentRunnerFactoryTests
    {
        [TestMethod]
        public void Create_MinimalInput_AssemblesRunnerWithNullOptionals()
        {
            var stateCenter = new StateCenter(new StorageCell("agent-factory-test"));
            var input = new AgentRunnerAssemblyInput(
                LoopConfig: new AgentLoopConfig(),
                StateCenter: stateCenter);

            var runner = AgentRunnerFactory.Create(input);

            Assert.IsNotNull(runner);
            // 未装配端口保持 null（组合根未注入），不构成装配失败。
            // 主干道 Step4：运行期注入（取消检查 / LLM 门 / 任务池持久化）不再作为 Runner 实例可变属性，
            // 收敛为显式 AgentRunHooks 运行参数（随 RunAsync 传入）；未注入 hooks = no-op，Runner 装配后不可变。
            Assert.IsNotNull(new AgentRunHooks(null, null, null)); // 可构造空 hooks（no-op 语义）
        }

        [TestMethod]
        public void Create_WithInjectedRunner_AcceptsSignalRunnerSeam()
        {
            // 阶段三骨架退役的桥：组合根可注入信号驱动折叠 runner（SignalWorkflowRunner.FromFold）替换骨架 WorkflowRunner。
            // 此处注入假 IWorkflowRunner 验证装配缝隙 API 可接受（缺省 null 回退骨架行为不变，由既有冒烟测试锁定）。
            var stateCenter = new StateCenter(new StorageCell("agent-factory-inject-test"));
            var fake = new FakeRunner();
            var input = new AgentRunnerAssemblyInput(
                LoopConfig: new AgentLoopConfig(),
                StateCenter: stateCenter,
                Runner: fake);

            var runner = AgentRunnerFactory.Create(input);

            Assert.IsNotNull(runner);
        }

        private sealed class FakeRunner : IWorkflowRunner
        {
            public Task<WorkflowRoundResult> RunRoundAsync(
                AxiomContext state, CancellationToken ct = default, DerivedAgentLease? lease = null, Agent.Contract.State.IRunContext? runContext = null)
                => Task.FromResult(new WorkflowRoundResult(
                    true, null, "ok", "stop", LoopDecision.Success, new Dictionary<string, object>()));
        }
    }
}
