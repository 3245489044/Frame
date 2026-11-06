// FieldUsageIndex —— 离线语义索引工具（供约束墙 CW-04/10/13 与 CF-W-14 消费）。
//
// 复用（新建前已查）：
//   · 语法树：RoslynMethod.Core CSharpSourceProvider.Instance（与 RoslynJson/其它 Roslyn 工具同一解析单源）。
//   · 编译单元：CompilationSkeleton.BuildFromTrees（本轮新增的多树重载，单树 Build 行为未改）。
//   · 输入清单：由 Build-FieldUsageIndex.ps1 从 ConstraintWall.settings.psd1 / 分级表 / 声明面物化，
//     本工具不解析 psd1（避免第二真源）。
//
// fail-closed（不产出"半对"索引）：
//   · BuildOutput 缺失 / 排除装配意外存在 / 源文件缺失 / 期望字段缺失 / 同名配置类型歧义
//     ⇒ 非零退出且**不写产物**。
//   · 编译诊断错误只记录（不单独阻断），但索引必须完成"配置类型声明面"绑定，否则同上失败。

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace RoslynMethod.Tools.FieldUsageIndex;

internal static class Program
{
    private const string Schema = "constraint-field-usage-v1";

    private static int Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            var manifest = Manifest.Load(options.ManifestPath);

            if (manifest.SourceFiles.Count == 0) return Fail("源文件清单为空（拒绝空转索引）");
            if (manifest.References.Count == 0) return Fail("引用清单为空（拒绝空转索引）");
            if (manifest.BuildOutputDir.Length == 0 || !Directory.Exists(manifest.BuildOutputDir))
                return Fail($"BuildOutput 缺失：{manifest.BuildOutputDir}（拒绝产出部分绑定索引）");

            var excluded = new HashSet<string>(manifest.ExcludedAssemblies.Select(n => n + ".dll"), StringComparer.OrdinalIgnoreCase);
            foreach (var reference in manifest.References)
            {
                if (excluded.Contains(Path.GetFileName(reference)))
                    return Fail($"排除装配意外进入引用集：{reference}（拒绝产出部分绑定索引）");
            }

