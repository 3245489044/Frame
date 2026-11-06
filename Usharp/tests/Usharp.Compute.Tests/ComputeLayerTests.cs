using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.DslMethod.Model;
using Usharp.Compute;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Compute.Tests;

[TestClass]
public sealed class ComputeLayerTests
{
    private static readonly IUsharpDomainPack[] BasePacks = { UsharpDomainPack.Instance };
    private static readonly IUsharpDomainPack[] ComputePacks = { UsharpDomainPack.Instance, new UsharpComputePack() };

    // ── 辅助 ───────────────────────────────────────────────────────────────

    private static DslDocument[] ParseDocuments(params string[] texts)
    {
        var scope = UsharpPrefixScope.Resolve(ComputePacks);
        var docs = new DslDocument[texts.Length];
        for (var i = 0; i < texts.Length; i++)
            docs[i] = UsharpParser.Parse(texts[i], $"f{i}.usharp", UsharpParser.DefaultLib, scope);
        return docs;
    }

    private static ComputeCompileResult CompileCompute(string text, Func<string, string, bool>? exists = null)
        => UsharpComputeCompiler.Compile(ParseDocuments(text), exists);

    private static UsharpCompilation CompileWithCompute(string text, string path = "t.usharp")
        => UsharpCompilation.CompileSources(
            new[] { new UsharpSourceFile(path, text) }, ComputePacks);

    private static string[] Codes(UsharpCompilation compilation)
        => compilation.Diagnostics.Select(d => d.Code).ToArray();

    private static string[] Codes(ComputeCompileResult result)
        => result.Diagnostics.Select(d => d.Code).ToArray();

    /// <summary>可复算的「定义集」快照：声明序 + 种类 + 名 + 行 + 字段（发射层的输入面）。</summary>
    private static string DefinitionSet(UsharpCompilation compilation)
        => string.Join("\n", compilation.Declarations.All.Select(d =>
            $"{d.Kind}|{d.Name}|{d.LineNumber}|{string.Join(";", d.Fields.Select(f => f.Key + "=" + f.Value))}"));

    private static ComputeUnitIr SingleUnit(ComputeCompileResult result, string calcId)
    {
        var program = result.FindProgram(calcId);
        Assert.IsNotNull(program, $"程序 {calcId} 未产出（诊断：{string.Join(",", Codes(result))}）");
        return program!.FindUnit(calcId)!;
    }

    // ── ① 纯声明零差异 ─────────────────────────────────────────────────────

    private const string PureDeclaration =
        "[Space] s1: coords=x,y | parent=root\n"
        + "[Node] n1: in=a | out=b\n"
        + "[Branch] br1: judge=selector_a | then=t1 | else=t2\n";

    [TestMethod]
    public void PureDeclaration_DiagnosticsAndEmittedJson_IdenticalWithComputePack()
    {
        var without = UsharpCompilation.CompileSources(
            new[] { new UsharpSourceFile("pure.usharp", PureDeclaration) }, BasePacks);
        var with = UsharpCompilation.CompileSources(
            new[] { new UsharpSourceFile("pure.usharp", PureDeclaration) }, ComputePacks);

        CollectionAssert.AreEqual(
            without.Diagnostics.Select(d => d.ToString()).ToArray(),
            with.Diagnostics.Select(d => d.ToString()).ToArray(),
            "装载计算包不改变纯声明文件的诊断");

        Assert.AreEqual(without.Declarations.All.Count, with.Declarations.All.Count);
        Assert.IsTrue(DefinitionSet(without) == DefinitionSet(with),
            "装载计算包不改变既有定义集（发射层输入面）");
        Assert.AreEqual(0, UsharpComputeCompiler.Compile(with.Documents).Programs.Count,
            "纯声明文件不产生任何计算程序");
        Assert.AreEqual(false, UsharpComputeClassifier.HasComputation(with.Documents[0]));
    }

