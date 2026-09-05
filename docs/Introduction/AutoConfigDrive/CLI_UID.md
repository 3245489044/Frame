# FrameManage CLI 调用 UI 前端操作文档

## 配置路径说明

- **启动程序路径**: `FrameManage.exe`
- **快捷方式**: `FrameManage - 快捷方式.lnk`
- **Json配置文件路径**: `FrameManage.Modular/FrameManage/Config`
- **CLI脚本配置文件**: `Script\TEST`
- **Action处理器路径**: `FrameManage.Modular/FrameManage.ActionHandlers`
---

## 一、CLI 命令封装测试

### 1.1 直接命令模式（推荐）

**命令格式：**
```bash
FrameManage.exe load <configPath> [formName] [show]
```

**参数说明：**

| 参数 | 必填 | 说明 |
|------|------|------|
| `load` | 是 | 命令类型，标识加载配置文件 |
| `<configPath>` | 是 | 配置文件路径（支持相对路径和绝对路径） |
| `[formName]` | 否 | 指定表单注册名称，不指定则使用配置文件中的名称 |
| `[show]` | 否 | 加载后自动显示表单窗口 |

**测试示例：**
```bash
# 基本用法 - 测试加载配置文件
FrameManage.exe load Config/GraphDatabaseManager.json

# 指定表单名称并显示 - 测试UI展示
FrameManage.exe load Config/GraphDatabaseManager.json GraphDatabaseManager show

# 使用相对路径测试
FrameManage.exe load "Config/GraphDatabaseManager.json"

# 测试批量加载命令文件
FrameManage.exe --file commands.txt
```

### 1.2 UI 命令模式

**命令格式：**
```bash
FrameManage.exe ui load <configPath>
```

**测试示例：**
```bash
FrameManage.exe ui load Config/GraphDatabaseManager.json
```

### 1.3 命令文件批量执行测试

**命令文件示例（commands.txt）：**
```bash
# 测试命令序列
load Config/GraphDatabaseManager.json GraphDatabaseManager
show GraphDatabaseManager
click btnLoadGraph
sleep 1000
snapshot GraphDatabaseManager
```

### 1.4 常用测试命令

```bash
# 表单操作测试
load Config/GraphDatabaseManager.json GraphDatabaseManager show
show GraphDatabaseManager
close GraphDatabaseManager
list

# 控件操作测试
set txtGraphPath "Data/graph.json"
get txtGraphPath
click btnLoadGraph

# 数据库操作测试
readdb Data/graph_data.db
loadstorage Data/
refresh

# 其他测试命令
sleep 2000
setoutput readable
snapshot GraphDatabaseManager
ai 分析这个图数据
--help
```

---

## 二、UI 调用

### 2.1 配置文件结构

**完整配置示例：**

```json
{
  "Form": {
    "Name": "GraphDatabaseManager",
    "Title": "图数据库管理器",
    "Width": 800,
    "Height": 600
  },
  "Controls": [
    {
      "Type": "button",
      "Name": "btnLoadGraph",
      "Text": "加载图数据",
      "Location": { "X": 10, "Y": 10 },
      "Size": { "Width": 120, "Height": 26 },
      "Events": [
        {
          "EventName": "click",
          "Action": "loadgraphdata"
        }
      ]
    },
    {
      "Type": "textbox",
      "Name": "txtGraphPath",
      "Location": { "X": 10, "Y": 45 },
      "Size": { "Width": 300, "Height": 22 },
      "ReadOnly": false
    },
    {
      "Type": "listview",
      "Name": "lstGraphNodes",
      "Location": { "X": 10, "Y": 80 },
      "Size": { "Width": 760, "Height": 200 },
      "FullRowSelect": true,
      "GridLines": true,
      "Columns": [
        { "Name": "节点ID", "Width": 150 },
        { "Name": "类型", "Width": 120 },
        { "Name": "名称", "Width": 200 }
      ]
    },
    {
      "Type": "tabcontrol",
      "Name": "tabControl1",
      "Location": { "X": 10, "Y": 300 },
      "Size": { "Width": 760, "Height": 250 },
      "Tabs": [
        {
          "Name": "TabPage1",
          "Text": "节点信息",
          "Controls": [
            {
              "Type": "label",
              "Name": "lblNodeCount",
              "Text": "节点数: 0",
              "Location": { "X": 10, "Y": 10 }
            }
          ]
        }
      ]
    }
  ]
}
```

