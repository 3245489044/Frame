# 自回归代码强化学习框架 (CodeRLFramework) 设计方案

## 一、概述

### 1.1 目标

基于 **Agent** 框架（Transformer + 强化学习基础设施）和 **RoslynMethod** 框架（C# 代码分析、生成、重构能力），构建一个**自回归的代码强化学习框架**，能够根据已有的函数库排列组合生成代码。

### 1.2 核心思想

> **将代码生成建模为序列决策过程：Agent 在"函数向量空间"中自回归地选择下一个代码片段（函数调用），RoslynMethod 提供代码的语法分析和合法性验证作为环境反馈，Agent 的 RL 策略根据反馈优化代码组合策略。**

### 1.3 输入输出定义

#### 输入

```
{
  Codebase: string[],           // 完整的代码库（多个源文件）
  Template: string,             // 模板代码（用户提供的骨架/示例）
  Weights: {
    CodebaseWeight: double,     // 代码库权重 α [0, 1]
    TemplateWeight: double,     // 模板权重 β [0, 1]
    ExplorationRate: double     // 探索率 ε [0, 1]
  }
}
```

#### 输出

```
{
  Code: string,                 // 生成的代码
  Confidence: double,           // 置信度 [0, 1]
  FunctionTrace: [{             // 使用的函数追踪
    FunctionId: string,
    Source: "codebase" | "template",
    Weight: double
  }]
}
```

---

## 二、整体架构

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CodeRLFramework                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────┐    ┌─────────────────────────────────────────┐    │
│  │    Input Layer      │    │         Vector Space Layer              │    │
│  │                     │    │                                         │    │
│  │  Codebase ──────────┼───►│  RoslynMethod Analysis                 │    │
│  │  Template ──────────┼───►│    ↓                                   │    │
│  │  Weights ───────────┼───►│  FunctionVectorSpace_A (Codebase)      │    │
│  └─────────────────────┘    │  FunctionVectorSpace_B (Template)      │    │
│                             │    ↓                                   │    │
│                             │  WeightedFusion                        │    │
│                             │  FusedSpace = α·Space_A + β·Space_B   │    │
│                             └──────────────────┬──────────────────────┘    │
│                                                │                           │
│                             ┌──────────────────▼──────────────────────┐    │
│                             │      Generation Layer (Agent)           │    │
│                             │                                         │    │
│                             │  Transformer Policy Network             │    │
│                             │    ↓ (自回归)                           │    │
│                             │  Action Selection (函数选择)            │    │
│                             │    ↓                                    │    │
│                             │  OutputVectorSpace                      │    │
│                             └──────────────────┬──────────────────────┘    │
│                                                │                           │
│                             ┌──────────────────▼──────────────────────┐    │
│                             │      Output Layer (RoslynMethod)        │    │
│                             │                                         │    │
│                             │  VectorToCode Decoder                   │    │
│                             │    ↓                                    │    │
│                             │  CodeTransformSkeleton                  │    │
│                             │    ↓                                    │    │
│                             │  SyntaxOptimizer                        │    │
│                             │    ↓                                    │    │
│                             │  Generated Code                         │    │
│                             └─────────────────────────────────────────┘    │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    RL Training Loop                                 │   │
│  │                                                                     │   │
│  │  Generate → Compile → Test → Reward → Update Policy → Repeat       │   │
│  │                                                                     │   │
│  │  Reward Signals:                                                    │   │
│  │    • Compilation Success: +10                                       │   │
│  │    • Test Pass Rate: +5 × pass_rate                                 │   │
│  │    • Code Quality: +3 × (1 - complexity_ratio)                      │   │
│  │    • Type Safety: +2                                                │   │
│  │    • Compilation Error: -5 × error_count                            │   │
│  │    • Runtime Exception: -10                                         │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 三、核心组件详细设计

### 3.1 输入层 (Input Layer)

#### 3.1.1 CodebaseInput

```csharp
/// <summary>
/// 代码库输入
/// 支持多文件代码库的输入和预处理
/// </summary>
public class CodebaseInput
{
    /// <summary>代码库源文件列表</summary>
    public List<string> SourceFiles { get; set; }
    
    /// <summary>文件路径到代码内容的映射</summary>
    public Dictionary<string, string> FileContents { get; set; }
    
    /// <summary>代码库权重 α</summary>
    public double Weight { get; set; } = 0.7;
    
    /// <summary>是否递归分析子目录</summary>
    public bool Recursive { get; set; } = true;
    
    /// <summary>文件过滤模式（如 "*.cs"）</summary>
    public string FilePattern { get; set; } = "*.cs";
}
```

#### 3.1.2 TemplateInput

```csharp
/// <summary>
/// 模板代码输入
/// 用户提供的代码骨架，指导生成方向
/// </summary>
public class TemplateInput
{
    /// <summary>模板代码内容</summary>
    public string Code { get; set; }
    
    /// <summary>模板权重 β</summary>
    public double Weight { get; set; } = 0.3;
    
    /// <summary>模板中的占位符标记（如 "// TODO: 填充代码"）</summary>
    public string Placeholder { get; set; } = "// TODO";
    
    /// <summary>是否保留模板结构</summary>
    public bool PreserveStructure { get; set; } = true;
}
```

#### 3.1.3 WeightConfig

```csharp
/// <summary>
/// 权重配置
/// 控制代码库和模板对生成结果的影响程度
/// </summary>
public class WeightConfig
{
    /// <summary>代码库权重 α [0, 1]</summary>
    public double CodebaseWeight { get; set; } = 0.7;
    
    /// <summary>模板权重 β [0, 1]</summary>
    public double TemplateWeight { get; set; } = 0.3;
    
    /// <summary>探索率 ε [0, 1]（RL 训练时使用）</summary>
    public double ExplorationRate { get; set; } = 0.1;
    
    /// <summary>温度参数（控制生成多样性）</summary>
    public double Temperature { get; set; } = 1.0;
    
    /// <summary>是否归一化权重（α + β = 1）</summary>
    public bool NormalizeWeights { get; set; } = true;
    
    /// <summary>
    /// 获取归一化后的权重
    /// </summary>
    public (double normalizedAlpha, double normalizedBeta) GetNormalizedWeights()
    {
        if (!NormalizeWeights)
            return (CodebaseWeight, TemplateWeight);
        
        double sum = CodebaseWeight + TemplateWeight;
        if (sum <= 0) return (0.5, 0.5);
        return (CodebaseWeight / sum, TemplateWeight / sum);
    }
}
```

---

### 3.2 向量空间层 (Vector Space Layer)

#### 3.2.1 FunctionVector（函数向量）

```csharp
/// <summary>
/// 函数向量——代码库中每个函数的向量化表示
/// </summary>
public class FunctionVector
{
    // ===== 标识信息 =====
    /// <summary>函数唯一标识</summary>
    public string Id { get; set; }
    
    /// <summary>函数名</summary>
    public string Name { get; set; }
    
    /// <summary>完整签名（含参数类型）</summary>
    public string Signature { get; set; }
    
    /// <summary>所属类名</summary>
    public string ClassName { get; set; }
    
    /// <summary>所属命名空间</summary>
    public string Namespace { get; set; }
    
    // ===== 向量表示（核心） =====
    /// <summary>语义向量——函数语义的嵌入表示</summary>
    public double[] SemanticVector { get; set; }
    
    /// <summary>结构向量——AST 结构的嵌入表示</summary>
    public double[] StructuralVector { get; set; }
    
    /// <summary>关系向量——调用关系的嵌入表示</summary>
    public double[] RelationVector { get; set; }
    
    // ===== 元数据 =====
    /// <summary>返回类型</summary>
    public string ReturnType { get; set; }
    
    /// <summary>参数列表</summary>
    public List<ParameterInfo> Parameters { get; set; } = new();
    
    /// <summary>复杂度评分</summary>
    public int ComplexityScore { get; set; }
    
    /// <summary>是否纯函数</summary>
    public bool IsPure { get; set; }
    
    /// <summary>是否有副作用</summary>
    public bool HasSideEffects { get; set; }
    
    /// <summary>调用此函数的其他函数列表</summary>
    public List<string> CalledBy { get; set; } = new();
    
    /// <summary>此函数调用的其他函数列表</summary>
    public List<string> Calls { get; set; } = new();
    
    /// <summary>源代码</summary>
    public string SourceCode { get; set; }
    
    /// <summary>来源（"codebase" 或 "template"）</summary>
    public string Source { get; set; }
    
    /// <summary>来源权重</summary>
    public double SourceWeight { get; set; } = 1.0;
    
    // ===== 向量运算 =====
    
    /// <summary>
    /// 获取完整特征向量（语义 + 结构 + 关系的拼接）
    /// </summary>
    public double[] GetFullVector()
    {
        int semanticDim = SemanticVector?.Length ?? 0;
        int structuralDim = StructuralVector?.Length ?? 0;
        int relationDim = RelationVector?.Length ?? 0;
        
        var full = new double[semanticDim + structuralDim + relationDim];
        int offset = 0;
        
        if (SemanticVector != null)
        {
            Array.Copy(SemanticVector, 0, full, offset, semanticDim);
            offset += semanticDim;
        }
        if (StructuralVector != null)
        {
            Array.Copy(StructuralVector, 0, full, offset, structuralDim);
            offset += structuralDim;
        }
        if (RelationVector != null)
        {
            Array.Copy(RelationVector, 0, full, offset, relationDim);
        }
        
        return full;
    }
    
    /// <summary>
    /// 计算与另一个函数向量的余弦相似度
    /// </summary>
    public double CosineSimilarity(FunctionVector other)
    {
        var v1 = GetFullVector();
        var v2 = other.GetFullVector();
        
        if (v1.Length != v2.Length) return 0;
        
        double dot = 0, norm1 = 0, norm2 = 0;
        for (int i = 0; i < v1.Length; i++)
        {
            dot += v1[i] * v2[i];
            norm1 += v1[i] * v1[i];
            norm2 += v2[i] * v2[i];
        }
        
        double magnitude = Math.Sqrt(norm1) * Math.Sqrt(norm2);
        return magnitude > 0 ? dot / magnitude : 0;
    }
}

/// <summary>
/// 参数信息
/// </summary>
public class ParameterInfo
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool IsOptional { get; set; }
    public string DefaultValue { get; set; }
}
```

