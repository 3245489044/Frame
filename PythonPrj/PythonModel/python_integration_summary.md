# Python 模型集成与运行环境配置总结

## 整体架构

```
DeepSeek API
    ↑
[PythonModel/]                         ← Python 层
    ├── clients/                        ← 模型客户端（模块化）
    │   ├── base.py                     ← BaseModelClient 抽象基类
    │   └── deepseek.py                 ← DeepSeekClient 实现
    ├── DeepSeekInvoke.py               ← 兼容层（委托给 clients.deepseek）
    └── __init__.py                     ← 统一导出
    ↑
[PythonModel.Bridge]                   ← C# 桥接层
    ├── PythonEnvironment.cs            ← Python 运行时自动发现
    ├── PythonRuntime.cs                ← PythonEngine 生命周期管理
    ├── IPythonModelService.cs          ← 泛化服务接口
    └── PythonModelBridge.cs            ← 桥接实现（ChatComplete/Async）
    ↑
[FunctionStorage/ModelLibrary.cs]       ← 函数注册层，注册到 FunctionExecutionFramework
    ↑
[FileDrivenFramework]                   ← 使用层
    ├── DrivenConfig/JsonConfig/DeepSeekChat.json  ← UI 配置
    ├── DrivenConfig/Scripts/test_deepseek_chat.txt ← 测试脚本
    └── src/Command.cs                  ← CLI 命令执行
```

## 模块结构说明

### Python 层 (`PythonModel/`)

| 文件/目录 | 职责 |
|-----------|------|
| `clients/base.py` | `BaseModelClient` 抽象基类，定义 `chat()` / `chat_stream()` / `set_api_key()` 接口 |
| `clients/deepseek.py` | `DeepSeekClient` 实现，封装 OpenAI SDK 调用 DeepSeek API |
| `DeepSeekInvoke.py` | **兼容层**，保留原有模块级函数接口，内部委托给 `DeepSeekClient` |
| `__init__.py` | 统一导出所有公开接口 |

**扩展新模型**：在 `clients/` 下新建文件（如 `tongyi.py`），继承 `BaseModelClient` 即可。

### C# 桥接层 (`PythonModel.Bridge/`)

| 文件 | 职责 |
|------|------|
| `PythonEnvironment.cs` | 静态工具类，6 种策略自动发现 Python 安装路径 |
| `PythonRuntime.cs` | **Python 引擎生命周期管理**（线程安全单例，从 Bridge 中分离） |
| `IPythonModelService.cs` | **泛化服务接口**（不绑定具体模型名） |
| `PythonModelBridge.cs` | **桥接实现**，通过 pythonnet 调用 Python 侧模块 |

### 关键模块

#### PythonEnvironment.cs

静态工具类，完全不依赖 `Python.Runtime`。API 简洁：

- `ResolveHome(hint?)` → 发现 Python 安装目录
- `FindDllName(homePath)` → 确定 python DLL 文件名
- `ResolveModulePath(hint?)` → 确定 Python 搜索路径（PYTHONPATH）

#### PythonRuntime.cs

从原 `DeepSeekBridge` 中提取的引擎生命周期管理：

- `Initialize()` → 线程安全，只执行一次
- `Shutdown()` → 安全关闭
- `ThrowIfNotInitialized()` → 守卫方法

#### ModelLibrary.cs

注册到 `FunctionExecutionFramework` 的函数：

| 注册名 | 签名 | 说明 |
|-------|------|------|
| `ChatComplete` | `(string systemPrompt, string userInput) → string` | 同步调用 |
| `ChatCompleteAsync` | `(string systemPrompt, string userInput) → Task<string>` | 异步调用 |
| `ChatCompleteRaw` | `(string input) → string` | 空 system prompt，带 Console 日志 |
| `SetApiKey` | `(string apiKey) → void` | 设置 API Key |

生命周期：`Lazy<PythonModelBridge>` 线程安全惰性初始化，`Shutdown()` 显式释放。

#### DeepSeekChat.json UI 配置

聊天风格布局，两个区域 1:1 比例：

