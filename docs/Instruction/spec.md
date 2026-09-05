# 编译映射与工程规范（Compilation & Engineering Specification）

> **层级**：跨层 | **职责**：公理→代码的编译映射接口 + 工程落地规范
> **源公理**：axioms.md 全篇 | **目标语言**：C# (MainProj.sln)
> **代码示例**：docs/Refactoring/code-examples/spec-code-examples.md

---

## 1. 公理分层编译策略（L0 → L3 的代码生成映射）

编译器将公理的 Formula_body 编译为运行时断言或接口约定，将元数据块中的 `enf` 编译为运行时监控器（Monitor）注入点。

### 1.1 层级映射（代码生成视角）

> **三层视角分工**：
> - **公理层语义**（axioms.md `[AX] AxiomHierarchyStructure`）：定义层级不可变性约束、持久化方式等通用语义——**权威源**
> - **引擎运行时视角**（engine.md §一）：定义各层级上的策略/规范焦点——**运行时行为**
> - **本表（编译映射视角）**：定义各层级生成的目标代码形态——**实现绑定**
>
> 三层视角正交互补，不重复相同信息。`[CR: axioms.md:AxiomHierarchyStructure [AX]]`（权威源）`、[CR: engine.md §一]`
> **变更规则**：修改 LAYER_ORDER 结构必须从 axioms.md 开始，其余两文件仅同步更新映射关系。

| 层级 | 代码生成角色 | 实现机制（C#） |
|------|------------|--------------|
| L0 | 硬核不变式 | 编译期常量 + 静态构造函数断言 |
| L0.5 | 元推理规则 | 状态机 DSL（C# 状态模式） |
| L1 | 社会共识 | 接口约定（≥2 签名校验） |
| L2 | 领域公理 | Versioned Spec + DB 迁移 |
| L3 | 配置参数 | appsettings.json / IOptions |

### 1.2 编译流程

```
1. 解析器识别公理声明 → 提取 Formula_body 和 ---meta 块
2. Formula_body → 编译为接口约束 / 静态断言
3. ---meta 中的 enf → 编译为运行时监控器注入点
4. ---meta 中的 irr → 编译为不可变性检查（L0 项为 const/static readonly）
5. ---meta 中的 src → 编译为依赖追踪元数据
```

---

## 2. 神谕依赖（Externalized Consistency）接口规范

### 设计原则

公理将一致性系于外部签名 `∃sig`。代码实现必须显式定义 **TCB（可信计算基）**：

1. **创世配置（Genesis Config）**：启动时强制读取由 ≥3 个独立根密钥签名的 `genesis.sig` 文件
2. **证明令牌接口（IProofOracle）**：`Provable_{L2}("CF≥threshold")` 不执行定理证明，而是调用 `IProofOracle`
3. **开发/生产分离**：开发环境直接查本地状态，生产环境验证链下签名

### 2.1 证明令牌接口（IProofOracle）

```csharp
/// 证明 Oracle 接口：外部化证明验证
/// dev 环境：witness 为空，直接查询本地状态变量 (#if DEBUG)
/// prod 环境：witness 必须是链下验证者委员会签名的 Merkle 证明或 ZK-SNARK 证明
/// 层级约束：level 不能为 L0 或 L0.5（防止自指）
/// 签名约束：VerifyProof 内部必须检查 witness 包含 ≥2 个独立签名(由不同验证者密钥签署)
///   违反后果：VerifyProof 返回 false，不论证明内容如何
///   实现检查点：解析 witness 中的签名集合 → 去重验证者公钥 → count≥2 → 验证 Merkle/ZK 证明
public interface IProofOracle
{
    bool VerifyProof(Layer level, string statement, byte[] witness);
}

/// 扩展：签名计数验证（默认实现）
public static class ProofOracleExtensions
{
    public static bool VerifyMinSignatures(this IProofOracle oracle, byte[] witness)
    {
        // 默认实现：解析签名集合，去重公钥，返回 count≥2
        // dev 环境 (#if DEBUG)：witness 为空时返回 ENABLE_META_ROUND_TEST 标志值
        return false;
    }
}
```

### 2.2 创世配置（GenesisConfig）

```csharp
/// 启动时强制读取 genesis.sig（≥3 独立根密钥签名）
public class GenesisConfig
{
    public Hash ContractRegistryHash { get; set; }
    public List<PublicKey> RootKeys { get; set; }
    public List<Signature> GenesisSignature { get; set; }

    /// 验证签名数量 ≥ 3 + 每个签名的合法性 + contractRegistry 哈希
    public void Verify()
    {
        // 实现签名验证逻辑
    }
}
```

> **免限约束**：dev 环境（`#if DEBUG`）下允许 witness 为空；`VerifyMinSignatures` 返回 `ENABLE_META_ROUND_TEST` 编译标志的值（默认 false）。
> 开发者需在 `.csproj` 或 `Directory.Build.props` 中显式设置 `ENABLE_META_ROUND_TEST=true` 以启用 MetaRound 测试路径。
> 设置方式：`<DefineConstants>ENABLE_META_ROUND_TEST</DefineConstants>`。

### 2.3 证明令牌生成与验证流程

```
L2 层公理判定流程:
  1. 对象层到达判定点（如 CF≥META_THRESHOLD）
  2. 生成 Provable_{L2}("CF≥threshold") 令牌请求
  3. 调用 IProofOracle.VerifyProof(L2, "CF≥threshold", witness)
  4. true → 触发 enters(MetaRound); false → 继续当前轮次或触发降级

关键约束:
- 元层（L0.5）只能引用 Ln（∀n≥1）的可证明性标签
- 元层不可直接读取对象层的裸状态变量
- 系统验证签名的有效性，而不验证证明内容
```

