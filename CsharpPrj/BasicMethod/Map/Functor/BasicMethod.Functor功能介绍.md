# BasicMethod.Functor — 函子框架

> **定位**：BasicMethod.Map 的第 9 个并行模块  
> **数学根基**：范畴论 — 函子理论（Functor Theory）  
> **核心公理**：`F(id) = id`，`F(g∘f) = F(g)∘F(f)`  
> **设计哲学**：中间层零修改，值自动传播  
> **变换模式**：A → A（恒等传播，值不变）

---

## 1. 概述

`BasicMethod.Functor` 是一个基于**函子理论**的通用跨层值传递框架。它解决了跨项目/跨模块调用链中，值从 Source 端传递到 Sink 端需要修改所有中间层的问题。

### 问题

传统方式下，每新增一个跨层传递的值，所有中间层都需要新增属性 + 赋值操作，复杂度 = O(n)：

```mermaid
graph TD
    subgraph 传统方式["❌ 传统方式：每层需修改"]
        direction LR
        S1[Source] --> L1["Layer₁<br/>+ 属性 + 赋值"]
        L1 --> L2["Layer₂<br/>+ 属性 + 赋值"]
        L2 --> Dots["..."]
        Dots --> Ln["Layerₙ<br/>+ 属性 + 赋值"]
        Ln --> K[Sink]
    end

    style S1 fill:#e1f5fe,stroke:#01579b
    style L1 fill:#ffcdd2,stroke:#b71c1c
    style L2 fill:#ffcdd2,stroke:#b71c1c
    style Dots fill:#fff9c4,stroke:#f57f17
    style Ln fill:#ffcdd2,stroke:#b71c1c
    style K fill:#e1f5fe,stroke:#01579b
```

### 解决方案

基于函子理论：**每个中间层对值的操作都是恒等函子** `F(v) = v`。通过 AsyncLocal 隐式传播，中间层无需任何修改：

```mermaid
graph TD
    subgraph Functor方式["✅ Functor 方式：中间层零修改"]
        direction LR
        S2[Source<br/>设置值] --> F1["Layer₁<br/>自动传播"]
        F1 --> F2["Layer₂<br/>自动传播"]
        F2 --> FDots["..."]
        FDots --> Fn["Layerₙ<br/>自动传播"]
        Fn --> K2[Sink<br/>获取值]
    end

    style S2 fill:#e1f5fe,stroke:#01579b
    style F1 fill:#c8e6c9,stroke:#2e7d32
    style F2 fill:#c8e6c9,stroke:#2e7d32
    style FDots fill:#fff9c4,stroke:#f57f17
    style Fn fill:#c8e6c9,stroke:#2e7d32
    style K2 fill:#e1f5fe,stroke:#01579b
```

### 与 Map 其他模块的变换模式对比

| 模块 | 变换模式 | 语义 | 维度 |
|------|---------|------|------|
| **Transformers** | A → B | 跨类型转换 | 横向（同层） |
| **Functor** | A → A | 恒等传播（值不变） | 纵向（跨层） |
| **Calculus** | T → T | 结构变换（值变化） | 横向（同层） |

**核心区别**：
- Functor 的 `A→A` 是**值不变地传播**（恒等函子），关注的是"怎么传"
- Calculus 的 `T→T` 是**值主动地变换**（微积分变换），关注的是"怎么变"

---

## 2. 数学基础

### 函子（Functor）

函子 `F: C → D` 是范畴间的结构保持映射：

```mermaid
flowchart LR
    subgraph 范畴C["范畴 C（Source 端）"]
        direction TB
        X1["对象 X<br/>（Form / Config / Logger）"]
        Y1["对象 Y"]
        X1 -->|"态射 f"| Y1
    end

    subgraph 范畴D["范畴 D（Sink 端）"]
        direction TB
        X2["F(X)<br/>（隐式传递的值）"]
        Y2["F(Y)"]
        X2 -->|"F(f)"| Y2
    end

    范畴C -->|"函子 F: C → D"| 范畴D

    style 范畴C fill:#e3f2fd,stroke:#1565c0,color:#1565c0
    style 范畴D fill:#fce4ec,stroke:#c62828,color:#c62828
    style X1 fill:#bbdefb,stroke:#1565c0
    style Y1 fill:#bbdefb,stroke:#1565c0
    style X2 fill:#ffcdd2,stroke:#c62828
    style Y2 fill:#ffcdd2,stroke:#c62828
```

