# 宏观多元映射框架 — SkeletonFlow 编程实现

> 基于 ℱ_macro = lim_{ε→0} 𝒜_ε [ ℱ_micro ] 的完整 SkeletonFlow 代码实现
> 版本: v2.0 — 合并骨架理念 + 实现指南 + 性能分析 + 增量更新 + 物理集成

---

## 目录

- [宏观多元映射框架 — SkeletonFlow 编程实现](#宏观多元映射框架--skeletonflow-编程实现)
  - [目录](#目录)
  - [1. 核心理念：从字段骨架到算法骨架](#1-核心理念从字段骨架到算法骨架)
    - [1.1 什么是"骨架"？](#11-什么是骨架)
    - [1.2 从字段到算法的升华](#12-从字段到算法的升华)
    - [1.3 骨架的数学本质](#13-骨架的数学本质)
    - [1.4 从数学公式到骨架的映射](#14-从数学公式到骨架的映射)
  - [2. SkeletonFlow 骨架哲学](#2-skeletonflow-骨架哲学)
    - [2.1 骨架的三大原则](#21-骨架的三大原则)
    - [2.2 骨架的代数结构](#22-骨架的代数结构)
    - [2.3 骨架代数的性质](#23-骨架代数的性质)
    - [2.4 骨架的上下文](#24-骨架的上下文)
    - [2.5 组合子与数学算子的对应](#25-组合子与数学算子的对应)
  - [3. Step 0：前置准备 — 核心类型](#3-step-0前置准备--核心类型)
    - [3.1 SkeletonFlow 核心接口](#31-skeletonflow-核心接口)
    - [3.2 核心组合子操作](#32-核心组合子操作)
    - [3.3 快速创建骨架](#33-快速创建骨架)
  - [4. Step 1：字段骨架 — 定义数据形状](#4-step-1字段骨架--定义数据形状)
    - [4.1 微观状态 MicroState](#41-微观状态-microstate)
    - [4.2 ε 参数集 EpsilonSet](#42-ε-参数集-epsilonset)
    - [4.3 宏观状态 MacroState](#43-宏观状态-macrostate)
    - [4.4 物理状态 PhysicsState](#44-物理状态-physicsstate)
  - [5. Step 2：算法骨架 — 微观状态变换](#5-step-2算法骨架--微观状态变换)
  - [6. Step 3：算法骨架 — 理想状态算子 𝒜₀](#6-step-3算法骨架--理想状态算子-𝒜)
  - [7. Step 4：算法骨架 — 线性响应算子 (∂𝒜/∂ε)|₀](#7-step-4算法骨架--线性响应算子-𝒜ε)
  - [8. Step 5：组合子 — 泰勒展开管道](#8-step-5组合子--泰勒展开管道)
  - [9. Step 6：组合子 — 收敛逼近 lim\_{ε→0}](#9-step-6组合子--收敛逼近-lim_ε0)
  - [10. Step 7：组合子 — 完整公式管道](#10-step-7组合子--完整公式管道)
  - [11. Step 8：组合子 — 并行多元映射](#11-step-8组合子--并行多元映射)

---

## 1. 核心理念：从字段骨架到算法骨架

### 1.1 什么是"骨架"？

**骨架（Skeleton）** 是一个抽象概念，它描述的是"结构"而非"内容"：

```
字段骨架：数据结构的形状（字段名、类型、关系）
算法骨架：计算过程的形状（输入、变换、输出）
```

在 SkeletonFlow 中，`ISkeleton<TInput, TOutput>` 统一了这两种骨架：

```
ISkeleton<TInput, TOutput> = 一个"算法骨架"
  ├── TInput  = 骨架的输入形状
  ├── TOutput = 骨架的输出形状
  └── Execute = 骨架的填充逻辑
```

### 1.2 从字段到算法的升华

```
传统编程：    字段（数据） → 算法（函数） → 结果
SkeletonFlow：字段骨架 → 算法骨架 → 组合骨架 → 执行

字段骨架：    定义"数据应该长什么样"
算法骨架：    定义"计算应该怎么走"
组合骨架：    定义"多个算法如何连接"
```

### 1.3 骨架的数学本质

```
一个骨架 ISkeleton<TInput, TOutput> 在数学上等价于：

  f : TInput → TOutput

即：从输入类型到输出类型的映射。

骨架的组合（Then）等价于函数的复合：
  Then : (A → B) × (B → C) → (A → C)
```

### 1.4 从数学公式到骨架的映射

```
ℱ_macro(x) = lim_{ε→0} 𝒜_ε [ ℱ_micro(x) ]
     │
     ├── ℱ_micro(x)  →  字段骨架 MicroState
     │                   + 算法骨架 MicroStateSkeleton
     │
     ├── 𝒜_ε          →  算法骨架 MacroOperatorSkeleton
     │                   + 字段骨架 EpsilonSet (通过 Context)
     │
     ├── lim_{ε→0}    →  组合子 Loop
     │                   + 算法骨架 ConvergenceSkeleton
     │
     ├── 泰勒展开      →  组合子 Parallel
     │                   + 算法骨架 IdealStateSkeleton
     │                   + 算法骨架 LinearResponseSkeleton
     │
     ├── 联系矩阵      →  组合子 Parallel
     │                   + 组合子 Combine
     │
     └── 完整公式      →  组合子 Then
                         Flow.Start<Position>()
                           .Then(MicroStateSkeleton)
                           .Then(MacroOperatorSkeleton)
                           .Then(ConvergenceSkeleton)
```

---

## 2. SkeletonFlow 骨架哲学

### 2.1 骨架的三大原则

```
原则一：每个骨架只做一件事
  ISkeleton<TInput, TOutput> 只封装一个变换
  输入类型 TInput 和输出类型 TOutput 定义了变换的边界

原则二：骨架可以组合
  小骨架 → Then → 大骨架 → Then → 更大骨架
  组合后的骨架仍然是 ISkeleton<,>

原则三：骨架是类型安全的
  编译期检查输入输出类型是否匹配
  类型不匹配的骨架无法组合
```

### 2.2 骨架的代数结构

SkeletonFlow 定义了 5 种骨架操作，构成一个**完整的骨架代数**：

```
骨架代数 (S, Then, Branch, Loop, Parallel, ForEach)

Then      : 顺序组合 — 两个骨架首尾相连
Branch    : 条件分支 — 根据条件选择不同的骨架路径
Loop      : 迭代循环 — 条件为真时重复执行骨架
Parallel  : 并行执行 — 同时执行多个骨架
ForEach   : 集合提升 — 将单元素骨架提升为集合骨架
```

### 2.3 骨架代数的性质

```
封闭性：所有操作的结果仍然是骨架
结合律：(a.Then(b)).Then(c) = a.Then(b.Then(c))
单位元：IdentitySkeleton 满足 a.Then(Id) = Id.Then(a) = a
分配律：ForEach(a.Then(b)) = ForEach(a).Then(ForEach(b))
```

### 2.4 骨架的上下文

每个骨架在执行时共享一个 `SkeletonContext`：

```
SkeletonContext = 骨架执行的"环境"
  ├── CancellationToken  — 取消令牌
  ├── Items              — 共享数据字典
  ├── ServiceProvider    — 依赖注入
  └── Context            — 底层上下文
```

这意味着：**骨架之间可以通过上下文传递信息**，这是"连续联系"的运行时基础。

### 2.5 组合子与数学算子的对应

```
数学算子          组合子          含义
────────────────────────────────────────
算子复合 ∘       Then            先执行 A，再执行 B
条件选择         Branch          根据条件选择路径
迭代逼近 lim     Loop            重复执行直到收敛
并行映射         Parallel        同时执行多个映射
集合提升         ForEach         对集合中每个元素执行映射
```

---

## 3. Step 0：前置准备 — 核心类型

### 3.1 SkeletonFlow 核心接口

```csharp
// ── 核心接口 ──
// ISkeleton<TInput, TOutput> 是一个"算法骨架"
// 它封装了一个从 TInput 到 TOutput 的变换

public interface ISkeleton<in TInput, TOutput> : ISkeleton
{
    string Name { get; }                                    // 骨架名称
    string Description { get; }                             // 骨架描述
    TOutput Execute(TInput input, SkeletonContext context = null);  // 同步执行
    Task<TOutput> ExecuteAsync(TInput input, SkeletonContext context = null); // 异步执行
}

// ── 执行上下文 ──
// SkeletonContext 是骨架执行的"环境"
// 骨架之间通过 Context 共享数据（包括 ε 参数）

public class SkeletonContext : IDisposable
{
    public CancellationToken CancellationToken { get; set; }  // 取消令牌
    public IDictionary<string, object> Items { get; }         // 共享数据字典
    public IServiceProvider ServiceProvider { get; set; }     // 依赖注入
    // ... 还有 GetData<T> / SetData<T> 等方法
}
```

### 3.2 核心组合子操作

```csharp
// ── 5 种组合子操作 ──

// 1. Then — 顺序组合
//    数学对应：算子复合 𝒜 ∘ ℬ
public static ISkeleton<TInput, TOutput> Then<TInput, TIntermediate, TOutput>(
    this ISkeleton<TInput, TIntermediate> first,
    ISkeleton<TIntermediate, TOutput> next);

// 2. Branch — 条件分支
//    数学对应：条件选择 if ε then 𝒜 else ℬ
public static ISkeleton<TInput, TOutput> Branch<TInput, TOutput>(
    this ISkeleton<TInput, bool> condSkel,
    ISkeleton<TInput, TOutput> trueBranch,
    ISkeleton<TInput, TOutput> falseBranch);

// 3. Loop — 迭代循环
//    数学对应：极限逼近 lim_{ε→0}
public static ISkeleton<TInput, TInput> Loop<TInput>(
    this ISkeleton<TInput, bool> condSkel,
    ISkeleton<TInput, TInput> body);

// 4. Parallel — 并行执行
//    数学对应：并行映射 (𝒜, ℬ)
public static ISkeleton<TInput, (TOutput1, TOutput2)> Parallel<TInput, TOutput1, TOutput2>(
    this ISkeleton<TInput, TOutput1> s1,
    ISkeleton<TInput, TOutput2> s2);

// 5. ForEach — 集合提升
//    数学对应：集合映射 𝒜[ℱ₁, ℱ₂, ...]
public static ISkeleton<IEnumerable<TInput>, IEnumerable<TOutput>> ForEach<TInput, TOutput>(
    this ISkeleton<TInput, TOutput> element);
```

### 3.3 快速创建骨架

```csharp
// 使用 DelegateSkeleton 快速创建骨架（无需定义新类）
var mySkeleton = new DelegateSkeleton<InputType, OutputType>(
    "MySkeleton",                           // 名称
    sync: input => { /* 同步逻辑 */ },       // 同步委托
    async: input => { /* 异步逻辑 */ }       // 异步委托（可选）
);
```

---

## 4. Step 1：字段骨架 — 定义数据形状

### 4.1 微观状态 MicroState

```csharp
// ── 字段骨架：微观状态 ──
// 数学定义：ℱ_micro(x) = (M(x), B(x), F(x), V(x))

public struct MicroState
{
    public int M;  // 质量粒子计数
    public int B;  // 键合粒子计数
    public int F;  // 自由粒子计数
    public int V;  // 空隙粒子计数
    
    public int Total => M + B + F + V;
    
    // 归一化比值
    public float RatioM => Total > 0 ? (float)M / Total : 0f;
    public float RatioB => Total > 0 ? (float)B / Total : 0f;
    public float RatioF => Total > 0 ? (float)F / Total : 0f;
    public float RatioV => Total > 0 ? (float)V / Total : 0f;
    
    // 空间梯度（用于计算相邻体素之间的微观状态变化）
    public static MicroState Gradient(MicroState a, MicroState b, float distance)
    {
        return new MicroState
        {
            M = (int)((b.M - a.M) / distance),
            B = (int)((b.B - a.B) / distance),
            F = (int)((b.F - a.F) / distance),
            V = (int)((b.V - a.V) / distance),
        };
    }
}
```

### 4.2 ε 参数集 EpsilonSet

```csharp
// ── 字段骨架：ε 参数集 ──
// 数学定义：ε = (ε₁, ε₂, ..., ε_m) ∈ [0, 1]ᵐ

public struct EpsilonSet
{
    public float Porosity;      // ε₁ — 孔隙率
    public float Defect;        // ε₂ — 缺陷率
    public float Impurity;      // ε₃ — 杂质率
    public float Excitation;    // ε₄ — 激发度
    public float BondStrength;  // ε₅ — 键合强度
    public float Weather;       // ε₆ — 风化度
    public float Thermal;       // ε₇ — 热力学
    public float Stress;        // ε₈ — 应力
    public float DensityFactor; // ε₉ — 密度因子
    
    // 默认值：所有 ε = 0（理想状态）
    public static EpsilonSet Ideal => default;
    
    // 将 ε 参数注入 SkeletonContext
    public void ApplyTo(SkeletonContext ctx)
    {
        ctx.SetData(this, "EpsilonSet");
    }
    
    // 从 SkeletonContext 提取 ε 参数
    public static EpsilonSet From(SkeletonContext ctx)
    {
        return ctx.TryGetData<EpsilonSet>(out var eps, "EpsilonSet") 
            ? eps : Ideal;
    }
}
```

### 4.3 宏观状态 MacroState

```csharp
// ── 字段骨架：宏观状态 ──
// 数学定义：ℱ_macro(x) = (P₁(x), P₂(x), ..., P_n(x))

public struct MacroState
{
    // 9 个宏观属性
    public float Density;      // P₁ — 密度
    public float Hardness;     // P₂ — 硬度
    public float ColorHue;     // P₃ — 颜色色相
    public float Lightness;    // P₄ — 发光强度
    public float Viscosity;    // P₅ — 粘度
    public float Reactivity;   // P₆ — 化学反应性
    public float MeltingPoint; // P₇ — 熔点
    public float Elasticity;   // P₈ — 弹性
    public float Opacity;      // P₉ — 不透明度
    
    // 向量运算
    public static MacroState operator +(MacroState a, MacroState b) => new()
    {
        Density = a.Density + b.Density,
        Hardness = a.Hardness + b.Hardness,
        ColorHue = a.ColorHue + b.ColorHue,
        Lightness = a.Lightness + b.Lightness,
        Viscosity = a.Viscosity + b.Viscosity,
        Reactivity = a.Reactivity + b.Reactivity,
        MeltingPoint = a.MeltingPoint + b.MeltingPoint,
        Elasticity = a.Elasticity + b.Elasticity,
        Opacity = a.Opacity + b.Opacity,
    };
    
    public static MacroState operator *(float scalar, MacroState v) => new()
    {
        Density = scalar * v.Density,
        Hardness = scalar * v.Hardness,
        ColorHue = scalar * v.ColorHue,
        Lightness = scalar * v.Lightness,
        Viscosity = scalar * v.Viscosity,
        Reactivity = scalar * v.Reactivity,
        MeltingPoint = scalar * v.MeltingPoint,
        Elasticity = scalar * v.Elasticity,
        Opacity = scalar * v.Opacity,
    };
    
    // 范数
    public float Norm() => MathF.Sqrt(
        Density * Density + Hardness * Hardness + 
        ColorHue * ColorHue + Lightness * Lightness +
        Viscosity * Viscosity + Reactivity * Reactivity +
        MeltingPoint * MeltingPoint + Elasticity * Elasticity +
        Opacity * Opacity);
    
    // 差值范数（用于收敛判断）
    public float DiffNorm(MacroState other) => (this - other).Norm();
    public static MacroState operator -(MacroState a, MacroState b) => a + (-1f) * b;
}
```

### 4.4 物理状态 PhysicsState

```csharp
// ── 字段骨架：物理状态 ──
// 物理状态是 ℱ_macro 的一个投影

public struct PhysicsState
{
    // 基础物理属性
    public float Mass;           // 质量（由 M 决定）
    public float Volume;         // 体积（由 M+V 决定）
    public float Density;        // 密度（由 M/(M+V) 决定）
    
    // 力学属性
    public float Hardness;       // 硬度（由 B/(B+F) 决定）
    public float Elasticity;     // 弹性模量（由 F/(B+F) 决定）
    public float Viscosity;      // 粘度（由 B/(B+F+V) 决定）
    public float Friction;       // 摩擦系数
    
    // 相态属性
    public PhaseState Phase;     // 相态（固体/液体/气体/等离子体）
    public float MeltingPoint;   // 熔点（由 B 决定）
    public float BoilingPoint;   // 沸点（由 F 决定）
    
    // 运动属性
    public Vector3 Velocity;     // 速度
    public Vector3 Acceleration; // 加速度
    public float AngularVelocity;// 角速度
    
    // 相互作用属性
    public float GravitationalMass;  // 引力质量
    public float InertialMass;       // 惯性质量
    public float CollisionRestitution;// 碰撞恢复系数
}

// ── 相态枚举 ──
public enum PhaseState : byte
{
    Solid = 0,      // 固体：B >> F
    Liquid = 1,     // 液体：F >> B
    Gas = 2,        // 气体：V >> M+B+F
    Plasma = 3,     // 等离子体：F >> M+B (极高激发度)
    Unknown = 4,
}
```

---

## 5. Step 2：算法骨架 — 微观状态变换

```csharp
// ── 算法骨架：微观状态 ──
// 数学：ℱ_micro : ℝ³ → 𝕄
// 输入：空间位置 Vector3
// 输出：MicroState (M, B, F, V)

public class MicroStateSkeleton : SkeletonBase<Vector3, MicroState>
{
    public MicroStateSkeleton() : base("MicroState", "从空间位置计算微观状态 ℱ_micro(x)") { }
    
    protected override MicroState ExecuteCore(Vector3 position, SkeletonContext ctx)
    {
        // 从位置 x 计算 M:B:F:V 四维粒子计数
        // 这里使用噪声函数生成示例数据
        // 实际实现中，可以从地形生成器、体素数据源等获取
        
        float noise = PerlinNoise(position.X * 0.1f, position.Y * 0.1f, position.Z * 0.1f);
        float noise2 = PerlinNoise(position.X * 0.05f + 100, position.Y * 0.05f, position.Z * 0.05f);
        
        return new MicroState
        {
            M = (int)(noise * 100 + 50),      // 质量粒子
            B = (int)(noise2 * 80 + 20),      // 键合粒子
            F = (int)((1 - noise) * 30 + 5),  // 自由粒子
            V = (int)((1 - noise2) * 20 + 2), // 空隙粒子
        };
    }
    
    protected override Task<MicroState> ExecuteCoreAsync(Vector3 input, SkeletonContext ctx)
    {
        return Task.Run(() => ExecuteCore(input, ctx), ctx.CancellationToken);
    }
    
    // 辅助：Perlin 噪声（简化版）
    private static float PerlinNoise(float x, float y, float z)
    {
        return (MathF.Sin(x * 1.5f) * MathF.Cos(y * 2.3f) * MathF.Sin(z * 1.8f) + 1f) * 0.5f;
    }
}
```

---

## 6. Step 3：算法骨架 — 理想状态算子 𝒜₀

```csharp
// ── 算法骨架：理想状态 ──
// 数学：𝒜_0 : 𝕄 → 𝕄𝕒𝕔
// 输入：MicroState
// 输出：MacroState（ε=0 的理想状态）

public class IdealStateSkeleton : SkeletonBase<MicroState, MacroState>
{
    public IdealStateSkeleton() : base("IdealState", "计算理想状态的宏观属性 𝒜₀[ℱ_micro]") { }
    
    protected override MacroState ExecuteCore(MicroState micro, SkeletonContext ctx)
    {
        // 从 M:B:F:V 计算所有宏观属性的基准值
        // 这是 ε=0 的理想状态
        
        float total = micro.Total;
        if (total <= 0) return default;
        
        float rm = micro.M / total;  // 质量比
        float rb = micro.B / total;  // 键合比
        float rf = micro.F / total;  // 自由比
        float rv = micro.V / total;  // 空隙比
        
        return new MacroState
        {
            Density = rm,                          // 密度：质量粒子占比
            Hardness = rb * 10f,                   // 硬度：键合粒子占比 × 10
            ColorHue = (rm * 0.1f + rb * 0.3f + rf * 0.5f + rv * 0.7f) % 1.0f, // 颜色
            Lightness = rf * 0.5f,                 // 发光强度：自由粒子占比
            Viscosity = rb * 10f,                  // 粘度：键合粒子占比 × 10
            Reactivity = rf * 5f,                  // 化学反应性：自由粒子占比 × 5
            MeltingPoint = rm * 1000f,             // 熔点：质量粒子占比 × 1000
            Elasticity = rf * 5f,                  // 弹性：自由粒子占比 × 5
            Opacity = 1f - rv,                     // 不透明度：1 - 空隙占比
        };
    }
    
    protected override Task<MacroState> ExecuteCoreAsync(MicroState input, SkeletonContext ctx)
    {
        return Task.Run(() => ExecuteCore(input, ctx), ctx.CancellationToken);
    }
}
```

---

## 7. Step 4：算法骨架 — 线性响应算子 (∂𝒜/∂ε)|₀

```csharp
// ── 算法骨架：线性响应 ──
// 数学：(∂𝒜/∂ε_i)|₀ : 𝕄 → 𝕄𝕒𝕔
// 输入：MicroState
// 输出：MacroState（第 i 个 ε 参数的一阶修正向量）

public class LinearResponseSkeleton : SkeletonBase<MicroState, MacroState>
{
    private readonly int _epsilonIndex;  // 第 i 个 ε 参数
    
    public LinearResponseSkeleton(int epsilonIndex) 
        : base($"LinearResponse_{epsilonIndex}", $"第 {epsilonIndex} 个 ε 参数的线性响应") 
    {
        _epsilonIndex = epsilonIndex;
    }
    
    protected override MacroState ExecuteCore(MicroState micro, SkeletonContext ctx)
    {
        float total = micro.Total;
        if (total <= 0) return default;
        
        float rm = micro.M / total;
        float rb = micro.B / total;
        float rf = micro.F / total;
        float rv = micro.V / total;
        
        // 每个 ε 参数对所有 9 个宏观属性的影响系数
        return _epsilonIndex switch
        {
            // ε₁ = Porosity（孔隙率）：主要影响密度和硬度
            0 => new MacroState
            {
                Density = -0.3f * rm,
                Hardness = -2.0f * rb,
                Opacity = -0.5f * (1 - rv),
            },
            
            // ε₂ = Defect（缺陷率）：主要影响硬度和弹性
            1 => new MacroState
            {
                Hardness = -3.0f * rb,
                Elasticity = -2.0f * rf,
                MeltingPoint = -100f * rm,
            },
            
            // ε₃ = Impurity（杂质率）：主要影响颜色和化学反应性
            2 => new MacroState
            {
                ColorHue = 0.2f,
                Reactivity = 2.0f * rf,
                MeltingPoint = -50f * rm,
            },
            
            // ε₄ = Excitation（激发度）：主要影响发光
            3 => new MacroState
            {
                Lightness = 0.8f * rf,
                ColorHue = 0.1f,
            },
            
            // ε₅ = BondStrength（键合强度）：主要影响硬度和粘度
            4 => new MacroState
            {
                Hardness = 5.0f * rb,
                Viscosity = 5.0f * rb,
                Elasticity = -3.0f * rf,
            },
            
            // ε₆ = Weather（风化度）：影响所有属性
            5 => new MacroState
            {
                Density = -0.2f * rm,
                Hardness = -2.0f * rb,
                ColorHue = 0.15f,
                Reactivity = 1.0f * rf,
            },
            
            _ => default,
        };
    }
    
    protected override Task<MacroState> ExecuteCoreAsync(MicroState input, SkeletonContext ctx)
    {
        return Task.Run(() => ExecuteCore(input, ctx), ctx.CancellationToken);
    }
}
```

---

## 8. Step 5：组合子 — 泰勒展开管道

```csharp
// ── 算法骨架：泰勒展开 ──
// 数学：𝒜_ε[ℱ] = 𝒜_0[ℱ] + Σ ε_i · (∂𝒜/∂ε_i)|₀ [ℱ]

public class TaylorExpansionSkeleton : SkeletonBase<MicroState, MacroState>
{
    private readonly IdealStateSkeleton _ideal = new();
    private readonly List<LinearResponseSkeleton> _responses;
    
    public TaylorExpansionSkeleton(int epsilonCount = 9) 
        : base("TaylorExpansion", "泰勒展开：𝒜₀ + Σ ε_i · (∂𝒜/∂ε_i)|₀")
    {
        _responses = Enumerable.Range(0, epsilonCount)
            .Select(i => new LinearResponseSkeleton(i))
            .ToList();
    }
    
    protected override MacroState ExecuteCore(MicroState micro, SkeletonContext ctx)
    {
        var eps = EpsilonSet.From(ctx);
        var epsArray = new[] 
        { 
            eps.Porosity, eps.Defect, eps.Impurity, 
            eps.Excitation, eps.BondStrength, eps.Weather,
            eps.Thermal, eps.Stress, eps.DensityFactor 
        };
        
        // 零阶项：𝒜₀[ℱ_micro]
        var result = _ideal.Execute(micro, ctx);
        
        // 一阶修正：Σ ε_i · (∂𝒜/∂ε_i)|₀ [ℱ_micro]
        for (int i = 0; i < epsArray.Length && i < _responses.Count; i++)
        {
            if (MathF.Abs(epsArray[i]) < 0.0001f) continue;
            
            var correction = _responses[i].Execute(micro, ctx);
            result = result + epsArray[i] * correction;
        }
        
        return result;
    }
    
    protected override Task<MacroState> ExecuteCoreAsync(MicroState input, SkeletonContext ctx)
    {
        return Task.Run(() => ExecuteCore(input, ctx), ctx.CancellationToken);
    }
}

// ── 辅助骨架：ε 修正 ──
public class EpsilonCorrectionSkeleton : SkeletonBase<MacroState, MacroState>
{
    public EpsilonCorrectionSkeleton() 
        : base("EpsilonCorrection", "应用 ε 参数修正") { }
    
    protected override MacroState ExecuteCore(MacroState ideal, SkeletonContext ctx)
    {
        var eps = EpsilonSet.From(ctx);
        
        return new MacroState
        {
            Density = ideal.Density * (1f - eps.Porosity * 0.3f),
            Hardness = ideal.Hardness * MathF.Exp(-eps.Defect * 2f),
            ColorHue = (ideal.ColorHue + eps.Impurity * 0.2f) % 1.0f,
            Lightness = ideal.Lightness + eps.Excitation * 0.5f,
            Viscosity = ideal.Viscosity * (1f + eps.BondStrength * 0.5f),
            Reactivity = ideal.Reactivity + eps.Weather * 2f,
            MeltingPoint = ideal.MeltingPoint * (1f - eps.Impurity * 0.1f),
            Elasticity = ideal.Elasticity * (1f - eps.Defect * 0.3f),
            Opacity = ideal.Opacity * (1f - eps.Porosity * 0.5f),
        };
    }
    
    protected override Task<MacroState> ExecuteCoreAsync(MacroState input, SkeletonContext ctx)
    {
        return Task.Run(() => ExecuteCore(input, ctx), ctx.CancellationToken);
    }
}
```

---

## 9. Step 6：组合子 — 收敛逼近 lim_{ε→0}

```csharp
// ── 算法骨架：收敛逼近 ──
// 数学：lim_{ε→0} 𝒜_ε [ ℱ_micro ]

public class ConvergenceSkeleton : SkeletonBase<MicroState, MacroState>
{
    private readonly float _threshold;  // 收敛阈值 δ
    private readonly int _maxSteps;     // 最大步数
    
    public ConvergenceSkeleton(float threshold = 0.001f, int maxSteps = 100) 
        : base("Convergence", $"收敛逼近 lim_ε→0 (δ={threshold}, max={maxSteps})")
    {
        _threshold = threshold;
        _maxSteps = maxSteps;
    }
    
    protected override MacroState ExecuteCore(MicroState micro, SkeletonContext ctx)
    {
        float epsilon = 1.0f;
        var eps = EpsilonSet.From(ctx);
        
        // 初始计算：ε = 1.0
        eps.Porosity = epsilon;
        eps.ApplyTo(ctx);
        
        var taylor = new TaylorExpansionSkeleton();
        var current = taylor.Execute(micro, ctx);
        
        for (int step = 0; step < _maxSteps; step++)
        {
            epsilon /= 2f;
            
            // 更新 ε 参数
            eps.Porosity = epsilon;
            eps.Defect = epsilon;
            eps.Impurity = epsilon;
            eps.ApplyTo(ctx);
            
            // 计算新值
            var next = taylor.Execute(micro, ctx);
            
            // 检查收敛
            float diff = current.DiffNorm(next);
            if (diff < _threshold)
            {
                return next;
            }
            
            current = next;
        }
        
        return current;
    }
    
    protected override Task<MacroState> ExecuteCoreAsync(MicroState input, SkeletonContext ctx)
    {
        return Task.Run(() => ExecuteCore(input, ctx), ctx.CancellationToken);
    }
}
```

---

## 10. Step 7：组合子 — 完整公式管道

```csharp
// ── 算法骨架：完整公式 ──
// 数学：ℱ_macro(x) = lim_{ε→0} 𝒜_ε [ ℱ_micro(x) ]

public class MacroStateSkeleton : SkeletonBase<Vector3, MacroState>
{
    public MacroStateSkeleton() 
        : base("MacroState", "完整宏观映射：ℱ_macro(x) = lim_ε→0 𝒜_ε[ℱ_micro(x)]") { }
    
    protected override MacroState ExecuteCore(Vector3 position, SkeletonContext ctx)
    {
        var pipeline = Flow.Start<Vector3>()
            .Then(new MicroStateSkeleton())       // ℱ_micro(x)
            .Then(new TaylorExpansionSkeleton())  // 𝒜_ε[ℱ_micro]
            .Then(new ConvergenceSkeleton());     // lim_{ε→0}
        
        return pipeline.Execute(position, ctx);
    }
    
    protected override Task<MacroState> ExecuteCoreAsync(Vector3 input, SkeletonContext ctx)
    {
        return Task.Run(() => ExecuteCore(input, ctx), ctx.CancellationToken);
    }
}

// ── 使用 Flow 构建器 ──
public static class MacroMappingPipeline
{
    // 标准管道：位置 → 宏观状态
    public static Flow<Vector3, MacroState> BuildStandard()
    {
        return Flow.Start<Vector3>()
            .Then(new MicroStateSkeleton())         // ℱ_micro(x)
            .Then(new IdealStateSkeleton())          // 𝒜₀[ℱ_micro]
            .Then(new EpsilonCorrectionSkeleton());  // + ε 修正
    }
    
    // 完整管道：位置 → 收敛宏观状态
    public static Flow<Vector3, MacroState> BuildFull()
    {
        return Flow.Start<Vector3>()
            .Then(new MicroStateSkeleton())         // ℱ_micro(x)
            .Then(new TaylorExpansionSkeleton())    // 𝒜_ε[ℱ_micro]
            .Then(new ConvergenceSkeleton());       // lim_{ε→0}
    }
    
    // 带 ε 参数的管道
    public static Flow<(Vector3, EpsilonSet), MacroState> BuildWithEpsilon()
    {
        return Flow.Start<(Vector3, EpsilonSet)>()
            .Then(new ExtractPositionSkeleton())     // 提取位置
            .Then(new MicroStateSkeleton())          // ℱ_micro(x)
            .Then(new TaylorExpansionSkeleton())     // 𝒜_ε[ℱ_micro]
            .Then(new ConvergenceSkeleton());        // lim_{ε→0}
    }
}

// ── 辅助骨架：提取位置 ──
public class ExtractPositionSkeleton : SkeletonBase<(Vector3, EpsilonSet), Vector3>
{
    public ExtractPositionSkeleton() : base("ExtractPosition", "从元组中提取位置") { }
    
    protected override Vector3 ExecuteCore((Vector3, EpsilonSet) input, SkeletonContext ctx)
    {
        var (pos, eps) = input;
        eps.ApplyTo(ctx);
        return pos;
    }
    
    protected override Task<Vector3> ExecuteCoreAsync((Vector3, EpsilonSet) input, SkeletonContext ctx)
    {
        return Task.Run(() => ExecuteCore(input, ctx), ctx.CancellationToken);
    }
}
```

---

## 11. Step 8：组合子 — 并行多元映射

```csharp
// ── 使用 Parallel 组合子 ──
// 数学：P_i(x) = lim_{ε→0} ⟨𝒜_ε [ ℱ_micro(x) ], e_i ⟩

public static class ParallelPropertyMapping
{
    // 并行计算密度和硬度
    public static ISkeleton<Vector3, (float, float)> BuildDensityAndHardness()
    {
        var densitySkel = Flow.Start<