    [TestMethod]
    public void FencedComputeMarker_IsNotAUnit_AndNotBoundaryViolation()
    {
        var text =
            "# 说明\n```\n[Compute] ghost: kind=expr | returns=int | pure=strict\n  return 1\n```\n"
            + "[Node] n1: in=a | out=b\n";
        var compilation = CompileWithCompute(text);
        Assert.IsFalse(UsharpComputeClassifier.HasComputation(compilation.Documents[0]));
        Assert.IsFalse(Codes(compilation).Contains(ComputeCodes.BoundaryViolation));
    }

    // ── ② 表达式层 → IR/字节码/VM ───────────────────────────────────────────

    [TestMethod]
    public void Expr_Arithmetic_EvaluatesWithPrecedence()
    {
        var result = CompileCompute(
            "[Compute] seven: kind=expr | returns=int | pure=strict\n"
            + "  return 1 + 2 * 3\n");
        Assert.AreEqual(0, result.Diagnostics.Count, string.Join(";", Codes(result)));
        var program = result.FindProgram("seven")!;
        var vm = new ComputeVm(program);
        var run = vm.Run();
        Assert.AreEqual(ComputeStatus.Completed, run.Status, run.Message);
        Assert.AreEqual(7L, run.Output!.IntValue);
    }

    [TestMethod]
    public void Program_CanonicalJson_IsDeterministic_And_Versioned()
    {
        var text = "[Compute] seven: kind=expr | returns=int | pure=strict\n  return 1 + 2 * 3\n";
        var first = CompileCompute(text).FindProgram("seven")!;
        var second = CompileCompute(text).FindProgram("seven")!;

        var json = ComputeProgramCodec.ToCanonicalJson(first);
        Assert.AreEqual(json, ComputeProgramCodec.ToCanonicalJson(second));
        Assert.AreEqual(first.Hash, second.Hash);
        StringAssert.Contains(json, "\"ir\":\"compute-ir/1\"");
        StringAssert.Contains(json, "\"bytecodeVersion\":1");
        StringAssert.Contains(json, "\"vmVersion\":1");

        var decoded = ComputeProgramCodec.TryDecode(json, first.Hash);
        Assert.IsTrue(decoded.Ok, decoded.Message);
        Assert.AreEqual(7L, new ComputeVm(decoded.Program!).Run().Output!.IntValue);
    }

    [TestMethod]
    public void Loop_StepMustBePositiveIntegerLiteral()
    {
        var zero = CompileCompute(
            "[Compute] f: kind=fn | returns=int | pure=strict | budget=10\n"
            + "  var acc = 0\n"
            + "  loop i from 0 to 5 step 0 do\n"
            + "    set acc = acc + i\n"
            + "  end\n"
            + "  return acc\n");
        CollectionAssert.Contains(Codes(zero), ComputeCodes.LoopInvalid);

        var fractional = CompileCompute(
            "[Compute] f: kind=fn | returns=int | pure=strict | budget=10\n"
            + "  var acc = 0\n"
            + "  loop i from 0 to 5 step 1.5 do\n"
            + "    set acc = acc + i\n"
            + "  end\n"
            + "  return acc\n");
        CollectionAssert.Contains(Codes(fractional), ComputeCodes.LoopInvalid);
    }

    [TestMethod]
    public void Program_VersionMismatch_IsRejected()
    {
        var program = CompileCompute(
            "[Compute] seven: kind=expr | returns=int | pure=strict\n  return 7\n")
            .FindProgram("seven")!;
        var json = ComputeProgramCodec.ToJson(program)
            .Replace("\"bytecodeVersion\":1", "\"bytecodeVersion\":2", StringComparison.Ordinal);
        var decoded = ComputeProgramCodec.TryDecode(json);
        Assert.IsFalse(decoded.Ok);
        Assert.AreEqual(ComputeCodes.VersionRejected, decoded.Code);
    }