**核心公理**：

- **对象映射**：对每个对象 `X ∈ C`，有 `F(X) ∈ D`
- **态射映射**：对每个态射 `f: X → Y ∈ C`，有 `F(f): F(X) → F(Y) ∈ D`
- **恒等保持**：`F(idₓ) = id_{F(x)}`
- **复合保持**：`F(g ∘ f) = F(g) ∘ F(f)`

### 在框架中的对应

| 函子概念 | 框架对应 |
|---------|---------|
| 范畴 C | Source 端（如 FileDriven） |
| 范畴 D | Sink 端（如 Operation） |
| 对象 X | 要传递的值（如 Form、Config、Logger） |
| 态射 f | 调用链中的每个中间层 |
| 恒等函子 Id | 中间层对值的操作：`F(v) = v`（值不变） |
| 函子复合 | 多个中间层的串联：`Fₙ ∘ ... ∘ F₁ = Id` |

### 与 Calculus 的理论对比

```mermaid
flowchart TB
    subgraph "数学理论"
        direction LR
        FTheory[范畴论<br/>函子 F: C→D]
        CTheory[微积分<br/>导数/积分/极限]
    end
    
    subgraph "框架映射"
        direction LR
        FFrame[Functor<br/>A→A 恒等传播]
        CFrame[Calculus<br/>T→T 结构变换]
    end
    
    subgraph "核心语义"
        direction LR
        FSem["值的传递<br/>怎么传？"]
        CSem["值的变化<br/>怎么变？"]
    end
    
    FTheory --> FFrame
    CTheory --> CFrame
    FFrame --> FSem
    CFrame --> CSem
    
    style FTheory fill:#e3f2fd,stroke:#1565c0
    style CTheory fill:#f3e5f5,stroke:#6a1b9a
    style FFrame fill:#e8f5e9,stroke:#2e7d32
    style CFrame fill:#fff3e0,stroke:#e65100
    style FSem fill:#e1f5fe,stroke:#01579b
    style CSem fill:#fff8e1,stroke:#ff8f00
```

| 维度 | Functor（函子） | Calculus（微积分） |
|------|----------------|-------------------|
| **数学基础** | 范畴论 — 函子 F: C→D | 微积分 — 微分/积分/演化/收敛 |
| **核心公理** | `F(id) = id`，`F(g∘f) = F(g)∘F(f)` | 微积分基本定理：`∫f'(x)dx = f(b)-f(a)` |
| **变换模式** | A → A（恒等传播） | T → T（结构变换） |
| **语义** | **值的传递**（跨层隐式传递） | **值的变换**（同层结构变化） |
| **关注点** | **怎么传**（中间层零修改） | **怎么变**（分解-累积-迭代-收敛） |
| **维度** | **纵向**（跨模块/跨层） | **横向**（同模块/同层） |
| **实现方式** | `AsyncLocal` 隐式上下文 | 纯函数变换 `T → T` |
| **状态性** | 有状态（共享上下文存储） | 无状态（纯函数） |
| **数据流向** | Source → 中间层（零修改）→ Sink | T → T → T → T（每步都变换） |
| **核心能力** | 隐式上下文、共享作用域 | 分解变换、累积聚合、迭代演化、收敛逼近 |

---

## 3. 核心接口

### 接口继承体系

```mermaid
classDiagram
    class IFunctor {
        <<interface>>
        +Name string
        +Get~T~() T
        +Set~T~(T value) void
        +Get(string key) object?
        +Set(string key, object? value) void
        +Has~T~() bool
        +Has(string key) bool
        +Remove~T~() bool
        +Remove(string key) bool
    }

    class IFunctorScope {
        <<interface>>
        +Parent IFunctorScope?
        +CreateScope() IFunctorScope
        +Snapshot() IReadOnlyDictionary
    }

    class FunctorScope {
        -AsyncLocal~IFunctorScope?~ _current
        +Name string
        +Get~T~() T
        +Set~T~(T value) void
        +CreateScope() IFunctorScope
        +Dispose() void
    }

    class IFunctorFactory {
        <<interface>>
        +Create(string name) IFunctorScope
    }

    IFunctor <|-- IFunctorScope : 继承 + IDisposable
    IFunctorScope <|.. FunctorScope : 实现
    IFunctorFactory ..> IFunctorScope : 创建

    style IFunctor fill:#e3f2fd,stroke:#1565c0
    style IFunctorScope fill:#e8f5e9,stroke:#2e7d32
    style FunctorScope fill:#fff3e0,stroke:#e65100
    style IFunctorFactory fill:#f3e5f5,stroke:#6a1b9a
```

