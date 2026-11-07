using System.Text.Json;
using BasicMethod.Declarative.Ledger;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// 多面对账（声明面 ↔ 派发面 ↔ 运行期装配面）CLI：**唯一判定实现 = <see cref="FaceReconciler"/>**。
/// <para>
/// 与 <c>compare-faces</c> 的区别：本命令输出每一面实际数量、覆盖等级（complete / partial / empty / unknown）
/// 与不可绿原因；**空面一律不绿**（空集不是「无需对账」）。面文件缺失/不可读 ⇒ 该面 <c>loaded=false</c>（红），
/// 而不是静默当空集。退出码：0 绿；1 红；2 输入形态错误。
/// </para>
/// </summary>
internal static class ReconcileFacesCommand
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
            Console.Error.WriteLine("[ledger:forge] reconcile-faces 至少需要两个 --face <名>=<键集 JSON>");
            return 2;
        }

        var faces = new List<ReconcileFace>();
        foreach (var spec in faceSpecs)
        {
            var split = spec.IndexOf('=');
            if (split <= 0)
            {
                Console.Error.WriteLine($"[ledger:forge] --face 需形如 名=路径: {spec}");
                return 2;
            }

            var name = spec[..split].Trim();
            var path = spec[(split + 1)..];
            if (name.Length == 0)
            {
                Console.Error.WriteLine($"[ledger:forge] --face 面名为空: {spec}");
                return 2;
            }

            if (!File.Exists(path))
            {
                // 面文件缺失不是「空集」：装载失败 ⇒ loaded=false ⇒ 覆盖 unknown ⇒ 红。
                faces.Add(new ReconcileFace(name, [], Loaded: false, Source: path));
                continue;
            }

            try
            {
                faces.Add(new ReconcileFace(name, FaceKeySetReader.Read(path), Loaded: true, Source: path));
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"[ledger:forge] 键集不可读 {path}: {exception.Message}");
                faces.Add(new ReconcileFace(name, [], Loaded: false, Source: path));
            }
        }

        var refName = string.IsNullOrWhiteSpace(reference) ? faces[0].Name : reference.Trim();
        if (!faces.Any(face => string.Equals(face.Name, refName, StringComparison.Ordinal)))
        {
            Console.Error.WriteLine($"[ledger:forge] 参照面未登记: {refName}");
            return 2;
        }

        // 边：显式 --ref ⇒ ref → 其余面；否则按输入序串成链。
        IReadOnlyList<ReconcileEdge>? edges = reference is null
            ? null
            : faces
                .Where(face => !string.Equals(face.Name, refName, StringComparison.Ordinal))
                .Select(face => new ReconcileEdge(refName, face.Name))
                .ToArray();

        var terminalTo = faces
            .LastOrDefault(face => !string.Equals(face.Name, refName, StringComparison.Ordinal))?.Name
            ?? refName;

        var report = FaceReconciler.Reconcile(faces, edges, terminalFrom: refName, terminalTo: terminalTo);

        var payload = new ReconcileFacesResult(
            Ok: report.Ok,
            Coverage: report.Coverage.ToString().ToLowerInvariant(),
            Reference: refName,
            Faces: report.Faces
                .Select(face => new ReconcileFaceOutput(face.Name, face.Count, face.Loaded, face.IsEmpty, face.Source))
                .ToArray(),
            Edges: report.Edges
                .Select(edge => new ReconcileEdgeOutput(edge.From, edge.To, edge.Missing, edge.Extra))
                .ToArray(),
            TerminalMissing: report.TerminalMissing,
            Reasons: report.Reasons);

        var json = JsonSerializer.Serialize(payload, OutputOptions);
        if (string.IsNullOrWhiteSpace(outPath))
            Console.Out.WriteLine(json);
        else
            File.WriteAllText(outPath, json);

        return report.Ok ? 0 : 1;
    }

    private sealed record ReconcileFacesResult(
        bool Ok,
        string Coverage,
        string Reference,
        IReadOnlyList<ReconcileFaceOutput> Faces,
        IReadOnlyList<ReconcileEdgeOutput> Edges,
        IReadOnlyList<string> TerminalMissing,
        IReadOnlyList<string> Reasons);

    private sealed record ReconcileFaceOutput(
        string Name,
        int Count,
        bool Loaded,
        bool Empty,
        string? Source);

    private sealed record ReconcileEdgeOutput(
        string From,
        string To,
        IReadOnlyList<string> Missing,
        IReadOnlyList<string> Extra);
}
