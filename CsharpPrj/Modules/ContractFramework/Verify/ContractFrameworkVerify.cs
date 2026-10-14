using ContractFramework.Abstractions;
using ContractFramework.Catalog;
using ContractFramework.Gate;
using ContractFramework.Registry;

namespace ContractFramework.Verify;

/// <summary>
/// 契约框架自证（正/负对照）——「元契约正/负对照（缺 carrier ⇒ ⊥）」的机器可复算实现。
/// <para>镜像 <c>ValidationFramework.Verify.FrameworkVerify</c>；零外部依赖（BCL-only）。</para>
/// </summary>
public static class ContractFrameworkVerify
{
    /// <summary>运行全部对照；返回是否全通过，并输出逐项摘要。</summary>
    public static bool Run(out string summary)
    {
        var results = new List<string>();
        var passed = true;
        passed &= Case("元契约·合法契约 ⇒ wellformed", WellFormedPositive, results);
        passed &= Case("元契约·缺 carrier ⇒ ⊥", MissingCarrier, results);
        passed &= Case("元契约·语义载体（语义词）⇒ ⊥", NarrativeCarrier, results);
        passed &= Case("元契约·缺 evidence.recomputeCmd ⇒ ⊥", MissingEvidence, results);
        passed &= Case("载体铁律·语义词检出", NarrativeDetection, results);
        passed &= Case("执行·未注册执行体 ⇒ ⊥", UnregisteredHost, results);
        passed &= Case("执行·执行体违例 ⇒ F（FailFast 抛出）", HostViolation, results);
        passed &= Case("执行·执行体通过 ⇒ T（放行）", HostPass, results);
        passed &= Case("加载·目录合并去重 + 索引", CatalogRoundTrip, results);
        passed &= Case("裁决·三值合取（⊥ 吸收）", VerdictConjunction, results);

        summary = $"ContractFrameworkVerify: {(passed ? "PASS" : "FAIL")}" + Environment.NewLine
            + string.Join(Environment.NewLine, results);
        return passed;
    }

    private static bool Case(string name, Func<bool> body, List<string> results)
    {
        try
        {
            var ok = body();
            results.Add($"[{(ok ? "PASS" : "FAIL")}] {name}");
            return ok;
        }
        catch (Exception ex)
        {
            results.Add($"[FAIL] {name}: {ex.Message}");
            return false;
        }
    }

    private static ContractDescriptor Valid(string id = "CF-1/Probe")
        => new(
            ContractId.Parse(id),
            "不变量：同父身份键唯一（可判定谓词）",
            ContractCarrier.Create("StructuralFact", ["已声明属性名集合", "模板解析后的同父键集合"]),
            ContractTier.Assembly,
            ContractEffect.FailFast,
            ContractHostRef.Create("DotNet", "ContractFramework.Verify.ContractFrameworkVerify"),
            ContractEvidence.Create("Invoke-GateCheck.ps1 -Gate ContractRegistry", "positive.json", "negative.json"));

    private static bool WellFormedPositive()
        => ContractWellFormedness.IsWellFormed(Valid());

    private static bool MissingCarrier()
    {
        var descriptor = new ContractDescriptor(
            ContractId.Parse("CF-1/MissingCarrier"),
            "sem",
            carrier: null,
            ContractTier.Assembly,
            ContractEffect.FailFast,
            ContractHostRef.Create("DotNet", "X"),
            ContractEvidence.Create("cmd", "p", "n"));
        return !ContractWellFormedness.IsWellFormed(descriptor, out var reason) && reason.Contains("Carrier");
    }

    private static bool NarrativeCarrier()
    {
        var carrier = ContractCarrier.Create("StructuralFact", ["动机：因为顺手，收益高，风险低"]);
        var descriptor = new ContractDescriptor(
            ContractId.Parse("CF-1/NarrativeCarrier"),
            "sem",
            carrier,
            ContractTier.Assembly,
            ContractEffect.FailFast,
            ContractHostRef.Create("DotNet", "X"),
            ContractEvidence.Create("cmd", "p", "n"));
        return !carrier.IsStructural && !ContractWellFormedness.IsWellFormed(descriptor, out _);
    }

    private static bool MissingEvidence()
    {
        var noCmd = new ContractDescriptor(
            ContractId.Parse("CF-1/NoRecompute"),
            "sem",
            ContractCarrier.Create("StructuralFact", ["计数"]),
            ContractTier.Gate,
            ContractEffect.Block,
            ContractHostRef.Create("PowerGate", "check.ps1"),
            ContractEvidence.Create(null, "p", "n"));

        var noControls = new ContractDescriptor(
            ContractId.Parse("CF-1/NoControls"),
            "sem",
            ContractCarrier.Create("StructuralFact", ["计数"]),
            ContractTier.Gate,
            ContractEffect.Block,
            ContractHostRef.Create("PowerGate", "check.ps1"),
            ContractEvidence.Create("cmd", null, null));

        return !ContractWellFormedness.IsWellFormed(noCmd, out _)
            && !noControls.Evidence!.IsComplete
            && !ContractWellFormedness.IsWellFormed(noControls, out _);
    }