- 回复区：`multiline, readOnly`，占上半部分
- 输入框 + 发送按钮：同一行，水平分割

事件绑定：`sendButton → ChatCompleteRaw → inputs: [userInputTextBox] → output: responseTextBox`

#### CLI click 命令处理

`UICommandManager.ClickControl()` 直接从控件的 `Tag` 读取 `ControlConfig`，绕过 UI 事件链路：

1. 从 `btn.Tag` 读取 `ControlConfig.Events.Bindings`
2. 用 `btn.FindForm()` 替代 `TopLevelControl` 获取表单（CLI 模式下无消息泵）
3. 提取输入控件值 → 调用 `FunctionExecutionFramework.ExecuteAsync()`
4. 写回输出控件

---

# 踩坑经验记录（Debugging Journey）

## 坑 1：DLL 指向错误 → `python3.dll` vs `python313.dll`

### 现象

```
TypeInitializationException: The type initializer for 'Delegates' threw an exception.
 ---> MissingMethodException: Failed to load symbol Py_IncRef.
 ---> Win32Exception (127): 找不到指定的程序。
```

### 时间线

1. **初始代码**：`FindDllName()` 硬编码返回 `python3.dll`
2. **第一次升级**：pythonnet 升到 `3.0.5`，但依然报同样的错
3. **排查过程**：搜索 GitHub Issues → 发现是 python3.dll 是**稳定 ABI 转发层**，只含少量转发函数，不含 `Py_IncRef` 等核心 C API
4. **根因确认**：`python3.dll` 是轻型转发器，`python313.dll` / `python39.dll` 才是完整 Python 运行库

### 解决

`FindDllName()` 改为**优先返回版本化 DLL**：

```csharp
var versionDlls = Directory.GetFiles(homePath, "python*.dll")
    .Where(f => Path.GetFileName(f) != "python3.dll" && ...)
    .OrderByDescending(f => Path.GetFileName(f))
    .ToArray();
if (versionDlls.Length > 0) return Path.GetFileName(versionDlls[0]);
```

### 教训

- `python3.dll` ≠ Python 运行库，只是 ABI 层
- pythonnet 需要完整版 DLL（`python39.dll` / `python313.dll`），两者在相同目录下
- 如果只测有没有 `python3.dll` 就认为环境可用，一定是错的

---

## 坑 2：Python 版本不匹配 → pythonnet 3.0.3 vs Python 3.13

### 现象

升级到 `python313.dll` 后报：

```
MissingMethodException: Failed to load symbol _PyThreadState_UncheckedGet.
```

### 发现

查 NuGet 版本历史：

| pythonnet 版本 | 支持 Python |
|---|---|
| 3.0.3 | 3.7 ~ 3.12 |
| **3.0.5** | **3.7 ~ 3.13**（changelog 明确标注） |
| 3.1.0-rc.0 | 3.7 ~ 3.14（预览版） |

`3.0.3` 太老，不认识 Python 3.13 的新符号。`3.0.5` 才正式支持。

### 解决

将 `PythonModel.Bridge.csproj` 中的 pythonnet 从 `3.0.3` 升到 `3.0.5`。

### 教训

- NuGet 包版本必须与 Python 版本对齐
- 查版本兼容性要去 NuGet.org 看 changelog
- 不能假设 "最新 Python 一定配最新 pythonnet"

---

## 坑 3：encodings 找不到 → PythonPath 未包含标准库

### 现象

```
Fatal Python error: Failed to import encodings module
ModuleNotFoundError: No module named 'encodings'
```

### 根因

`PythonEngine.PythonPath` 只设置了项目模块路径（`pythonmodel/`、`PythonModel/`），没有包含 Python 标准库路径（`Lib/`）。Python 启动时第一个加载的就是 `encodings` 模块。

### 解决

在 `ResolveModulePath()` 中自动追加标准库路径：

```csharp
string home = ResolveHome(hint);
string libPath = Path.Combine(home, "Lib");
if (Directory.Exists(libPath)) paths.Add(libPath);

string sitePackagesPath = Path.Combine(home, "Lib", "site-packages");
if (Directory.Exists(sitePackagesPath)) paths.Add(sitePackagesPath);
```

