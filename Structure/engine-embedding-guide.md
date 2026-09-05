# Engine（GameEditor）嵌入式构建到 FileDriven WinForms 指南

> 目的：把 `Engine/GameEditor`（虚拟矩阵 + ECS + D3D11 渲染引擎）作为可交互能力嵌入
> `FileDriven` 的 WinForms 宿主，完全对齐 `Structure/skills` 的三段式流水线：
> ① 注册函数（C#）→ ② 创建 JSON 配置（UI/工作流）→ ③ 创建运行脚本。
> 版本基线：net10.0-windows / WinForms / D3D11 后端 / `TConfig.GameMath` 已归位。

---

## 0. 结论速览

**可以嵌入，而且很顺**。关键依据：

1. **D3D11 交换链可绑定任意 HWND** —— `D3D11RenderDevice.CreateSwapChain(IntPtr windowHandle, w, h)`
   内部走 `CreateSwapChainForHwnd`，只要把 WinForms 控件（Panel/UserControl）的 `.Handle` 传进去，
   引擎就渲染到 WinForms 控件内部，**不需要**引擎自带的 `NativeWindow`（Win32 顶层窗口）。
2. **Engine 已有单一聚拢点 `EngineHinge`**（`GameEditor.Scene/Runtime/EngineHinge.cs`）：
   `UpdateFrame(FrameInput)` / `RenderSlice()` / `RayMarchFrame(CameraPose)` / `Shutdown()`，
   嵌入时宿主只需调这 4 个方法，跨层调用全部收敛，无需了解 ECS/Graphics/VirtualMatrix 内部。
3. **两边共享同一套 CsharpPrj 底层**：FileDriven.Business 与 Engine 各 csproj 都引用
   `BasicMethod / Modules / Share / Operation / Plate(TConfig)`；`TConfig.GameMath`
   （Vec3/Color/Matrix4x4/SlicePlane/Aabb）已落地为单一事实源，**无数学类型冲突**。
4. **跨程序集注册已有先例**：`AgentUiLibrary`（`Agent.Launcher.AgentUiLibrary, Agent.Launcher`）
   就是"外部程序集注册库 + function-libraries.json 显式登记 + 宿主 ProjectReference 保证 DLL 进输出目录"，
   Engine 完全照抄这条路径。

**嵌入形态一句话**：Engine 作为被引用的程序集（不保留自己的 Exe 入口），由 FileDriven 宿主创建
`EngineStateCenter + EnginePipeline`（跳过 `WindowInitStage` 的窗口创建），把 D3D11 交换链绑定到
一个自定义 WinForms 渲染控件（`enginecanvas`），帧循环由 WinForms 消息泵（`Application.Idle`）驱动，
渲染控制通过注册函数 + JSON UI + 工作流暴露。

---

## 1. 现状盘点（两边关键事实）

### 1.1 FileDriven 侧（宿主 / WinForms）

| 项目 | 作用 | 关键点 |
|------|------|--------|
| `FileDriven.Launcher` | 宿主入口（WinExe） | `Program.cs → FileDrivenExecutor.Run(args)`；WinForms；引用 Execution + Registry + Mathematics |
| `FileDriven.Business` | 业务注册库所在 | `Registrations/{BuiltIn,Cline,Math,UI,Workflow}`；`[FunctionLibrary]` + `RegisterAll(ITrConfigRegistry)` |
| `FileDriven.Execution` | 执行骨架 | `FileDrivenExecutor`（9 步骨架）→ `FrameworkInitializer`（6 组件）→ GUI 模式 |
| `DrivenConfig/Configs/function-libraries.json` | 注册清单 | `type` 用程序集限定名；跨程序集库显式登记（§1.4 三条件） |
| `DrivenConfig/JsonConfig/*.json` | UI 配置 | `form` + `views[].controls[]`；`type` 映射控件 |
| `DrivenConfig/WorkFlow/**/*.json` | 工作流 | 步骤编排：`stepName/funcName/inputKeys/outputKey` |
| `DrivenConfig/Scripts/**/*.json` | 脚本 | `exec/set/click/get` 冒烟与回归 |