### 2.2 支持的控件类型

| 类型 | 说明 | 特有属性 |
|------|------|----------|
| `button` | 按钮 | `Events` |
| `textbox` | 文本框 | `Multiline`, `ReadOnly`, `ScrollBars` |
| `label` | 标签 | - |
| `listview` | 列表视图 | `FullRowSelect`, `GridLines`, `Columns` |
| `combobox` | 下拉框 | `Items` |
| `checkbox` | 复选框 | - |
| `panel` | 面板容器 | `Controls` |
| `groupbox` | 分组框 | `Controls` |
| `tabcontrol` | 选项卡控件 | `Tabs` |

### 2.3 事件绑定配置

```json
{
  "Events": [
    {
      "EventName": "click",
      "Action": "loadgraphdata",
      "HandlerParams": {
        "param1": "value1",
        "param2": "value2"
      }
    }
  ]
}
```

**支持的事件：**
- `click`：按钮点击事件

**支持的 Action：**
- `loadstoragedata`：加载存储数据
- `browsepath`：浏览路径
- `loadgraphdata`：加载图数据

### 2.4 Action 处理器自动注册机制

所有底层运行函数（Action 处理器）应放在 `FrameManage.Modular\FrameManage.ActionHandlers` 目录下，系统会自动扫描并注册这些处理器。

**处理器注册规则：**

| 规则 | 说明 |
|------|------|
| **命名约定** | 处理器类名需以 `ActionHandler` 结尾，如 `LoadGraphDataActionHandler` |
| **接口实现** | 必须实现 `IActionHandler` 接口，包含 `ActionName` 属性和 `Execute` 方法 |
| **自动扫描** | 系统启动时自动扫描 `FrameManage.ActionHandlers` 程序集 |
| **注册方式** | 通过依赖注入容器自动注册，无需手动配置 |

**处理器类示例结构：**

```csharp
public class LoadGraphDataActionHandler : IActionHandler
{
    public string ActionName => "loadgraphdata";
    
    public void Execute(HandlerParams param)
    {
        // 执行加载图数据逻辑
    }
}
```

**自动注册流程：**

```
程序启动
    │
    ▼
扫描 FrameManage.ActionHandlers 程序集
    │
    ▼
发现所有实现 IActionHandler 接口的类
    │
    ▼
提取 ActionName 属性
    │
    ▼
注册到 ActionHandlerFactory
    │
    ▼
UI事件触发时通过 ActionName 查找并执行对应处理器
```

### 2.5 快捷方式配置

**目标路径：**
```
FrameManage.exe load Config/GraphDatabaseManager.json GraphDatabaseManager show
```

**起始位置：**
```
FrameManage.Modular/FrameManage/bin/Release
```

---

## 三、具体函数运行

### 3.1 程序启动流程

```
程序启动
        │
        ▼
┌───────────────────────────────┐
│  扫描 FrameManage.ActionHandlers │
│  自动注册所有 IActionHandler    │
└───────────────┬───────────────┘
                │
                ▼
┌───────────────────┐
│ FrameManage.exe   │
└─────────┬─────────┘
          │
          ▼
┌───────────────────┐
│ RunCommandLineMode│
│   解析命令参数     │
└─────────┬─────────┘
          │
          ▼
┌───────────────────┐
│ ExecuteCommand    │
│ 判断命令类型      │
└─────────┬─────────┘
          │
     ┌────┴────┐
     │         │
     ▼         ▼
 "ui "开头   其他命令
     │         │
     ▼         ▼
ExecuteUI  UICommandManager
 Command   .ExecuteCommand
     │         │
     ▼         ▼
WinForms   LoadFormConfig
UIEngine   → CreateForm
.LoadConfig → RegisterForm
     │         │
     └────┬────┘
          │
          ▼
     form.Show()
     显示 UI 窗口
```