#### 3.2.2 FunctionVectorSpace（函数向量空间）

```csharp
/// <summary>
/// 函数向量空间——所有函数向量的集合，支持加权融合和查询
/// </summary>
public class FunctionVectorSpace
{
    /// <summary>函数向量列表</summary>
    public List<FunctionVector> Functions { get; set; } = new();
    
    /// <summary>向量维度</summary>
    public int VectorDimension { get; set; }
    
    /// <summary>空间来源标识</summary>
    public string Source { get; set; }
    
    /// <summary>空间权重</summary>
    public double Weight { get; set; } = 1.0;
    
    // ===== 空间操作 =====
    
    /// <summary>
    /// 加权融合两个向量空间
    /// 
    /// 融合策略：
    /// 1. 对两个空间中同名的函数，向量做加权平均
    /// 2. 只存在于一个空间的函数，按该空间权重缩放后加入
    /// 3. 最终向量 = α · v_codebase + β · v_template
    /// </summary>
    /// <param name="other">另一个向量空间</param>
    /// <param name="selfWeight">自身权重 α</param>
    /// <param name="otherWeight">对方权重 β</param>
    /// <returns>融合后的向量空间</returns>
    public FunctionVectorSpace Fuse(
        FunctionVectorSpace other,
        double selfWeight,
        double otherWeight)
    {
        var result = new FunctionVectorSpace
        {
            VectorDimension = this.VectorDimension,
            Source = $"Fused({this.Source}:{selfWeight}, {other.Source}:{otherWeight})",
            Weight = 1.0
        };
        
        // 按名称索引当前空间的函数
        var funcIndex = this.Functions
            .GroupBy(f => f.Name)
            .ToDictionary(g => g.Key, g => g.First());
        
        var processedNames = new HashSet<string>();
        
        // 处理两个空间都有的函数（加权融合）
        foreach (var func in other.Functions)
        {
            if (funcIndex.TryGetValue(func.Name, out var existing))
            {
                var fused = FuseVectors(existing, func, selfWeight, otherWeight);
                result.Functions.Add(fused);
                processedNames.Add(func.Name);
            }
        }
        
        // 处理只存在于当前空间的函数
        foreach (var func in this.Functions)
        {
            if (!processedNames.Contains(func.Name))
            {
                var scaled = ScaleVector(func, selfWeight);
                result.Functions.Add(scaled);
            }
        }
        
        // 处理只存在于对方空间的函数
        foreach (var func in other.Functions)
        {
            if (!processedNames.Contains(func.Name))
            {
                var scaled = ScaleVector(func, otherWeight);
                result.Functions.Add(scaled);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// 融合两个同名函数的向量
    /// </summary>
    private static FunctionVector FuseVectors(
        FunctionVector a, FunctionVector b,
        double weightA, double weightB)
    {
        var result = new FunctionVector
        {
            Id = a.Id,
            Name = a.Name,
            Signature = a.Signature,
            ClassName = a.ClassName,
            Namespace = a.Namespace,
            ReturnType = a.ReturnType,
            Parameters = a.Parameters,
            ComplexityScore = (int)(a.ComplexityScore * weightA + b.ComplexityScore * weightB),
            IsPure = a.IsPure && b.IsPure,
            HasSideEffects = a.HasSideEffects || b.HasSideEffects,
            SourceCode = weightA >= weightB ? a.SourceCode : b.SourceCode,
            Source = "fused",
            SourceWeight = 1.0
        };
        
        // 加权融合语义向量
        result.SemanticVector = WeightedAverage(a.SemanticVector, b.SemanticVector, weightA, weightB);
        result.StructuralVector = WeightedAverage(a.StructuralVector, b.StructuralVector, weightA, weightB);
        result.RelationVector = WeightedAverage(a.RelationVector, b.RelationVector, weightA, weightB);
        
        // 合并调用关系
        result.Calls.AddRange(a.Calls);
        result.Calls.AddRange(b.Calls.Where(c => !result.Calls.Contains(c)));
        result.CalledBy.AddRange(a.CalledBy);
        result.CalledBy.AddRange(b.CalledBy.Where(c => !result.CalledBy.Contains(c)));
        
        return result;
    }
    
    /// <summary>
    /// 加权平均两个向量
    /// </summary>
    private static double[] WeightedAverage(double[] v1, double[] v2, double w1, double w2)
    {
        if (v1 == null && v2 == null) return null;
        if (v1 == null) return v2.Select(x => x * w2).ToArray();
        if (v2 == null) return v1.Select(x => x * w1).ToArray();
        
        int len = Math.Max(v1.Length, v2.Length);
        var result = new double[len];
        
        for (int i = 0; i < len; i++)
        {
            double val1 = i < v1.Length ? v1[i] : 0;
            double val2 = i < v2.Length ? v2[i] : 0;
            result[i] = val1 * w1 + val2 * w2;
        }
        
        return result;
    }
    
    /// <summary>
    /// 缩放向量
    /// </summary>
    private static FunctionVector ScaleVector(FunctionVector v, double scale)
    {
        var result = new FunctionVector
        {
            Id = v.Id,
            Name = v.Name,
            Signature = v.Signature,
            ClassName = v.ClassName,
            Namespace = v.Namespace,
            ReturnType = v.ReturnType,
            Parameters = v.Parameters,
            ComplexityScore = (int)(v.ComplexityScore * scale),
            IsPure = v.IsPure,
            HasSideEffects = v.HasSideEffects,
            SourceCode = v.SourceCode,
            Source = v.Source,
            SourceWeight = v.SourceWeight * scale,
            SemanticVector = v.SemanticVector?.Select(x => x * scale).ToArray(),
            StructuralVector = v.StructuralVector?.Select(x => x * scale).ToArray(),
            RelationVector = v.RelationVector?.Select(x => x * scale).ToArray(),
            Calls = v.Calls.ToList(),
            CalledBy = v.CalledBy.ToList()
        };
        
        return result;
    }
    
    // ===== 查询方法 =====
    
    /// <summary>
    /// 按名称搜索函数
    /// </summary>
    public List<FunctionVector> SearchByName(string name)
    {
        return Functions
            .Where(f => f.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.SourceWeight)
            .ToList();
    }
    
    /// <summary>
    /// 按向量相似度搜索（KNN）
    /// </summary>
    public List<(FunctionVector func, double similarity)> SearchByVector(
        double[] queryVector, int topK = 10)
    {
        return Functions
            .Select(f => (func: f, similarity: CosineSimilarity(queryVector, f.GetFullVector())))
            .OrderByDescending(x => x.similarity)
            .Take(topK)
            .ToList();
    }
    
    /// <summary>
    /// 按返回类型过滤
    /// </summary>
    public List<FunctionVector> FilterByReturnType(string returnType)
    {
        return Functions
            .Where(f => f.ReturnType == returnType)
            .ToList();
    }
    
    /// <summary>
    /// 获取兼容的函数（参数类型匹配）
    /// </summary>
    public List<FunctionVector> GetCompatibleFunctions(
        string returnType, List<string> argTypes)
    {
        return Functions
            .Where(f => f.ReturnType == returnType)
            .Where(f => f.Parameters.Count == argTypes.Count)
            .Where(f => f.Parameters.Zip(argTypes, (p, a) => p.Type == a).All(x => x))
            .ToList();
    }
    
    private static double CosineSimilarity(double[] v1, double[] v2)
    {
        if (v1.Length != v2.Length) return 0;
        double dot = 0, norm1 = 0, norm2 = 0;
        for (int i = 0; i < v1.Length; i++)
        {
            dot += v1[i] * v2[i];
            norm1 += v1[i] * v1[i];
            norm2 += v2[i] * v2[i];
        }
        return (Math.Sqrt(norm1) * Math.Sqrt(norm2)) > 0 
            ? dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2)) 
            : 0;
    }
}
```

