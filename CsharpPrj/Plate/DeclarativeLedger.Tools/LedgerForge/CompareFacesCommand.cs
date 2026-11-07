using System.Text.Json;
using BasicMethod.Declarative.Ledger;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// 多面键集对拍（A2/A8）：**唯一比较实现** = <see cref="LedgerKeyClosure"/>（Ordinal）。
/// <para>
/// 输入只承载「已抽取的键集」（面名 → JSON 数组），不承载任何业务判据；
/// 各门的业务规则（域闭集、模板占位、目录归属等）仍留在各自门内（必要分层）。
/// </para>
/// 输出统一为 violations：<c>extra</c> = 相对于参照面多出的键；<c>missing</c> = 相对参照面缺失的键。
/// 退出码：0 一致；1 有差异；2 输入缺失/不可判定。
/// </summary>
internal static class CompareFacesCommand
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static int Run(IReadOnlyList<string> faceSpecs, string? reference, string? outPath)
    {
        if (faceSpecs.Count < 2)
        {
            Console.Error.WriteLine("[ledger:forge] compare-faces 至少需要两个 --face <名>=<键集 JSON>");
            return 2;
        }

        var faces = new List<(string Name, IReadOnlyList<string> Keys)>();
        foreach (var spec in faceSpecs)
        {
            var split = spec.IndexOf('=');
            if (split <= 0)
            {
                Console.Error.WriteLine($"[ledger:forge] --face 需形如 名=路径: {spec}");
                return 2;
            }

            var name = spec[..split];
            var path = spec[(split + 1)..];
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"[ledger:forge] 键集文件缺失: {path}");
                return 2;
            }

            try
            {
                var keys = FaceKeySetReader.Read(path);
                faces.Add((name, keys));
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"[ledger:forge] 键集不可读 {path}: {exception.Message}");
                return 2;
            }
        }

        var refName = string.IsNullOrWhiteSpace(reference) ? faces[0].Name : reference;
        if (faces.All(face => !string.Equals(face.Name, refName, StringComparison.Ordinal)))
        {
            Console.Error.WriteLine($"[ledger:forge] 参照面未登记: {refName}");
            return 2;
        }

        var closure = new LedgerKeyClosure(faces.Select(face => (face.Name, face.Keys)));
        var violations = new List<FaceViolation>();

        // 对称对拍：other 多出的键 = extra；other 缺失的键 = missing。键集语义全部由 LedgerKeyClosure(Ordinal) 提供。
        foreach (var face in faces.Where(face => !string.Equals(face.Name, refName, StringComparison.Ordinal)))
        {
            var referenceKeys = closure.KeysOfSorted(refName);
            var known = new HashSet<string>(referenceKeys, StringComparer.Ordinal);
            foreach (var key in face.Keys)
                if (!known.Contains(key))
                    violations.Add(new FaceViolation(face.Name, "extra", key));

            var present = new HashSet<string>(face.Keys, StringComparer.Ordinal);
            foreach (var key in referenceKeys)
                if (!present.Contains(key))
                    violations.Add(new FaceViolation(face.Name, "missing", key));
        }

        var payload = new CompareFacesResult(violations.Count == 0, refName, violations);
        var json = JsonSerializer.Serialize(payload, OutputOptions);
        if (string.IsNullOrWhiteSpace(outPath))
        {
            Console.Out.WriteLine(json);
        }
        else
        {
            File.WriteAllText(outPath, json);
        }

        return violations.Count == 0 ? 0 : 1;
    }

    private sealed record FaceViolation(string Face, string Kind, string Key);

    private sealed record CompareFacesResult(bool Ok, string Reference, IReadOnlyList<FaceViolation> Violations);
}
