using System;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentTest
{
    internal class Program
    {
        private static AgentOrchestrator? _orchestrator;

        static async Task<int> Main(string[] args)
        {
            var serviceProvider = ConfigureServices();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            try
            {
                logger.LogInformation("=== Agent 测试控制台启动 ===");

                if (args.Length > 0)
                {
                    var command = args[0].ToLower();
                    if (command == "run")
                    {
                        var query = string.Join(" ", args, 1, args.Length - 1);
                        await RunAgentWithQuery(query, logger);
                        return 0;
                    }
                    await ProcessCommandLineArgs(args, logger);
                }
                else
                {
                    await StartInteractiveMode(logger);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Agent 测试控制台运行出错");
                return -1;
            }
            finally
            {
                _orchestrator?.Reset();
                logger.LogInformation("=== Agent 测试控制台关闭 ===");
            }

            return 0;
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            return services.BuildServiceProvider();
        }

        private static async Task ProcessCommandLineArgs(string[] args, ILogger<Program> logger)
        {
            if (args.Length == 0) return;

            var command = args[0].ToLower();

            switch (command)
            {
                case "test":
                    logger.LogInformation("运行 Agent 测试...");
                    break;
                case "help":
                    ShowHelp();
                    break;
                default:
                    logger.LogWarning("未知命令: {Command}", command);
                    ShowHelp();
                    break;
            }
        }

        private static async Task StartInteractiveMode(ILogger<Program> logger)
        {
            Console.WriteLine("Agent 测试控制台 - 交互模式");
            Console.WriteLine("输入 'help' 查看命令列表");

            while (true)
            {
                Console.Write("> ");
                var input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input)) continue;

                var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var command = parts[0].ToLower();

                switch (command)
                {
                    case "help":
                        ShowHelp();
                        break;
                    case "exit":
                    case "quit":
                        return;
                    case "run":
                        var query = string.Join(" ", parts, 1, parts.Length - 1);
                        if (string.IsNullOrWhiteSpace(query))
                        {
                            Console.WriteLine("请输入查询内容，例如: run 今天天气怎么样");
                        }
                        else
                        {
                            await RunAgentWithQuery(query, logger);
                        }
                        break;
                    case "init":
                        await InitializeAgent(logger);
                        break;
                    case "reset":
                        _orchestrator?.Reset();
                        _orchestrator = null;
                        Console.WriteLine("Agent 已重置");
                        break;
                    case "status":
                        ShowAgentStatus();
                        break;
                    default:
                        Console.WriteLine($"未知命令: {command}, 输入 'help' 查看帮助");
                        break;
                }
            }
        }

        private static async Task InitializeAgent(ILogger<Program> logger)
        {
            if (_orchestrator != null)
            {
                Console.WriteLine("Agent 已初始化，正在重新初始化...");
                _orchestrator.Reset();
            }

            try
            {
                Console.WriteLine("正在初始化 Agent（包含 LLM 规划器、MCP 工具、安全护栏）...");
                _orchestrator = await AgentModuleRegistrar.SetupAgentAsync();
                Console.WriteLine("Agent 初始化成功！");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("DEEPSEEK_API_KEY"))
            {
                Console.WriteLine($"⚠️  初始化失败：缺少 DeepSeek API Key");
                Console.WriteLine($"   请设置环境变量: set DEEPSEEK_API_KEY=your_api_key");
                logger.LogWarning(ex, "初始化失败：缺少 API Key");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 初始化失败：{ex.Message}");
                logger.LogError(ex, "Agent 初始化失败");
            }
        }

        private static async Task RunAgentWithQuery(string query, ILogger<Program> logger)
        {
            try
            {
                if (_orchestrator == null)
                {
                    Console.WriteLine("Agent 未初始化，正在自动初始化...");
                    await InitializeAgent(logger);
                    if (_orchestrator == null)
                    {
                        Console.WriteLine("无法运行：Agent 初始化失败");
                        return;
                    }
                }

                Console.WriteLine($"\n=== 执行查询 ===");
                Console.WriteLine($"输入: {query}");
                Console.WriteLine("----------------");

                using var cts = new CancellationTokenSource();
                var task = _orchestrator.RunAsync(query, cts.Token);

                Console.WriteLine("正在执行（按 Ctrl+C 取消）...\n");

                try
                {
                    await task;
                    Console.WriteLine("\n=== 执行完成 ===");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("\n=== 执行已取消 ===");
                }
                catch (TimeoutException ex)
                {
                    Console.WriteLine($"\n=== 执行超时 ===");
                    Console.WriteLine($"错误: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n=== 执行出错 ===");
                    Console.WriteLine($"错误: {ex.Message}");
                    logger.LogError(ex, "Agent 执行出错");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 运行失败：{ex.Message}");
                logger.LogError(ex, "RunAgentWithQuery 出错");
            }
        }

        private static void ShowAgentStatus()
        {
            if (_orchestrator == null)
            {
                Console.WriteLine("Agent 状态：未初始化");
            }
            else
            {
                Console.WriteLine($"Agent 状态：已初始化");
                Console.WriteLine($"默认超时：{_orchestrator.DefaultTimeout.TotalSeconds} 秒");
            }
        }

        private static void ShowHelp()
        {
            Console.WriteLine("\n=== 命令列表 ===");
            Console.WriteLine("help                - 显示此帮助信息");
            Console.WriteLine("exit/quit           - 退出控制台");
            Console.WriteLine("init                - 初始化 Agent（加载 LLM 规划器、MCP 工具、安全护栏）");
            Console.WriteLine("run <query>         - 运行 Agent 并执行查询（自动初始化）");
            Console.WriteLine("reset               - 重置 Agent 状态");
            Console.WriteLine("status              - 显示 Agent 当前状态");
            Console.WriteLine();
            Console.WriteLine("环境变量:");
            Console.WriteLine("  DEEPSEEK_API_KEY  - DeepSeek API Key（必需）");
            Console.WriteLine();
            Console.WriteLine("使用示例:");
            Console.WriteLine("  > run 今天北京天气怎么样");
            Console.WriteLine("  > run 帮我分析这个代码的功能");
            Console.WriteLine("  > run 查询最新的新闻");
            Console.WriteLine();
            Console.WriteLine("单元测试运行方式:");
            Console.WriteLine("  dotnet test CentralTest\\AgentTest\\Agent.Core.Tests\\Agent.Core.Tests.csproj");
            Console.WriteLine("  dotnet test --filter TestCategory=LLM_Integration");
            Console.WriteLine();
        }
    }
}