    [TestMethod]
    public void Program_TamperedJson_IsRejected()
    {
        var program = CompileCompute(
            "[Compute] seven: kind=expr | returns=int | pure=strict\n  return 1 + 2 * 3\n")
            .FindProgram("seven")!;
        var json = ComputeProgramCodec.ToJson(program).Replace("push_int", "push_str",
            StringComparison.Ordinal);
        var decoded = ComputeProgramCodec.TryDecode(json);
        Assert.IsFalse(decoded.Ok);
        Assert.AreEqual(ComputeCodes.VersionRejected, decoded.Code);
    }

    [TestMethod]
    public void Header_ExpressionInDeclarationValue_IsBoundaryViolation()
    {
        var result = CompileCompute(
            "[Compute] bad: kind=expr | returns=int | pure=strict | budget=1+2\n"
            + "  return 1\n");
        CollectionAssert.Contains(Codes(result), ComputeCodes.HeaderInvalid);
    }

    [TestMethod]
    public void Header_UnknownField_IsRejected()
    {
        var result = CompileCompute(
            "[Compute] bad: kind=expr | returns=int | pure=strict | body_expr=x\n"
            + "  return 1\n");
        CollectionAssert.Contains(Codes(result), ComputeCodes.HeaderInvalid);
    }

    [TestMethod]
    public void Type_ImplicitPromotion_IsRejected()
    {
        var result = CompileCompute(
            "[Compute] bad: kind=expr | returns=int | pure=strict\n"
            + "  return 1 + 1.5\n");
        CollectionAssert.Contains(Codes(result), ComputeCodes.TypeMismatch);
    }

    // ── ③ 绑定与赋值 ───────────────────────────────────────────────────────

    [TestMethod]
    public void Let_Var_Set_And_If_Work()
    {
        var result = CompileCompute(
            "[Compute] classify: kind=fn | args=n:int | returns=int | pure=strict\n"
            + "  var acc = 0\n"
            + "  let limit = 10\n"
            + "  if n > limit then\n"
            + "    set acc = 1\n"
            + "  else\n"
            + "    set acc = 2\n"
            + "  end\n"
            + "  return acc\n");
        Assert.AreEqual(0, result.Diagnostics.Count, string.Join(";", Codes(result)));
        var program = result.FindProgram("classify")!;
        Assert.AreEqual(1L, new ComputeVm(program, null, new[] { ComputeValue.Int(20) }).Run().Output!.IntValue);
        Assert.AreEqual(2L, new ComputeVm(program, null, new[] { ComputeValue.Int(5) }).Run().Output!.IntValue);
    }

    [TestMethod]
    public void WriteImmutable_IsRejected()
    {
        var result = CompileCompute(
            "[Compute] f: kind=fn | returns=int | pure=strict\n"
            + "  let x = 1\n"
            + "  set x = 2\n"
            + "  return x\n");
        CollectionAssert.Contains(Codes(result), ComputeCodes.WriteImmutable);
    }

    [TestMethod]
    public void ReadBeforeDefine_IsRejected()
    {
        var result = CompileCompute(
            "[Compute] f: kind=fn | args=c:bool | returns=int | pure=strict\n"
            + "  if c then\n"
            + "    var x = 1\n"
            + "  end\n"
            + "  return x\n");
        CollectionAssert.Contains(Codes(result), ComputeCodes.ReadBeforeDefine);
    }

    [TestMethod]
    public void DuplicateBinding_IsRejected()
    {
        var result = CompileCompute(
            "[Compute] f: kind=fn | returns=int | pure=strict\n"
            + "  let x = 1\n"
            + "  let x = 2\n"
            + "  return x\n");
        CollectionAssert.Contains(Codes(result), ComputeCodes.BindingDuplicate);
    }

