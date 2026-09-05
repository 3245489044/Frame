# FrameManage CLI_UI 运行时流程文档

## 一、运行时流程概述

本文档详细描述 FrameManage CLI 模式下 UI 操作的完整运行时流程，包括命令解析、UI 创建、事件处理等核心环节，并提供故障排除指南。

---

## 二、完整运行时流程

### 2.1 流程架构图

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        FrameManage CLI 运行时流程                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌──────────────┐    ┌────────────────┐    ┌──────────────────────┐         │
│  │ 命令输入层    │───→│ 命令解析层      │───→│ 命令执行层           │         │
│  │ (CLI Input)  │    │ (CommandParser)│    │ (CommandExecutor)    │         │
│  └──────────────┘    └────────────────┘    └──────────┬───────────┘         │
│                                                        │                    │
│                                                        ▼                    │
│  ┌────────────────────────────────────────────────────────────────────┐     │
│  │                      UI 配置加载与控件创建                          │     │
│  │  ConfigLoader → JSON解析 → FormBuilder → ControlFactory → 事件绑定  │     │
│  └────────────────────────────────────────────────────────────────────┘     │
│                                                        │                    │
│                                                        ▼                    │
│  ┌────────────────────────────────────────────────────────────────────┐     │
│  │                      Action 处理器调用流程                          │     │
│  │  事件触发 → ActionName查找 → Handler.Execute() → 业务逻辑执行       │     │
│  └────────────────────────────────────────────────────────────────────┘     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 详细流程步骤

#### 步骤 1：程序启动与初始化

```csharp
// 入口点：Program.cs
public static int Main(string[] args)
{
    // 1.1 初始化核心服务
    var services = ConfigureServices();
    
    // 1.2 注册 Action 处理器
    ActionHandlerFactory.RegisterHandlers(services);
    
    // 1.3 解析命令行参数
    if (args.Any(a => a.StartsWith("--")))
    {
        return RunCommandLineMode(args);
    }
    
    // 1.4 否则进入 GUI 模式
    return RunGuiMode();
}
```

**关键点**：
- 系统自动扫描 `FrameManage.ActionHandlers` 程序集
- 注册所有实现 `IActionHandler` 接口的类
- 根据命令行参数判断运行模式

#### 步骤 2：命令行参数解析

```csharp
// 命令解析逻辑
public Command Parse(string[] args)
{
    // 支持的命令格式：
    // - FrameManage.exe load <configPath> [formName] [show]
    // - FrameManage.exe --file <commands.txt>
    // - FrameManage.exe --cli (交互模式)
    
    if (args.Contains("--file"))
    {
        return new BatchFileCommand(args);
    }
    
    if (args.Contains("--cli"))
    {
        return new InteractiveCommand();
    }
    
    if (args[0].Equals("load", StringComparison.OrdinalIgnoreCase))
    {
        return new LoadConfigCommand(args);
    }
    
    return new UnknownCommand(args);
}
```

#### 步骤 3：配置文件加载与解析

```csharp
// ConfigLoader.cs
public FormConfig LoadConfig(string configPath)
{
    // 3.1 验证路径
    if (!File.Exists(configPath))
    {
        throw new FileNotFoundException("配置文件不存在", configPath);
    }
    
    // 3.2 读取并解析 JSON
    var jsonContent = File.ReadAllText(configPath);
    var config = JsonSerializer.Deserialize<FormConfig>(jsonContent);
    
    // 3.3 验证配置完整性
    ValidateConfig(config);
    
    return config;
}
```

**配置验证规则**：
| 校验项 | 规则 | 错误处理 |
|--------|------|----------|
| Form.Name | 必填，非空 | 抛出 ValidationException |
| Controls[] | 数组非空 | 警告，创建空表单 |
| Control.Type | 必填 | 跳过该控件 |
| Control.Name | 必填 | 自动生成唯一名称 |
| Button.Events | 可选 | 无事件绑定 |

#### 步骤 4：动态表单创建

```csharp
// FormBuilder.cs
public Form CreateForm(FormConfig config)
{
    var form = new Form
    {
        Name = config.Form.Name,
        Text = config.Form.Title,
        Size = new Size(config.Form.Width, config.Form.Height)
    };
    
    // 递归创建所有控件
    foreach (var controlConfig in config.Controls)
    {
        var control = ControlFactory.Create(controlConfig);
        form.Controls.Add(control);
    }
    
    return form;
}
```

**控件创建流程**：
```
ControlFactory.Create(config)
    │
    ├─→ 根据 Type 确定控件类型
    │
    ├─→ 创建对应 WinForms 控件实例
    │
    ├─→ 设置位置、大小、文本等属性
    │
    ├─→ 绑定事件处理器（如 Events 配置存在）
    │
    └─→ 注册到控件缓存
```