            var trees = new List<SyntaxTree>();
            var inputs = new List<InputFile>();
            foreach (var file in manifest.SourceFiles.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(file)) return Fail($"源文件不存在：{file}");
                var text = File.ReadAllText(file);
                trees.Add(CSharpSourceProvider.Instance.Parse(text, file));
                inputs.Add(new InputFile
                {
                    Path = Rel(manifest.RepoRoot, file),
                    Sha256 = Sha256(text)
                });
            }

            var references = new List<MetadataReference>();
            foreach (var path in TrustedPlatformAssemblies())
            {
                try { references.Add(MetadataReference.CreateFromFile(path)); } catch { /* 个别 TPA 不可读不影响语义绑定 */ }
            }
            var referenceFiles = new List<InputFile>();
            foreach (var file in manifest.References.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(file)) return Fail($"引用装配不存在：{file}");
                references.Add(MetadataReference.CreateFromFile(file));
                referenceFiles.Add(new InputFile
                {
                    Path = Rel(manifest.RepoRoot, file),
                    Sha256 = Sha256(File.ReadAllBytes(file))
                });
            }
            referenceFiles.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));

            var config = new CompilationSkeleton.CompilationConfig
            {
                AssemblyName = "ConstraintFieldUsageIndex",
                CollectDiagnostics = true,
                AutoResolveReferences = false,
                AdditionalReferences = references
            };
            var built = CompilationSkeleton.BuildFromTrees(trees, config);
            if (built.Compilation == null || built.SyntaxTrees.Count == 0)
            {
                return Fail($"多树编译失败：{built.ErrorSummary}");
            }

            var configTypes = new HashSet<string>(manifest.ConfigTypes, StringComparer.Ordinal);
            var derivedFields = new HashSet<string>(manifest.DerivedFields, StringComparer.Ordinal);
            var semanticFields = new HashSet<string>(manifest.SemanticFields, StringComparer.Ordinal);

            var declarations = new List<DeclarationRecord>();
            var typeOwners = new Dictionary<string, string>(StringComparer.Ordinal);
            var ambiguousTypes = new List<string>();

            foreach (var tree in built.SyntaxTrees)
            {
                var model = built.GetSemanticModel(tree);
                if (model == null) continue;
                foreach (var typeDecl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    var symbol = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                    if (symbol == null || !configTypes.Contains(symbol.Name)) continue;

                    var fqn = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
                    if (typeOwners.TryGetValue(symbol.Name, out var owner) && owner != fqn)
                    {
                        ambiguousTypes.Add($"{symbol.Name}: {owner} / {fqn}");
                        continue;
                    }
                    typeOwners[symbol.Name] = fqn;

                    foreach (var member in symbol.GetMembers())
                    {
                        if (member is not IFieldSymbol && member is not IPropertySymbol) continue;
                        if (member.IsStatic) continue;
                        var memberType = member is IFieldSymbol f ? f.Type : ((IPropertySymbol)member).Type;
                        var line = member.Locations.FirstOrDefault(l => l.IsInSource)?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
                        var key = symbol.Name + "." + member.Name;
                        var role = derivedFields.Contains(key) ? "Derived"
                            : semanticFields.Contains(key) ? "Semantic"
                            : "Presentation";
                        declarations.Add(new DeclarationRecord
                        {
                            Type = symbol.Name,
                            TypeFqn = fqn,
                            Field = member.Name,
                            FieldType = memberType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            Role = role,
                            DeclFile = Rel(manifest.RepoRoot, tree.FilePath),
                            DeclLine = line
                        });
                    }
                }
            }

            if (ambiguousTypes.Count > 0)
            {
                return Fail("配置类型同名歧义（无法做唯一归属）：" + string.Join("; ", ambiguousTypes));
            }

            var producedKeys = new HashSet<string>(declarations.Select(d => d.Type + "." + d.Field), StringComparer.Ordinal);
            var missingFields = manifest.ExpectedFields.Where(k => !producedKeys.Contains(k)).ToList();
            if (missingFields.Count > 0)
            {
                return Fail("声明面字段未被语义绑定（缺 " + missingFields.Count + "）：" + string.Join(", ", missingFields.Take(20)));
            }
            if (declarations.Count == 0) return Fail("语义绑定后配置字段为 0（拒绝空转索引）");

            var fieldNames = new HashSet<string>(declarations.Select(d => d.Field), StringComparer.Ordinal);
            var accesses = new List<AccessRecord>();
            var heldTypes = new List<HeldRecord>();
            var unresolvedReceivers = 0;
            var unresolvedMethodCalls = 0;

            foreach (var tree in built.SyntaxTrees)
            {
                var model = built.GetSemanticModel(tree);
                if (model == null) continue;

                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    switch (node)
                    {
                        case MemberAccessExpressionSyntax ma:
                            CollectAccess(ma, ma.Name, model, manifest, configTypes, fieldNames, accesses, ref unresolvedReceivers);
                            break;
                        case MemberBindingExpressionSyntax mb:
                            CollectAccess(mb, mb.Name, model, manifest, configTypes, fieldNames, accesses, ref unresolvedReceivers);
                            break;
                        case InvocationExpressionSyntax inv:
                            CollectMethodWrite(inv, model, manifest, configTypes, accesses, ref unresolvedMethodCalls);
                            break;
                    }
                }

                foreach (var decl in tree.GetRoot().DescendantNodes())
                {
                    switch (decl)
                    {
                        case VariableDeclaratorSyntax vd:
                            CollectVariableHeld(vd, model, manifest, configTypes, heldTypes);
                            break;
                        case PropertyDeclarationSyntax pd:
                            CollectTypeHeld(pd.Type, model.GetDeclaredSymbol(pd) as IPropertySymbol,
                                "Property", false, pd, model, manifest, configTypes, heldTypes);
                            break;
                        case ParameterSyntax ps:
                            CollectTypeHeld(ps.Type, model.GetDeclaredSymbol(ps) as IParameterSymbol,
                                "Parameter", false, ps, model, manifest, configTypes, heldTypes);
                            break;
                        case MethodDeclarationSyntax md:
                            CollectTypeHeld(md.ReturnType, model.GetDeclaredSymbol(md) as IMethodSymbol,
                                "Return", false, md, model, manifest, configTypes, heldTypes);
                            break;
                    }
                }
            }

            accesses.Sort((a, b) =>
            {
                var c = StringComparer.Ordinal.Compare(a.File, b.File);
                if (c != 0) return c;
                c = a.Line.CompareTo(b.Line);
                if (c != 0) return c;
                c = StringComparer.Ordinal.Compare(a.Kind, b.Kind);
                if (c != 0) return c;
                return StringComparer.Ordinal.Compare(a.Field ?? "", b.Field ?? "");
            });
            declarations.Sort((a, b) => StringComparer.Ordinal.Compare(a.Type + "." + a.Field, b.Type + "." + b.Field));
            heldTypes.Sort((a, b) =>
            {
                var c = StringComparer.Ordinal.Compare(a.File, b.File);
                if (c != 0) return c;
                c = a.Line.CompareTo(b.Line);
                if (c != 0) return c;
                return StringComparer.Ordinal.Compare(a.Type, b.Type);
            });

            var errorSamples = built.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(10)
                .Select(d =>
                {
                    var span = d.Location.GetLineSpan();
                    return $"{d.Id} {span.Path}:{span.StartLinePosition.Line + 1}: {d.GetMessage()}";
                })
                .ToList();

            var artifact = new Artifact
            {
                Schema = Schema,
                GeneratorVersion = manifest.GeneratorVersion,
                BuiltAtUtc = DateTime.UtcNow.ToString("O"),
                SettingsInputs = manifest.SettingsInputs,
                Inputs = inputs.OrderBy(i => i.Path, StringComparer.Ordinal).ToList(),
                References = referenceFiles,
                Compilation = new CompilationInfo
                {
                    Trees = built.SyntaxTrees.Count,
                    ErrorCount = built.Errors.Count,
                    ErrorSamples = errorSamples
                },
                ConfigTypes = typeOwners.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new ConfigTypeRecord { Name = kv.Key, TypeFqn = kv.Value })
                    .ToList(),
                Fields = declarations,
                Accesses = accesses,
                HeldTypes = heldTypes,
                UnresolvedReceivers = unresolvedReceivers,
                UnresolvedMethodCalls = unresolvedMethodCalls
            };

            WriteAtomic(options.OutputPath, JsonSerializer.Serialize(artifact, JsonOptions));
            Console.WriteLine($"FieldUsageIndex: trees={artifact.Compilation.Trees} fields={artifact.Fields.Count} " +
                              $"accesses={artifact.Accesses.Count} held={artifact.HeldTypes.Count} " +
                              $"unresolvedReceivers={unresolvedReceivers} errors={artifact.Compilation.ErrorCount}");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail("工具异常：" + ex.Message);
        }
    }

    private static void CollectAccess(
        ExpressionSyntax node,
        SimpleNameSyntax name,
        SemanticModel model,
        Manifest manifest,
        HashSet<string> configTypes,
        HashSet<string> fieldNames,
        List<AccessRecord> accesses,
        ref int unresolvedReceivers)
    {
        var info = model.GetSymbolInfo(node).Symbol;
        if (info == null)
        {
            if (fieldNames.Contains(name.Identifier.ValueText)) unresolvedReceivers++;
            return;
        }
        if (info is not IPropertySymbol && info is not IFieldSymbol) return;

        var owner = info.ContainingType;
        if (owner == null || !configTypes.Contains(owner.Name)) return;

        var isWrite = IsWriteTarget(node);
        var inInitializer = node.Ancestors().OfType<InitializerExpressionSyntax>().Any();
        var receiver = node is MemberAccessExpressionSyntax ma ? ma.Expression.ToString() : "?.";
        var enclosing = Enclosing(node);

        accesses.Add(new AccessRecord
        {
            File = Rel(manifest.RepoRoot, node.SyntaxTree.FilePath),
            Line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            Kind = isWrite ? "Write" : "Read",
            Access = "Direct",
            ReceiverExpr = receiver,
            ReceiverType = owner.Name,
            Field = info.Name,
            Api = null,
            EnclosingType = enclosing.Type,
            EnclosingMethod = enclosing.Method,
            InInitializer = inInitializer
        });
    }

    private static void CollectMethodWrite(
        InvocationExpressionSyntax inv,
        SemanticModel model,
        Manifest manifest,
        HashSet<string> configTypes,
        List<AccessRecord> accesses,
        ref int unresolvedMethodCalls)
    {
        if (manifest.MutatorApis.Count == 0) return;

        var symbol = model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
        if (symbol == null)
        {
            var invokedName = inv.Expression is MemberAccessExpressionSyntax m ? m.Name.Identifier.ValueText : null;
            if (invokedName != null && manifest.MutatorApis.Any(a => a.EndsWith("." + invokedName, StringComparison.Ordinal)))
                unresolvedMethodCalls++;
            return;
        }

        var ownerName = symbol.ContainingType?.Name ?? "";
        var full = ownerName + "." + symbol.Name;
        var matched = manifest.MutatorApis.Any(a => a == full || a == symbol.Name);
        if (!matched) return;

        ExpressionSyntax? receiver = null;
        ITypeSymbol? receiverType = null;

        if (inv.Expression is MemberAccessExpressionSyntax instance)
        {
            var rt = model.GetTypeInfo(instance.Expression).Type;
            if (rt != null && configTypes.Contains(rt.Name)) { receiver = instance.Expression; receiverType = rt; }
        }
        if (receiver == null)
        {
            foreach (var arg in inv.ArgumentList.Arguments)
            {
                var t = model.GetTypeInfo(arg.Expression).Type;
                if (t != null && configTypes.Contains(t.Name)) { receiver = arg.Expression; receiverType = t; break; }
            }
        }
        if (receiver == null || receiverType == null) return;

        var enclosing = Enclosing(inv);
        accesses.Add(new AccessRecord
        {
            File = Rel(manifest.RepoRoot, inv.SyntaxTree.FilePath),
            Line = inv.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            Kind = "MethodWrite",
            Access = "Method",
            ReceiverExpr = receiver.ToString(),
            ReceiverType = receiverType.Name,
            Field = null,
            Api = full,
            EnclosingType = enclosing.Type,
            EnclosingMethod = enclosing.Method,
            InInitializer = false
        });
    }

    private static void CollectVariableHeld(
        VariableDeclaratorSyntax vd,
        SemanticModel model,
        Manifest manifest,
        HashSet<string> configTypes,
        List<HeldRecord> heldTypes)
    {
        var symbol = model.GetDeclaredSymbol(vd);
        var type = symbol switch
        {
            ILocalSymbol l => l.Type,
            IFieldSymbol f => f.Type,
            _ => null
        };
        if (type == null || !ContainsConfig(type, configTypes, manifest.ExemptTypes)) return;

        var declaredAs = vd.Parent is LocalDeclarationStatementSyntax lds &&
                         lds.Declaration.Type.IsVar ? "Var" : "Explicit";
        heldTypes.Add(new HeldRecord
        {
            File = Rel(manifest.RepoRoot, vd.SyntaxTree.FilePath),
            Line = vd.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            SymbolKind = symbol is IFieldSymbol ? "Field" : "Local",
            Type = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            DeclaredAs = declaredAs,
            EnclosingType = Enclosing(vd).Type
        });
    }

    private static void CollectTypeHeld(
        TypeSyntax typeSyntax,
        ISymbol? symbol,
        string symbolKind,
        bool isVar,
        SyntaxNode node,
        SemanticModel model,
        Manifest manifest,
        HashSet<string> configTypes,
        List<HeldRecord> heldTypes)
    {
        var type = symbol switch
        {
            IPropertySymbol p => p.Type,
            IParameterSymbol p => p.Type,
            IMethodSymbol m => m.ReturnType,
            _ => model.GetTypeInfo(typeSyntax).Type
        };
        if (type == null || !ContainsConfig(type, configTypes, manifest.ExemptTypes)) return;

        heldTypes.Add(new HeldRecord
        {
            File = Rel(manifest.RepoRoot, node.SyntaxTree.FilePath),
            Line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            SymbolKind = symbolKind,
            Type = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            DeclaredAs = isVar ? "Var" : "Explicit",
            EnclosingType = Enclosing(node).Type
        });
    }

    private static bool ContainsConfig(ITypeSymbol type, HashSet<string> configTypes, IReadOnlyCollection<string> exemptTypes)
    {
        if (type is INamedTypeSymbol named)
        {
            if (exemptTypes.Contains(named.Name)) return false;
            if (configTypes.Contains(named.Name)) return true;
            foreach (var arg in named.TypeArguments)
            {
                if (ContainsConfig(arg, configTypes, exemptTypes)) return true;
            }
            return false;
        }
        if (type is IArrayTypeSymbol array) return ContainsConfig(array.ElementType, configTypes, exemptTypes);
        if (type is IPointerTypeSymbol pointer) return ContainsConfig(pointer.PointedAtType, configTypes, exemptTypes);
        return false;
    }

    private static bool IsWriteTarget(ExpressionSyntax node)
    {
        var parent = node.Parent;
        if (parent is AssignmentExpressionSyntax assignment && assignment.Left == node) return true;
        if (parent is PrefixUnaryExpressionSyntax prefix &&
            (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)) &&
            prefix.Operand == node) return true;
        if (parent is PostfixUnaryExpressionSyntax postfix &&
            (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression)) &&
            postfix.Operand == node) return true;
        return false;
    }

    private static (string Type, string Method) Enclosing(SyntaxNode node)
    {
        var type = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "";
        var method = node.Ancestors()
            .Select(a => a switch
            {
                MethodDeclarationSyntax m => m.Identifier.ValueText,
                ConstructorDeclarationSyntax c => c.Identifier.ValueText,
                LocalFunctionStatementSyntax f => f.Identifier.ValueText,
                _ => null
            })
            .FirstOrDefault(n => n != null) ?? "";
        return (type, method);
    }

    private static IEnumerable<string> TrustedPlatformAssemblies()
    {
        var raw = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrEmpty(raw)) return Array.Empty<string>();
        return raw.Split(Path.PathSeparator).Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    }

    private static string Rel(string root, string path)
    {
        var full = Path.GetFullPath(path);
        var rootFull = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return full.Replace('\\', '/');
        return full.Substring(rootFull.Length).Replace('\\', '/');
    }

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FieldUsageIndex: " + message);
        return 2;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    private sealed class CliOptions
    {
        public string ManifestPath = "";
        public string OutputPath = "";

        public static CliOptions Parse(string[] args)
        {
            var o = new CliOptions();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--manifest" when i + 1 < args.Length: o.ManifestPath = args[++i]; break;
                    case "--output" when i + 1 < args.Length: o.OutputPath = args[++i]; break;
                    default: throw new ArgumentException("未知参数：" + args[i]);
                }
            }
            if (o.ManifestPath.Length == 0 || o.OutputPath.Length == 0)
                throw new ArgumentException("必须提供 --manifest 与 --output");
            return o;
        }
    }

    private sealed class Manifest
    {
        public string Schema = "";
        public string GeneratorVersion = "";
        public string RepoRoot = "";
        public string BuildOutputDir = "";
        public List<string> SourceFiles = new();
        public List<string> References = new();
        public List<string> ConfigTypes = new();
        public List<string> ExpectedFields = new();
        public List<string> SemanticFields = new();
        public List<string> DerivedFields = new();
        public List<string> MutatorApis = new();
        public List<string> ExemptTypes = new();
        public List<string> ExcludedAssemblies = new();
        public List<InputFile> SettingsInputs = new();

        public static Manifest Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("manifest 不存在", path);
            var m = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true });
            if (m == null) throw new InvalidDataException("manifest 解析为空");
            if (m.Schema != "constraint-field-index-manifest-v1") throw new InvalidDataException("manifest schema 不匹配：" + m.Schema);
            return m;
        }
    }

    public sealed class InputFile
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
    }

    public sealed class Artifact
    {
        public string Schema { get; set; } = "";
        public string GeneratorVersion { get; set; } = "";
        public string BuiltAtUtc { get; set; } = "";
        public List<InputFile> SettingsInputs { get; set; } = new();
        public List<InputFile> Inputs { get; set; } = new();
        public List<InputFile> References { get; set; } = new();
        public CompilationInfo Compilation { get; set; } = new();
        public List<ConfigTypeRecord> ConfigTypes { get; set; } = new();
        public List<DeclarationRecord> Fields { get; set; } = new();
        public List<AccessRecord> Accesses { get; set; } = new();
        public List<HeldRecord> HeldTypes { get; set; } = new();
        public int UnresolvedReceivers { get; set; }
        public int UnresolvedMethodCalls { get; set; }
    }

    public sealed class CompilationInfo
    {
        public int Trees { get; set; }
        public int ErrorCount { get; set; }
        public List<string> ErrorSamples { get; set; } = new();
    }

    public sealed class ConfigTypeRecord
    {
        public string Name { get; set; } = "";
        public string TypeFqn { get; set; } = "";
    }

    public sealed class DeclarationRecord
    {
        public string Type { get; set; } = "";
        public string TypeFqn { get; set; } = "";
        public string Field { get; set; } = "";
        public string FieldType { get; set; } = "";
        public string Role { get; set; } = "";
        public string DeclFile { get; set; } = "";
        public int DeclLine { get; set; }
    }

    public sealed class AccessRecord
    {
        public string File { get; set; } = "";
        public int Line { get; set; }
        public string Kind { get; set; } = "";
        public string Access { get; set; } = "";
        public string? ReceiverExpr { get; set; }
        public string? ReceiverType { get; set; }
        public string? Field { get; set; }
        public string? Api { get; set; }
        public string EnclosingType { get; set; } = "";
        public string EnclosingMethod { get; set; } = "";
        public bool InInitializer { get; set; }
    }

    public sealed class HeldRecord
    {
        public string File { get; set; } = "";
        public int Line { get; set; }
        public string SymbolKind { get; set; } = "";
        public string Type { get; set; } = "";
        public string DeclaredAs { get; set; } = "";
        public string EnclosingType { get; set; } = "";
    }

    private static void WriteAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }
}