### IFunctor

```csharp
public interface IFunctor
{
    string Name { get; }
    T? Get<T>() where T : class;           // 按类型获取
    void Set<T>(T value) where T : class;  // 按类型设置
    object? Get(string key);               // 按键获取
    void Set(string key, object? value);   // 按键设置
    bool Has<T>();                         // 按类型检查
    bool Has(string key);                  // 按键检查
    bool Remove<T>();                      // 按类型移除
    bool Remove(string key);               // 按键移除
}
```

### IFunctorScope

```csharp
public interface IFunctorScope : IFunctor, IDisposable
{
    IFunctorScope? Parent { get; }         // 父作用域
    IFunctorScope CreateScope();           // 创建子作用域
    IReadOnlyDictionary<string, object> Snapshot();  // 快照
}
```

---

## 4. 使用方式

### 调用流程

```mermaid
sequenceDiagram
    participant Source as Source 端
    participant Functor as Functor 框架
    participant Middle as 中间层（零修改）
    participant Sink as Sink 端

    Source->>Functor: Functor.Begin("FileDriven")
    activate Functor
    Functor->>Functor: AsyncLocal 创建作用域
    Source->>Functor: scope.Set(mainForm)
    Source->>Functor: scope.Set("config", config)
    Source->>Middle: 调用中间层方法

    Note over Middle: 中间层无需任何<br/>Functor 相关代码

    Middle->>Sink: 调用 Sink 端方法
    Sink->>Functor: Functor.Current?.Get<Form>()
    Functor-->>Sink: 返回 mainForm
    Sink->>Functor: Functor.Current?.Get("config")
    Functor-->>Sink: 返回 config
    Sink-->>Middle: 返回结果
    Middle-->>Source: 返回结果

    Source->>Functor: scope.Dispose()
    deactivate Functor
    Note over Functor: AsyncLocal 自动恢复<br/>到上一个作用域
```

### 方式一：静态入口（推荐）

```csharp
// Source 端：创建作用域并设置值
using (var scope = Functor.Begin("FileDriven"))
{
    scope.Set(mainForm);           // 按类型：Set<Form>(mainForm)
    scope.Set("config", config);   // 按键：Set("config", config)
    scope.Set(logger);             // 按类型：Set<ILogger>(logger)

    // 调用链中的任意代码都可以获取
    // ...
}

// Sink 端：获取值
var form = Functor.Current?.Get<Form>();
var cfg = Functor.Current?.Get("config");
```

### 方式二：Fluent Builder

```csharp
using var scope = Functor.Create("FileDriven")
    .With(mainForm)           // 按类型
    .With("config", config)   // 按键
    .With(logger)             // 按类型
    .Build();

// 或直接执行代码块
Functor.Create("FileDriven")
    .With(mainForm)
    .BuildAndRun(scope =>
    {
        // 调用链中的代码
    });
```

### 方式三：嵌套作用域

```csharp
using var root = Functor.Begin("Root");
root.Set<Form>(mainForm);

using var child = root.CreateScope();
child.Set<ILogger>(logger);  // 子作用域可以覆盖或新增值

// 子作用域中获取：优先从当前作用域查找，递归到父作用域
var form = child.Get<Form>();    // 从父作用域获取
var log = child.Get<ILogger>();  // 从当前作用域获取

// 子作用域释放后，自动恢复父作用域
```

---

## 5. 与 Map 其他模块的关系