#### 3.2.3 VectorExtractor（向量提取器）

```csharp
/// <summary>
/// 向量提取器——基于 RoslynMethod 从代码中提取函数向量
/// 
/// 复用 RoslynMethod 的以下能力：
/// - SyntaxElementExtractor: 提取方法、属性、字段
/// - QuerySkeleton: 语法树查询
/// - ComplexityCalculator: 复杂度计算
/// - PurityAnalyzer: 纯度分析
/// - VariableAnalyzer: 变量分析
/// - ReferenceAnalyzer: 引用分析
/// </summary>
public class VectorExtractor
{
    // ===== 语义特征提取 =====
    
    /// <summary>
    /// 提取函数的语义向量
    /// 基于函数名、参数类型、返回类型的嵌入
    /// </summary>
    public double[] ExtractSemanticVector(MethodDeclarationSyntax method)
    {
        // 特征维度设计：
        // [0-49]:  函数名哈希嵌入 (50维)
        // [50-99]:  参数类型组合编码 (50维)
        // [100-149]: 返回类型编码 (50维)
        // [150-199]: 命名空间编码 (50维)
        // [200-249]: 修饰符编码 (50维)
        
        var vector = new double[250];
        
        // 1. 函数名嵌入（使用字符级哈希）
        string name = method.Identifier.Text;
        for (int i = 0; i < name.Length; i++)
        {
            int idx = Math.Abs(name[i].GetHashCode()) % 50;
            vector[idx] += 1.0 / name.Length;
        }
        
        // 2. 参数类型组合编码
        var paramTypes = method.ParameterList.Parameters
            .Select(p => p.Type?.ToString() ?? "var")
            .ToList();
        for (int i = 0; i < paramTypes.Count && i < 10; i++)
        {
            int idx = 50 + (Math.Abs(paramTypes[i].GetHashCode()) % 50);
            vector[idx] += 1.0;
        }
        
        // 3. 返回类型编码
        string returnType = method.ReturnType.ToString();
        int retIdx = 100 + (Math.Abs(returnType.GetHashCode()) % 50);
        vector[retIdx] = 1.0;
        
        // 4. 命名空间编码（通过 RoslynMethod 的 SyntaxHelper）
        var ns = GetNamespace(method);
        if (!string.IsNullOrEmpty(ns))
        {
            int nsIdx = 150 + (Math.Abs(ns.GetHashCode()) % 50);
            vector[nsIdx] = 1.0;
        }
        
        // 5. 修饰符编码
        foreach (var modifier in method.Modifiers)
        {
            int modIdx = 200 + (Math.Abs(modifier.Text.GetHashCode()) % 50);
            vector[modIdx] = 1.0;
        }
        
        return vector;
    }
    
    /// <summary>
    /// 提取函数的结构向量
    /// 基于 AST 结构的嵌入
    /// </summary>
    public double[] ExtractStructuralVector(MethodDeclarationSyntax method)
    {
        // 特征维度设计：
        // [0-9]:   语句类型分布 (10维)
        // [10-19]:  控制流复杂度 (10维)
        // [20-29]:  表达式类型分布 (10维)
        // [30-39]:  代码度量 (10维)
        
        var vector = new double[40];
        
        // 1. 语句类型分布
        var statements = method.DescendantNodes().OfType<StatementSyntax>().ToList();
        foreach (var stmt in statements)
        {
            int idx = stmt.Kind() switch
            {
                SyntaxKind.ReturnStatement => 0,
                SyntaxKind.IfStatement => 1,
                SyntaxKind.ForStatement or SyntaxKind.ForEachStatement => 2,
                SyntaxKind.WhileStatement or SyntaxKind.DoStatement => 3,
                SyntaxKind.SwitchStatement => 4,
                SyntaxKind.TryStatement => 5,
                SyntaxKind.UsingStatement => 6,
                SyntaxKind.LocalDeclarationStatement => 7,
                SyntaxKind.ExpressionStatement => 8,
                _ => 9
            };
            vector[idx] += 1.0 / statements.Count;
        }
        
        // 2. 控制流复杂度（复用 RoslynMethod 的 ComplexityCalculator）
        vector[10] = ComplexityCalculator.CalculateMethodComplexity(method) / 100.0;
        vector[11] = method.DescendantNodes().OfType<IfStatementSyntax>().Count() / 20.0;
        vector[12] = method.DescendantNodes().OfType<LoopSyntax>().Count() / 10.0;
        vector[13] = method.DescendantNodes().OfType<TryStatementSyntax>().Count() / 5.0;
        
        // 3. 表达式类型分布
        var expressions = method.DescendantNodes().OfType<ExpressionSyntax>().ToList();
        foreach (var expr in expressions)
        {
            int idx = 20 + (expr.Kind() switch
            {
                SyntaxKind.InvocationExpression => 0,
                SyntaxKind.MemberAccessExpression => 1,
                SyntaxKind.BinaryExpression => 2,
                SyntaxKind.UnaryExpression => 3,
                SyntaxKind.ConditionalExpression => 4,
                SyntaxKind.LambdaExpression => 5,
                SyntaxKind.ObjectCreationExpression => 6,
                _ => 7
            });
            if (idx < 30) vector[idx] += 1.0 / Math.Max(expressions.Count, 1);
        }
        
        // 4. 代码度量
        vector[30] = Math.Min(method.ToString().Length / 1000.0, 1.0);  // 代码长度
        vector[31] = Math.Min(method.DescendantNodes().OfType<IdentifierNameSyntax>().Count() / 100.0, 1.0);  // 标识符密度
        vector[32] = method.ParameterList.Parameters.Count / 10.0;  // 参数数量
        
        return vector;
    }
    
    /// <summary>
    /// 提取函数的关系向量
    /// 基于调用图的嵌入
    /// </summary>
    public double[] ExtractRelationVector(
        MethodDeclarationSyntax method,
        Dictionary<string, List<string>> callGraph)
    {
        var vector = new double[50];
        string methodName = method.Identifier.Text;
        
        // 1. 被调用关系编码
        if (callGraph.TryGetValue(methodName, out var callees))
        {
            foreach (var callee in callees)
            {
                int idx = Math.Abs(callee.GetHashCode()) % 25;
                vector[idx] += 1.0;
            }
        }
        
        // 2. 调用者关系编码
        foreach (var kvp in callGraph)
        {
            if (kvp.Value.Contains(methodName))
            {
                int idx = 25 + (Math.Abs(kvp.Key.GetHashCode()) % 25);
                vector[idx] += 1.0;
            }
        }
        
        return vector;
    }
    
    /// <summary>
    /// 从代码中提取完整的函数向量空间
    /// </summary>
    public FunctionVectorSpace ExtractFromCode(string code, string source = "codebase")
    {
        var space = new FunctionVectorSpace
        {
            Source = source,
            VectorDimension = 340,  // 250 + 40 + 50
            Weight = source == "codebase" ? 0.7 : 0.3
        };
        
        try
        {
            // 复用 RoslynMethod 的 SyntaxElementExtractor 提取方法
            var elements = SyntaxElementExtractor.Extract(code);
            
            // 解析语法树
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();
            
            // 构建调用图（复用 ReferenceAnalyzer）
            var callGraph = BuildCallGraph(root);
            
            // 提取每个方法的向量
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                var element = elements.FirstOrDefault(e => e.Name == method.Identifier.Text);
                
                var funcVector = new FunctionVector
                {
                    Id = $"{source}_{method.Identifier.Text}_{method.Span.Start}",
                    Name = method.Identifier.Text,
                    Signature = GetMethodSignature(method),
                    ClassName = GetClassName(method),
                    Namespace = GetNamespace(method),
                    ReturnType = method.ReturnType.ToString(),
                    Parameters = method.ParameterList.Parameters
                        .Select(p => new ParameterInfo
                        {
                            Name = p.Identifier.Text,
                            Type = p.Type?.ToString() ?? "var"
                        }).ToList(),
                    ComplexityScore = element?.ComplexityScore ?? 0,
                    IsPure = element?.IsPure ?? false,
                    HasSideEffects = element?.HasSideEffects ?? true,
                    SourceCode = method.ToString(),
                    Source = source,
                    SourceWeight = space.Weight,
                    SemanticVector = ExtractSemanticVector(method),
                    StructuralVector = ExtractStructuralVector(method),
                    RelationVector = ExtractRelationVector(method, callGraph)
                };
                
                space.Functions.Add(funcVector);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"向量提取失败: {ex.Message}");
        }
        
        return space;
    }
    
    // ===== 辅助方法 =====
    
    private string GetMethodSignature(MethodDeclarationSyntax method)
    {
        var returnType = method.ReturnType.ToString();
        var name = method.Identifier.Text;
        var parameters = string.Join(", ", 
            method.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier.Text}"));
        return $"{returnType} {name}({parameters})";
    }
    
    private string GetClassName(MethodDeclarationSyntax method)
    {
        var classDecl = method.Ancestors()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault();
        return classDecl?.Identifier.Text ?? "";
    }
    
    private string GetNamespace(MethodDeclarationSyntax method)
    {
        var nsDecl = method.Ancestors()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();
        return nsDecl?.Name?.ToString() ?? "";
    }
    
    private Dictionary<string, List<string>> BuildCallGraph(SyntaxNode root)
    {
        var graph = new Dictionary<string, List<string>>();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        
        foreach (var method in methods)
        {
            var name = method.Identifier.Text;
            if (!graph.ContainsKey(name))
                graph[name] = new List<string>();
            
            var invocations = method.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Select(inv => (inv.Expression as MemberAccessExpressionSyntax)
                    ?.Name?.Identifier.Text ?? 
                    (inv.Expression as IdentifierNameSyntax)?.Identifier.Text)
                .Where(x => x != null);
            
            foreach (var callee in invocations)
            {
                if (!graph[name].Contains(callee))
                    graph[name].Add(callee);
            }
        }
        
        return graph;
    }
}
```

