using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


namespace CentralConsole
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            var serviceProvider = ConfigureServices();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            var programRunner = serviceProvider.GetRequiredService<ProgramRunner>();

            try
            {
                logger.LogInformation("=== Central Console 启动 ===");
                
                programRunner.LoadProgramsFromConfig(PathManager.ConfigFilePath);

                if (args.Length == 0)
                {
                    await StartInteractiveMode(programRunner, logger);
                }
                else
                {
                    await ProcessCommandLineArgs(args, programRunner, logger, serviceProvider);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "中央控制台运行出错");
                return -1;
            }
            finally
            {
                programRunner.StopAllPrograms();
                logger.LogInformation("=== Central Console 关闭 ===");
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
            
            services.AddSingleton<ProgramRunner>();
            
            return services.BuildServiceProvider();
        }

        private static async Task ProcessCommandLineArgs(string[] args, ProgramRunner programRunner, ILogger<Program> logger, IServiceProvider serviceProvider)
        {
            if (args.Length == 0) return;

            var command = args[0].ToLower();
            var programName = args.Length > 1 ? args[1] : null;

            switch (command)
            {
                case "test":
                    logger.LogWarning("测试运行功能已移除，请使用 'dotnet test' 命令运行测试");
                    break;
                case "list":
                    ListPrograms(programRunner, logger);
                    break;
                case "select":
                    await SelectAndRunProgram(programRunner, logger);
                    break;
                case "start":
                    if (!string.IsNullOrEmpty(programName))
                    {
                        await programRunner.RunProgramAsync(programName);
                    }
                    else
                    {
                        await programRunner.RunAllProgramsAsync();
                        logger.LogInformation("所有程序已启动完成");
                    }
                    break;
                case "stop":
                    if (!string.IsNullOrEmpty(programName))
                    {
                        programRunner.StopProgram(programName);
                    }
                    else
                    {
                        programRunner.StopAllPrograms();
                        logger.LogInformation("所有程序已停止");
                    }
                    break;
                case "build":
                    if (!string.IsNullOrEmpty(programName))
                    {
                        await programRunner.BuildProgramAsync(programName);
                    }
                    else
                    {
                        foreach (var (name, info) in programRunner.GetPrograms())
                        {
                            if (info.Enabled)
                            {
                                await programRunner.BuildProgramAsync(name);
                            }
                        }
                        logger.LogInformation("所有程序构建完成");
                    }
                    break;
                case "status":
                    ShowStatus(programRunner);
                    break;
                case "enable":
                    if (!string.IsNullOrEmpty(programName))
                    {
                        if (programRunner.EnableProgram(programName))
                        {
                            logger.LogInformation("程序已启用: {ProgramName}", programName);
                        }
                        else
                        {
                            logger.LogError("程序不存在: {ProgramName}", programName);
                        }
                    }
                    break;
                case "disable":
                    if (!string.IsNullOrEmpty(programName))
                    {
                        if (programRunner.DisableProgram(programName))
                        {
                            logger.LogInformation("程序已禁用: {ProgramName}", programName);
                        }
                        else
                        {
                            logger.LogError("程序不存在: {ProgramName}", programName);
                        }
                    }
                    break;
                default:
                    logger.LogWarning("未知命令: {Command}", command);
                    ShowHelp();
                    break;
            }
        }

        private static async Task StartInteractiveMode(ProgramRunner programRunner, ILogger<Program> logger)
        {
            Console.WriteLine("进入交互模式，输入 'help' 查看命令列表");
            
            while (true)
            {
                Console.Write("> ");
                var input = Console.ReadLine()?.Trim();
                
                if (string.IsNullOrEmpty(input)) continue;
                
                var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var command = parts[0].ToLower();
                var programName = parts.Length > 1 ? parts[1] : null;
                
                switch (command)
                {
                    case "help":
                        ShowHelp();
                        break;
                    case "list":
                        ListPrograms(programRunner, logger);
                        break;
                    case "select":
                        await SelectAndRunProgram(programRunner, logger);
                        break;
                    case "start":
                        if (!string.IsNullOrEmpty(programName))
                        {
                            await programRunner.RunProgramAsync(programName);
                        }
                        else
                        {
                            await programRunner.RunAllProgramsAsync();
                            Console.WriteLine("所有程序已启动完成");
                        }
                        break;
                    case "stop":
                        if (!string.IsNullOrEmpty(programName))
                        {
                            programRunner.StopProgram(programName);
                        }
                        else
                        {
                            programRunner.StopAllPrograms();
                            Console.WriteLine("所有程序已停止");
                        }
                        break;
                    case "build":
                        if (!string.IsNullOrEmpty(programName))
                        {
                            await programRunner.BuildProgramAsync(programName);
                        }
                        else
                        {
                            foreach (var (name, info) in programRunner.GetPrograms())
                            {
                                if (info.Enabled)
                                {
                                    await programRunner.BuildProgramAsync(name);
                                }
                            }
                            Console.WriteLine("所有程序构建完成");
                        }
                        break;
                    case "status":
                        ShowStatus(programRunner);
                        break;
                    case "enable":
                        if (!string.IsNullOrEmpty(programName))
                        {
                            if (programRunner.EnableProgram(programName))
                            {
                                Console.WriteLine($"程序已启用: {programName}");
                            }
                            else
                            {
                                Console.WriteLine($"程序不存在: {programName}");
                            }
                        }
                        break;
                    case "disable":
                        if (!string.IsNullOrEmpty(programName))
                        {
                            if (programRunner.DisableProgram(programName))
                            {
                                Console.WriteLine($"程序已禁用: {programName}");
                            }
                            else
                            {
                                Console.WriteLine($"程序不存在: {programName}");
                            }
                        }
                        break;
                    case "exit":
                    case "quit":
                        return;
                    default:
                        Console.WriteLine($"未知命令: {command}, 输入 'help' 查看帮助");
                        break;
                }
            }
        }

        private static void ListPrograms(ProgramRunner programRunner, ILogger<Program> logger)
        {
            var programs = programRunner.GetPrograms();
            
            if (programs.Count == 0)
            {
                logger.LogInformation("未发现任何程序");
                return;
            }

            logger.LogInformation("已发现的程序列表:");
            foreach (var (name, info) in programs)
            {
                logger.LogInformation("- {Name}: {Description} [{Status}] {Enabled}", 
                    name, info.Description, info.Status, 
                    info.Enabled ? "已启用" : "已禁用");
            }
        }

        private static void ShowStatus(ProgramRunner programRunner)
        {
            var programs = programRunner.GetPrograms();
            
            Console.WriteLine("\n=== 程序状态 ===");
            foreach (var (name, info) in programs)
            {
                var statusColor = info.Status switch
                {
                    ProgramStatus.Running => ConsoleColor.Green,
                    ProgramStatus.Error => ConsoleColor.Red,
                    ProgramStatus.Building => ConsoleColor.Yellow,
                    _ => ConsoleColor.Gray
                };
                
                Console.ForegroundColor = statusColor;
                Console.Write($"[{info.Status}]");
                Console.ResetColor();
                Console.WriteLine($" {name}");
                
                if (!string.IsNullOrEmpty(info.ErrorMessage))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  错误: {info.ErrorMessage}");
                    Console.ResetColor();
                }
            }
            Console.WriteLine();
        }

        private static void ShowHelp()
        {
            Console.WriteLine("\n=== 命令列表 ===");
            Console.WriteLine("help                - 显示此帮助信息");
            Console.WriteLine("list                - 列出所有已发现的程序");
            Console.WriteLine("select              - 交互式选择要运行的程序");
            Console.WriteLine("start [程序名]       - 启动指定程序或所有程序");
            Console.WriteLine("stop [程序名]        - 停止指定程序或所有程序");
            Console.WriteLine("build [程序名]       - 编译指定程序或所有程序");
            Console.WriteLine("status              - 查看程序状态");
            Console.WriteLine("enable [程序名]      - 启用指定程序");
            Console.WriteLine("disable [程序名]     - 禁用指定程序");
            Console.WriteLine("exit/quit           - 退出控制台");
            Console.WriteLine();
        }

        private static async Task SelectAndRunProgram(ProgramRunner programRunner, ILogger<Program> logger)
        {
            var programs = programRunner.GetPrograms();
            
            if (programs.Count == 0)
            {
                logger.LogInformation("未发现任何程序");
                return;
            }

            Console.WriteLine("\n=== 选择要运行的程序 ===");
            var programList = programs.Where(p => p.Value.Enabled).ToList();
            
            if (programList.Count == 0)
            {
                Console.WriteLine("没有可用的已启用程序");
                return;
            }

            for (int i = 0; i < programList.Count; i++)
            {
                var (name, info) = programList[i];
                var statusColor = info.Status switch
                {
                    ProgramStatus.Running => ConsoleColor.Green,
                    ProgramStatus.Error => ConsoleColor.Red,
                    ProgramStatus.Building => ConsoleColor.Yellow,
                    _ => ConsoleColor.Gray
                };
                
                Console.ForegroundColor = statusColor;
                Console.Write($"[{info.Status}]");
                Console.ResetColor();
                Console.WriteLine($" {i + 1}. {name}");
                Console.WriteLine($"   {info.Description}");
            }

            Console.WriteLine("\n请输入要运行的程序序号 (输入 'q' 返回主菜单):");
            Console.Write("> ");
            var input = Console.ReadLine()?.Trim();
            
            if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (int.TryParse(input, out int index) && index >= 1 && index <= programList.Count)
            {
                var selectedProgram = programList[index - 1];
                Console.WriteLine($"正在启动: {selectedProgram.Key}");
                await programRunner.RunProgramAsync(selectedProgram.Key);
            }
            else
            {
                Console.WriteLine("无效的输入，请输入有效的序号");
            }
        }
    }
}