    [TestMethod]
    public void While_IsExplicitlyNotEnabled()
    {
        var result = CompileCompute(
            "[Compute] f: kind=fn | returns=int | pure=strict | budget=10\n"
            + "  var i = 0\n"
            + "  while i < 10 do\n"
            + "    set i = i + 1\n"
            + "  end\n"
            + "  return i\n");
        CollectionAssert.Contains(Codes(result), ComputeCodes.SyntaxInvalid);
        StringAssert.Contains(string.Join(";", result.Diagnostics.Select(d => d.Message)), "第 4 步");
    }

    [TestMethod]
    public void RecursionCycle_IsRejected()
    {
        var result = CompileCompute(
            "[Compute] a: kind=fn | returns=int | pure=strict\n"
            + "  return b(1)\n"
            + "[Compute] b: kind=fn | args=x:int | returns=int | pure=strict\n"
            + "  return a()\n");
        CollectionAssert.Contains(Codes(result), ComputeCodes.RecursionNotEnabled);
    }

    // ── ④ 有界循环 + 预算/切片/快照 ─────────────────────────────────────────

    [TestMethod]
    public void Loop_SumsRange_WithStaticIterations()
    {
        var result = CompileCompute(
            "[Compute] sum: kind=fn | returns=int | pure=strict | budget=10000\n"
            + "  var acc = 0\n"
            + "  loop i from 0 to 5 do\n"
            + "    set acc = acc + i\n"
            + "  end\n"
            + "  return acc\n");
        Assert.AreEqual(0, result.Diagnostics.Count, string.Join(";", Codes(result)));
        var unit = SingleUnit(result, "sum");
        Assert.AreEqual(1, unit.Loops.Count);
        Assert.AreEqual(5L, unit.Loops[0].Iterations);
        Assert.AreEqual(10L, new ComputeVm(result.FindProgram("sum")!).Run().Output!.IntValue);
    }

    [TestMethod]
    public void Loop_WithoutBudget_IsRejected()
    {
        var result = CompileCompute(
            "[Compute] sum: kind=fn | returns=int | pure=strict\n"
            + "  var acc = 0\n"
            + "  loop i from 0 to 4 do\n"
            + "    set acc = acc + i\n"
            + "  end\n"
            + "  return acc\n");
        CollectionAssert.Contains(Codes(result), ComputeCodes.BudgetMissing);
    }

    [TestMethod]
    public void BudgetExhaustion_StopsAtBudget()
    {
        var result = CompileCompute(
            "[Compute] sum: kind=fn | returns=int | pure=strict | budget=8\n"
            + "  var acc = 0\n"
            + "  loop i from 0 to 100 do\n"
            + "    set acc = acc + i\n"
            + "  end\n"
            + "  return acc\n");
        Assert.AreEqual(0, result.Diagnostics.Count, string.Join(";", Codes(result)));
        var unit = SingleUnit(result, "sum");
        var run = new ComputeVm(result.FindProgram("sum")!).Run();
        Assert.AreEqual(ComputeStatus.BudgetExceeded, run.Status);
        Assert.AreEqual(ComputeCodes.BudgetExceeded, run.Code);
        Assert.AreEqual(unit.Budget, run.Steps);
    }

    [TestMethod]
    public void SliceSuspend_SnapshotResume_SameResult()
    {
        var result = CompileCompute(
            "[Compute] sum: kind=fn | returns=int | pure=strict | budget=10000\n"
            + "  var acc = 0\n"
            + "  loop i from 0 to 10 do\n"
            + "    set acc = acc + i\n"
            + "  end\n"
            + "  return acc\n");
        var program = result.FindProgram("sum")!;

        var full = new ComputeVm(program).Run();
        Assert.AreEqual(45L, full.Output!.IntValue);

        var vm = new ComputeVm(program);
        var suspended = vm.Step(4);
        Assert.AreEqual(ComputeStatus.Suspended, suspended.Status);
        var snapshot = vm.ExportSnapshot();
        var json = ComputeSnapshotCodec.ToJson(snapshot);

        var loaded = ComputeVm.TryResume(program, null, json, out var resumed);
        Assert.IsTrue(loaded.Accepted, loaded.Message + "（快照应可续跑）");
        var done = resumed!.Run();
        Assert.AreEqual(ComputeStatus.Completed, done.Status, done.Message);
        Assert.AreEqual(45L, done.Output!.IntValue);
    }