---

### 3.3 生成层 (Generation Layer)

#### 3.3.1 CodeTokenizer（代码 Tokenizer）

```csharp
/// <summary>
/// 代码 Tokenizer——将函数向量空间映射为 Agent 可处理的 token 序列
/// 
/// 每个 token 对应一个函数调用，token 的 logits 由函数向量的相似度决定
/// </summary>
public class CodeTokenizer
{
    private FunctionVectorSpace _vectorSpace;
    private Dictionary<int, FunctionVector> _tokenToFunction;
    private Dictionary<string, int> _functionToToken;
    
    /// <summary>词汇表大小（函数数量）</summary>
    public int VocabSize => _tokenToFunction.Count;
    
    /// <summary>特殊 token</summary>
    public const int PAD_TOKEN = 0;
    public const int SOS_TOKEN = 1;  // Start of Sequence
    public const int EOS_TOKEN = 2;  // End of Sequence
    public const int UNK_TOKEN = 3;  // Unknown
    
    public CodeTokenizer(FunctionVectorSpace vectorSpace)
    {
        _vectorSpace = vectorSpace;
        _tokenToFunction = new Dictionary<int, FunctionVector>();
        _functionToToken = new Dictionary<string, int>();
        
        BuildTokenMapping();
    }
    
    private void BuildTokenMapping()
    {
        int tokenId = 4;  // 从 4 开始，0-3 为特殊 token
        
        foreach (var func in _vectorSpace.Functions)
        {
            _tokenToFunction[tokenId] = func;
            _functionToToken[func.Name] = tokenId;
            tokenId++;
        }
    }
    
    /// <summary>
    /// 将函数名序列编码为 token ID 序列
    /// </summary>
    public int[] Encode(List<string> functionNames)
    {
        var tokens = new List<int> { SOS_TOKEN };
        
        foreach (var name in functionNames)
        {
            if (_functionToToken.TryGetValue(name, out int tokenId))
                tokens.Add(tokenId);
            else
                tokens.Add(UNK_TOKEN);
        }
        
        tokens.Add(EOS_TOKEN);
        return tokens.ToArray();
    }
    
    /// <summary>
    /// 将 token ID 序列解码为函数向量序列
    /// </summary>
    public List<FunctionVector> Decode(int[] tokenIds)
    {
        var functions = new List<FunctionVector>();
        
        foreach (var tokenId in tokenIds)
        {
            if (tokenId == SOS_TOKEN || tokenId == PAD_TOKEN)
                continue;
            if (tokenId == EOS_TOKEN)
                break;
            
            if (_tokenToFunction.TryGetValue(tokenId, out var func))
                functions.Add(func);
        }
        
        return functions;
    }
    
    /// <summary>
    /// 获取 token ID 对应的函数向量
    /// </summary>
    public FunctionVector GetFunction(int tokenId)
    {
        return _tokenToFunction.GetValueOrDefault(tokenId);
    }
    
    /// <summary>
    /// 获取函数名对应的 token ID
    /// </summary>
    public int GetTokenId(string functionName)
    {
        return _functionToToken.GetValueOrDefault(functionName, UNK_TOKEN);
    }
    
    /// <summary>
    /// 获取所有 token 的向量矩阵（用于 Transformer 的 Embedding 层）
    /// 形状: [VocabSize, VectorDimension]
    /// </summary>
    public double[,] GetTokenEmbeddingMatrix()
    {
        int vocabSize = VocabSize;
        int dim = _vectorSpace.VectorDimension;
        var matrix = new double[vocabSize, dim];
        
        foreach (var kvp in _tokenToFunction)
        {
            int tokenId = kvp.Key;
            var func = kvp.Value;
            var fullVector = func.GetFullVector();
            
            for (int d = 0; d < fullVector.Length && d < dim; d++)
            {
                matrix[tokenId, d] = fullVector[d];
            }
        }
        
        return matrix;
    }
}
```

#### 3.3.2 CodeGenerationAgent（代码生成 Agent）

```csharp
/// <summary>
/// 代码生成 Agent——基于 Agent 框架的 Transformer + RL 能力
/// 
/// 复用 Agent 框架的以下能力：
/// - TransformerModel: 自回归生成
/// - PPOFitting: 策略优化
/// - RuntimeEngine: 训练推理一体化
/// - BeamSearchSkeleton: 束搜索
/// - SamplingSkeleton: 采样策略
/// </summary>
public class CodeGenerationAgent
{
    private readonly TransformerModel _policyNetwork;
    private readonly TransformerConfig _config;
    private readonly CodeTokenizer _tokenizer;
    private readonly RuntimeContext _context;
    private readonly Random _rng;
    
    /// <summary>当前生成的 token 序列</summary>
    public List<int> CurrentSequence { get; private set; }
    
    /// <summary>当前步的 logits</summary>
    public Tensor CurrentLogits { get; private set; }
    
    public CodeGenerationAgent(
        CodeTokenizer tokenizer,
        TransformerConfig? config = null)
    {
        _tokenizer = tokenizer;
        _config = config ?? TransformerConfig.Default;
        _rng = new Random();
        
        // 创建策略网络（复用 Agent 的 TransformerModel）
        _policyNetwork = new TransformerModel(_config);
        
        // 创建运行时上下文（复用 Agent 的 RuntimeContext）
        _context = new RuntimeContext(new RuntimeConfig
        {
            ModelDim = _config.ModelDim,
            NumLayers = _config.NumLayers,
            NumHeads = _config.NumHeads,
            VocabSize = _tokenizer.VocabSize
        });
        
        CurrentSequence = new List<int>();
    }
    
    /// <summary>
    /// 自回归生成代码 token 序列
    /// 
    /// 复用 Agent 的 TransformerModel.GenerateWithCache 机制
    /// </summary>
    /// <param name="contextTokens">上下文 token（模板编码后的初始序列）</param>
    /// <param name="maxSteps">最大生成步数</param>
    /// <param name="temperature">采样温度</param>
    /// <returns>生成的 token ID 序列</returns>
    public int[] Generate(
        int[] contextTokens,
        int maxSteps = 50,
        double temperature = 1.0)
    {
        // 复用 TransformerModel 的自回归生成能力
        return _policyNetwork.GenerateWithCache(
            contextTokens,
            maxNewTokens: maxSteps,
            temperature: temperature);
    }
    
    /// <summary>
    /// 从 logits 选择动作（函数 token）
    /// 
    /// 支持多种选择策略：
    /// 1. 贪心：选择概率最高的函数
    /// 2. 温度采样：按概率分布采样
    /// 3. ε-贪心：以 ε 概率随机探索
    /// </summary>
    public int SelectAction(
        Tensor logits,
        double temperature = 1.0,
        double epsilon = 0.1)
    {
        // ε-贪心探索
        if (_rng.NextDouble() < epsilon)
        {
            // 随机探索
            return _rng.Next(4, _tokenizer.VocabSize); // 跳过特殊 token
        }
        
        // 温度采样（复用 TransformerModel 的采样逻辑）
        return TransformerModel.SampleToken(logits, temperature, _rng);
    }
    
    /// <summary>
    /// 获取当前策略的动作概率分布
    /// </summary>
    public double[] GetActionProbs(Tensor logits, double temperature = 1.0)
    {
        var probs = logits.Divide(temperature).Softmax(dim: 2);
        int vocabSize = probs.Dimension;
        var result = new double[vocabSize];
        
        for (int i = 0; i < vocabSize; i++)
            result[i] = probs[0, 0, i];
        
        return result;
    }
    
    /// <summary>
    /// 计算动作的对数概率（用于策略梯度）
    /// </summary>
    public double GetLogProb(int action, Tensor logits)
    {
        var probs = logits.Softmax(dim: 2);
        double prob = probs[0, 0, action];
        return Math.Log(Math.Max(prob, 1e-10));
    }
}
```

