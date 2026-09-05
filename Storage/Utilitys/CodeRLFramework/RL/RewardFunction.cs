using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using CodeRLFramework.Pipeline;
using CodeRLFramework.Pipeline.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using WorkFlowMethod.GraphFramework.Snapshots;

namespace CodeRLFramework.RL
{
    public class RewardFunction
    {
        private readonly string _testProjectPath;
        private readonly ConcurrentDictionary<string, double> _compileCache;

        public double CompileWeight { get; set; } = 0.4;

        public double TestWeight { get; set; } = 0.4;

        public double QualityWeight { get; set; } = 0.2;

        public RewardFunction(string testProjectPath = "")
        {
            _testProjectPath = testProjectPath;
            _compileCache = new ConcurrentDictionary<string, double>();
        }

        public double Calculate(string generatedCode, string testCode = "")
        {
            return Calculate(generatedCode, testCode, null, null);
        }

        public double Calculate(
            string generatedCode,
            string testCode = "",
            GraphSnapshot? graphSnapshot = null,
            GraphNavigationContext? navContext = null)
        {
            double compileReward = CalculateCompileReward(generatedCode);

            double testReward = 0;
            if (!string.IsNullOrEmpty(testCode))
            {
                testReward = CalculateTestReward(generatedCode, testCode);
            }

            double qualityReward = CalculateQualityReward(generatedCode);

            double graphStructureReward = 0;
            if (graphSnapshot != null)
            {
                graphStructureReward = CalculateGraphStructureReward(generatedCode, graphSnapshot);
            }

            double navigationQualityReward = 0;
            if (navContext != null)
            {
                navigationQualityReward = CalculateNavigationQualityReward(navContext, graphSnapshot);
            }

            double totalReward = CompileWeight * compileReward
                + TestWeight * testReward
                + QualityWeight * qualityReward
                + 0.20 * graphStructureReward
                + 0.15 * navigationQualityReward;

            return Math.Clamp(totalReward, -1.0, 1.0);
        }


        public double CalculateCompileReward(string code)
        {
            string cacheKey = code;
            if (_compileCache.TryGetValue(cacheKey, out var cached))
                return cached;

            try
            {
                var tree = CSharpSyntaxTree.ParseText(code);
                var compilation = CSharpCompilation.Create("RewardCheck")
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                    .AddReferences(
                        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location))
                    .AddSyntaxTrees(tree);

                using var ms = new MemoryStream();
                EmitResult result = compilation.Emit(ms);

                double reward;
                if (result.Success)
                {
                    int warningCount = result.Diagnostics.Count(d =>
                        d.Severity == DiagnosticSeverity.Warning);
                    reward = 1.0 - (warningCount * 0.1);
                    reward = Math.Max(reward, 0.5);
                }
                else
                {
                    int errorCount = result.Diagnostics.Count(d =>
                        d.Severity == DiagnosticSeverity.Error);
                    reward = -0.5 - (errorCount * 0.1);
                    reward = Math.Max(reward, -1.0);
                }

                _compileCache[cacheKey] = reward;
                return reward;
            }
            catch
            {
                return -1.0;
            }
        }

        public double CalculateTestReward(string generatedCode, string testCode)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "CodeRL_Test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                try
                {
                    File.WriteAllText(Path.Combine(tempDir, "Generated.cs"), generatedCode);
                    File.WriteAllText(Path.Combine(tempDir, "GeneratedTest.cs"), testCode);

                    string csproj = $@"<Project Sdk=""Microsoft.NET.Sdk"">
                        <PropertyGroup>
                            <TargetFramework>net10.0</TargetFramework>
                            <ImplicitUsings>enable</ImplicitUsings>
                            <Nullable>enable</Nullable>
                            <IsPackable>false</IsPackable>
                            <IsTestProject>true</IsTestProject>
                        </PropertyGroup>
                        <ItemGroup>
                            <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.13.0"" />
                            <PackageReference Include=""xunit"" Version=""2.9.3"" />
                            <PackageReference Include=""xunit.runner.visualstudio"" Version=""3.0.2"" />
                        </ItemGroup>
                    </Project>";
                    File.WriteAllText(Path.Combine(tempDir, "TestProject.csproj"), csproj);

                    using var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = $"test \"{tempDir}\" --no-restore",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    bool exited = process.WaitForExit(30_000);
                    if (!exited)
                    {
                        process.Kill(entireProcessTree: true);
                        return -0.5;
                    }
                    string output = process.StandardOutput.ReadToEnd();

                    if (process.ExitCode == 0)
                    {
                        return 1.0;
                    }
                    else
                    {
                        int passed = 0, failed = 0;
                        var match = Regex.Match(output, @"passed:\s*(\d+)");
                        if (match.Success) passed = int.Parse(match.Groups[1].Value);
                        match = Regex.Match(output, @"failed:\s*(\d+)");
                        if (match.Success) failed = int.Parse(match.Groups[1].Value);

                        int total = passed + failed;
                        if (total > 0)
                            return (double)passed / total - 0.5;
                        return -0.5;
                    }
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch
            {
                return -0.5;
            }
        }

