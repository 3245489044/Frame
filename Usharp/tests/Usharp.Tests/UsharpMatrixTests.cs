using BasicMethod.Declarative.Spec;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Strategy.DecisionRules;
using Usharp.Matrix;
using Usharp.Semantics;

namespace Usharp.Tests;

/// <summary>
/// 批 U2：[Matrix] 驱动既有矩阵实例 —— 值域矩阵（ParameterMatrix）与转移矩阵（转移表）两读法。
/// 闸A 复用 ParameterMatrix.Row 的装载期判据（未声明坐标即失败），不另写第二份校验。
/// 判据读法（DecisionGraph）/治理读法（ModuleAxisMatrix）待 U2b（需真实消费方与空间词法扩展）。
/// </summary>
[TestClass]
public sealed class UsharpMatrixTests
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyVars = new Dictionary<string, object?>();

    private const string ValuesDoc = """
        [Space] Param: kind=values
          progress:ordinal(0..3)
          budget:ordinal(0..3)
          halt:flag

        [Matrix] ProgressMatrix: over=Param
          progress = progress + 1
          budget = budget + 1
          halt = halt
        """;

    private static UsharpCompilation Compile(string text)
        => UsharpCompilation.Compile(new[] { ("matrix.ushar", text) });

    /// <summary>装配单个矩阵并要求无诊断（失败时把报表带进断言消息）。</summary>
    private static UsharpMatrixBinding Single(string text)
    {
        var result = UsharpMatrixEmitter.Emit(Compile(text));
        Assert.IsFalse(result.HasErrors, result.Report());
        Assert.AreEqual(1, result.Bindings.Count, result.Report());
        return result.Bindings[0];
    }

    [TestMethod]
    public void ValueSpaceMatrix_InstantiatesParameterMatrix_WithSchemaFromDeclaration()
    {
        var binding = Single(ValuesDoc);

        Assert.AreEqual(UsharpMatrixKind.Parameter, binding.Kind);
        Assert.AreEqual("Param", binding.OverSpaceId);
        Assert.AreEqual(3, binding.RowCount, "行数须与 [Matrix] 缩进块声明逐条一致");
        CollectionAssert.AreEqual(new[] { "progress", "budget", "halt" },
            binding.Rows.Select(r => r.OutputKey).ToArray());

        var schema = binding.Schema!;
        CollectionAssert.AreEqual(new[] { "progress", "budget", "halt" },
            schema.Specs.Select(s => s.Name).ToArray(), "坐标系须与 [Space] 声明逐条一致");

        var progress = schema.Get("progress");
        Assert.AreEqual(ParameterKind.Ordinal, progress.Kind);
        Assert.AreEqual(0d, progress.Min);
        Assert.AreEqual(3d, progress.Max);
        Assert.AreEqual(ParameterKind.Flag, schema.Get("halt").Kind, ":flag → ParameterKind.Flag");

        Assert.AreEqual(3, binding.ParameterMatrix!.RowCount);
    }

    [TestMethod]
    public void ValueSpaceMatrix_RowExpression_IsLinearBiasAndTerms()
    {
        var binding = Single(ValuesDoc);

        var progress = binding.Rows.Single(r => r.OutputKey == "progress");
        Assert.AreEqual(1d, progress.Expression.Bias, "`+ 1` 须解析为偏置");
        Assert.AreEqual(1, progress.Expression.Terms.Count);
        Assert.AreEqual("progress", progress.Expression.Terms[0].Axis);
        Assert.AreEqual(1d, progress.Expression.Terms[0].Weight);
        CollectionAssert.AreEqual(new[] { "progress" }, progress.InputRefs.ToArray());

        var halt = binding.Rows.Single(r => r.OutputKey == "halt");
        Assert.AreEqual(0d, halt.Expression.Bias, "`halt = halt` 为纯承接（无偏置）");
        Assert.AreEqual(1d, halt.Expression.Terms[0].Weight);
    }

    [TestMethod]
    public void ValueSpaceMatrix_ApplyTo_AdvancesWholeVector()
    {
        var binding = Single(ValuesDoc);
        var schema = binding.Schema!;
        var matrix = binding.ParameterMatrix!;

        var step0 = ParameterState.CreateDefault(schema);
        var step1 = step0.Apply(matrix.ApplyTo(new ParametricStepInput(step0, 0, EmptyVars)));
        Assert.AreEqual(1d, step1.Get("progress"));
        Assert.AreEqual(1d, step1.Get("budget"));
        Assert.AreEqual(0d, step1.Get("halt"), "承接行不改变分量");

        var step2 = step1.Apply(matrix.ApplyTo(new ParametricStepInput(step1, 1, EmptyVars)));
        Assert.AreEqual(2d, step2.Get("progress"), "整向量一次变换（非逐条顺序 if）");
        Assert.AreEqual(2d, step2.Get("budget"));
    }

    [TestMethod]
    public void ValueSpaceMatrix_UndeclaredCoordinate_FailsGateA_WithAttribution()
    {
        var compilation = Compile("""
            [Space] Param: kind=values
              progress:ordinal(0..3)

            [Matrix] M: over=Param
              progress = progress + ghost
            """);

        // 闸A 主判据在装载期（UsharpValidator）
        Assert.IsTrue(compilation.Errors.Any(d => d.Code == UsharpCodes.MatrixRowInputUndeclared),
            "UsharpValidator 须报 US1004：\n" + compilation.Report());

        // 装配期闸A（ParameterMatrix.Row）失败须同样可归因
        var result = UsharpMatrixEmitter.Emit(compilation);
        Assert.IsTrue(result.Diagnostics.Any(d =>
                d.Code == UsharpCodes.MatrixRowInputUndeclared && d.Message.Contains("装载期闸A")),
            "装配期须归因为 US1004（含 ParameterMatrix.Row 报文）：\n" + result.Report());
    }

    [TestMethod]
    public void KeySpaceMatrix_ProducesTransitionTable()
    {
        var binding = Single("""
            [Space] Step: kind=keys
              start
              guard_input
              provide_prompt

            [Matrix] Chain: over=Step
              guard_input = start
              provide_prompt = guard_input
            """);

        Assert.AreEqual(UsharpMatrixKind.Transition, binding.Kind);
        Assert.IsNull(binding.ParameterMatrix, "键型空间不产 ParameterMatrix（坐标无值域、不可量化）");
        Assert.IsNull(binding.Schema);

        var table = binding.TransitionTable!;
        Assert.AreEqual(2, table.Count);
        Assert.AreEqual("start", table["guard_input"]);
        Assert.AreEqual("guard_input", table["provide_prompt"]);
    }

    [TestMethod]
    public void KeySpaceMatrix_NonTransitionShape_ReportsUS0004()
    {
        var result = UsharpMatrixEmitter.Emit(Compile("""
            [Space] Step: kind=keys
              start
              guard_input

            [Matrix] Chain: over=Step
              guard_input = start + 1
            """));

        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == UsharpCodes.MatrixRowInvalid),
            "转移矩阵行须为 `next = cur` 形态，否则 US0004：\n" + result.Report());
    }

    [TestMethod]
    public void ValueSpaceMatrix_UnparsableRowExpression_ReportsUS0004()
    {
        var result = UsharpMatrixEmitter.Emit(Compile("""
            [Space] Param: kind=values
              progress:ordinal(0..3)

            [Matrix] M: over=Param
              progress = progress + * 2
            """));

        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == UsharpCodes.MatrixRowInvalid),
            "不可解析的行右式须报 US0004：\n" + result.Report());
    }

    [TestMethod]
    public void ValueSpaceMatrix_DuplicateAxisInRow_MergesWeights()
    {
        var binding = Single("""
            [Space] Param: kind=values
              progress:ordinal(0..3)
              budget:ordinal(0..3)

            [Matrix] M: over=Param
              progress = progress + progress + budget * 2
            """);

        var row = binding.Rows.Single();
        Assert.AreEqual(2, row.Expression.Terms.Count, "重复坐标合并为一项");
        Assert.AreEqual(2d, row.Expression.Terms.Single(t => t.Axis == "progress").Weight);
        Assert.AreEqual(2d, row.Expression.Terms.Single(t => t.Axis == "budget").Weight);
    }

    [TestMethod]
    public void Sample_AgentLoop_ProgressMatrix_IsBindable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "agent-loop.ushar");
        Assert.IsTrue(File.Exists(path), $"样例缺失（测试输出未见 samples）：{path}");

        var compilation = UsharpCompilation.Compile(new[] { (path, File.ReadAllText(path)) });
        var result = UsharpMatrixEmitter.Emit(compilation);

        Assert.IsFalse(result.HasErrors, result.Report());
        Assert.IsTrue(result.TryGet("ProgressMatrix", out var binding), result.Report());
        Assert.AreEqual(UsharpMatrixKind.Parameter, binding.Kind);
        Assert.AreEqual("Param", binding.OverSpaceId);
        Assert.AreEqual(3, binding.RowCount, "ProgressMatrix 声明 3 行（progress/budget/halt）");
        CollectionAssert.AreEqual(new[] { "progress", "budget", "halt" },
            binding.Rows.Select(r => r.OutputKey).ToArray());
    }
}
