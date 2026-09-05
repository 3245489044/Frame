using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Logging;

namespace CentralConsole
{
    public class ProgramRunner
    {
        private readonly ILogger<ProgramRunner> _logger;
        private readonly ConcurrentDictionary<string, Process> _runningProcesses = new();
        private readonly ConcurrentDictionary<string, ProgramInfo> _programs = new();

        public ProgramRunner(ILogger<ProgramRunner> logger)
        {
            _logger = logger;
        }

        public IReadOnlyDictionary<string, ProgramInfo> GetPrograms() => _programs.ToDictionary(kv => kv.Key, kv => kv.Value);

        public void LoadProgramsFromConfig(string configPath)
        {
            string actualConfigPath = configPath;
            
            var possiblePaths = new List<string>
            {
                configPath,
                Path.Combine(AppContext.BaseDirectory, "programs.json"),
                Path.Combine(PathManager.WorkspaceRoot, "programs.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "programs.json"),
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "programs.json")
            };
            
            bool foundConfig = false;
            foreach (var path in possiblePaths)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    actualConfigPath = path;
                    _logger.LogInformation("找到配置文件: {Path}", actualConfigPath);
                    foundConfig = true;
                    break;
                }
            }
            
            if (!foundConfig)
            {
                _logger.LogWarning("未找到配置文件，尝试的位置:");
                foreach (var path in possiblePaths)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        _logger.LogWarning("  - {Path}", path);
                    }
                }
                AutoDiscoverPrograms();
                return;
            }

            try
            {
                var configContent = File.ReadAllText(actualConfigPath);
                var config = Newtonsoft.Json.JsonConvert.DeserializeObject<ProgramConfig>(configContent);
                
                if (config?.Programs != null)
                {
                    foreach (var program in config.Programs)
                    {
                        var fullPath = PathManager.GetFullPath(program.ProjectPath);
                        var workingDir = PathManager.GetWorkingDirectory(fullPath, program.WorkingDirectory);
                        
                        _programs.TryAdd(program.Name, new ProgramInfo
                        {
                            Name = program.Name,
                            Description = program.Description,
                            ProjectPath = fullPath,
                            WorkingDirectory = workingDir,
                            Enabled = program.Enabled,
                            Status = ProgramStatus.Discovered,
                            BuildConfiguration = program.BuildConfiguration ?? "Debug",
                            IsLibrary = program.IsLibrary ?? false,
                            LibraryEntryMethod = program.LibraryEntryMethod,
                            Arguments = program.Arguments
                        });
                        
                        _logger.LogInformation("已加载程序配置: {Name}", program.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载配置文件失败");
                AutoDiscoverPrograms();
            }
        }

        private void AutoDiscoverPrograms()
        {
            _logger.LogInformation("自动发现项目...");
            
            var searchPaths = new List<string>
            {
                PathManager.WorkspaceRoot,
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ""
            };
            
            var csprojFiles = new List<string>();
            
            foreach (var rootPath in searchPaths.Distinct().Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
            {
                try
                {
                    var files = Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories)
                        .Where(path => !path.Contains("bin") && !path.Contains("obj") && !path.Contains("CentralConsole"))
                        .ToList();
                    csprojFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "无法搜索目录: {Path}", rootPath);
                }
            }

            csprojFiles = csprojFiles.Distinct().ToList();

            if (csprojFiles.Count == 0)
            {
                _logger.LogWarning("未发现任何 .csproj 文件");
                AddKnownProjects();
                return;
            }

            foreach (var csprojPath in csprojFiles)
            {
                var projectDir = Path.GetDirectoryName(csprojPath);
                var projectName = Path.GetFileNameWithoutExtension(csprojPath);
                
                if (string.IsNullOrEmpty(projectDir)) continue;
                
                _programs.TryAdd(projectName, new ProgramInfo
                {
                    Name = projectName,
                    Description = $"自动发现的项目: {projectName}",
                    ProjectPath = csprojPath,
                    WorkingDirectory = projectDir,
                    Enabled = true,
                    Status = ProgramStatus.Discovered,
                    BuildConfiguration = "Debug",
                    IsLibrary = !HasProgramClass(projectDir)
                });
                
                _logger.LogInformation("自动发现程序: {Name}", projectName);
            }
        }
        
        private void AddKnownProjects()
        {
            var knownProjects = new[]
            {
                ("UniversalReference", "UniversalReference/UniversalWorkflowFramework.csproj"),
                ("WorkFlowMethod", "WorkFlowMethod/WorkFlowMethod.csproj"),
                ("CellFramework.Host.Tests", "CellFrame/CellFramework.Host/CellFramework.Host.csproj"),
                ("WdUI.Tests", "WdUI/Test/WdUI.Test.csproj")
            };
            
            foreach (var (name, relativePath) in knownProjects)
            {
                foreach (var rootPath in new[] { PathManager.WorkspaceRoot, Directory.GetCurrentDirectory() })
                {
                    if (string.IsNullOrEmpty(rootPath)) continue;
                    
                    var fullPath = Path.Combine(rootPath, relativePath);
                    if (File.Exists(fullPath))
                    {
                        var projectDir = Path.GetDirectoryName(fullPath);
                        _programs.TryAdd(name, new ProgramInfo
                        {
                            Name = name,
                            Description = $"手动添加的已知项目: {name}",
                            ProjectPath = fullPath,
                            WorkingDirectory = projectDir,
                            Enabled = true,
                            Status = ProgramStatus.Discovered,
                            BuildConfiguration = "Debug"
                        });
                        _logger.LogInformation("手动添加程序: {Name}", name);
                        break;
                    }
                }
            }
        }

        private bool HasProgramClass(string directory)
        {
            var programFile = Path.Combine(directory, "Program.cs");
            if (File.Exists(programFile))
            {
                var content = File.ReadAllText(programFile);
                return content.Contains("class Program") || content.Contains("public static void Main");
            }
            return false;
        }

        public async Task<bool> BuildProgramAsync(string programName)
        {
            if (!_programs.TryGetValue(programName, out var programInfo))
            {
                _logger.LogError("程序不存在: {Name}", programName);
                return false;
            }

            try
            {
                _logger.LogInformation("正在构建程序: {Name}", programName);
                programInfo.Status = ProgramStatus.Building;

                var buildResult = await Cli.Wrap("dotnet")
                    .WithArguments($"build \"{programInfo.ProjectPath}\" -c {programInfo.BuildConfiguration}")
                    .WithWorkingDirectory(programInfo.WorkingDirectory)
                    .ExecuteBufferedAsync();

                if (buildResult.ExitCode == 0)
                {
                    programInfo.Status = ProgramStatus.Built;
                    programInfo.OutputPath = programInfo.IsLibrary 
                        ? PathManager.GetLibraryOutputPath(programInfo) 
                        : PathManager.GetOutputPath(programInfo);
                    _logger.LogInformation("程序构建成功: {Name}", programName);
                    return true;
                }
                else
                {
                    programInfo.Status = ProgramStatus.Error;
                    programInfo.ErrorMessage = buildResult.StandardError;
                    _logger.LogError("程序构建失败: {Name}\n{Error}", programName, buildResult.StandardError);
                    return false;
                }
            }
            catch (Exception ex)
            {
                programInfo.Status = ProgramStatus.Error;
                programInfo.ErrorMessage = ex.Message;
                _logger.LogError(ex, "构建程序时发生异常: {Name}", programName);
                return false;
            }
        }

        public async Task<bool> RunProgramAsync(string programName, string[] args = null)
        {
            if (!_programs.TryGetValue(programName, out var programInfo))
            {
                _logger.LogError("程序不存在: {Name}", programName);
                return false;
            }

            if (programInfo.Status != ProgramStatus.Built)
            {
                if (!await BuildProgramAsync(programName))
                {
                    return false;
                }
            }

            try
            {
                _logger.LogInformation("正在启动程序: {Name}", programName);
                programInfo.Status = ProgramStatus.Running;

                if (programInfo.IsLibrary && !string.IsNullOrEmpty(programInfo.LibraryEntryMethod))
                {
                    return await RunLibraryMethodAsync(programName, programInfo);
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = programInfo.OutputPath,
                        Arguments = args != null ? string.Join(" ", args) : "",
                        WorkingDirectory = programInfo.WorkingDirectory,
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WindowStyle = ProcessWindowStyle.Normal
                    }
                };

                process.Start();
                _runningProcesses.TryAdd(programName, process);

                await Task.Run(() => process.WaitForExit());

                _runningProcesses.TryRemove(programName, out _);
                programInfo.Status = process.ExitCode == 0 ? ProgramStatus.Stopped : ProgramStatus.Error;

                if (process.ExitCode != 0)
                {
                    programInfo.ErrorMessage = $"进程退出码: {process.ExitCode}";
                }

                _logger.LogInformation("程序已结束: {Name} (退出码: {ExitCode})", programName, process.ExitCode);
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                programInfo.Status = ProgramStatus.Error;
                programInfo.ErrorMessage = ex.Message;
                _logger.LogError(ex, "运行程序时发生异常: {Name}", programName);
                return false;
            }
        }

        private async Task<bool> RunLibraryMethodAsync(string programName, ProgramInfo programInfo)
        {
            try
            {
                var dllPath = programInfo.OutputPath?.Replace(".exe", ".dll");
                if (!File.Exists(dllPath))
                {
                    programInfo.ErrorMessage = $"找不到类库文件: {dllPath}";
                    _logger.LogError(programInfo.ErrorMessage);
                    programInfo.Status = ProgramStatus.Error;
                    return false;
                }

                var assembly = Assembly.LoadFrom(dllPath);
                
                var parts = programInfo.LibraryEntryMethod?.Split('.') ?? Array.Empty<string>();
                if (parts.Length < 2)
                {
                    programInfo.ErrorMessage = "无效的入口方法格式，应为: Namespace.ClassName.MethodName";
                    _logger.LogError(programInfo.ErrorMessage);
                    programInfo.Status = ProgramStatus.Error;
                    return false;
                }

                var className = string.Join(".", parts.Take(parts.Length - 1));
                var methodName = parts.Last();

                var type = assembly.GetType(className);
                if (type == null)
                {
                    programInfo.ErrorMessage = $"找不到类: {className}";
                    _logger.LogError(programInfo.ErrorMessage);
                    programInfo.Status = ProgramStatus.Error;
                    return false;
                }

                var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    programInfo.ErrorMessage = $"找不到方法: {methodName}";
                    _logger.LogError(programInfo.ErrorMessage);
                    programInfo.Status = ProgramStatus.Error;
                    return false;
                }

                _logger.LogInformation("[{Name}] 调用类库方法: {Method}", programName, programInfo.LibraryEntryMethod);
                
                var parameters = method.GetParameters();
                var args = new object[parameters.Length];
                
                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    
                    if (paramType == typeof(ILogger))
                    {
                        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                        args[i] = loggerFactory.CreateLogger(programInfo.Name);
                    }
                    else if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(ILogger<>))
                    {
                        args[i] = _logger;
                    }
                    else if (paramType == typeof(string))
                    {
                        args[i] = programInfo.Arguments ?? string.Empty;
                    }
                    else if (paramType.IsValueType && parameters[i].HasDefaultValue)
                    {
                        args[i] = parameters[i].DefaultValue;
                    }
                    else if (paramType.IsValueType)
                    {
                        args[i] = Activator.CreateInstance(paramType);
                    }
                    else
                    {
                        args[i] = null;
                    }
                }
                
                var result = method.Invoke(null, args);
                
                if (result is Task taskResult)
                {
                    await taskResult;
                }
                
                programInfo.Status = ProgramStatus.Stopped;
                _logger.LogInformation("[{Name}] 类库方法调用完成", programName);
                return true;
            }
            catch (Exception ex)
            {
                programInfo.ErrorMessage = ex.InnerException?.Message ?? ex.Message;
                _logger.LogError(ex, "[{Name}] 调用类库方法时发生异常", programName);
                programInfo.Status = ProgramStatus.Error;
                return false;
            }
        }

        public async Task RunAllProgramsAsync()
        {
            var tasks = new List<Task>();
            
            foreach (var (name, info) in _programs)
            {
                if (info.Enabled)
                {
                    tasks.Add(RunProgramAsync(name));
                }
            }
            
            await Task.WhenAll(tasks);
        }

        public void StopProgram(string programName)
        {
            if (_runningProcesses.TryGetValue(programName, out var process))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    _runningProcesses.TryRemove(programName, out _);
                    
                    if (_programs.TryGetValue(programName, out var info))
                    {
                        info.Status = ProgramStatus.Stopped;
                    }
                    
                    _logger.LogInformation("程序已停止: {Name}", programName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "停止程序时发生异常: {Name}", programName);
                }
            }
        }

        public void StopAllPrograms()
        {
            foreach (var programName in _runningProcesses.Keys)
            {
                StopProgram(programName);
            }
        }

        public bool EnableProgram(string programName)
        {
            if (_programs.TryGetValue(programName, out var info))
            {
                info.Enabled = true;
                return true;
            }
            return false;
        }

        public bool DisableProgram(string programName)
        {
            if (_programs.TryGetValue(programName, out var info))
            {
                info.Enabled = false;
                return true;
            }
            return false;
        }
    }
}

public class ProgramConfig
{
    public List<ProgramItem> Programs { get; set; } = new();
}

public class ProgramItem
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public bool Enabled { get; set; } = true;
    public string? BuildConfiguration { get; set; }
    public bool? IsLibrary { get; set; }
    public string? LibraryEntryMethod { get; set; }
    public string? Arguments { get; set; }
}

public class ProgramInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public bool Enabled { get; set; } = true;
    public ProgramStatus Status { get; set; } = ProgramStatus.Discovered;
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
    public string BuildConfiguration { get; set; } = "Debug";
    public bool IsLibrary { get; set; } = false;
    public string? LibraryEntryMethod { get; set; } = null;
    public string? Arguments { get; set; } = null;
}

public enum ProgramStatus
{
    Discovered,
    Building,
    Built,
    Running,
    Stopped,
    Error
}