        public double CalculateQualityReward(string code)
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();
                double reward = 0;

                int lineCount = code.Split('\n').Length;
                if (lineCount >= 50 && lineCount <= 200)
                    reward += 0.3;
                else if (lineCount >= 20 && lineCount <= 500)
                    reward += 0.15;
                else
                    reward -= 0.1;

                var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
                if (methods.Count >= 1 && methods.Count <= 10)
                    reward += 0.2;
                else if (methods.Count > 10)
                    reward -= 0.1;

                int pascalCaseCount = methods.Count(m =>
                    char.IsUpper(m.Identifier.Text[0]));
                if (methods.Count > 0 && pascalCaseCount == methods.Count)
                    reward += 0.2;
                else if (pascalCaseCount > 0)
                    reward += 0.1;

                int commentLines = code.Split('\n')
                    .Count(l => l.TrimStart().StartsWith("//") ||
                                l.TrimStart().StartsWith("/*") ||
                                l.TrimStart().StartsWith("*"));
                double commentRatio = lineCount > 0 ? (double)commentLines / lineCount : 0;
                if (commentRatio >= 0.05 && commentRatio <= 0.3)
                    reward += 0.15;
                else if (commentRatio > 0)
                    reward += 0.05;

                int complexity = methods.Sum(m =>
                    m.DescendantNodes().Count(n =>
                        n is IfStatementSyntax ||
                        n is WhileStatementSyntax ||
                        n is ForStatementSyntax ||
                        n is ForEachStatementSyntax ||
                        n is SwitchStatementSyntax));
                if (complexity <= 5)
                    reward += 0.15;
                else if (complexity <= 10)
                    reward += 0.05;
                else
                    reward -= 0.1;