UI 控件创建链：`ConfigDrivenControlFactory`（JSON → ECS 实体）→ `ControlStrategyRegistry.Create<Control>(type)`
→ 工厂来自 `ControlTypeRegistry.ControlFactories`（静态注册 ~30 种：button/label/panel/picturebox/webbrowser/...）。
**`ControlStrategyRegistry.Register(string, Func<object>)` 是公开的运行时注册入口**，是新增自定义渲染控件的扩展点。

### 1.2 Engine 侧（被嵌入）

| 项目 | 作用 | 嵌入要点 |
|------|------|----------|
| `GameEditor.Graphics` | 渲染抽象接口 | `IRenderDevice/IRenderContext/ISwapChain/ITexture/...` |
| `GameEditor.Graphics.D3D11` | D3D11 后端 | **`D3D11RenderDevice.CreateSwapChain(hwnd,w,h)` → `CreateSwapChainForHwnd`**（嵌入根基） |
| `GameEditor.VirtualMatrix` | 函数化体素世界 | `IVirtualMatrix.GetMacroState/BaseGen/Edits`；`MacroResponseTable` 单一事实源 |
| `GameEditor.ECS` | 实体组件系统 | `TransformSystem/SceneManager` |
| `GameEditor.Scene` | 运行时聚合 | **`Runtime/EngineHinge.cs`**（端口 + DTO + Bind + 帧编排）；`Pipeline/Stages/*` 7 个初始化阶段 |
| `GameEditor.Windowing` | Win32 顶层窗口 | **嵌入时不用**（改由 WinForms 控件提供 HWND） |
| `GameEditor.Configuration` | 启动配置 | `EngineStartupConfig`（三级合并：默认值 ← json ← 命令行） |
| `GameEditor.Standalone` | 独立入口（Exe） | **嵌入时不引用**；其 `Program.cs` 是"如何组装 + 驱动"的权威参考 |

Engine 初始化管线（Standalone Program.cs 现成代码，嵌入时复用）：

```
EngineStateCenter
  └─ EnginePipeline("Startup")
       ├─ BackendInitStage(seed, chunkSize)      # VirtualMatrix 后端
       ├─ SceneManagerInitStage()                # ECS 场景
       ├─ WindowInitStage(w,h,title)             # ← 嵌入时替换：改为"外部 HWND 注入"
       ├─ DeviceInitStage()                      # D3D11 设备/工厂
       ├─ ResourceInitStage(rtW,rtH)             # 渲染目标/纹理
       ├─ ShaderInitStage()                      # HLSL 编译
       └─ GPUEvaluatorInitStage(rtW,rtH)         # GPU 光线步进
  └─ EngineHinge.Bind(center)                    # 唯一接线点
```

帧循环（Standalone 用 `StaticPipeline<RenderFrameContext>` + `while(window.IsAlive())`）——
嵌入时改为 WinForms `Application.Idle` 驱动，只调 `hinge.UpdateFrame / RayMarchFrame / RenderSlice`。

---

## 2. 嵌入架构（三段式映射）

```
┌──────────────────────── FileDriven.Launcher (WinExe / WinForms) ───────────────────────┐
│  MainForm(MainLayout)                                                                  │
│   ├─ enginecanvas ──► EngineRenderControl (自定义 UserControl，持有 EngineHost 快照)     │
│   └─ 按钮 ──click──► 工作流 ──► 注册函数 ──► EngineHost ──► EngineHinge                  │
│                        │                    ▲                    │                      │
│                        ▼                    │                    ▼                      │
│  DrivenConfig/JsonConfig/EngineViewer.json  │          EngineStateCenter                │
│  DrivenConfig/WorkFlow/Engine/*.json        │          EnginePipeline(7 Stage, 去 Window)│
│  DrivenConfig/Scripts/Engine/*.json         │          D3D11 SwapChain ⇄ viewport.Handle │
└─────────────────────────────────────────────┼──────────────────────────────────────────┘
                                              │ ProjectReference
┌─────────────────────────────────────────────┼──────────────────────────────────────────┐
│  Engine 程序集（被引用，不保留 Exe 入口）          │                                          │
│  GameEditor.Scene / Graphics / Graphics.D3D11 / VirtualMatrix / ECS / Configuration      │
│  Runtime/EngineHinge.cs（UpdateFrame/RenderSlice/RayMarchFrame/Shutdown）                │
│  TConfig.GameMath（与 FileDriven 共用同一 TConfig 程序集）                                │
└─────────────────────────────────────────────────────────────────────────────────────────┘
```