#### 3.3.3 OutputVectorSpace（输出向量空间）

```csharp
/// <summary>
/// 输出向量空间——Agent 生成的函数序列的向量化表示
/// 
/// 每个向量对应一个被选择的函数调用，包含调用上下文信息
/// </summary>
public class OutputVectorSpace
{
    /// <summary>输出向量列表（每个向量对应一个函数调用）</summary>
    public List<OutputVector> Vectors { get; set; } = new();
    
    /// <summary>生成置信度</summary>
    public double Confidence { get; set; }
    
    /// <summary>生成时的温度参数</summary>
    public double Temperature { get; set; }
    
    /// <summary>函数追踪信息</summary>
    public List<FunctionTrace> FunctionTrace { get; set; } = new();
}

/// <summary>
/// 输出向量——单个函数调用的向量化表示
/// </summary>
public class OutputVector
{
    /// <summary>选择的函数 ID</summary>
    public string FunctionId { get; set; }
    
    /// <summary>函数名</summary>
    public string FunctionName { get; set; }
    
    /// <summary>函数向量（来自 FunctionVectorSpace）</summary>
    public double[] Vector { get; set; }
    
    /// <summary>选择概率</summary>
    public double Probability { get; set; }
    
    /// <summary>来源（"codebase" / "template" / "fused"）</summary>
    public string Source { get; set; }
    
    /// <summary>来源权重</summary>
    public double SourceWeight { get; set; }
    
    /// <summary>参数绑定信息</summary>
    public Dictionary<string, string> ParameterBindings { get; set; } = new();
}

/// <summary>
/// 函数追踪信息
/// </summary>
public class FunctionTrace
{
    public string FunctionId { get; set; }
    public string Source { get; set; }
    public double Weight { get; set; }
}
```

---

### 3.4 输出层 (Output Layer)

#### 3.4.1 VectorToCodeConverter（向量到代码转换器）

```csharp
/// <summary>
/// 向量到代码转换器——将输出向量空间转换为可编译的代码
/// 
/// 复用 RoslynMethod 的以下能力：
/// - CodeTransformSkeleton: 创建方法声明、语句
/// - SyntaxOptimizer: 语法优化
/// - MethodMergeEngine: 方法合并
/// - QuerySkeleton: 语法树查询
/// </summary>
public class VectorToCodeConverter
{
    private readonly FunctionVectorSpace _functionSpace;
    private readonly CodeTokenizer _tokenizer;
    
    public VectorToCodeConverter(
        FunctionVectorSpace functionSpace,
        CodeTokenizer tokenizer)
    {
        _functionSpace = functionSpace;
        _tokenizer = tokenizer;
    }
    
    /// <summary>
    /// 将输出向量空间转换为代码
    /// </summary>
    public string Convert(OutputVectorSpace outputSpace, string templateCode = "")
    {
        // Step 1: 解码输出向量为函数调用序列
        var functionCalls = DecodeOutputVectors(outputSpace);
        
        // Step 2: 生成代码
        var code = GenerateCode(functionCalls, templateCode);
        
        // Step 3: 语法优化（复用 SyntaxOptimizer）
        code = SyntaxOptimizer.RewriteMethod(code);
        
        return code;
    }
    
    /// <summary>
    /// 从输出向量解码函数调用序列
    /// </summary>
    private List<FunctionCall> DecodeOutputVectors(OutputVectorSpace outputSpace)
    {
        var calls = new List<FunctionCall>();
        
        foreach (var vector in outputSpace.Vectors)
        {
            // 在函数空间中搜索最匹配的函数
            var matches = _functionSpace.SearchByVector(vector.Vector, topK: 1);
            if (matches.Count > 0)
            {
                var (bestMatch, similarity) = matches[0];
                calls.Add(new FunctionCall
                {
                    FunctionId = bestMatch.Id,
                    FunctionName = bestMatch.Name,
                    Parameters = bestMatch.Parameters
                        .Select(p => new ParameterBinding
                        {
                            Name = p.Name,
                            Type = p.Type,
                            Value = ResolveParameterValue(p, vector)
                        }).ToList(),
                    Confidence = similarity
                });
            }
        }
        
        return calls;
    }
    
    /// <summary>
    /// 生成代码
    /// </summary>
    private string GenerateCode(
        List<FunctionCall> calls,
        string templateCode)
    {
        // 复用 CodeTransformSkeleton 创建方法
        var skeleton = new CodeTransformSkeleton();
        
        // 解析模板代码，提取结构
        if (!string.IsNullOrEmpty(templateCode))
        {
            return GenerateWithTemplate(calls, templateCode);
        }
        
        // 无模板时，自动生成完整方法
        return GenerateStandaloneMethod(calls, skeleton);
    }
    
    /// <summary>
    /// 基于模板生成代码
    /// </summary>
    private string GenerateWithTemplate(
        List<FunctionCall> calls,
        string templateCode)
    {
        // 解析模板语法树
        var tree = CSharpSyntaxTree.ParseText(templateCode);
        var root = tree.GetRoot();
        
        // 查找模板中的占位符方法
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        var rewriter = new CodeInsertionRewriter(calls);
        var newRoot = rewriter.Visit(root);
        
        return newRoot.NormalizeWhitespace().ToFullString();
    }
    
    /// <summary>
    /// 生成独立方法
    /// </summary>
    private string GenerateStandaloneMethod(
        List<FunctionCall> calls,
        CodeTransformSkeleton skeleton)
    {
        // 创建方法声明
        var method = skeleton.CreateMethod("void", "GeneratedMethod");
        
        // 为每个函数调用生成语句
        foreach (var call in calls)
        {
            var invocation = CreateInvocationExpression(call);
            var statement = skeleton.ConvertToStatement(invocation);
            method = skeleton.AddStatementsToBlock(method, statement);
        }
        
        return method.NormalizeWhitespace().ToFullString();
    }
    
    /// <summary>
    /// 创建函数调用表达式
    /// </summary>
    private InvocationExpressionSyntax CreateInvocationExpression(FunctionCall call)
    {
        var arguments = call.Parameters
            .Select(p => SyntaxFactory.Argument(
                SyntaxFactory.IdentifierName(p.Value ?? p.Name)))
            .ToList();
        
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(call.FunctionName))
            .WithArgumentList(SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(arguments)));
    }
    
    /// <summary>
    /// 解析参数值
    /// </summary>
    private string ResolveParameterValue(ParameterInfo param, OutputVector vector)
    {
        if (vector.ParameterBindings.TryGetValue(param.Name, out var value))
            return value;
        return param.Name; // 默认使用参数名
    }
}

/// <summary>
/// 函数调用信息
/// </summary>
public class FunctionCall
{
    public string FunctionId { get; set; }
    public string FunctionName { get; set; }
    public List<ParameterBinding> Parameters { get; set; } = new();
    public double Confidence { get; set; }
}

/// <summary>
/// 参数绑定
/// </summary>
public class ParameterBinding
{
    public string Name { get; set; }
    public string Type { get; set; }
    public string Value { get; set; }
}

/// <summary>
/// 代码插入重写器——将函数调用插入模板代码
/// </summary>
public class CodeInsertionRewriter : CSharpSyntaxRewriter
{
    private readonly List<FunctionCall> _calls;
    private int _callIndex;
    
    public CodeInsertionRewriter(List<FunctionCall> calls)
    {
        _calls = calls;
        _callIndex = 0;
    }
    
    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        // 在方法体中插入函数调用
        if (node.Body != null)
        {
            var existingStatements = node.Body.Statements.ToList();
            var newStatements = new List<StatementSyntax>();
            
            foreach (var stmt in existingStatements)
            {
                // 替换占位符注释为生成的代码
                if (IsPlaceholder(stmt))
                {
                    while (_callIndex < _calls.Count)
                    {
                        var call = _calls[_callIndex++];
                        newStatements.Add(CreateCallStatement(call));
                    }
                }
                else
                {
                    newStatements.Add(stmt);
                }
            }
            
            var newBody = node.Body.WithStatements(SyntaxFactory.List(newStatements));
            return node.WithBody(newBody);
        }
        
        return base.VisitMethodDeclaration(node);
    }
    
    private bool IsPlaceholder(StatementSyntax stmt)
    {
        return stmt.ToString().Contains("TODO") || 
               stmt.ToString().Contains("// 填充代码");
    }
    
    private StatementSyntax CreateCallStatement(FunctionCall call)
    {
        var invocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(call.FunctionName));
        
        return SyntaxFactory.ExpressionStatement(invocation)
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
    }
}
```