#### 步骤 5：事件绑定与 Action 调用

```csharp
// ControlFactory.cs
private void BindEvents(Control control, ControlConfig config)
{
    if (config.Events == null || !config.Events.Any())
    {
        return;
    }
    
    foreach (var eventConfig in config.Events)
    {
        switch (eventConfig.EventName.ToLower())
        {
            case "click":
                if (control is Button btn)
                {
                    btn.Click += (sender, e) => ExecuteAction(eventConfig.Action);
                }
                break;
        }
    }
}

private void ExecuteAction(string actionName)
{
    // 通过 ActionName 查找处理器
    var handler = ActionHandlerFactory.GetHandler(actionName);
    
    if (handler == null)
    {
        throw new InvalidOperationException($"未找到 Action 处理器: {actionName}");
    }
    
    // 执行处理器
    handler.Execute(new HandlerParams());
}
```

---

## 三、日志系统架构

### 3.1 日志存储机制

FrameManage 采用**多层日志存储架构**，日志同时输出到多个目标：

```
┌─────────────────────────────────────────────────────────────────────┐
│                        日志输出架构                                 │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│                    日志事件                                          │
│                         │                                          │
│          ┌──────────────┼──────────────┐                           │
│          ▼              ▼              ▼                           │
│    ┌──────────┐   ┌──────────┐   ┌──────────────┐                  │
│    │ 控制台    │   │ 文件存储  │   │ 数据库存储    │                  │
│    │ Console  │   │ File     │   │ SQLite       │                  │
│    └────┬─────┘   └────┬─────┘   └──────┬───────┘                  │
│         │              │                 │                         │
│         ▼              ▼                 ▼                         │
│    实时显示      logs/application.log   FrameManageLogs.db          │
│    (Information)  (Warning+)           (All Levels)                │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 日志存储目标说明

| 存储目标 | 路径 | 日志级别 | 特点 |
|---------|------|---------|------|
| **控制台** | 终端输出 | Information | 实时显示，程序退出后丢失 |
| **文件日志** | `logs/application.log` | Warning | 持久化，按级别过滤 |
| **数据库日志** | `DataManage/DataBase/FrameManageLogs.db` | All | 完整记录，支持查询分析 |

### 3.3 默认日志器配置

```csharp
// LoggingModule.cs - 默认日志器配置
public static ILogger DefaultLogger
{
    get
    {
        if (_defaultLogger == null)
        {
            _defaultLogger = new CompositeLogger(
                new ConsoleLogger(LogLevel.Information),      // 控制台输出
                new FileLogger("logs/application.log", LogLevel.Warning)  // 文件输出
            );
        }
        return _defaultLogger;
    }
}
```

### 3.4 日志级别定义

| 级别 | 名称 | 说明 | 使用场景 |
|------|------|------|---------|
| 0 | Trace | 最详细的调试信息 | 开发调试，详细流程追踪 |
| 1 | Debug | 调试信息 | 开发调试，关键步骤记录 |
| 2 | Information | 一般信息 | 程序运行状态，正常操作记录 |
| 3 | Warning | 警告信息 | 潜在问题，需要关注 |
| 4 | Error | 错误信息 | 操作失败，需要处理 |
| 5 | Critical | 严重错误 | 程序崩溃，需要立即处理 |

### 3.5 聚合日志管理器

`AggregatedLogManager` 提供日志聚合和定时刷新功能：

```csharp
public AggregatedLogManager(ILogStorage logStorage, ILogger? innerLogger = null)
{
    _logStorage = logStorage;
    _innerLogger = innerLogger;
    
    // 定时刷新：每30秒将内存中的日志刷新到数据库
    _flushTimer = new Timer(FlushTimerCallback, null, 
                           TimeSpan.FromSeconds(30),  // 首次延迟
                           TimeSpan.FromSeconds(30)); // 刷新间隔
}
```

**工作流程**：
```
日志写入
    │
    ▼
内存缓存 (LogGroup)
    │
    ├─→ 时间窗口触发 (默认5分钟)
    │       │
    │       ▼
    │  批量写入数据库
    │
    └─→ 定时刷新触发 (每30秒)
            │
            ▼
       写入数据库
