using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Modules;
using Agent.Contract;
using Agent.Axioms;
using WfContext = WorkFlowMethod.Orchestration.Context.Models.WorkflowContext;

namespace Agent.Modules.Tests
{
    /// <summary>
    /// ClineClient 集成冒烟测试：依赖真实 node + ClineApi/build/cli.js。
    ///  - 零成本管理命令（Version/Config/History）：node 与 cli.js 就绪时自动运行，不调用大模型。
    ///  - 真实 AI 调用（Ask/Plan/Resume）已移除——改为对 IAgentExecutor 注入 fake 的离线测试，不消耗真实 API。
    /// 缺失前置环境（node / cli.js）时 Assert.Inconclusive 跳过，不污染单元测试结果。
    /// </summary>
    [TestClass]
    [TestCategory("Modules_Integration")]
    public class ClineClientIntegrationTests
    {
        private ClineClient? _client;

        [TestInitialize]
        public void TestInitialize()
        {
            if (!NodeAvailable())
            {
                Assert.Inconclusive("当前环境缺少 node 可执行文件，跳过集成冒烟测试");
            }

            try
            {
                // 默认构造会从输出目录向上遍历定位 ClineApi/build/cli.js
                _client = new ClineClient();
            }
            catch (FileNotFoundException ex)
            {
                Assert.Inconclusive($"无法定位 ClineApi/build/cli.js，跳过集成冒烟测试：{ex.Message}");
            }
        }

        // ---- 零成本管理命令（不调用大模型） ----

        [TestMethod]
        public async Task VersionAsync_应返回版本信息()
        {
            var result = await _client!.VersionAsync();

            Assert.IsNotNull(result, "VersionAsync 不应返回 null");
            Assert.IsTrue(result.Ok || !string.IsNullOrWhiteSpace(result.Raw),
                $"版本命令应返回可识别结果（Ok={result.Ok}, Raw={result.Raw}）");
        }

        [TestMethod]
        public async Task ConfigAsync_应返回配置()
        {
            var result = await _client!.ConfigAsync();

            Assert.IsNotNull(result, "ConfigAsync 不应返回 null");
            Assert.IsTrue(result.Ok || !string.IsNullOrWhiteSpace(result.Raw),
                $"配置命令应返回可识别结果（Ok={result.Ok}, Raw={result.Raw}）");
        }

        [TestMethod]
        public async Task HistoryAsync_应返回历史会话()
        {
            var result = await _client!.HistoryAsync(limit: 5);

            Assert.IsNotNull(result, "HistoryAsync 不应返回 null");
            Assert.IsTrue(result.Ok || !string.IsNullOrWhiteSpace(result.Raw),
                $"历史命令应返回可识别结果（Ok={result.Ok}, Raw={result.Raw}）");
        }

        private static bool NodeAvailable()
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo("node", "--version")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                });
                proc?.WaitForExit(5000);
                return proc is not null && proc.HasExited && proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}