---

### 3.5 强化学习训练循环 (RL Training Loop)

#### 3.5.1 CodeEnvironment（代码环境）

```csharp
/// <summary>
/// 代码环境——提供编译验证和奖励计算
/// 
/// 复用 RoslynMethod 的以下能力：
/// - DynamicCompilation: 动态编译
/// - CompilationErrorChecker: 编译错误检查
/// - SyntaxOptimizer: 语法优化
/// </summary>
public class CodeEnvironment
{
    /// <summary>
    /// 编译生成的代码并返回结果
    /// </summary>
    public CompilationResult Compile(string code)
    {
        // 复用 RoslynMethod 的 DynamicCompilation
        var compilation = new DynamicCompilation();
        return compilation.Compile(code);
    }
    
    /// <summary>
    /// 计算奖励
    /// </summary>
    public double ComputeReward(
        string generatedCode,
        string templateCode,
        OutputVectorSpace outputSpace)
    {
        double reward = 0;
        
        // 1. 编译成功奖励
        var compileResult = Compile(generatedCode);
        if (compileResult.Success)
        {
            reward += 10.0;
        }
        else
        {
            reward -= 5.0 * compileResult.Errors.Count;
            return reward; // 编译失败，不再计算其他奖励
        }
        
        // 2. 代码质量奖励（复用 RoslynMethod 的复杂度分析）
        double complexity = CalculateComplexity(generatedCode);
        double maxComplexity = 100.0;
        reward += 3.0 * (1.0 - Math.Min(complexity / maxComplexity, 1.0));
        
        // 3. 类型安全奖励
        if (compileResult.HasTypeErrors == false)
            reward += 2.0;
        
        // 4. 模板匹配奖励（如果提供了模板）
        if (!string.IsNullOrEmpty(templateCode))
        {
            double templateSimilarity = CalculateTemplateSimilarity(
                generatedCode, templateCode);
            reward += 5.0 * templateSimilarity;
        }
        
        // 5. 函数多样性奖励（鼓励使用不同的函数）
        double diversity = CalculateFunctionDiversity(outputSpace);
        reward += 2.0 * diversity;
        
        return reward;
    }
    
    private double CalculateComplexity(string code)
    {
        // 复用 RoslynMethod 的 ComplexityCalculator
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        
        double totalComplexity = 0;
        int count = 0;
        foreach (var method in methods)
        {
            totalComplexity += ComplexityCalculator.CalculateMethodComplexity(method);
            count++;
        }
        
        return count > 0 ? totalComplexity / count : 0;
    }
    
    private double CalculateTemplateSimilarity(string generated, string template)
    {
        // 基于 AST 结构相似度
        var genTree = CSharpSyntaxTree.ParseText(generated);
        var tmplTree = CSharpSyntaxTree.ParseText(template);
        
        var genMethods = genTree.GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        var tmplMethods = tmplTree.GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        
        if (genMethods.Count == 0 || tmplMethods.Count == 0)
            return 0;
        
        // 比较方法签名相似度
        double similarity = 0;
        foreach (var genMethod in genMethods)
        {
            foreach (var tmplMethod in tmplMethods)
            {
                if (genMethod.Identifier.Text == tmplMethod.Identifier.Text)
                {
                    similarity += 0.5;
                    if (genMethod.ParameterList.Parameters.Count == 
                        tmplMethod.ParameterList.Parameters.Count)
                        similarity += 0.5;
                }
            }
        }
        
        return Math.Min(similarity / tmplMethods.Count, 1.0);
    }
    
    private double CalculateFunctionDiversity(OutputVectorSpace outputSpace)
    {
        if (outputSpace.Vectors.Count <= 1)
            return 0;
        
        var uniqueFunctions = outputSpace.Vectors
            .Select(v => v.FunctionId)
            .Distinct()
            .Count();
        
        return (double)uniqueFunctions / outputSpace.Vectors.Count;
    }
}
```

#### 3.5.2 RL Training Loop

```csharp
/// <summary>
/// 强化学习训练循环
/// 
/// 复用 Agent 框架的以下能力：
/// - PPOFitting: PPO 策略优化
/// - PolicyGradientFitting: REINFORCE 策略梯度
/// - RuntimeEngine: 训练推理一体化
/// - TrajectoryStep: 轨迹记录
/// </summary>
public class CodeRLTrainer
{
    private readonly CodeGenerationAgent _agent;
    private readonly CodeEnvironment _environment;
    private readonly VectorToCodeConverter _converter;
    private readonly WeightConfig _weightConfig;
    
    /// <summary>轨迹缓冲区</summary>
    private List<TrajectoryStep> _trajectory;
    
    /// <summary>训练历史</summary>
    public List<double> RewardHistory { get; } = new();
    
    public CodeRLTrainer(
        CodeGenerationAgent agent,
        CodeEnvironment environment,
        VectorToCodeConverter converter,
        WeightConfig weightConfig)
    {
        _agent = agent;
        _environment = environment;
        _converter = converter;
        _weightConfig = weightConfig;
        _trajectory = new List<TrajectoryStep>();
    }
    
    /// <summary>
    /// 执行一个训练 episode
    /// </summary>
    public TrainResult RunEpisode(
        FunctionVectorSpace fusedSpace,
        string templateCode,
        int maxSteps = 20)
    {
        var episodeReward = 0.0;
        var tokenizer = new CodeTokenizer(fusedSpace);
        
        // Step 1: 编码模板为初始 token 序列
        var contextTokens = EncodeTemplate(templateCode, tokenizer);
        
        // Step 2: 自回归生成
        var generatedTokens = _agent.Generate(
            contextTokens,
            maxSteps: maxSteps,
            temperature: _weightConfig.Temperature);
        
        // Step 3: 解码为输出向量空间
        var selectedFunctions = tokenizer.Decode(generatedTokens);
        var outputSpace = BuildOutputSpace(selectedFunctions, generatedTokens);
        
        // Step 4: 转换为代码
        var generatedCode = _converter.Convert(outputSpace, templateCode);
        
        // Step 5: 计算奖励
        var reward = _environment.ComputeReward(
            generatedCode, templateCode, outputSpace);
        
        // Step 6: 记录轨迹
        RecordTrajectory(contextTokens, generatedTokens, reward);
        
        episodeReward += reward;
        RewardHistory.Add(episodeReward);
        
        return new TrainResult
        {
            GeneratedCode = generatedCode,
            Reward = reward,
            OutputSpace = outputSpace,
            EpisodeReward = episodeReward
        };
    }
    
    /// <summary>
    /// 执行策略更新（复用 Agent 的 PPOFitting）
    /// </summary>
    public void UpdatePolicy()
    {
        if (_trajectory.Count == 0) return;
        
        // 复用 Agent 的 PPOFitting 进行策略优化
        var ppoStrategy = new PPOFitting();
        // 将轨迹转换为训练数据并更新策略网络
        // ...
        
        _trajectory.Clear();
    }
    
    private int[] EncodeTemplate(string template, CodeTokenizer tokenizer)
    {
        if (string.IsNullOrEmpty(template))
            return new[] { CodeTokenizer.SOS_TOKEN };
        
        // 从模板中提取函数名
        var tree = CSharpSyntaxTree.ParseText(template);
        var root = tree.GetRoot();
        var functionNames = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(inv => (inv.Expression as IdentifierNameSyntax)?.Identifier.Text)
            .Where(name => name != null)
            .ToList();
        
        return tokenizer.Encode(functionNames);
    }
    
    private OutputVectorSpace BuildOutputSpace(
        List<FunctionVector> functions,
        int[] tokens)
    {
        var space = new OutputVectorSpace();
        
        foreach (var func in functions)
        {
            space.Vectors.Add(new OutputVector
            {
                FunctionId = func.Id,
                FunctionName = func.Name,
                Vector = func.GetFullVector(),
                Probability = 1.0 / functions.Count,
                Source = func.Source,
                SourceWeight = func.SourceWeight
            });
            
            space.FunctionTrace.Add(new FunctionTrace
            {
                FunctionId = func.Id,
                Source = func.Source,
                Weight = func.SourceWeight
            });
        }
        
        space.Confidence = functions.Count > 0 ? 1.0 / functions.Count : 0;
        space.Temperature = _weightConfig.Temperature;
        
        return space;
    }
    
    private void RecordTrajectory(
        int[] contextTokens,
        int[] generatedTokens,
        double reward)
    {
        // 记录每一步的轨迹
        for (int i = 0; i < generatedTokens.Length; i++)
        {
            _trajectory.Add(new TrajectoryStep
            {
                Action = generatedTokens[i],
                Reward = reward / generatedTokens.Length,
                Done = i == generatedTokens.Length - 1
            });
        }
    }
}

/// <summary>
/// 训练结果
/// </summary>
public class TrainResult
{
    public string GeneratedCode { get; set; }
    public double Reward { get; set; }
    public OutputVectorSpace OutputSpace { get; set; }
    public double EpisodeReward { get; set; }
}
```