对应 skills 三段模型：

| 段 | 交付物 | 位置 | 本场景落地 |
|----|--------|------|-----------|
| ① 注册 | `EngineHost.cs` + `EngineLibrary.cs`（`[FunctionLibrary]`） | `Engine/GameEditor.Host/`（新项目）或 `FileDriven.Business/Registrations/Engine/` | `Engine_Start / Engine_Stop / Engine_SetCamera / Engine_RayMarchStep / Engine_RenderSlice / Engine_GetStatus` |
| ② 配置 | `JsonConfig/EngineViewer.json` + `WorkFlow/Engine/EngineViewerWorkflow.json` | `FileDriven/DrivenConfig/` | `enginecanvas` 画布控件 + 按钮 → 工作流 |
| ③ 脚本 | `Scripts/Engine/engine_smoke_test.json` | `FileDriven/DrivenConfig/Scripts/` | exec Engine_Start → exec Engine_RayMarchStep → get viewport |

---

## 3. 实施步骤

### 3.1 ① 注册层（C#）

**3.1.1 新建宿主组合根 `EngineHost`**（单例，仿 AgentUiHost 的"组合根反转"：装配决策归宿主，UI 层只读快照）

```csharp
// Engine/GameEditor.Host/EngineHost.cs（新项目 GameEditor.Host，WinForms 依赖）
public sealed class EngineHost : IDisposable
{
    public static EngineHost Instance { get; } = new();      // 宿主进程内单例

    private EngineStateCenter? _center;                       // 引擎级资源容器
    private EngineHinge? _hinge;                              // 聚拢点
    private IntPtr _targetHwnd;                               // WinForms 控件 Handle
    private bool _started;

    /// <summary>在 WinForms 控件 Handle 上启动引擎（懒初始化，等价 Standalone 的 7 阶段管线，去掉 WindowInitStage）。</summary>
    public bool Start(IntPtr controlHwnd, int width, int height, int seed = 7, string mode = "RayMarch")
    {
        if (_started) return true;
        _targetHwnd = controlHwnd;

        var center = new EngineStateCenter();
        var pipeline = new EnginePipeline("Startup")
            .Register(new BackendInitStage(seed: seed, chunkSize: 16))
            .Register(new SceneManagerInitStage())
            // ★ 不注册 WindowInitStage —— 窗口由 WinForms 提供
            .Register(new DeviceInitStage())
            .Register(new ResourceInitStage(rtWidth: width, rtHeight: height))
            .Register(new ShaderInitStage())
            .Register(new GPUEvaluatorInitStage(rtWidth: width, rtHeight: height));

        var context = new EnginePipelineContext(center.Center);
        pipeline.ExecuteWithDiagnosticsAsync(context).GetAwaiter().GetResult();   // 首次调用同步完成
        if (context.HasError) return false;

        // ★ 用 WinForms 控件 HWND 创建交换链（替代 WindowInitStage 内部创建的 NativeWindow）
        var device  = center.Read<IRenderDevice>(EngineStateKeys.RenderDevice);
        var swap    = device.CreateSwapChain(_targetHwnd, width, height);
        center.Write(EngineStateKeys.SwapChain, swap);

        _center = center;
        _hinge  = EngineHinge.Bind(center.Center);
        _started = true;
        return true;
    }

    public void Resize(int width, int height)
    {
        if (_center?.Read<ISwapChain>(EngineStateKeys.SwapChain) is D3D11SwapChain d3d &&
            _center.Read<IRenderContext>(EngineStateKeys.RenderContext) is D3D11RenderContext ctx)
        {
            d3d.Resize(width, height);                                  // 现成逻辑（Standalone Program.cs 有同款）
            using var backBuffer = d3d.GetBackBuffer();
            ctx.SetRenderTarget(backBuffer);
            ctx.SetViewport(0, 0, width, height);
        }
    }

    public void Frame(float dt, in CameraPose pose) => _hinge?.RayMarchFrame(pose);  // 或 RenderSlice()
    public string GetStatus() => _hinge == null ? "stopped" : "running";
    public void Dispose() { _hinge?.Shutdown(); _hinge?.Dispose(); _center?.Shutdown(); _started = false; }
}
```