### 3.2 核心处理步骤

| 步骤 | 组件 | 方法 | 说明 |
|------|------|------|------|
| 0 | ActionHandlerFactory | `RegisterHandlers()` | **自动扫描 FrameManage.ActionHandlers，注册所有 IActionHandler** |
| 1 | Program.cs | `RunCommandLineMode()` | CLI 模式入口 |
| 2 | Program.cs | `ExecuteCommand()` | 命令分发 |
| 3 | UICommandManager.cs | `LoadFormConfig()` | 读取 JSON 配置文件 |
| 4 | UICommandManager.cs | `CreateForm()` | 动态创建 WinForms 表单 |
| 5 | UICommandManager.cs | `RegisterForm()` | 注册表单到控件缓存 |
| 6 | WinForms | `form.Show()` | 显示 UI 窗口 |

### 3.3 函数调用关系

```
程序启动
    │
    ▼
ActionHandlerFactory.RegisterHandlers()
    │
    └─→ 扫描 FrameManage.ActionHandlers 程序集
    │       └─→ 注册所有实现 IActionHandler 的类
    │               └─→ 建立 ActionName → Handler 映射
    │
    ▼
CLI命令输入
    │
    ▼
RunCommandLineMode()
    │
    ▼
ExecuteCommand(command)
    │
    ├─→ UI命令 → ExecuteUICommand() → WinForms UIEngine.LoadConfig()
    │
    └─→ 其他命令 → UICommandManager.ExecuteCommand()
                      │
                      ├─→ LoadFormConfig(configPath)
                      │       └─→ 读取JSON配置文件
                      │
                      ├─→ CreateForm(formConfig)
                      │       └─→ 动态创建WinForms控件
                      │               └─→ 绑定事件 → 通过ActionName查找Handler
                      │
                      ├─→ RegisterForm(formName, form)
                      │       └─→ 注册到控件缓存
                      │
                      └─→ ShowForm(formName)
                              └─→ form.Show()
```

### 3.4 Action 处理器调用流程

当 UI 事件触发时，通过以下流程调用对应处理器：

```
用户点击按钮 / 触发事件
    │
    ▼
获取事件配置中的 ActionName (如 "loadgraphdata")
    │
    ▼
ActionHandlerFactory.GetHandler(ActionName)
    │
    ▼
调用 Handler.Execute(HandlerParams)
    │
    ▼
执行具体业务逻辑
```

---

## 四、基于封装脚本的CLI_UI测试方案

### 4.1 测试脚本架构

基于 `Script\TEST` 目录的封装脚本实现，构建完整的CLI_UI测试体系：

```
测试脚本层
    │
    ├── ui-runner.ps1          # UI运行器入口
    ├── test_gui.ps1           # GUI测试脚本
    ├── test_ui_operations.ps1 # UI操作测试脚本
    └── read_database.ps1      # 数据库读取测试脚本
            │
            ▼
    CLI命令层
            │
            ├── load           # 加载配置
            ├── click          # 控件点击
            ├── set/get        # 控件值操作
            └── sleep          # 等待操作
                    │
                    ▼
    FrameManage.exe CLI模式
```

### 4.2 测试脚本封装实现

#### 4.2.1 UI运行器脚本 (ui-runner.ps1)

```powershell
Write-Host "=================================================="
Write-Host " FrameManage - UI运行器脚本"
Write-Host "=================================================="
Write-Host ""

# 启动UI运行器
dotnet run --project "FrameManage.Start" -- --ui-runner $args

Write-Host ""
Write-Host "UI运行器执行完成！"
```

#### 4.2.2 GUI测试脚本 (test_gui.ps1)

```powershell
Write-Host "=================================================="
Write-Host " FrameManage - GUI测试脚本"
Write-Host "=================================================="
Write-Host ""

# 启动GUI测试
dotnet run --project "FrameManage.Start" -- --gui-test

Write-Host ""
Write-Host "GUI测试完成！"
```

#### 4.2.3 UI操作测试脚本 (test_ui_operations.ps1)