    [TestMethod]
    public void Snapshot_VersionMismatch_RejectedAndMarked()
    {
        var result = CompileCompute(
            "[Compute] seven: kind=expr | returns=int | pure=strict\n"
            + "  return 7\n");
        var program = result.FindProgram("seven")!;
        var vm = new ComputeVm(program);
        vm.Step(1);
        var json = ComputeSnapshotCodec.ToJson(vm.ExportSnapshot())
            .Replace("\"snapshotVersion\":1", "\"snapshotVersion\":99", StringComparison.Ordinal);

        var decoded = ComputeSnapshotCodec.TryDecode(json, program);
        Assert.IsFalse(decoded.Accepted);
        Assert.AreEqual(ComputeCodes.VersionRejected, decoded.Code);
        Assert.AreEqual(ComputeCodes.StateDiscarded, decoded.MarkCode);
    }

    // ── ⑤ 宿主边界与效果 ───────────────────────────────────────────────────

    [TestMethod]
    public void HostBoundary_DecimalIsExplicitlyQuantized()
    {
        var result = CompileCompute(
            "[Compute] money: kind=expr | returns=decimal | pure=strict\n"
            + "  return 0.1 + 0.2\n");
        Assert.AreEqual(0, result.Diagnostics.Count, string.Join(";", Codes(result)));
        var run = new ComputeVm(result.FindProgram("money")!).Run();
        Assert.AreEqual("0.300000", ComputeValue.DecimalText(run.Output!.DecimalValue));

        Assert.IsTrue(ComputeHostBoundary.TryFromMatrixDouble(0.1 + 0.2, out var converted, out var trace));
        Assert.AreEqual(ComputeHostBoundary.DecimalScale, trace.Scale);
        Assert.AreEqual(ComputeHostBoundary.RoundingRule, trace.Rounding);
        Assert.AreEqual("from_matrix", trace.Note);
        Assert.AreEqual(0.300000m, converted);
    }

    [TestMethod]
    public void HostBoundary_ExplicitConversion_LeavesTrace()
    {
        var result = CompileCompute(
            "[Compute] h: kind=expr | returns=host | pure=strict\n"
            + "  return to_host(1)\n");
        Assert.AreEqual(0, result.Diagnostics.Count, string.Join(";", Codes(result)));
        var run = new ComputeVm(result.FindProgram("h")!).Run();
        Assert.AreEqual(1, run.Conversions.Count);
        Assert.AreEqual("int", run.Conversions[0].From);
        Assert.AreEqual("host", run.Conversions[0].To);
        Assert.AreEqual("none", run.Conversions[0].Rounding);
        Assert.AreEqual("vm.convert", run.Conversions[0].Note);
    }