---

### 3.6 主入口 (CodeRLFramework)

```csharp
/// <summary>
/// 自回归代码强化学习框架主入口
/// 
/// 整合所有组件，提供统一的 API
/// </summary>
public class CodeRLFramework
{
    private readonly VectorExtractor _extractor;
    private readonly WeightConfig _weightConfig;
    
    public CodeRLFramework(WeightConfig? weightConfig = null)
    {
        _extractor = new VectorExtractor();
        _weightConfig = weightConfig ?? new WeightConfig();
    }
    
    /// <summary>
    /// 执行代码生成
    /// </summary>
    /// <param name="codebase">代码库源文件列表</param>
    /// <param name="template">模板代码</param>
    /// <returns>生成结果</returns>
    public GenerationResult Generate(
        List<string> codebase,
        string template = "")
    {
        // Step 1: 提取代码库向量空间
        var codebaseSpace = new FunctionVectorSpace();
        foreach (var code in codebase)
        {
            var space = _extractor.ExtractFromCode(code, "codebase");
            codebaseSpace = MergeSpaces(codebaseSpace, space);
        }
        
        // Step 2: 提取模板向量空间
        FunctionVectorSpace templateSpace = null;
        if (!string.IsNullOrEmpty(template))
        {
            templateSpace = _extractor.ExtractFromCode(template, "template");
        }
        
        // Step 3: 加权融合
        var (alpha, beta) = _weightConfig.GetNormalizedWeights();
        var fusedSpace = templateSpace != null
            ? codebaseSpace.Fuse(templateSpace, alpha, beta)
            : codebaseSpace;
        
        // Step 4: 创建 Tokenizer
        var tokenizer = new CodeTokenizer(fusedSpace);
        
        // Step 5: 创建 Agent
        var agent = new CodeGenerationAgent(tokenizer);
        
        // Step 6: 编码模板
        var contextTokens = EncodeTemplate(template, tokenizer);
        
        // Step 7: 自回归生成
        var generatedTokens = agent.Generate(
            contextTokens,
            maxSteps: 50,
            temperature: _weightConfig.Temperature);
        
        // Step 8: 解码为输出向量空间
        var selectedFunctions = tokenizer.Decode(generatedTokens);
        var outputSpace = BuildOutputSpace(selectedFunctions, generatedTokens);
        
        // Step 9: 转换为代码
        var converter = new VectorToCodeConverter(fusedSpace, tokenizer);
        var generatedCode = converter.Convert(outputSpace, template);
        
        return new GenerationResult
        {
            Code = generatedCode,
            Confidence = outputSpace.Confidence,
            FunctionTrace = outputSpace.FunctionTrace,
            OutputSpace = outputSpace
        };
    }
    
    /// <summary>
    /// 执行强化学习训练
    /// </summary>
    public TrainResult Train(
        List<string> codebase,
        string template,
        int episodes = 10)
    {
        // 提取并融合向量空间
        var codebaseSpace = new FunctionVectorSpace();
        foreach (var code in codebase)
        {
            var space = _extractor.ExtractFromCode(code, "codebase");
            codebaseSpace = MergeSpaces(codebaseSpace, space);
        }
        
        var templateSpace = _extractor.ExtractFromCode(template, "template");
        var (alpha, beta) = _weightConfig.GetNormalizedWeights();
        var fusedSpace = codebaseSpace.Fuse(templateSpace, alpha, beta);
        
        // 创建组件
        var tokenizer = new CodeTokenizer(fusedSpace);
        var agent = new CodeGenerationAgent(tokenizer);
        var environment = new CodeEnvironment();
        var converter = new VectorToCodeConverter(fusedSpace, tokenizer);
        var trainer = new CodeRLTrainer(agent, environment, converter, _weightConfig);
        
        // 训练循环
        TrainResult bestResult = null;
        for (int episode = 0; episode < episodes; episode++)
        {
            var result = trainer.RunEpisode(fusedSpace, template);
            
            if (bestResult == null || result.Reward > bestResult.Reward)
                bestResult = result;
            
            // 策略更新
            trainer.UpdatePolicy();
        }
        
        return bestResult;
    }
    
    private FunctionVectorSpace MergeSpaces(
        FunctionVectorSpace a, FunctionVectorSpace b)
    {
        // 简单的空间合并（不进行加权融合）
        var result = new FunctionVectorSpace
        {
            VectorDimension = Math.Max(a.VectorDimension, b.VectorDimension),
            Source = $"Merge({a.Source}, {b.Source})"
        };
        
        result.Functions.AddRange(a.Functions);
        result.Functions.AddRange(b.Functions);
        
        return result;
    }
    
    private int[] EncodeTemplate(string template, CodeTokenizer tokenizer)
    {
        if (string.IsNullOrEmpty(template))
            return new[] { CodeTokenizer.SOS_TOKEN };
        
        var tree = CSharpSyntaxTree.ParseText(template);
        var root = tree.GetRoot();
        var functionNames = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(inv => (inv.Expression as IdentifierNameSyntax)?.Identifier.Text)
            .Where(name => name != null)
            .ToList();
        
        return tokenizer.Encode(functionNames);
    }
    
    private OutputVectorSpace BuildOutputSpace(
        List<FunctionVector> functions, int[] tokens)
    {
        var space = new OutputVectorSpace();
        
        foreach (var func in functions)
        {
            space.Vectors.Add(new OutputVector
            {
                FunctionId = func.Id,
                FunctionName = func.Name,
                Vector = func.GetFullVector(),
                Probability = 1.0 / Math.Max(functions.Count, 1),
                Source = func.Source,
                SourceWeight = func.SourceWeight
            });
            
            space.FunctionTrace.Add(new FunctionTrace
            {
                FunctionId = func.Id,
                Source = func.Source,
                Weight = func.SourceWeight
            });
        }
        
        space.Confidence = functions.Count > 0 ? 1.0 / functions.Count : 0;
        return space;
    }
}

/// <summary>
/// 生成结果
/// </summary>
public class GenerationResult
{
    public string Code { get; set; }
    public double Confidence { get; set; }
    public List<FunctionTrace> FunctionTrace { get; set; }
    public OutputVectorSpace OutputSpace { get; set; }
}
```

---

## 四、框架复用总结

### 4.1 复用 Agent 框架的能力

| 组件 | 复用方式 | 用途 |
|------|---------|------|
| `TransformerModel` | 直接复用 | 自回归代码生成，KV Cache 加速 |
| `TransformerModel.GenerateWithCache` | 直接复用 | 高效的 token 序列生成 |
| `TransformerModel.SampleToken` | 直接复用 | 温度采样、贪心采样 |
| `PPOFitting` | 扩展复用 | 策略优化，重要性采样 |
| `PolicyGradientFitting` | 扩展复用 | REINFORCE 策略梯度 |
| `RuntimeContext` | 直接复用 | 模型状态管理 |
| `RuntimeEngine` | 扩展复用 | 训练推理一体化 |
| `BaseFittingStrategy` | 继承复用 | 模板方法模式 |
| `BeamSearchSkeleton` | 直接复用 | 束搜索代码生成 |
| `SamplingSkeleton` | 直接复用 | 采样策略 |
| `AttentionSkeleton` | 直接复用 | 注意力计算 |
| `FeedForwardSkeleton` | 直接复用 | FFN 计算 |
| `EmbeddingSkeleton` | 直接复用 | Token/Position 嵌入 |
| `KVCache` | 直接复用 | KV 缓存加速 |
| `AgentFlow` | 扩展复用 | Pipeline 管线架构 |