同时在 `DeepSeekBridge.Initialize()` 中也追加 `DLLs/` 目录。

### 教训

- `PythonEngine.PythonHome` 只是告诉 pythonnet Python 在哪，标准库搜索还是要靠 `PythonEngine.PythonPath`
- Python 启动阶段会先导入 `encodings`，`Lib/encodings` 必须能找到
- 嵌入包（embed）没有 `Lib/encodings`，完整安装版才有

---

## 坑 4：CLI 模式下控件绑定失效 → TopLevelControl 为 null

### 现象

CLI 模式（`--file test.txt`）下点击按钮，输入参数为空，API 收到空字符串。

### 根因

`UIConnector` 中的 `ExtractParameters()` 使用 `control.TopLevelControl as Form` 获取表单引用。但 CLI 模式下控件没有添加到可见表单，`TopLevelControl` 返回 `null`，参数无法提取。

### 解决

`Command.cs` 的 `ClickControl()` 重写为从 `btn.Tag` 读取 `ControlConfig` 绑定配置，用 `btn.FindForm()` 替代 `TopLevelControl`，直接调用 `FunctionExecutionFramework` 执行函数：

```csharp
var config = btn.Tag as ControlConfig;
var root = btn.FindForm();               // ← CLI 模式下也能工作
var parameters = ExtractFromBindings(...);
var result = FunctionExecutionFramework.ExecuteAsync(functionName, parameters);
WriteBackToOutput(result, bindings.Output);
```

### 教训

- WinForms 的 `TopLevelControl` 依赖消息泵，CLI 模式下不可用
- `btn.FindForm()` 在 non-visible 状态下也能正常工作
- 控件的 `Tag` 属性是 WdUI 框架存储 `ControlConfig` 的关键通道
- `PerformClick()` 触发 UI 事件在 CLI 模式下也不可靠，最佳方案是绕过事件链路直接调用

---

## 坑 5：Async + Python GIL 死锁

### 现象

`ChatCompleteRaw` 注册为异步函数（`Func<string, Task<string>>`）时，执行永远不返回。

### 根因

CLI 模式下无 `SynchronizationContext`，`DeepSeekBridge.ChatCompleteAsync()` 内部用 `Task.Run` + `Py.GIL()`，当外部调用链同时持有 GIL 或等待 Task 时出现死锁。

### 解决

`ChatCompleteRaw` 改回同步：

```csharp
(Func<string, string>)(input =>
{
    Console.WriteLine($"[DeepSeek] >>> 发送请求... 输入: {input}");
    var response = _bridge.Value.ChatComplete("", input);
    Console.WriteLine($"[DeepSeek] <<< 收到回复 ({response.Length} 字符)");
    return response;
})
```

### 教训

- Python.GIL + async/await 的组合在 Console/CLI 场景下容易死锁
- 简单场景用同步更可靠，异步留给真正需要并行的场景

---

## 坑 6：Python 路径发现脆弱性

### 原始设计

`DeepSeekBridge.ResolvePythonHome()` 只有 3 种策略：

1. 参数路径
2. `PYTHONHOME` 环境变量
3. 嵌入路径 `{BaseDir}\python_env\env`

换成目录就找不到 Python。

### 重写后

抽取为独立 `PythonEnvironment.cs`，6 种策略逐级尝试：

1. hint 参数
2. `PYTHONHOME` 环境变量
3. `py` 启动器（`py -0`）
4. Windows 注册表
5. 常见安装路径
6. `PATH` 环境变量

在任何装了 Python 的机器上都能自动发现。

### 教训

- 不要依赖相对路径和单点环境变量
- 参考 `py.exe` 启动器的设计：扫描系统注册信息
- 跨机器、跨用户场景下，自动发现比环境变量更可靠

---

## 坑 7：模块导入路径错误 → `PythonModel.DeepSeekInvoke` vs `DeepSeekInvoke`

### 现象

```
PythonException: No module named 'PythonModel.DeepSeekInvoke'
```