```powershell
Write-Host "=================================================="
Write-Host " FrameManage - UI操作测试"
Write-Host "=================================================="
Write-Host ""

# 启动CLI并执行命令
$cliPath = "dotnet run --project `"FrameManage.Start`" -- --cli"

# 创建临时命令文件
$commands = @(
    "load d:\Project\Workflow_\FrameManage.Modular\FrameManage.Start\Config\DataManageDatabaseForm.json DatabaseForm show",
    "list",
    "set txtStoragePath d:\Project\Workflow_\DataManage\DataBase\MultiDimensionalWorkflowGraph.db",
    "get txtStoragePath",
    "click btnLoadData",
    "sleep 2000",
    "get lblStatus",
    "get lblTotal"
)

$commands | Out-File -FilePath "temp_commands.txt" -Encoding utf8

# 通过管道传递命令
Get-Content "temp_commands.txt" | dotnet run --project "FrameManage.Start" -- --cli

# 清理临时文件
Remove-Item "temp_commands.txt" -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "UI操作测试完成！"
```

### 4.3 命令序列文件测试

#### 4.3.1 命令序列文件格式 (db_query_commands.txt)

```bash
# 测试命令序列
load Config/GraphDatabaseManager.json GraphDatabaseManager show
list
set txtGraphPath DataManage/DataBase/Graphdb/MultiDimensionalWorkflowGraph.db
get txtGraphPath
click btnLoadGraph
sleep 2000
get lblNodeCount
get lblEdgeCount
snapshot GraphDatabaseManager
```

#### 4.3.2 命令序列执行方式

```bash
# 方式一：通过--file参数加载
FrameManage.exe --file db_query_commands.txt

# 方式二：通过管道传递
Get-Content db_query_commands.txt | FrameManage.exe --cli

# 方式三：dotnet run方式
Get-Content db_query_commands.txt | dotnet run --project "FrameManage.Start" -- --cli
```

### 4.4 自动化测试流程

#### 4.4.1 完整测试流程

```
测试开始
    │
    ▼
启动UI运行器 (ui-runner.ps1)
    │
    ▼
加载配置文件 (load命令)
    │
    ▼
执行控件操作序列
    │   ├─→ set 控件赋值
    │   ├─→ click 按钮点击
    │   ├─→ get 结果验证
    │   └─→ sleep 等待操作
    │
    ▼
获取测试结果
    │
    ▼
生成测试报告
    │
    ▼
测试结束
```

#### 4.4.2 测试结果验证示例

```powershell
# 验证加载状态
$status = "get lblStatus" | dotnet run --project "FrameManage.Start" -- --cli
if ($status -eq "加载成功") {
    Write-Host "测试通过！" -ForegroundColor Green
} else {
    Write-Host "测试失败！状态: $status" -ForegroundColor Red
}

# 验证节点数量
$nodeCount = "get lblNodeCount" | dotnet run --project "FrameManage.Start" -- --cli
if ($nodeCount -ge 1) {
    Write-Host "节点数验证通过: $nodeCount" -ForegroundColor Green
}
```

### 4.5 跨平台脚本支持

| 平台 | 脚本文件 | 执行方式 |
|------|----------|----------|
| Windows (CMD) | `read_database.cmd` | `read_database.cmd` |
| Windows (PowerShell) | `read_database.ps1`, `test_ui_operations.ps1` | `powershell .\read_database.ps1` |
| Linux/Mac | `read_database.sh` | `./read_database.sh` |

#### 批处理脚本示例 (read_database.cmd)

```batch
@echo off
echo ==================================================
echo FrameManage - 数据库读取测试
echo ==================================================
echo.

cd /d d:\Project\Workflow_\FrameManage.Modular
FrameManage\bin\Debug\net10.0-windows\FrameManage.exe --file Tools\db_query_commands.txt

echo.
echo 数据库读取测试完成！
pause
```

### 4.6 交互式测试模式

```bash
# 进入交互式CLI模式
FrameManage.exe --cli