> 注意：`EngineStateKeys.SwapChain` 写入发生在 `DeviceInitStage` 之前会怎样？—— 顺序无碍，
> 只要在 `EngineHinge.Bind` 之前把 `SwapChain` 写入 StateCenter 即可（Hinge.Bind 从 center 读资源）。
> 若 `DeviceInitStage` 内部已创建过交换链（需要查证），则改为"注入外部 HWND"参数，让 Stage 直接用
> `controlHwnd` 建交换链，避免二次创建。

**3.1.2 新建注册库 `EngineLibrary`**（对齐 01-register-function.md §1.2 模板）

```csharp
// Engine/GameEditor.Host/EngineLibrary.cs
using Modules.Registry;
using TConfig;

namespace GameEditor.Host;

[FunctionLibrary]
public static class EngineLibrary
{
    public static void RegisterAll(ITrConfigRegistry registry)
    {
        registry.Register("Engine_Start",
            (Func<int, string, string>)((seed, mode) =>
                EngineHost.Instance.Start(ControlHost.GetViewportHandle(), 800, 600, seed, mode)
                    ? "engine started" : "engine start failed"),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "seed",  Type = typeof(int), IsOptional = true, DefaultValue = 7 },
                new ParameterDescriptor { Name = "mode",  Type = typeof(string), IsOptional = true, DefaultValue = "RayMarch" }
            },
            "在视口控件上启动引擎");

        registry.Register("Engine_Stop", (Func<string>)(() => { EngineHost.Instance.Dispose(); return "engine stopped"; }),
            "停止并释放引擎资源");

        registry.Register("Engine_SetCamera",
            (Func<double, double, double, double, double, double, string>)((x, y, z, yaw, pitch, fov) =>
            { EngineHost.Instance.SetCamera(x, y, z, yaw, pitch, fov); return $"camera {x},{y},{z}"; }),
            new IParamConfig[] { /* x/y/z/yaw/pitch/fov 描述符 */ },
            "设置相机姿态");

        registry.Register("Engine_RayMarchStep", (Func<string>)(() => EngineHost.Instance.StepRayMarch() ? "ok" : "idle"),
            "推进一帧 GPU 光线步进渲染");

        registry.Register("Engine_GetStatus", (Func<string>)(() => EngineHost.Instance.GetStatus()), "查询引擎状态");
    }
}
```

**3.1.3 csproj 接线（跨程序集注册三条件，对齐 01-register-function.md §1.4）**

在 `FileDriven.Business.csproj` 增加：

```xml
<!-- Engine 引擎（跨程序集注册库：使 Engine DLL 进入输出目录，function-libraries.json 可 Type.GetType 解析） -->
<ProjectReference Include="..\..\Engine\GameEditor.Scene\GameEditor.Scene.csproj" />
<ProjectReference Include="..\..\Engine\GameEditor.Graphics\GameEditor.Graphics.csproj" />
<ProjectReference Include="..\..\Engine\GameEditor.Graphics.D3D11\GameEditor.Graphics.D3D11.csproj" />
<ProjectReference Include="..\..\Engine\GameEditor.VirtualMatrix\GameEditor.VirtualMatrix.csproj" />
<ProjectReference Include="..\..\Engine\GameEditor.Configuration\GameEditor.Configuration.csproj" />
<ProjectReference Include="..\..\Engine\GameEditor.Host\GameEditor.Host.csproj" />
```