### 4.2 复用 RoslynMethod 框架的能力

| 组件 | 复用方式 | 用途 |
|------|---------|------|
| `SyntaxElementExtractor` | 直接复用 | 提取方法、属性、字段 |
| `QuerySkeleton` | 直接复用 | 语法树查询 |
| `CodeTransformSkeleton` | 直接复用 | 创建方法声明、语句 |
| `SyntaxOptimizer` | 直接复用 | 语法优化、注释移除 |
| `EnhancedMethodMergeEngine` | 扩展复用 | 方法合并策略 |
| `DynamicCompilation` | 直接复用 | 编译验证 |
| `CompilationErrorChecker` | 直接复用 | 编译错误检查 |
| `ComplexityCalculator` | 直接复用 | 复杂度计算 |
| `PurityAnalyzer` | 直接复用 | 纯度分析 |
| `ReferenceAnalyzer` | 直接复用 | 引用分析 |
| `RoslynRefactoringFramework` | 扩展复用 | 代码重构规则 |
| `MethodTransformer` | 直接复用 | 方法转换 |

### 4.3 新增组件

| 组件 | 说明 |
|------|------|
| `FunctionVector` | 函数向量表示 |
| `FunctionVectorSpace` | 函数向量空间 |
| `VectorExtractor` | 向量提取器 |
| `CodeTokenizer` | 代码 Tokenizer |
| `CodeGenerationAgent` | 代码生成 Agent |
| `OutputVectorSpace` | 输出向量空间 |
| `VectorToCodeConverter` | 向量到代码转换器 |
| `CodeEnvironment` | 代码环境 |
| `CodeRLTrainer` | RL 训练器 |
| `CodeRLFramework` | 框架主入口 |

---

## 五、项目结构

```
CodeRLFramework/
├── CodeRLFramework.csproj
├── Input/
│   ├── CodebaseInput.cs
│   ├── TemplateInput.cs
│   └── WeightConfig.cs
├── VectorSpace/
│   ├── FunctionVector.cs
│   ├── FunctionVectorSpace.cs
│   └── VectorExtractor.cs
├── Generation/
│   ├── CodeTokenizer.cs
│   ├── CodeGenerationAgent.cs
│   └── OutputVectorSpace.cs
├── Output/
│   ├── VectorToCodeConverter.cs
│   └── CodeInsertionRewriter.cs
├── Training/
│   ├── CodeEnvironment.cs
│   ├── CodeRLTrainer.cs
│   └── TrainResult.cs
├── Core/
│   ├── CodeRLFramework.cs
│   └── GenerationResult.cs
└── Tests/
    └── CodeRLTests.cs
```

---

## 六、使用示例

```csharp
// ===== 示例 1: 基本代码生成 =====

// 准备代码库
var codebase = new List<string>
{
    @"
    public static class MathUtils
    {
        public static int[] Sort(int[] arr) { ... }
        public static int[] Filter(int[] arr, Func<int, bool> pred) { ... }
        public static int[] Map(int[] arr, Func<int, int> f) { ... }
        public static int Reduce(int[] arr, Func<int, int, int> f, int init) { ... }
    }",
    @"
    public static class StringUtils
    {
        public static string Join(string[] strs, string sep) { ... }
        public static string[] Split(string str, char sep) { ... }
    }"
};

// 准备模板
var template = @"
public class Solution {
    public int[] Process(int[] data) {
        // TODO: 填充代码
    }
}";

// 配置权重
var weights = new WeightConfig
{
    CodebaseWeight = 0.7,   // 代码库影响 70%
    TemplateWeight = 0.3,   // 模板影响 30%
    Temperature = 0.8       // 适度多样性
};

// 创建框架并生成
var framework = new CodeRLFramework(weights);
var result = framework.Generate(codebase, template);

Console.WriteLine(result.Code);
// 可能输出:
// public class Solution {
//     public int[] Process(int[] data) {
//         var sorted = Sort(data);
//         var filtered = Filter(sorted, x => x > 0);
//         return filtered;
//     }
// }


// ===== 示例 2: 强化学习训练 =====

var trainResult = framework.Train(
    codebase,
    template,
    episodes: 20);

Console.WriteLine($"最佳奖励: {trainResult.Reward}");
Console.WriteLine($"生成代码:\n{trainResult.GeneratedCode}");
```

---

## 七、权重控制机制总结

权重控制体现在三个层面：

### 7.1 输入层权重
- `CodebaseWeight (α)`: 控制代码库对生成的影响
- `TemplateWeight (β)`: 控制模板对生成的影响
- 归一化：`α' = α / (α + β)`, `β' = β / (α + β)`

### 7.2 向量空间融合权重
- 同名函数向量融合：`v_fused = α' · v_codebase + β' · v_template`
- 独有函数缩放：`v_scaled = α' · v`（仅代码库有）或 `v_scaled = β' · v`（仅模板有）

### 7.3 生成过程权重
- **探索率 ε**：控制 Agent 在生成时随机探索的概率
- **温度参数 T**：控制生成多样性，T 越高多样性越大
- **函数选择偏置**：对高频函数的偏好权重

### 7.4 权重控制公式总结

```
输入层:    α' = α / (α + β),  β' = β / (α + β)
融合层:    v_fused = α' · v_codebase + β' · v_template
生成层:    P(action) = softmax(logits / T)
探索:      P(explore) = ε
```

---

## 八、数据流图

```
┌──────────────┐    ┌──────────────────┐    ┌──────────────────┐
│  代码库文件   │───►│ RoslynMethod     │───►│ FunctionVector   │
│  (多个 .cs)  │    │ 语法分析         │    │ Space (Codebase) │
└──────────────┘    └──────────────────┘    └────────┬─────────┘
                                                     │
┌──────────────┐    ┌──────────────────┐    ┌────────▼─────────┐
│  模板代码     │───►│ RoslynMethod     │───►│ FunctionVector   │
│  (单个 .cs)  │    │ 语法分析         │    │ Space (Template) │
└──────────────┘    └──────────────────┘    └────────┬─────────┘
                                                     │
                                          ┌──────────▼──────────┐
                                          │   Weighted Fusion   │
                                          │  α·Space_A + β·Space_B │
                                          └──────────┬──────────┘
                                                     │
                                          ┌──────────▼──────────┐
                                          │   CodeTokenizer     │
                                          │  (函数 → Token ID)  │
                                          └──────────┬──────────┘
                                                     │
                                          ┌──────────▼──────────┐
                                          │  Transformer Model  │
                                          │  (自回归生成)        │
                                          └──────────┬──────────┘
                                                     │
                                          ┌──────────▼──────────┐
                                          │  OutputVectorSpace  │
                                          │  (Token → 函数向量)  │
                                          └──────────┬──────────┘
                                                     │
                                          ┌──────────▼──────────┐
                                          │  VectorToCode       │
                                          │  (向量 → 代码文本)   │
                                          └──────────┬──────────┘
                                                     │
                                          ┌──────────▼──────────┐
                                          │  生成的代码         │
                                          │  (可编译的 .cs)     │
                                          └─────────────────────┘
```

---

## 九、关键设计决策

### 9.1 为什么选择函数级 Tokenization？
- **粒度适中**：比字符级更高效，比类级更灵活
- **语义明确**：每个 token 对应一个有意义的函数调用
- **可组合性**：函数是代码组合的基本单元

### 9.2 为什么使用加权向量空间融合？
- **可解释性**：权重直接反映代码库和模板的影响程度
- **灵活性**：用户可以根据需求调整权重
- **可扩展性**：支持多个代码源的融合

### 9.3 为什么复用 Agent 的 Transformer？
- **成熟的自回归机制**：KV Cache、RoPE、温度采样
- **完整的 RL 支持**：PPO、REINFORCE、轨迹管理
- **Skeleton 架构**：可组合的计算单元

### 9.4 为什么复用 RoslynMethod？
- **完整的代码分析**：语法树、语义模型、复杂度
- **代码生成能力**：语法树创建、转换、优化
- **编译验证**：动态编译、错误检查

---

## 十、后续扩展方向

1. **多代码库融合**：支持多个代码库的加权融合
2. **层次化生成**：先选择结构（if/for/while），再选择函数
3. **增量学习**：在线微调策略网络
4. **代码质量评估**：集成更多代码质量指标
5. **测试生成**：自动生成测试用例验证代码正确性
6. **多语言支持**：扩展到 Python、Java 等语言
7. **分布式训练**：多 Agent 并行训练
8. **可视化**：生成过程的可视化追踪