    [TestMethod]
    public void HostBoundary_ImplicitMixing_IsRejected()
    {
        var exists = (string space, string key) => true;
        var result = CompileCompute(
            "[Compute] f: kind=fn | returns=int | pure=scoped | effects=ks.read\n"
            + "  return $S.k + 1\n", exists);
        CollectionAssert.Contains(Codes(result), ComputeCodes.TypeMismatch,
            "诊断：" + string.Join(";", result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    public void Effect_StrictPurity_IsRejected()
    {
        var result = CompileCompute(
            "[Compute] f: kind=proc | pure=strict | effects=emit\n"
            + "  emit(\"ch\", to_host(1))\n");
        CollectionAssert.Contains(Codes(result), ComputeCodes.PurityInvalid);
    }

    [TestMethod]
    public void Effect_Undeclared_IsRejected()
    {
        var result = CompileCompute(
            "[Compute] f: kind=proc | pure=scoped\n"
            + "  emit(\"ch\", to_host(1))\n");
        CollectionAssert.Contains(Codes(result), ComputeCodes.EffectUndeclared);
    }

    [TestMethod]
    public void Effect_MissingPort_IsRuntimeDenied()
    {
        var result = CompileCompute(
            "[Compute] f: kind=proc | pure=impure | effects=emit\n"
            + "  emit(\"ch\", to_host(1))\n");
        Assert.AreEqual(0, result.Diagnostics.Count, string.Join(";", Codes(result)));
        var run = new ComputeVm(result.FindProgram("f")!).Run();
        Assert.AreEqual(ComputeStatus.EffectDenied, run.Status);
        Assert.AreEqual(ComputeCodes.EffectDenied, run.Code);
    }

    // ── ⑥ 边界判据与清单 ───────────────────────────────────────────────────

    [TestMethod]
    public void Boundary_IndentedComputeMarker_IsRejected()
    {
        var text =
            "[Node] n1: in=a | out=b\n"
            + "  [Compute] hidden: kind=expr | returns=int | pure=strict\n"
            + "    return 1\n";
        var compilation = CompileWithCompute(text);
        CollectionAssert.Contains(Codes(compilation), ComputeCodes.BoundaryViolation);
        Assert.IsFalse(UsharpComputeClassifier.HasComputation(compilation.Documents[0]));
    }

    [TestMethod]
    public void Boundary_TopLevelStatementWord_InComputeFile_IsRejected()
    {
        var compilation = CompileWithCompute(
            "[Compute] c: kind=expr | returns=int | pure=strict\n"
            + "  return 1\n"
            + "let stray = 2\n");
        CollectionAssert.Contains(Codes(compilation), ComputeCodes.BoundaryViolation);
    }

    [TestMethod]
    public void Boundary_ReservedField_InComputeFile_IsRejected()
    {
        var compilation = CompileWithCompute(
            "[Compute] c: kind=expr | returns=int | pure=strict\n"
            + "  return 1\n"
            + "[Node] n1: expr=1+2 | in=a\n");
        CollectionAssert.Contains(Codes(compilation), ComputeCodes.BoundaryViolation);
    }

    [TestMethod]
    public void Boundary_JudgeExpression_OnlyRejectedInsideComputeFile()
    {
        const string pure = "[Branch] br: judge=a+b | then=t1 | else=t2\n";
        var pureCompilation = CompileWithCompute(pure);
        Assert.IsFalse(Codes(pureCompilation).Contains(ComputeCodes.BoundaryViolation));

        var mixed = CompileWithCompute(
            "[Compute] c: kind=expr | returns=int | pure=strict\n"
            + "  return 1\n" + pure);
        CollectionAssert.Contains(Codes(mixed), ComputeCodes.BoundaryViolation);
    }

    [TestMethod]
    public void Manifest_ListsOnlyComputeFiles_AndIsCanonical()
    {
        var docs = ParseDocuments(
            "[Node] n1: in=a | out=b\n",
            "[Compute] c: kind=expr | returns=int | pure=strict\n  return 1\n");
        var result = UsharpComputeCompiler.Compile(docs);
        Assert.AreEqual(0, result.Diagnostics.Count, string.Join(";", Codes(result)));

        var json = ComputeManifest.ToCanonicalJson(docs, result);
        Assert.AreEqual(json, ComputeManifest.ToCanonicalJson(docs, result));
        StringAssert.Contains(json, "\"manifestVersion\":1");
        StringAssert.Contains(json, "\"usharp.purity\":\"computation-isolated\"");
        StringAssert.Contains(json, "\"usharp.compute.units\":1");
        Assert.IsFalse(json.Contains("\"file\":\"f0.usharp\"", StringComparison.Ordinal),
            "纯声明文件不生成清单条目");
    }
}