> ① `GameEditor.Host` 需引用 `Modules/Registry`（`[FunctionLibrary]`）、`Plate/TConfig`
> （`ITrConfigRegistry`）—— Engine/Directory.Build.props 已提供 `$(ModulesCsproj)/$(PlateCsproj)`；
> ② 清单 type 用 `GameEditor.Host.EngineLibrary, GameEditor.Host`；
> ③ 宿主（FileDriven.Launcher 传递依赖）能解析该程序集 —— 上面的 ProjectReference 即满足。

在 `function-libraries.json` 追加：

```json
{ "name": "EngineLibrary", "type": "GameEditor.Host.EngineLibrary, GameEditor.Host", "enabled": true }
```

### 3.2 ② 配置层（JSON）

**3.2.1 注册 `enginecanvas` 渲染控件**

优先走公开扩展点 `ControlStrategyRegistry.Register`（无需改 CsharpPrj 共享代码）：

```csharp
// 放在 EngineHost 首次 Start 前（或 FileDriven 框架初始化阶段、或注册库内首次调用时）
ControlStrategyRegistry.Register("enginecanvas", () => new EngineRenderControl());
```

```csharp
// GameEditor.Host/EngineRenderControl.cs —— 自定义 UserControl
public sealed class EngineRenderControl : UserControl
{
    public EngineRenderControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque
               | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Resize += (_, _) => EngineHost.Instance.Resize(Math.Max(1, Width), Math.Max(1, Height));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ControlHost.SetViewportHandle(Handle);        // 静态桥：把控件 HWND 暴露给注册函数
        if (EngineHost.Instance.NeedStartOnHandle)     // 若 UI 配置约定"创建即启动"
            EngineHost.Instance.Start(Handle, Width, Height);
    }
}
```

> 若希望 JSON 校验/schema 也认识该类型（编辑期友好），可顺带在 `ControlTypeRegistry.RegisterAll()`
> 增加一行 `RegisterContainer("enginecanvas", () => new EngineRenderControl())`（需改 CsharpPrj，影响所有宿主）。
> 最小可行路径：只用 `ControlStrategyRegistry.Register`，叶子控件（无子控件）即可工作。

**3.2.2 UI 配置 `JsonConfig/EngineViewer.json`**（对齐 02-json-ui.md）

```json
{
  "description": "引擎查看器 - 虚拟矩阵渲染嵌入示例",
  "initCommands": [
    "exec EngineViewerWorkflow _stepSelector=Start"
  ],
  "form": { "title": "Engine Viewer", "width": 1000, "height": 720 },
  "views": [{
    "name": "MainView", "type": "Panel",
    "controls": [
      {
        "type": "enginecanvas", "name": "viewport",
        "layout": { "left": 0.02, "top": 0.08, "width": 0.96, "height": 0.78 }
      },
      {
        "type": "button", "name": "startButton", "text": "启动引擎",
        "events": {
          "click": "EngineViewerWorkflow",
          "bindings": {
            "parameters": [
              { "target": "_stepSelector", "source": "Start", "sourceType": "literal" }
            ],
            "output": "statusLabel"
          }
        },
        "layout": { "left": 0.02, "top": 0.9, "width": 0.12, "height": 0.05 }
      },
      {
        "type": "button", "name": "stopButton", "text": "停止",
        "events": {
          "click": "EngineViewerWorkflow",
          "bindings": {
            "parameters": [ { "target": "_stepSelector", "source": "Stop", "sourceType": "literal" } ],
            "output": "statusLabel"
          }
        },
        "layout": { "left": 0.15, "top": 0.9, "width": 0.12, "height": 0.05 }
      },
      {
        "type": "textbox", "name": "statusLabel", "readOnly": true,
        "layout": { "left": 0.55, "top": 0.9, "width": 0.43, "height": 0.05 }
      }
    ]
  }]
}
```

