using Plate.DslMethod.Model;
using Plate.DslMethod.Schema;
using Usharp.Semantics;

namespace Usharp.Emit;

/// <summary>
/// 一个 <c>[Node]</c> 声明的可执行体规格：<c>fn=</c> 解析出的函数名 + <c>inports/outports</c> 端口键
/// （端口键序 = 声明序，即 <c>RoundStep.ParameterKeys/ResultKeys</c> 的键序）。
/// <para><b>收集式输入端口</b>（<c>NodeExecutionKeys.CollectNodePorts</c>，形如 <c>nodes.*</c>）在 <see cref="InputPorts"/> 中
/// **原样保留**：它不是具名键，而是「本作用域全部节点输出源键整包入参」的保留标记 —— 语义在运行期由
/// <c>NodeStepExecutionService.ResolveInputsAsync</c> 展开；本读取器不做任何改写/过滤（声明面即真源）。</para>
/// </summary>
public sealed record UsharpNodeSpec(
    string Name,
    string FuncName,
    IReadOnlyList<string> InputPorts,
    IReadOnlyList<string> OutputPorts,
    string SourceFile,
    int LineNumber);

/// <summary>
/// <c>[Node]</c> 可执行体读取器：端口经既有 <see cref="ResourceNodeType.ParsePorts"/> 解析（单一口径），
/// 函数名取 <c>fn=</c> 字段（未声明时回退节点名 —— 节点名即可执行体登记键）。
/// </summary>
public static class UsharpNodePortReader
{
    /// <summary>读取全部 <c>[Node]</c> 声明（重复节点号保留首个，与闸A 的「声明即登记」一致）。</summary>
    public static IReadOnlyDictionary<string, UsharpNodeSpec> Read(UsharpDeclarationIndex declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var map = new Dictionary<string, UsharpNodeSpec>(StringComparer.Ordinal);
        foreach (var decl in declarations.Nodes)
        {
            if (string.IsNullOrWhiteSpace(decl.Name)) continue;

            var fn = decl.Field("fn");
            map.TryAdd(decl.Name, new UsharpNodeSpec(
                decl.Name,
                string.IsNullOrWhiteSpace(fn) ? decl.Name : fn.Trim(),
                ResourceNodeType.ParsePorts(decl.Field("inports")).Keys.ToList(),
                ResourceNodeType.ParsePorts(decl.Field("outports")).Keys.ToList(),
                decl.SourceFile,
                decl.LineNumber));
        }
        return map;
    }
}
