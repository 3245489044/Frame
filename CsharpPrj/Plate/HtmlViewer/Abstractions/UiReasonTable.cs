#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend.Abstractions;

/// <summary>
/// 原因码 → 文案坐标表（通用机制，**唯一映射点**）：全表注册一次，未注册码默认可见占位。
/// <para>存在理由：原因码双词表（观测域/呈现域）各自 switch 会给出不同文案；别名只登记一次，
/// 新增码必须入表（未入表 ⇒ 渲染为「未知」占位，不静默吞并）。</para>
/// </summary>
public sealed class UiReasonTable
{
    private readonly Dictionary<string, string> _locByCode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _alias = new(StringComparer.Ordinal);

    /// <summary>登记原因码 → 文案坐标（重复登记 ⇒ 抛：同一码不得两处映射）。</summary>
    public UiReasonTable Register(string code, string locKey)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(locKey))
            throw new ArgumentException("原因码与文案坐标均不可为空。");
        if (_locByCode.ContainsKey(code) || _alias.ContainsKey(code))
            throw new InvalidOperationException($"原因码重复登记: {code}");
        _locByCode[code.Trim()] = locKey.Trim();
        return this;
    }

    /// <summary>登记别名（另一域的同义码 → 已有码；别名不得自带映射）。</summary>
    public UiReasonTable RegisterAlias(string aliasCode, string targetCode)
    {
        if (string.IsNullOrWhiteSpace(aliasCode) || string.IsNullOrWhiteSpace(targetCode))
            throw new ArgumentException("别名与目标码均不可为空。");
        if (_locByCode.ContainsKey(aliasCode))
            throw new InvalidOperationException($"别名不得同时是实体码: {aliasCode}");
        _alias[aliasCode.Trim()] = targetCode.Trim();
        return this;
    }

    /// <summary>原因码 → 文案坐标（经别名归一；未注册/别名悬空 ⇒ false）。</summary>
    public bool TryLoc(string? code, out string locKey)
    {
        locKey = string.Empty;
        if (string.IsNullOrWhiteSpace(code)) return false;
        var current = code!.Trim();
        if (_alias.TryGetValue(current, out var target)) current = target;
        if (!_locByCode.TryGetValue(current, out var loc)) return false;
        locKey = loc;
        return true;
    }

    /// <summary>已登记的全部实体码（诊断/门禁对账用）。</summary>
    public IReadOnlyCollection<string> Codes => _locByCode.Keys;
}
