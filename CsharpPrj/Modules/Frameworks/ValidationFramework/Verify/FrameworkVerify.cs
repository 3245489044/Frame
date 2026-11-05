
using System.Text.Json;
using ValidationFramework.Abstractions;
using ValidationFramework.Factory;
using ValidationFramework.Providers;

namespace ValidationFramework.Verify;

public static class FrameworkVerify
{
    public static bool Run(out string summary)
    {
        var results = new List<string>();
        var allPassed = true;

        allPassed &= VerifyEngineAssembly(results);
        allPassed &= VerifyContractRules(results);
        allPassed &= VerifyPipeline(results);
        allPassed &= VerifyFeedback(results);

        summary = $"FrameworkVerify: {(allPassed ? "PASS" : "FAIL")}\n" +
                  string.Join("\n", results);
        return allPassed;
    }

    private static bool VerifyEngineAssembly(List<string> results)
    {
        try
        {
            var engine = ValidationFrameworkFactory.CreateEngine(
                new ValidationFrameworkOptions
                {
                    Name = "VerifyEngine",
                    EnableContract = true,
                    EnableBuiltInContractRules = true
                });

            var contractRules = engine.GetRulesByCategory(ValidationRuleCategory.Contract);
            var ok = contractRules.Count >= 3;
            results.Add($"[{(ok ? "PASS" : "FAIL")}] 引擎装配: Contract 规则 {contractRules.Count} 条（期望≥3）");
            return ok;
        }
        catch (Exception ex)
        {
            results.Add($"[FAIL] 引擎装配: {ex.Message}");
            return false;
        }
    }

    private static bool VerifyContractRules(List<string> results)
    {
        try
        {
            const string sampleJson = """
            {
              "views": [
                {
                  "name": "MainView",
                  "events": {
                    "click": ["RegisteredFunction", "MissingFunction"]
                  }
                }
              ],
              "Workflows": {
                "DemoWorkflow": {
                  "steps": [
                    { "FunctionName": "RegisteredFunction" },
                    { "FunctionName": "NotRegisteredStep" }
                  ]
                }
              }
            }
            """;

            using var doc = JsonDocument.Parse(sampleJson);

            var provider = new CompositeValidationProvider(
                "VerifyProvider",
                functions: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["RegisteredFunction"] = Array.Empty<string>()
                });

            var context = new ValidationContext(ValidationMode.Loose, "verify-sample.json", sampleJson);
            context.WithProvider<IContractSignatureProvider>(provider);
            context.WithProvider<IObjectRegistry>(provider);

            var engine = ValidationFrameworkFactory.CreateEngine(
                new ValidationFrameworkOptions
                {
                    Name = "VerifyContractEngine",
                    EnableContract = true,
                    EnableBuiltInContractRules = true
                });

            var report = engine.ValidateAsync(doc.RootElement, context).GetAwaiter().GetResult();

            var contractDiagnostics = report.Diagnostics
                .Where(d => d.RuleName is "FunctionContract" or "WorkflowLegality")
                .ToList();

            var errorCount = contractDiagnostics.Count(d => d.Severity == Severity.Error);
            var ok = errorCount >= 2;
            results.Add($"[{(ok ? "PASS" : "FAIL")}] 契约规则: 检测到 {errorCount} 条契约错误（期望≥2）");
            return ok;
        }
        catch (Exception ex)
        {
            results.Add($"[FAIL] 契约规则: {ex.Message}");
            return false;
        }
    }

    private static bool VerifyPipeline(List<string> results)
    {
        try
        {
            const string goodJson = """
            {
              "name": "GoodConfig",
              "events": { "click": ["RegisteredFunction"] },
              "Workflows": {
                "W": { "steps": [ { "FunctionName": "RegisteredFunction" } ] }
              }
            }
            """;

            var provider = new CompositeValidationProvider(
                "PipelineProvider",
                functions: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["RegisteredFunction"] = Array.Empty<string>()
                });

            var context = new ValidationContext(ValidationMode.Loose, "good.json", goodJson);
            context.WithProvider<IContractSignatureProvider>(provider);
            context.WithProvider<IObjectRegistry>(provider);

            var pipeline = ValidationFrameworkFactory.CreatePipeline(
                new ValidationFrameworkOptions { Name = "VerifyPipeline" });

            using var doc = JsonDocument.Parse(goodJson);
            var report = pipeline.ExecuteAsync(doc.RootElement, context).GetAwaiter().GetResult();

            var ok = report.IsValid;
            results.Add($"[{(ok ? "PASS" : "FAIL")}] 管线: 全通过（IsValid={report.IsValid}, 诊断={report.Diagnostics.Count}）");
            return ok;
        }
        catch (Exception ex)
        {
            results.Add($"[FAIL] 管线: {ex.Message}");
            return false;
        }
    }

    private static bool VerifyFeedback(List<string> results)
    {
        try
        {
            var diagnostic = ValidationDiagnostic.Error(
                "FunctionContract",
                "函数 'MissingFunction' 未注册",
                new SourceLocation("verify.json", "$.views[0].events.click[1]"),
                "可用函数: RegisteredFunction");
            var report = ValidationReport.Create(
                "FeedbackEngine",
                new[] { diagnostic },
                TimeSpan.FromMilliseconds(5));

            var formatter = ValidationFrameworkFactory.CreateFeedbackFormatter();
            var llmJson = formatter.ToLlmFeedback(report);

            var ok = llmJson.Contains("MissingFunction")
                && llmJson.Contains("\"errorCount\"")
                && llmJson.Contains("RegisteredFunction");
            results.Add($"[{(ok ? "PASS" : "FAIL")}] 反馈格式化: LLM JSON 含修复指令");
            return ok;
        }
        catch (Exception ex)
        {
            results.Add($"[FAIL] 反馈格式化: {ex.Message}");
            return false;
        }
    }
}