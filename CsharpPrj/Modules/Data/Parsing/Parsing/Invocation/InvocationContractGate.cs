using System;
using System.Collections.Generic;
using System.Linq;
using ContractFramework.Abstractions;
using ContractFramework.Gate;
using ContractFramework.Ledger;
using ContractFramework.Registry;
using Modules.Parsing.Models;

namespace Modules.Parsing.Invocation;

/// <summary>调用面契约主题（结构事实载体：脚本命令序列 + 来源标签）。</summary>
public sealed class InvocationContractSubject
{
    public const string FactKey = "invocation.subject";

    private InvocationContractReport? _report;

    public InvocationContractSubject(IReadOnlyList<MetaCommand> commands, string? sourceLabel)
    {
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        SourceLabel = sourceLabel;
    }

    public IReadOnlyList<MetaCommand> Commands { get; }

    public string? SourceLabel { get; }

    /// <summary>逐契约执行会多次求值 ⇒ 只算一次（单源，避免账本三条不同结果）。</summary>
    public InvocationContractReport Report => _report ??= InvocationContractValidator.Validate(Commands);
}

/// <summary>调用面契约执行体（DotNet 适配）：把校验器结果映射成三值裁决 + 违例清单。</summary>
public sealed class InvocationContractHost : IContractHost
{
    public ContractHostKind Kind => ContractHostKind.DotNet;

    public bool CarrierReady(ContractDescriptor contract, IContractContext context)
        => context is not null
           && context.TryGetFact<InvocationContractSubject>(InvocationContractSubject.FactKey, out var subject)
           && subject is not null;

    public ContractResult Evaluate(ContractDescriptor contract, IContractContext context)
    {
        if (context is null
            || !context.TryGetFact<InvocationContractSubject>(InvocationContractSubject.FactKey, out var subject)
            || subject is null)
        {
            return ContractResult.From(
            [
                new ContractOutcome(
                    contract.Id, contract.Tier, contract.Effect, ContractVerdict.Bottom, [],
                    "缺调用面主题（结构事实未注入）")
            ]);
        }

        var report = subject.Report;
        var violations = report.Issues
            .Select(issue => new ContractViolation(contract.Id, issue.ReasonCode, issue.ToString()))
            .ToArray();

        var outcome = new ContractOutcome(
            contract.Id,
            contract.Tier,
            contract.Effect,
            report.IsClean ? ContractVerdict.True : ContractVerdict.False,
            violations,
            report.IsClean ? null : $"违约 {violations.Length} 条（checked={report.Checked}, nameChecks={report.NameChecks}）");

        return ContractResult.From([outcome]);
    }
}

/// <summary>
/// 调用面契约门（唯一调用入口）：默认 Shadow（只记账可见化），Enforce 时阻断加载。
/// 裁决/取证全部经 <c>ContractFramework</c>（ContractGate + ContractRegistry + ContractLedger）。
/// </summary>
public static class InvocationContractGate
{
    public static InvocationContractDeclaration.Mode Mode => InvocationContractDeclaration.ResolveMode();

    /// <summary>
    /// 校验脚本命令序列。返回的报告带 <see cref="InvocationContractReport.Blocking"/>（Enforce 且违约 ⇒ true）。
    /// </summary>
    /// <param name="commands">已展开的最终命令序列。</param>
    /// <param name="sourceLabel">来源标签（脚本路径；诊断/账本用）。</param>
    /// <param name="ledgerPath">账本落盘路径；缺省读 <c>WF_INVOCATION_CONTRACT_LEDGER</c>，两者皆空 ⇒ 只记账内存。</param>
    public static InvocationContractReport Evaluate(
        IEnumerable<MetaCommand> commands,
        string? sourceLabel = null,
        string? ledgerPath = null)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var mode = InvocationContractDeclaration.ResolveMode();
        var list = commands.Where(command => command is not null).ToList();
        if (mode == InvocationContractDeclaration.Mode.Off)
            return new InvocationContractReport([], list.Count, 0, Blocking: false);

        var subject = new InvocationContractSubject(list, sourceLabel);
        var context = new ContractContext(sourceLabel: sourceLabel)
            .WithFact(InvocationContractSubject.FactKey, subject);

        var registry = new ContractRegistry();
        registry.RegisterHost(new InvocationContractHost());

        var contracts = InvocationContractDeclaration.Build(mode);
        foreach (var contract in contracts)
            registry.Register(contract);

        var result = ContractGate.Execute(contracts, ContractTier.Assembly, context, registry);

        AppendLedger(ledgerPath, sourceLabel, result);

        return subject.Report with { Blocking = mode == InvocationContractDeclaration.Mode.Enforce };
    }

    private static void AppendLedger(string? ledgerPath, string? sourceLabel, ContractResult result)
    {
        var path = string.IsNullOrWhiteSpace(ledgerPath)
            ? Environment.GetEnvironmentVariable("WF_INVOCATION_CONTRACT_LEDGER")
            : ledgerPath;

        if (string.IsNullOrWhiteSpace(path)) return;

        var ledger = new ContractLedger(path);
        ledger.Append(sourceLabel ?? "invocation", result);
        ledger.Flush();
    }
}
