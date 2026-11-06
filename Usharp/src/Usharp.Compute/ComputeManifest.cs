using Plate.DslMethod.Model;

namespace Usharp.Compute;

/// <summary>
/// 独立计算清单（<c>compute.manifest.json</c>）：只描述含 <c>[Compute]</c> 的文件，
/// 不写入既有诊断流/既有报告，因此纯声明文件的判定与产物零变化。
/// </summary>
public static class ComputeManifest
{
    public const string FileName = "compute.manifest.json";
    public const int ManifestVersion = 1;

    /// <summary>规范化 JSON（键序固定、无时间戳/GUID；同一输入两次产出字节相同）。</summary>
    public static string ToCanonicalJson(IReadOnlyList<DslDocument> documents,
        ComputeCompileResult result)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(result);

        var root = Cj.Obj()
            .Add("manifestVersion", ManifestVersion)
            .Add("files", FileEntries(documents, result));
        return CjWriter.Write(root);
    }

    public static string ToJson(IReadOnlyList<DslDocument> documents, ComputeCompileResult result)
        => ToCanonicalJson(documents, result);

    private static CjArray FileEntries(IReadOnlyList<DslDocument> documents,
        ComputeCompileResult result)
    {
        var array = Cj.Arr();
        foreach (var document in documents
                     .Where(UsharpComputeClassifier.HasComputation)
                     .OrderBy(d => d.FilePath, StringComparer.Ordinal))
        {
            var units = UsharpComputeClassifier.ComputeUnits(document);
            var behavior = UsharpComputeClassifier.BehaviorUnits(document);
            var entry = Cj.Obj()
                .Add("file", document.FilePath)
                .Add("usharp.purity", PurityLabel(document, behavior))
                .Add("usharp.compute.units", units.Count)
                .Add("usharp.compute.behaviorUnits", behavior)
                .Add("compute.programs", ProgramsFor(result, document.FilePath));
            array.Add(entry);
        }

        return array;
    }

    private static CjArray ProgramsFor(ComputeCompileResult result, string file)
    {
        var array = Cj.Arr();
        foreach (var program in result.Programs)
        {
            var root = program.FindUnit(program.RootId);
            if (root is null || !string.Equals(root.SourceFile, file, StringComparison.Ordinal)) continue;
            array.Add(Cj.Obj().Add("calcId", program.RootId).Add("hash", program.Hash));
        }

        return array;
    }

    private static string PurityLabel(DslDocument document, int behaviorUnits)
        => behaviorUnits > 0 ? "computation-mixed" : "computation-isolated";
}