    private static bool NarrativeDetection()
        => ContractCarrier.ContainsNarrative("风险：低，副作用：无")
            && !ContractCarrier.ContainsNarrative("已声明属性名集合");

    private static bool UnregisteredHost()
    {
        var registry = new ContractRegistry();
        var result = ContractGate.Execute([Valid()], ContractTier.Assembly, new ContractContext(), registry);
        return result.Verdict == ContractVerdict.Bottom
            && result.Unverifiable.Count == 1
            && !result.Released;
    }

    private static bool HostViolation()
    {
        var registry = new ContractRegistry();
        registry.RegisterHost(new FakeHost(ContractHostKind.DotNet, FakeMode.Violation));
        ContractDescriptor[] contracts = [Valid()];

        var result = ContractGate.Execute(contracts, ContractTier.Assembly, new ContractContext(), registry);
        var executed = result.Verdict == ContractVerdict.False && result.Violations.Count == 1;

        var threw = false;
        try
        {
            ContractGate.Enforce(contracts, ContractTier.Assembly, new ContractContext(), registry);
        }
        catch (ContractViolationException)
        {
            threw = true;
        }

        return executed && threw;
    }

    private static bool HostPass()
    {
        var registry = new ContractRegistry();
        registry.RegisterHost(new FakeHost(ContractHostKind.DotNet, FakeMode.Pass));
        ContractDescriptor[] contracts = [Valid()];

        var result = ContractGate.Execute(contracts, ContractTier.Assembly, new ContractContext(), registry);
        ContractGate.Enforce(contracts, ContractTier.Assembly, new ContractContext(), registry);
        return result.Verdict == ContractVerdict.True && result.Released;
    }

    private static bool CatalogRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cf-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string entry = """
                { "id": "INV-1/KeyIdentity", "semantics": "s",
                  "carrier": { "kind": "StructuralFact", "facts": ["键集合"] },
                  "tier": "Assembly", "effect": "FailFast",
                  "host": { "kind": "DotNet", "impl": "X" },
                  "evidence": { "recomputeCmd": "c", "positive": "p", "negative": "n" } }
                """;
            var contractsPath = Path.Combine(dir, "contracts.json");
            File.WriteAllText(contractsPath,
                "{ \"$schema\": \"contracts/v1\", \"contracts\": [" + entry + "," + entry + "] }");

            var direct = ContractCatalog.Load(contractsPath);
            var dedupedOk = direct.Descriptors.Count == 1
                && direct.Issues.Any(i => i.Kind == "DuplicateId")
                && direct.Descriptors[0].Id.Raw == "INV-1/KeyIdentity";

            var indexPath = Path.Combine(dir, "contracts.index.json");
            File.WriteAllText(indexPath, "{ \"$schema\": \"contracts-index/v1\", \"files\": [\"contracts.json\"] }");
            var viaIndex = ContractCatalog.LoadIndex(indexPath, dir);

            return dedupedOk && viaIndex.Descriptors.Count == 1;
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static bool VerdictConjunction()
        => ContractVerdictLogic.Conjunction(ContractVerdict.Bottom, ContractVerdict.True) == ContractVerdict.Bottom
            && ContractVerdictLogic.Conjunction(ContractVerdict.False, ContractVerdict.True) == ContractVerdict.False
            && ContractVerdictLogic.Conjunction(ContractVerdict.True, ContractVerdict.True) == ContractVerdict.True
            && !ContractVerdictLogic.IsReleased(ContractVerdict.Bottom);

    private enum FakeMode
    {
        Pass,
        Violation,
        Bottom,
    }

    private sealed class FakeHost : IContractHost
    {
        private readonly FakeMode _mode;

        public FakeHost(ContractHostKind kind, FakeMode mode)
        {
            Kind = kind;
            _mode = mode;
        }

        public ContractHostKind Kind { get; }

        public bool CarrierReady(ContractDescriptor contract, IContractContext context) => true;

        public ContractResult Evaluate(ContractDescriptor contract, IContractContext context)
        {
            var verdict = _mode switch
            {
                FakeMode.Violation => ContractVerdict.False,
                FakeMode.Bottom => ContractVerdict.Bottom,
                _ => ContractVerdict.True,
            };
            IReadOnlyList<ContractViolation> violations = verdict == ContractVerdict.False
                ? [new ContractViolation(contract.Id, "template-dup", "同父键冲突")]
                : [];
            var reason = verdict == ContractVerdict.Bottom ? "不可判定（对照）" : null;
            return ContractResult.From(
                [new ContractOutcome(contract.Id, contract.Tier, contract.Effect, verdict, violations, reason)]);
        }
    }
}