```

### 3.6 SQLite 日志存储

数据库日志存储使用 SQLite 实现，提供完整的日志管理能力：

```csharp
public interface ILogStorage
{
    Task InitializeAsync();
    Task StoreLogGroupAsync(LogGroup logGroup);
    Task<LogGroup?> GetLogGroupAsync(string groupId);
    Task<List<LogGroup>> GetLogGroupsAsync(DateTime? startTime, DateTime? endTime);
    Task<List<LogGroup>> GetLogGroupsByLevelAsync(LogLevel minLevel, ...);
    Task<List<LogGroup>> GetErrorLogGroupsAsync(...);
    Task<List<LogGroup>> GetRecentLogGroupsAsync(int count = 10);
    Task<long> GetLogGroupCountAsync();
    Task DeleteLogGroupAsync(string groupId);
    Task DeleteLogGroupsBeforeAsync(DateTime beforeTime);
    Task ClearAllAsync();
}
```

### 3.7 日志配置示例

```json
// appsettings.json 日志配置
{
  "Logging": {
    "EnableConsoleOutput": true,
    "EnableFileOutput": true,
    "EnableDatabaseOutput": true,
    "MinLogLevel": "Information",
    "FileLogPath": "logs/FrameManage.log",
    "DatabaseLogPath": "DataManage/DataBase/FrameManageLogs.db"
  }
}
```

---

## 四、常见运行时问题与修复

### 4.1 问题分类表

| 错误类型 | 错误信息 | 根因 | 修复方案 | 检测级别 |
|---------|---------|------|----------|----------|
| **配置加载失败** | `Config file not found` | 配置文件路径错误 | 检查路径是否正确，使用绝对路径 | 配置加载时 |
| **配置验证失败** | `Missing required property: Action` | 事件配置缺少 Action 属性 | 为事件配置添加 Action 或 HandlerParams.Action | 配置加载时 |
| **控件创建失败** | `Control not found` | 控件名称拼写错误或未创建 | 检查配置文件中控件 Name 属性 | 运行时 |
| **事件处理失败** | `Value cannot be null. (Parameter 'action')` | Action 参数为空 | 确保按钮 Events 配置包含有效的 Action | 运行时 |
| **处理器未找到** | `未找到 Action 处理器` | ActionName 拼写错误或处理器未注册 | 检查 ActionName 拼写，确保处理器类正确实现 | 运行时 |
| **数据库连接失败** | `数据库文件不存在` | 数据库路径错误 | 检查数据库文件路径配置 | 运行时 |

### 4.1.1 配置验证机制

系统在加载配置文件时会自动进行验证，以下是验证规则：

| 验证项 | 规则 | 错误级别 | 处理方式 |
|--------|------|----------|----------|
| `Form.Name` | 必填，非空 | 错误 | 抛出 ValidationException |
| `Controls[]` | 数组非空 | 警告 | 创建空表单并记录警告 |
| `Control.Type` | 必填 | 警告 | 跳过该控件并记录警告 |
| `Control.Name` | 必填 | 警告 | 自动生成唯一名称 |
| `EventConfig.EventName` | 必填 | 警告 | 跳过该事件绑定 |
| `EventConfig.Action` | 与 HandlerParams.Action 二选一 | 警告 | 使用默认空 Action |
| `Button.Events` | 可选 | 信息 | 无事件绑定 |

### 4.2 问题详细分析与修复

#### 问题 1：按钮点击失败 - Action 参数为 null

**错误日志**：
```
[Error] ControlFactory: Button click failed: 
Exception: Value cannot be null. (Parameter 'action')
Stack Trace: at CommandFramework.CreateCommand(String action, ...)
```

**根因分析**：
按钮配置的事件中缺少有效的 `Action` 属性。系统支持两种配置格式：
1. 直接使用 `Action` 属性：`{"EventName": "click", "Action": "generategraphid"}`
2. 使用 `HandlerParams` 中的 `Action`：`{"EventName": "click", "HandlerParams": {"Action": "generategraphid"}}`

如果两种格式都未提供，`action` 参数将为空，导致异常。

**修复方案**：
```json
// 格式一：直接使用 Action 属性（推荐）
{
  "Type": "button",
  "Name": "btnGenerateId",
  "Text": "生成ID",
  "Events": [
    {
      "EventName": "click",
      "Action": "generategraphid"
    }
  ]
}

// 格式二：使用 HandlerParams（兼容旧版本）
{
  "Type": "button",
  "Name": "btnGenerateId",
  "Text": "生成ID",
  "Events": [
    {
      "EventName": "click",
      "HandlerType": "custom",
      "HandlerParams": {
        "Action": "generategraphid"
      }
    }
  ]
}
```

**验证命令**：
```bash
# 验证配置文件
FrameManage.exe --validate Config/GraphReferenceManager.json

# 检查事件配置
FrameManage.exe --check-events Config/GraphReferenceManager.json
```

#### 问题 2：控件未找到

**错误日志**：
```
[DEBUG] Looking for control: 'btnLoadGraph'
[DEBUG] Control cache count: 0
Control not found: btnLoadGraph
```

**根因分析**：
1. 配置文件中控件名称拼写错误
2. 配置文件加载失败导致控件未创建
3. 控件名称与配置不匹配

**修复方案**：
1. 检查配置文件中控件的 `Name` 属性
2. 确保配置文件路径正确
3. 验证 JSON 语法正确

#### 问题 3：Action 处理器未注册

**错误日志**：
```
未找到 Action 处理器: loadgraphdata
```

**根因分析**：
1. 处理器类未实现 `IActionHandler` 接口
2. 处理器类名不符合命名约定（需以 `ActionHandler` 结尾）
3. 处理器所在程序集未被扫描

**修复方案**：
```csharp
// 确保处理器类正确实现接口
public class LoadGraphDataActionHandler : IActionHandler
{
    public string ActionName => "loadgraphdata";
    