                return Math.Clamp(reward, -0.5, 1.0);
            }
            catch
            {
                return 0;
            }
        }

        public double CalculateGraphStructureReward(
            string generatedCode,
            GraphSnapshot graphSnapshot)
        {
            if (string.IsNullOrEmpty(generatedCode))
                return -0.5;
            if (graphSnapshot == null || graphSnapshot.Nodes.Count == 0)
                return 0;

            double reward = 0;

            try
            {
                var tree = CSharpSyntaxTree.ParseText(generatedCode);
                var root = tree.GetRoot();
                var invokedMethods = root.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Select(inv => inv.Expression switch
                    {
                        IdentifierNameSyntax id => id.Identifier.Text,
                        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                        _ => null
                    })
                    .Where(name => name != null)
                    .Cast<string>()
                    .Distinct()
                    .ToList();

                if (invokedMethods.Count == 0)
                    return 0;

                var graphNodeNames = new HashSet<string>(
                    graphSnapshot.Nodes.Select(n => n.Name),
                    StringComparer.OrdinalIgnoreCase);

                int validCount = invokedMethods.Count(m => graphNodeNames.Contains(m));
                double validityRatio = (double)validCount / invokedMethods.Count;
                reward += validityRatio * 0.4;

                var edgeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var edge in graphSnapshot.Edges)
                {
                    edgeCounts.TryGetValue(edge.SourceId, out var srcCount);
                    edgeCounts[edge.SourceId] = srcCount + 1;
                    edgeCounts.TryGetValue(edge.TargetId, out var tgtCount);
                    edgeCounts[edge.TargetId] = tgtCount + 1;
                }

                int topCount = Math.Max(1, graphSnapshot.Nodes.Count * 3 / 10);
                var keyNodes = edgeCounts
                    .OrderByDescending(kv => kv.Value)
                    .Take(topCount)
                    .Select(kv => kv.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (keyNodes.Count > 0)
                {
                    int coveredKeyNodes = keyNodes.Count(k => invokedMethods.Contains(k));
                    double coverageRatio = (double)coveredKeyNodes / keyNodes.Count;
                    reward += coverageRatio * 0.3;
                }

                int invokedEdgeCount = 0;
                for (int i = 0; i < invokedMethods.Count - 1; i++)
                {
                    string from = invokedMethods[i];
                    string to = invokedMethods[i + 1];
                    bool hasEdge = graphSnapshot.Edges.Any(e =>
                        string.Equals(e.SourceId, from, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.TargetId, to, StringComparison.OrdinalIgnoreCase));
                    if (hasEdge) invokedEdgeCount++;
                }

                double edgeRatio = invokedMethods.Count > 1
                    ? (double)invokedEdgeCount / (invokedMethods.Count - 1)
                    : 0;
                reward += edgeRatio * 0.3;

                return Math.Clamp(reward, -1.0, 1.0);
            }
            catch
            {
                return 0;
            }
        }

        public double CalculateNavigationQualityReward(
            GraphNavigationContext navContext,
            GraphSnapshot? graphSnapshot = null)
        {
            if (navContext == null || navContext.Steps.Count == 0)
                return 0;

            double reward = 0;

            try
            {
                var steps = navContext.Steps;
                int totalSteps = steps.Count;

                int coherentSteps = 0;
                if (graphSnapshot != null)
                {
                    foreach (var step in steps)
                    {
                        bool hasEdge = graphSnapshot.Edges.Any(e =>
                            string.Equals(e.SourceId, step.CurrentNode, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(e.TargetId, step.SelectedNode, StringComparison.OrdinalIgnoreCase));
                        if (hasEdge) coherentSteps++;
                    }
                }
                else
                {
                    for (int i = 0; i < steps.Count - 1; i++)
                    {
                        if (string.Equals(steps[i].SelectedNode, steps[i + 1].CurrentNode, StringComparison.OrdinalIgnoreCase))
                            coherentSteps++;
                    }
                }

                double coherenceRatio = totalSteps > 0 ? (double)coherentSteps / totalSteps : 0;
                reward += coherenceRatio * 0.3;

                int uniqueNodes = navContext.VisitedSet.Count;
                double explorationRatio = Math.Min(1.0, (double)uniqueNodes / Math.Max(1, totalSteps));
                if (explorationRatio >= 0.5)
                    reward += 0.3;
                else if (explorationRatio >= 0.3)
                    reward += 0.15;
                else
                    reward -= 0.1;

                int fallbackCount = steps.Count(s => s.IsFallback);
                double fallbackRatio = totalSteps > 0 ? (double)fallbackCount / totalSteps : 0;
                if (fallbackRatio <= 0.2)
                    reward += 0.2;
                else if (fallbackRatio <= 0.5)
                    reward += 0.1;
                else if (fallbackRatio <= 0.8)
                    reward -= 0.1;
                else
                    reward -= 0.3;


                if (totalSteps >= 3 && totalSteps <= 20)
                    reward += 0.2;
                else if (totalSteps > 20)
                    reward -= 0.1;
                else if (totalSteps == 1)
                    reward += 0.05;

                return Math.Clamp(reward, -1.0, 1.0);
            }
            catch
            {
                return 0;
            }
        }

        public double CalculateStructureReward(
            List<string> functionNames,
            HashSet<string> validFunctions)
        {
            if (functionNames.Count == 0)
                return -0.5;

            double reward = 0;

            int validCount = functionNames.Count(f => validFunctions.Contains(f));
            reward += (double)validCount / functionNames.Count * 0.4;

            if (functionNames.Count >= 3 && functionNames.Count <= 20)
                reward += 0.3;
            else if (functionNames.Count > 20)
                reward -= 0.1;

            bool hasRepeated = false;
            for (int i = 1; i < functionNames.Count; i++)
            {
                if (functionNames[i] == functionNames[i - 1])
                {
                    hasRepeated = true;
                    break;
                }
            }
            if (!hasRepeated)
                reward += 0.3;

            return Math.Clamp(reward, -0.5, 1.0);
        }
    }
}