```mermaid
flowchart LR
    subgraph "互补模块"
        T[Transformers<br/>A→B 变换]
        S[SortMent<br/>编排执行顺序]
        B[Block<br/>显式上下文]
        C[Container<br/>执行单元]
        D[Declarative<br/>声明式描述]
        Calc[Calculus<br/>T→T 结构变换]
    end

    subgraph "无关模块"
        SM[StateMachine]
        DE[DecisionEngine]
        GF[GraphFoundation]
    end

    F[Functor<br/>A→A 恒等传播<br/>隐式上下文<br/>共享上下文<br/>怎么传]

    T -.->|互补| F
    S -.->|互补| F
    B -.->|互补| F
    C -.->|互补| F
    D -.->|互补| F
    Calc -.->|互补| F

    style F fill:#FF6B35,color:#fff,stroke:#cc4400,stroke-width:3px
    style T fill:#4A90D9,color:#fff
    style S fill:#7B68EE,color:#fff
    style B fill:#2E8B57,color:#fff
    style C fill:#20B2AA,color:#fff
    style D fill:#9370DB,color:#fff
    style Calc fill:#E65100,color:#fff
    style SM fill:#ccc,color:#666
    style DE fill:#ccc,color:#666
    style GF fill:#ccc,color:#666
```

| 模块 | 关系 | 具体对应 |
|------|------|---------|
| **Transformers** | 互补 | Transformers 做类型变换 `A→B`，Functor 做恒等传播 `A→A` |
| **Calculus** | 互补 | Calculus 做同层结构变换 `T→T`（值变化），Functor 做跨层恒等传播 `A→A`（值不变） |
| **SortMent** | 互补 | SortMent 编排执行顺序，Functor 提供执行时的隐式上下文 |
| **Block** | 互补 | BlockContext 是显式上下文，Functor 是隐式上下文 |
| **StateMachine** | 无关 | |
| **DecisionEngine** | 无关 | |
| **GraphFoundation** | 无关 | |
| **Container** | 互补 | Container 提供执行单元，Functor 提供执行单元的共享上下文 |
| **Declarative** | 互补 | Declarative 声明"有什么"，Functor 实现"怎么传" |

### 与 Calculus 的协作模式

Functor 和 Calculus 可以互补使用，形成完整的"传递 + 变换"链路：

```mermaid
flowchart LR
    subgraph "调用链（纵向）"
        direction LR
        Source --> Middle --> Sink
    end
    
    subgraph "Functor 作用"
        F[隐式传播上下文<br/>Form/Config/Logger]
    end
    
    subgraph "Calculus 作用"
        C[同层结构变换<br/>增量更新/累积合并/迭代演化]
    end
    
    Source -->|设置| F
    Middle -->|获取| F
    Sink -->|获取| F
    
    Source -->|使用| C
    Middle -->|使用| C
    Sink -->|使用| C
    
    style F fill:#e3f2fd,stroke:#1565c0
    style C fill:#f3e5f5,stroke:#6a1b9a
```

**协作代码示例**：

```csharp
// Functor：跨层传递配置上下文
using (var scope = Functor.Begin("Workflow"))
{
    scope.Set(baseConfig);  // 隐式传递基础配置
    
    // Calculus：同层进行配置的变换和合并
    var differentiator = new PropertyDifferentiator<Configuration>("Updater", ...);
    var integrator = new MergeIntegrator<Configuration>("Merger");
    
    // 在中间层获取配置，用 Calculus 变换
    var currentConfig = Functor.Current?.Get<Configuration>();
    var updated = differentiator.Differentiate(currentConfig);
    var merged = integrator.Integrate(currentConfig, updated);
    
    // 继续传递...
}
```

---

## 6. 文件结构

```
Functor/
├── Functor.csproj                    # 项目文件
├── Interfaces.cs                     # IFunctor, IFunctorScope, IFunctorFactory
├── FunctorScope.cs                   # 核心实现（AsyncLocal 隐式传播）
├── FunctorExtensions.cs              # 扩展方法
├── FunctorBuilder.cs                 # Fluent Builder + 静态入口
└── BasicMethod.Functor功能介绍.md    # 本文档
```

---

> **设计理念总结**：`BasicMethod.Functor` 以范畴论中的函子理论为思想源泉，将「跨层值传递」抽象为恒等函子的隐式传播模式。它的核心是**值在调用链中保持不变地流过**，中间层无需任何感知和修改。与 Calculus（同层结构变换）互补，共同构建从跨层传递到同层变换的完整基础能力。
