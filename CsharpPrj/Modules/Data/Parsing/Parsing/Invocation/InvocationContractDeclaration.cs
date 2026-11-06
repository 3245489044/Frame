using System;
using System.Collections.Generic;
using ContractFramework.Abstractions;

namespace Modules.Parsing.Invocation;

/// <summary>
/// 调用面契约声明（唯一宣言点）：三条契约由 <c>ContractFramework</c> 统一执行/取证，不再另写校验分支。
/// <para>执行体 = <see cref="InvocationContractHost"/>（Kind=DotNet）；载体 = 声明表/注册动作集/命名空间来源。</para>
/// <para>翻档 = 改 <see cref="Mode"/>（off/shadow/enforce），不是新增判断。</para>
/// </summary>
public static class InvocationContractDeclaration
{
    public const string DeclarationId = "INV-CALL/Declaration";
    public const string NameSpaceContractId = "INV-CALL/NameSpace";
    public const string ArgumentId = "INV-CALL/Argument";

    public const string HostImpl = "Modules.Parsing.Invocation.InvocationContractHost";

    /// <summary>批次模式（单一开关来源；缺省 Shadow）。</summary>
    public enum Mode
    {
        Off = 0,
        Shadow = 1,
        Enforce = 2,
    }

    public static Mode ResolveMode()
    {
        var raw = Environment.GetEnvironmentVariable("FILEDRIVEN_INVOCATION_CONTRACT");
        if (string.IsNullOrWhiteSpace(raw)) return Mode.Shadow;

        return raw.Trim().ToLowerInvariant() switch
        {
            "off" or "false" or "0" => Mode.Off,
            "enforce" or "strict" => Mode.Enforce,
            _ => Mode.Shadow,
        };
    }

    /// <summary>违规处置：shadow=只记账（Log），enforce=阻断（Block）。</summary>
    public static ContractEffect EffectFor(Mode mode)
        => mode == Mode.Enforce ? ContractEffect.Block : ContractEffect.Log;

    /// <summary>声明集（单一来源；契约目录 <c>contracts.json</c> 尚未接入时以此为准）。</summary>
    public static IReadOnlyList<ContractDescriptor> Build(Mode mode)
    {
        var effect = EffectFor(mode);
        return
        [
            Descriptor(
                DeclarationId,
                "不变量：脚本命令的动作必须在调用面声明表登记，且目标串 token 数满足声明的最小闭合",
                ["invocation.specs.json 的 specs[].action 集合", "CommandSystemBase 注册动作集合", "CommandSpecCoverage 门禁判据"],
                effect),
            Descriptor(
                NameSpaceContractId,
                "不变量：命令引用到的名字必须落在其声明的命名空间（控件 ≠ 容器宿主 ≠ 作用域 ≠ 探测函数 ≠ 配置文件 ≠ 窗体）",
                ["invocation.specs.json 的 specs[].targetSpace", "NameSpaceRegistry 已注册来源集合", "NameSpaceIndex.Resolve 命中三态"],
                effect),
            Descriptor(
                ArgumentId,
                "不变量：命令参数键必须在声明面（声明键 ∪ probe 参数 ∪ 具名参数空间），数值参数非法值必须可见",
                ["invocation.specs.json 的 specs[].args/argSpaces/allowProbeArgs", "InvocationArgs 三态读数"],
                effect),
        ];
    }

    private static ContractDescriptor Descriptor(
        string id,
        string semantics,
        IReadOnlyList<string> facts,
        ContractEffect effect)
        => new(
            ContractId.Parse(id),
            semantics,
            ContractCarrier.Create("StructuralFact", facts),
            ContractTier.Assembly,
            effect,
            ContractHostRef.Create("DotNet", HostImpl),
            ContractEvidence.Create(
                "Frame\\PowerShellScript\\Operation\\DSL\\Process\\Check-CommandSpecCoverage.ps1 -Check -NegativeControl",
                "注册面 = 声明面",
                "注册面多一条 ghost-command ⇒ 判红"));
}