`PythonEngine.Initialize()` 成功 ✅、sys.path 内容正确、`Lib/encodings` 也找到了，但导入 Python 模块失败。

### 根因

`ResolveModulePath()` 把 `pythonmodel` 目录本身加入了 sys.path：

```
sys.path = [
  ...\bin\Debug\...\pythonmodel,    ← 项目源码目录
  ...\Python313\Lib,
  ...\Python313\Lib\site-packages
]
```

但 `Py.Import("PythonModel.DeepSeekInvoke")` 需要的是 **父目录** 在 sys.path 中。Python 的 import 解析逻辑是：

```
Py.Import("PythonModel.DeepSeekInvoke")
  → 在 sys.path 的每个条目里查找 "PythonModel/__init__.py"
  → 如果条目是 ...\pythonmodel，则找 ...\pythonmodel\PythonModel\__init__.py → 不存在 ❌
  → 如果条目是 ...\（父目录），则找 ...\PythonModel\__init__.py → 存在 ✅
```

### 之前为什么 CentralConsole 测试通过了？

之前 CentralConsole 能导入成功，是因为运行时 CWD 恰好是项目目录，Python 默认将 CWD 加入 `sys.path`，CWD 下包含 `pythonmodel` 子目录。Launcher 的 CWD 不同，所以失败。

这解释了"之前测试通过但实际运行失败"的矛盾。

### 解决

两处修改：

**1. 导入方式**：`Py.Import("PythonModel.DeepSeekInvoke")` → `Py.Import("DeepSeekInvoke")`

当 `...\PythonModel` 目录本身就作为 sys.path 条目时，`import DeepSeekInvoke` 直接在该目录下找 `DeepSeekInvoke.py`，不涉及包名解析。

**2. 路径补全**：同时将 `Lib`、`site-packages`、`DLLs` 都在 `ResolveModulePath()` 中补齐，确保所有 Python 标准库和 C 扩展都能找到。

### 教训

- `Py.Import("A.B")` 需要 **`A` 的父目录** 在 sys.path 中
- `Py.Import("B")` 需要 **包含 `B.py` 的目录** 在 sys.path 中
- 两种方式互不兼容，不能混用
- `CWD`（当前工作目录）会被自动加入 sys.path，导致 Import 在开发环境中能跑、部署后失败——这是经典的环境差异 bug
- 调试方法：打印 `sys.path` 看实际内容，和预期对比

---

## 最终验证结果

运行 FileDrivenFramework Launcher CLI 测试确认端到端可用：

```
[DeepSeek] >>> 发送请求... 输入: 用简短的话介绍一下你自己。
[DeepSeek] <<< 收到回复 (127 字符)
[OK] responseTextBox = 我是DeepSeek，由深度求索公司创造的AI助手...

[DeepSeek] >>> 发送请求... 输入: 请用Python写一个冒泡排序算法。
[DeepSeek] <<< 收到回复 (3044 字符)
[OK] responseTextBox = 完整冒泡排序实现（含基础版+优化版+通用版）

[RESULT] Executed 12 commands: 12 succeeded, 0 failed
```

### 当前环境状态

| 组件 | 版本 | 路径 |
|------|------|------|
| Python 运行时 | 3.13.3 | `%LOCALAPPDATA%\Programs\Python\Python313` |
| `pythonnet` (NuGet) | **3.0.5** | `PythonModel.Bridge.csproj` |
| `openai` (pip) | 2.36.0 | Python 站点包 |
| `DeepSeekAPI` | 已配置 ✅ | 用户环境变量 |
| `PYTHONHOME` | 已配置 ✅ | 用户环境变量 |
| Python 引擎初始化 | 通过 ✅ | |
| Python 模块导入 | 通过 ✅ | |
| DeepSeek API 调用 | 通过 ✅ | 两次请求均返回有效回复 |

### 运行方式

```bash
# GUI 模式
FileDrivenFramework.Launcher --config DrivenConfig/JsonConfig/DeepSeekChat.json --show

# CLI 模式（自动化测试）
FileDrivenFramework.Launcher --file DrivenConfig/Scripts/test_deepseek_chat.txt
```