**3.2.3 工作流 `WorkFlow/Engine/EngineViewerWorkflow.json`**（对齐 03-json-workflow.md）

```json
{
  "workflowName": "EngineViewerWorkflow",
  "displayName": "引擎查看器工作流",
  "defaultInput": { "seed": "7", "mode": "RayMarch" },
  "steps": [
    { "stepName": "Start", "funcName": "Engine_Start", "inputKeys": ["seed", "mode"], "outputKey": "result", "parameters": ["${seed}", "${mode}"] },
    { "stepName": "Stop",  "funcName": "Engine_Stop",  "outputKey": "result", "parameters": [] }
  ],
  "outputMapping": { "result": "result" }
}
```

### 3.3 ③ 脚本层（回归/冒烟）

`Scripts/Engine/engine_smoke_test.json`（对齐 04-json-script.md）：

```json
{
  "description": "引擎嵌入冒烟测试",
  "script": "engine_smoke_test",
  "commands": [
    { "template": "echo-section", "title": "测试 Engine_Start" },
    { "action": "exec", "target": "Engine_Start", "value": "seed=7", "output": "statusLabel" },
    { "action": "get", "target": "statusLabel" },
    { "template": "echo-section", "title": "测试 Engine_RayMarchStep" },
    { "action": "exec", "target": "Engine_RayMarchStep", "output": "statusLabel" },
    { "action": "get", "target": "statusLabel" },
    { "action": "sleep", "value": "2000" },
    { "action": "exec", "target": "Engine_Stop", "output": "statusLabel" },
    { "action": "get", "target": "statusLabel" }
  ]
}
```

### 3.4 帧循环驱动（嵌入的灵魂）

WinForms 已有消息泵，**不能**用 Standalone 的 `while(window.IsAlive())` + `PeekMessage`。
在 MainForm 挂载空闲事件：

```csharp
// MainForm 中（或 EngineRenderControl 内部）
public void AttachEngineLoop()
{
    Application.Idle += (_, _) =>
    {
        if (!EngineHost.Instance.IsRunning) return;
        float dt = (float)_deltaWatch.Elapsed.TotalSeconds; _deltaWatch.Restart();
        var pose = new CameraPose(EngineHost.Instance.CameraPosition,
                                  EngineHost.Instance.CameraYaw,
                                  EngineHost.Instance.CameraPitch,
                                  EngineHost.Instance.Fov);
        EngineHost.Instance.Frame(dt, pose);   // → hinge.UpdateFrame + RayMarchFrame/RenderSlice
    };
}
```

备选：`System.Windows.Forms.Timer`（~60fps）。`Application.Idle` 是经典 game loop 模式，
UI 不卡、帧率不封顶。

### 3.5 生命周期

| 时机 | 动作 |
|------|------|
| 首次 `Engine_Start`（按钮/initCommands） | `EngineHost.Start(viewport.Handle, ...)`：7 阶段管线（去 Window）→ 外部 HWND 建交换链 → `EngineHinge.Bind` |
| 窗体 Resize | `EngineHost.Resize(w, h)`（控件 Resize 事件） |
| 窗体关闭（`FormClosing`） | `EngineHost.Dispose()` → `hinge.Shutdown()` → `stateCenter.Shutdown()`（逆序释放，对齐 Standalone） |
| 重复 Start | 幂等：已启动直接返回 |

---

## 4. 关键技术点与坑（对照 skills §5.2）