    public void Execute(HandlerParams param)
    {
        // 业务逻辑
    }
}
```

#### 问题 4：配置文件路径错误

**错误日志**：
```
Error loading config: Config file not found
```

**根因分析**：
1. 相对路径相对于程序运行目录
2. 路径分隔符使用错误（Windows 使用 `\`，Linux 使用 `/`）
3. 路径包含空格未加引号

**修复方案**：
```bash
# 使用绝对路径
FrameManage.exe load "d:\Project\Workflow_\FrameManage.Modular\FrameManage\Config\GraphReferenceManager.json"

# 使用相对路径（相对于 FrameManage.exe 所在目录）
FrameManage.exe load Config/GraphReferenceManager.json
```

---

## 五、调试与诊断指南

### 5.1 启用调试模式

```bash
# 启用调试日志
FrameManage.exe --debug load Config/GraphReferenceManager.json show

# 输出详细日志到文件
FrameManage.exe --debug --logfile debug.log load Config/GraphReferenceManager.json show
```

### 5.2 常用诊断命令

```bash
# 列出所有注册的表单
FrameManage.exe list

# 列出所有可用控件
FrameManage.exe load Config/GraphReferenceManager.json list

# 检查 Action 处理器注册情况
FrameManage.exe --list-handlers

# 验证配置文件语法
FrameManage.exe --validate Config/GraphReferenceManager.json
```

### 5.3 日志分析要点

**关键日志级别**：
- `[DEBUG]`：详细调试信息，包含控件查找、命令执行等
- `[Information]`：程序运行状态信息
- `[Warning]`：警告信息，如配置缺失等
- `[Error]`：错误信息，需重点关注

**需要关注的关键字**：
- `Control not found`：控件未找到
- `Action not found`：Action 处理器未找到
- `Error loading config`：配置加载失败
- `Exception`：异常信息

---

## 六、最佳实践建议

### 6.1 配置文件编写规范

1. **使用绝对路径**：在生产环境中使用绝对路径避免路径解析问题
2. **验证 JSON 语法**：使用在线工具或 IDE 验证 JSON 格式
3. **添加注释**：为复杂配置添加注释说明
4. **统一命名规范**：控件名称使用 PascalCase，ActionName 使用小写加下划线

### 6.2 命令序列编写规范

```bash
# 命令序列文件示例（推荐格式）
# 1. 加载配置
load Config/GraphReferenceManager.json GraphReferenceManager show
sleep 500

# 2. 设置控件值
set txtGraphPath "Data/Graph.db"
sleep 100

# 3. 执行操作
click btnLoadGraph
sleep 2000

# 4. 验证结果
get lblStatus
get lblNodeCount
```

**注意事项**：
- 在操作之间添加适当的 `sleep` 等待
- 使用注释说明每个步骤的目的
- 避免过长的命令序列，拆分为多个文件

### 6.3 测试脚本编写规范

```powershell
# PowerShell 测试脚本示例
$cliPath = "FrameManage.exe"
$configPath = "Config/GraphReferenceManager.json"
$outputFile = "test_results.txt"

# 执行测试
& $cliPath --file test_commands.txt | Out-File $outputFile

# 检查结果
$results = Get-Content $outputFile
if ($results -contains "成功") {
    Write-Host "测试通过！" -ForegroundColor Green
} else {
    Write-Host "测试失败！" -ForegroundColor Red
    exit 1
}
```

---

## 七、故障排除流程图

```
问题发生
    │
    ▼
检查错误日志
    │
    ├─→ [Error] Control not found?
    │       └─→ 检查配置文件中控件名称
    │
    ├─→ [Error] Action not found?
    │       └─→ 检查 ActionName 拼写和处理器注册
    │
    ├─→ [Error] Config file not found?
    │       └─→ 检查配置文件路径
    │
    └─→ 其他错误
            └─→ 启用调试模式获取详细信息
                    │
                    ▼
              分析调试日志
                    │
                    ▼
              定位问题根源
                    │
                    ▼
              实施修复方案
                    │
                    ▼
              重新运行测试
                    │
                    ▼
              验证修复效果
```

---

**版本**: v1.0  
**日期**: 2026-05-07  
**适用范围**: FrameManage CLI 模式运行时故障排除与调试