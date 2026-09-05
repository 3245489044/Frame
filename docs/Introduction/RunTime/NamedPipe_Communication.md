# 命名管道通信方案：PowerShell ↔ Launcher 进程间通信

## 1. 问题

### 1.1 当前架构

```
交互启动.bat (cmd 窗口)
  └─ PowerShell 进程（交互菜单）
       └─ Start-Process → 新窗口: FileDriven.Launcher.exe
            ├─ Console.WriteLine 输出到新窗口（不可见）
            └─ PowerShell 只能拿到 ExitCode
```

PowerShell 用 `Start-Process` 启动 Launcher.exe（WinForms 应用），Windows 为 GUI 程序创建新窗口，所有 `Console.WriteLine` 输出到新窗口，PowerShell 看不到。

### 1.2 约束条件

1. **PowerShell 脚本必须保留** — AI 通过 PowerShell 驱动整个流程
2. **Launcher 的 WinForms 窗口必须显示** — GUI 模式需要真实窗口
3. **所有 Console.WriteLine 输出必须回到 PowerShell 窗口**

---

## 2. 解决方案：命名管道重定向 Console.Out

### 2.1 核心思路

**PowerShell 创建命名管道服务器，Launcher 连接后，把 `Console.Out` 重定向到管道。这样 Launcher 的所有 `Console.WriteLine` 输出都通过管道传到 PowerShell，PowerShell 原样显示。**

```
PowerShell 进程                          Launcher.exe 进程
┌─────────────────────┐                ┌─────────────────────┐
│ 创建 NamedPipeServer │ ←──管道名───  │ 连接 NamedPipeClient │
│ (等待连接)           │                │                     │
│                     │                │ Console.SetOut(pipe) │
│ 实时读取管道数据     │ ←──原样输出──  │ Console.WriteLine()  │
│ 显示在控制台         │ ←──原样输出──  │ Console.WriteLine()  │
│                     │ ←──原样输出──  │ Console.WriteLine()  │
│ 管道关闭 → 退出      │                │ 程序退出 → 管道关闭   │
└─────────────────────┘                └─────────────────────┘
```

### 2.2 零依赖

- **PowerShell 端**：使用 .NET 的 `System.IO.Pipes.NamedPipeServerStream`（内置）
- **C# 端**：使用 .NET 的 `System.IO.Pipes.NamedPipeClientStream`（内置）
- **不需要任何 NuGet 包**

---

## 3. 详细设计

### 3.1 Launcher 端：Program.cs

```csharp
[STAThread]
static void Main(string[] args)
{
    var options = CliOptions.Parse(args);
    
    // ★ 如果传入了管道名称，重定向 Console.Out 到管道
    if (!string.IsNullOrEmpty(options.PipeName))
    {
        var pipeClient = new NamedPipeClient(options.PipeName);
        pipeClient.Connect();
        Console.SetOut(pipeClient.Writer);  // ★ 所有 Console.WriteLine 输出到管道
    }

    LoggingSetup.Initialize(options.LogCategories);
    FrameworkInitializer.Initialize();

    try
    {
        if (options.ShouldRunGuiMode())
        {
            GuiModeRunner.Run(options);
        }
        else
        {
            var orchestrator = new AsyncSyncOrchestrator();
            orchestrator.RunSync(() => CliModeRunner.RunAsync(options));
        }
    }
    finally
    {
        // ★ 恢复 Console.Out 并关闭管道
        if (!string.IsNullOrEmpty(options.PipeName))
        {
            var standardOutput = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(standardOutput);
        }
        FrameworkInitializer.Shutdown();
        ExecuteLogPipeline();
    }
}
```

### 3.2 新建：NamedPipeClient.cs

```csharp
// FileDriven.CLI/NamedPipeClient.cs
namespace FileDriven.CLI;

/// <summary>
/// 命名管道客户端 — 连接到 PowerShell 创建的管道服务器
/// 把 Console.Out 重定向到管道，所有输出实时传到 PowerShell
/// </summary>
public class NamedPipeClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly string _pipeName;

    public NamedPipeClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    /// <summary>
    /// 连接到 PowerShell 端的管道服务器
    /// </summary>
    public void Connect(int timeoutMs = 5000)
    {
        _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
        _pipe.Connect(timeoutMs);
        _writer = new StreamWriter(_pipe) { AutoFlush = true };
    }

    /// <summary>
    /// 获取管道写入器 — 用于 Console.SetOut()
    /// </summary>
    public StreamWriter Writer => _writer ?? throw new InvalidOperationException("Not connected");

    public void Dispose()
    {
        _writer?.Dispose();
        _pipe?.Dispose();
    }
}
```