| # | 主题 | 说明 |
|---|------|------|
| 1 | **HWND 绑定** | `CreateSwapChainForHwnd` 接受任意 HWND；`Panel/UserControl.Handle` 在 `OnHandleCreated` 后有效；D3D11 设备必须创建在控件 Handle 所在线程（WinForms UI 线程） |
| 2 | **消息泵冲突** | Engine 自带 `NativeWindow`/`PeekMessage` 循环与 WinForms 冲突；嵌入必须去掉 `WindowInitStage` 与 `while(IsAlive)`，改 `Application.Idle` |
| 3 | **资源生命周期** | EngineStateCenter 随宿主进程；关闭时逆序 Shutdown；`EngineHost` 单例保证多次注册调用幂等 |
| 4 | **Resize** | 复用 Standalone Program.cs 现成的 `Resized` 处理：`d3dSwap.Resize(w,h)` + `SetRenderTarget(backBuffer)` + `SetViewport` |
| 5 | **输入** | Standalone 用 `GetAsyncKeyState` 全局读（不依赖焦点），嵌入后可直接复用；或改控件 `KeyDown` → `FrameInput` |
| 6 | **TConfig 唯一性** | `TConfig.GameMath` 已归位；Engine 与 FileDriven 共用同一 `CsharpPrj/Plate/TConfig` 程序集，无 Vec3/Color 二义性（对照 EngineHinge_Design.md 阶段 A 已完成） |
| 7 | **输出目录** | FileDriven 输出 `FileDriven/BuildOutput/`，Engine 输出 `Engine/BuildOutput/`；被 ProjectReference 后引擎 DLL 会复制到 FileDriven 输出目录，正常 |
| 8 | **重复编译 CsharpPrj** | Engine 与 FileDriven 各自引用同一批 CsharpPrj 项目，构建时各自编译一份 DLL 到各自输出目录；版本一致（同一源码），无冲突，但注意两边 `Directory.Build.props` 路径变量不同（Engine 用 `$(CsharpRootDir)`，FileDriven 用 `..\CsharpPrj`） |
| 9 | **渲染线程** | 渲染必须发生在 UI 线程（设备创建线程）上，`Application.Idle` 天然满足；**不要**丢后台线程渲染同一设备（D3D11 非多线程上下文） |
| 10 | **控件类型注册** | 优先 `ControlStrategyRegistry.Register("enginecanvas", ...)`（公开 API，不动 CsharpPrj）；如需 schema 校验支持再加 `ControlTypeRegistry.RegisterAll()` 一行（影响所有宿主） |

---

## 5. 变更文件清单

**新增**
- `Engine/GameEditor.Host/GameEditor.Host.csproj`（WinForms 依赖，引用 Modules/Registry、Plate/TConfig、GameEditor.Scene 等）
- `Engine/GameEditor.Host/EngineHost.cs`（组合根单例：Start/Resize/Frame/Dispose）
- `Engine/GameEditor.Host/EngineLibrary.cs`（`[FunctionLibrary]` 注册库）
- `Engine/GameEditor.Host/EngineRenderControl.cs`（`enginecanvas` 控件 + `ControlHost` 静态桥）
- `FileDriven/DrivenConfig/JsonConfig/EngineViewer.json`（UI）
- `FileDriven/DrivenConfig/WorkFlow/Engine/EngineViewerWorkflow.json`（工作流）
- `FileDriven/DrivenConfig/Scripts/Engine/engine_smoke_test.json`（脚本）

**修改**
- `FileDriven/FileDriven.Business/FileDriven.Business.csproj`（+6 个 Engine ProjectReference）
- `FileDriven/DrivenConfig/Configs/function-libraries.json`（+EngineLibrary 登记）

**不动的**
- `Engine/GameEditor.Standalone/`（保留独立运行模式，与嵌入并行）
- `Engine/GameEditor.Windowing/NativeWindow.cs`（嵌入路径不用，独立路径继续用）
- CsharpPrj 共享代码（若只用 `ControlStrategyRegistry.Register` 扩展点）

---

## 6. 验收标准

1. `dotnet build FileDriven.sln` 零错误，`GameEditor.Host.dll` 进入 FileDriven 输出目录。
2. 启动 `--config EngineViewer.json --show`，视口控件内出现虚拟矩阵渲染画面（ray march 或 slice）。
3. 按钮/工作流可启停引擎、改相机；状态文本框回显。
4. `--script Scripts/Engine/engine_smoke_test.json` 冒烟通过。
5. 窗体缩放画面跟随（Resize 联动），关闭窗体资源逆序释放无异常。
6. 原有能力（Math/Cline/Agent）不受影响 —— Engine 以纯增量程序集 + 清单条目接入。