---

## 3. 连续域（C）与离散域（D）的拓扑落地规范

物理噪声导致的滞后效应，通过 **"双缓冲阈值"** 工程化实现。

### 3.1 双缓冲采样器（Sampler）接口

```csharp
/// Sampler 实现连续→离散的阈值采样
/// 对应公理 Continuum-SamplingThreshold:
///   δ(t+1)=ConvergeUpdate(δ(t),osc_hist)=δ(t)·α+osc_amp(t)·(1-α)
///   Nyquist_condition: 采样率sr≥2×max_freq;sr<2f_max→标记AliasingWarning(冻结δ不更新)
public class Sampler
{
    private DiscreteState _lastValid;
    private double _epsilon;
    private List<double> _oscHistory;
    private double _sampleRate;
    private bool _aliasingWarning;

    /// ConvergeUpdate 实现动态阈值更新
    public void ConvergeUpdate(double alpha) { }

    /// CheckNyquist 检查采样率是否满足 Nyquist 条件
    public bool CheckNyquist(double maxFreq) => _sampleRate >= 2 * maxFreq;

    /// Sample 采样连续状态，返回离散状态（偏离ε以上→返回上一个有效离散值）
    public DiscreteState Sample(ContinuousState c) => _lastValid;
}
```

### 3.2 因果锥（CausalCone）实现

使用 **混合逻辑时钟（HLC）** 代替物理时钟。`Event` 实现 `IComparable`，严格遵循 `→`（happened-before）的偏序。

```csharp
/// 混合逻辑时钟（HLC）
public readonly struct LogicalClock : IComparable<LogicalClock>, IEquatable<LogicalClock>
{
    public ulong Pt { get; }  // 物理时钟
    public ulong Lc { get; }  // 逻辑计数器

    public LogicalClock Tick() => new LogicalClock(Pt, Lc + 1);
    public LogicalClock Recv(LogicalClock other) =>
        new LogicalClock(Math.Max(Pt, other.Pt), Math.Max(Lc, other.Lc) + 1);

    public int CompareTo(LogicalClock other)
    {
        int cmp = Pt.CompareTo(other.Pt);
        return cmp != 0 ? cmp : Lc.CompareTo(other.Lc);
    }

    public bool Equals(LogicalClock other) => Pt == other.Pt && Lc == other.Lc;
    // 并发事件(¬(a→b)∧¬(b→a))由调用方判定
}

/// 事件结构，自动承载因果偏序
public class Event<T>
{
    public LogicalClock Clock { get; }
    public T Payload { get; }

    // 严格遵循 happened-before 偏序
    // 并发事件不可直接比较
}
```

### 3.3 连续-离散映射代码生成

| 公理组件 | 代码生成目标 |
|---------|-------------|
| `embed: Discrete → Continuous` | `TEmbedder.Embed<TDiscrete>(TDiscrete d) → Continuous<T>` |
| `sample: Continuous → Discrete` | `TSampler.Sample(Continuous<T> c) → DiscreteState` |
| Galois Connection 保底 | `Debug.Assert(Sample(Embed(d)).Equals(d))` |
| ε-近似保守性 | `Debug.Assert(Distance(Embed(Sample(c)), c) < Epsilon)` |

---

## 4. 元循环（MetaEvolution）与自反修正的工程规范

`MetaEvolution_Rule` 触发 `MetaRound`。为防止无限递归，引入 **"元层门卫（MetaGatekeeper）"** 机制。

### 4.1 元层门卫（MetaGatekeeper）

```csharp
/// 元层门卫：防止无限递归的守护线程
/// 1. L0.5 逻辑骨架编译为不可变的 C# static 只读函数
/// 2. enters(MetaRound) 由独立于主循环的监控线程执行
/// 3. 监控线程无法访问 L2 对象层的内存变量
public class MetaGatekeeper
{
    private readonly ProofRegistry _proofRegistry;
    private readonly ulong _metaThreshold;

    public bool ShouldEnterMetaRound() => _metaThreshold > 0;
}
```

### 4.2 L0.5 逻辑骨架编译约束

```csharp
/// 逻辑骨架：enters(MetaRound) ↔ Provable_{L2}("CF≥threshold") ∧ threshold > 0
public static class MetaEvolutionRule
{
    public static bool ShouldEnter(ulong threshold, bool cfProvable) =>
        cfProvable && threshold > 0;
}
```

### 4.3 降级路径（Degradation）— CRD 状态机

```csharp
public enum DegradationState
{
    Standard,
    DegradeA,
    DegradeB,
    SafeHalt
}

public static DegradationState DegradationTransition(DegradationState current, Context ctx)
{
    // 实现降级状态转换逻辑
    return current;
}
```

### 4.4 ContractRegistry

```csharp
public class ContractRegistry
{
    private readonly List<Hash> _whitelist = new();
    public Hash RegistryHash { get; private set; }

    public void AddContract(Hash hash, Signature[] signatures) { }
    public void RemoveContract(Hash hash, Signature[] signatures) { }
}
```

---

> **维护约定**：本文档中的接口签名应与 axioms.md 中的对应公理保持语义一致。
> 完整代码实现见 `docs/Refactoring/code-examples/spec-code-examples.md`。