### 3.3 CliOptions.cs — 增加 PipeName

```csharp
// 在 CliOptions 类中增加
/// <summary>命名管道名称（--pipe-name 参数）：用于与 PowerShell 通信</summary>
public string? PipeName { get; set; }
```

```csharp
// 在 Parse() 的 switch 中增加
case "--pipe-name":
    if (i + 1 < args.Length) options.PipeName = args[++i];
    break;
```

### 3.4 PowerShell 端：运行测试GUI模式.ps1

```powershell
# ★ 修改 run 分支中的 Start-Process 部分

# 生成唯一的管道名称
$pipeName = "FileDrivenPipe_$([System.Guid]::NewGuid().ToString("N"))"

# 创建命名管道服务器（在 PowerShell 端）
$pipeServer = New-Object System.IO.Pipes.NamedPipeServerStream($pipeName, [System.IO.Pipes.PipeDirection]::In)
$pipeReader = $null

try {
    # 启动 Launcher，传入管道名称
    $process = Start-Process -FilePath $launcherExe -ArgumentList "--show-gui --file `"$testPath`" --pipe-name `"$pipeName`"" -PassThru

    # 等待 Launcher 连接管道
    Write-Host "  等待 Launcher 连接..."
    $pipeServer.WaitForConnection()
    $pipeReader = New-Object System.IO.StreamReader($pipeServer)
    Write-Host "  Launcher 已连接，开始接收输出..."
    Write-Host ""

    # ★ 实时读取管道数据，原样显示
    # ★ Launcher 的所有 Console.WriteLine 输出都会通过管道传过来
    while (-not $process.HasExited -or $pipeServer.IsConnected) {
        $line = $pipeReader.ReadLine()
        if ($line) {
            Write-Host $line
        } else {
            # 管道关闭或读取超时
            if ($process.HasExited) { break }
            Start-Sleep -Milliseconds 100
        }
    }

    # 显示退出状态
    Write-Host ""
    if ($process.ExitCode -ne 0) {
        Write-Host "[完成] 测试执行结束，部分命令可能失败（错误码: $($process.ExitCode)）"
    } else {
        Write-Host "[完成] 测试执行结束"
    }
}
finally {
    if ($pipeReader) { $pipeReader.Dispose() }
    if ($pipeServer) { $pipeServer.Dispose() }
}
```

---

## 4. 改动清单

### 4.1 新增文件

| 文件 | 行数 | 说明 |
|------|------|------|
| `FileDriven/FileDriven.CLI/NamedPipeClient.cs` | ~40 行 | 命名管道客户端 |

### 4.2 修改文件

| 文件 | 改动量 | 说明 |
|------|--------|------|
| `FileDriven/FileDriven.CLI/CliOptions.cs` | +4 行 | 增加 `PipeName` 属性 + 参数解析 |
| `FileDriven/FileDriven.Launcher/Program.cs` | +15 行 | 管道初始化 + Console.SetOut |
| `Script/FileDriven/FileDriven/运行测试GUI模式.ps1` | ~30 行 | 管道服务器 + 实时读取显示 |

### 4.3 不需要改动的文件

| 文件 | 原因 |
|------|------|
| `BasicMethod/Logging/*` | 日志模块不需要改 |
| `FileDriven/FileDriven.CLI/PipeModeRunner.cs` | 已有管道模式，功能不同，保留 |
| `EnableMent/交互启动.bat` | 不需要改 |
| `EnableMent/CLI启动.bat` | 不需要改 |
| `EnableMent/UI启动.bat` | 不需要改 |

---

## 5. 优劣分析

### 优势

| 方面 | 说明 |
|------|------|
| **PowerShell 保留** | AI 仍然可以通过 PowerShell 驱动整个流程 |
| **所有输出可见** | Launcher 的所有 Console.WriteLine 原样传到 PowerShell |
| **零依赖** | 不需要 NuGet 包，.NET 和 PowerShell 都内置支持 |
| **改动最小** | 只新增 1 个文件，修改 3 个文件 |
| **兼容性** | 不传 --pipe-name 时行为不变 |

### 劣势

| 方面 | 说明 |
|------|------|
| **管道超时** | Launcher 需要在 5 秒内连接管道，否则超时 |
| **WinForms 窗口仍在** | Launcher 的 WinForms 窗口仍然会显示（这是 GUI 模式的正常行为） |
