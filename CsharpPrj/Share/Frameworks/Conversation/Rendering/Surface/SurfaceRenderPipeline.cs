#nullable enable

using System;
using System.Collections.Generic;
using Share.Conversation.Presentation;

namespace Share.Conversation.Rendering.Surface;

/// <summary>一次管线渲染的结果（计划 + 载体产物 + 可见失败清单）。</summary>
/// <param name="Plan">合成计划（唯一 compose 实现产出）。</param>
/// <param name="CarrierId">载体 id（声明面键）。</param>
/// <param name="Artifact">载体产物（未注册/投影失败 ⇒ null）。</param>
/// <param name="Failures">可见失败清单（禁静默）。</param>
public sealed record SurfacePipelineResult(
    RenderPlan Plan, string CarrierId, object? Artifact, IReadOnlyList<string> Failures);

/// <summary>
/// 载体无关渲染管线（**唯一生产路径**）：声明闭包 → 合成（FrameComposer）→ 载体投影（注册表）。
/// <para>
/// 载体通过 <see cref="Register"/> 注册（工厂按声明构造投影器），管线本身不认识任何具体载体；
/// 未注册载体 ⇒ 可见失败 + 空产物（禁静默回退到某个默认载体）。
/// </para>
/// <para>
/// 用途：HTML / WinForms / Text / Headless 及后续载体共用同一入口；新增载体 = 注册一行，不改管线。
/// </para>
/// </summary>
public sealed class SurfaceRenderPipeline
{
    /// <summary>载体未注册的失败码前缀。</summary>
    public const string UnregisteredCode = "carrier-unregistered";

    /// <summary>投影抛异常的失败码前缀。</summary>
    public const string ViolationCode = "project-violation";

    /// <summary>产物为空的失败码前缀。</summary>
    public const string ArtifactNullCode = "artifact-null";

    private readonly Dictionary<string, Func<SurfaceDeclarations, IBackendProjector<object>>> _factories =
        new(StringComparer.Ordinal);

    /// <summary>已注册载体 id 集（声明序 = 注册序）。</summary>
    public IReadOnlyCollection<string> CarrierIds => _factories.Keys;

    /// <summary>注册载体（重复注册 ⇒ 覆盖；空 id/工厂 ⇒ 抛）。</summary>
    /// <param name="carrierId">载体 id（声明面键）。</param>
    /// <param name="factory">按声明构造投影器的工厂。</param>
    public SurfaceRenderPipeline Register(
        string carrierId, Func<SurfaceDeclarations, IBackendProjector<object>> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[carrierId] = factory;
        return this;
    }

    /// <summary>是否已注册该载体。</summary>
    /// <param name="carrierId">载体 id。</param>
    public bool IsRegistered(string carrierId)
        => !string.IsNullOrEmpty(carrierId) && _factories.ContainsKey(carrierId);

    /// <summary>仅合成计划（不投影；供门禁/对拍复用同一 compose 实现）。</summary>
    /// <param name="items">渲染项（源序）。</param>
    /// <param name="policy">帧语义策略（可空）。</param>
    /// <param name="declarations">声明闭包。</param>
    /// <param name="viewKey">视图键。</param>
    public static RenderPlan Compose(
        IReadOnlyList<ConversationRenderItem> items,
        FramePolicy? policy,
        SurfaceDeclarations declarations,
        string viewKey = "")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(declarations);
        return FrameComposer.Compose(items, policy, declarations.ComposerOptions, viewKey);
    }

    /// <summary>渲染：合成 + 投影 + 可见失败记账。</summary>
    /// <param name="carrierId">载体 id。</param>
    /// <param name="items">渲染项（源序）。</param>
    /// <param name="policy">帧语义策略（可空）。</param>
    /// <param name="declarations">声明闭包。</param>
    /// <param name="viewKey">视图键。</param>
    public SurfacePipelineResult Render(
        string carrierId,
        IReadOnlyList<ConversationRenderItem> items,
        FramePolicy? policy,
        SurfaceDeclarations declarations,
        string viewKey = "")
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var failures = new List<string>();
        if (!declarations.Ui.IsDeclared) failures.Add("ui-program-unavailable");

        var plan = Compose(items, policy, declarations, viewKey);
        if (!_factories.TryGetValue(carrierId ?? string.Empty, out var factory))
        {
            failures.Add($"{UnregisteredCode}:{carrierId}");
            return new SurfacePipelineResult(plan, carrierId ?? string.Empty, null, failures);
        }

        object? artifact = null;
        try
        {
            artifact = factory(declarations).Project(plan);
        }
        catch (Exception ex)
        {
            failures.Add($"{ViolationCode}:{carrierId}:{ex.Message}");
        }

        if (artifact is null) failures.Add($"{ArtifactNullCode}:{carrierId}");
        return new SurfacePipelineResult(plan, carrierId ?? string.Empty, artifact, failures);
    }
}