# 交互式命令示例
> load Config/GraphDatabaseManager.json GraphDatabaseManager show
> list
> set txtGraphPath Data/Graph.db
> click btnLoadGraph
> sleep 1000
> get lblNodeCount
> exit
```

### 4.7 测试脚本目录结构

```
Script/
├── TEST/
│   ├── README.md                  # 脚本说明文档
│   ├── ui-runner.ps1              # UI运行器脚本
│   ├── ui-runner.bat              # UI运行器批处理
│   ├── test_gui.ps1               # GUI测试脚本
│   ├── test_gui.bat               # GUI测试批处理
│   ├── test_ui_operations.ps1     # UI操作测试脚本
│   ├── read_database.ps1          # PowerShell数据库读取脚本
│   ├── read_database.cmd          # CMD批处理脚本
│   ├── read_database.bat          # 批处理脚本
│   ├── read_database.sh           # Linux/Mac Shell脚本
│   ├── advanced_query.cmd         # 高级查询工具
│   ├── load_database.ps1          # 数据库加载脚本
│   ├── load_database.bat          # 数据库加载批处理
│   ├── Read-Database.ps1          # PowerShell读取脚本
│   └── db_query_commands.txt      # 命令序列文件
```

---

## 五、配置验证与测试命令

### 5.1 配置验证命令

```bash
# 验证配置文件语法和结构
FrameManage.exe --validate <configPath>

# 检查事件配置完整性
FrameManage.exe --check-events <configPath>

# 列出所有注册的 Action 处理器
FrameManage.exe --list-handlers

# 详细验证模式（显示所有警告和建议）
FrameManage.exe --validate --verbose <configPath>
```

### 5.2 验证输出示例

```bash
$ FrameManage.exe --validate Config/GraphReferenceManager.json
[INFO] Validating configuration: Config/GraphReferenceManager.json
[INFO] Form.Name: GraphReferenceManager ✓
[INFO] Controls count: 1 ✓
[WARN] Button 'btnGenerateId' has no Action configured in Events
[WARN] Consider adding "Action" property to event configuration
[INFO] Validation completed with 0 errors, 1 warning
```

### 5.3 测试前检查清单

在运行测试前，建议执行以下检查：

| 检查项 | 命令 | 说明 |
|--------|------|------|
| 配置文件存在 | `Test-Path Config/xxx.json` | 验证配置文件路径正确 |
| 配置格式验证 | `FrameManage.exe --validate` | 验证JSON结构和必填字段 |
| 事件配置检查 | `FrameManage.exe --check-events` | 检查按钮事件配置完整性 |
| 处理器注册检查 | `FrameManage.exe --list-handlers` | 确认所有Action处理器已注册 |
| 数据库文件检查 | `Test-Path Data/*.db` | 验证数据库文件存在 |

---

## 六、注意事项

### 6.1 路径问题

- **相对路径**：相对于程序运行目录（`FrameManage.exe` 所在目录）
- **绝对路径**：完整路径，建议使用双引号包裹（如包含空格）

### 6.2 配置文件校验

确保配置文件格式正确：
- JSON 语法正确
- `Form` 部分必填
- `Controls` 数组中的每个控件必须包含 `Type` 和 `Name`
- 按钮的 `Events` 配置建议包含 `Action` 属性

### 6.3 事件配置格式

支持两种事件配置格式：

```json
// 格式一：直接 Action 属性（推荐）
{
  "EventName": "click",
  "Action": "generategraphid"
}

// 格式二：HandlerParams（兼容旧版本）
{
  "EventName": "click",
  "HandlerType": "custom",
  "HandlerParams": {
    "Action": "generategraphid"
  }
}
```

### 6.4 权限要求

- 程序需要有读取配置文件目录的权限
- 如果涉及文件写入操作，需要有相应目录的写入权限

### 6.5 命令执行顺序

在批量命令文件中，命令按顺序执行，建议在需要等待的操作后添加 `sleep` 命令。

### 6.6 脚本执行注意事项

- PowerShell 脚本需要执行权限：`Set-ExecutionPolicy RemoteSigned`
- 命令序列文件编码需为 UTF-8
- 路径中包含空格时需使用引号包裹

---

**版本**: v1.2  
**日期**: 2026-05-07  
**适用范围**: FrameManage CLI 模式及封装脚本测试