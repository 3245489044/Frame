#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Share.Conversation.Rendering.Surface.Selection;

/// <summary>
/// 进程内选择源（**最小实现**）：当前档 + 覆盖 + 指纹。
/// <para>M3/M4 只读默认档；M7 接入刷新（Strategy.Refresh）时只替换 <see cref="Current"/> 的档/覆盖，不改本类型契约。</para>
/// <para>指纹 = SHA256(view|profile|carrier|覆盖键值有序)；同输入必同指纹（禁读时钟/随机）。</para>
/// </summary>
public sealed class SurfaceSelectionProvider : ISurfaceSelectionSource
{
    /// <summary>缺省档（与声明面 default 档一致）。</summary>
    public const string DefaultProfile = "default";

    private static readonly IReadOnlyDictionary<string, string> EmptyOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private volatile string _profileId = DefaultProfile;
    private IReadOnlyDictionary<string, string> _overrides = EmptyOverrides;

    /// <summary>当前选择源（装配期注入/运行期换肤的单一访问点）。</summary>
    public static SurfaceSelectionProvider Current { get; } = new();

    /// <summary>当前档（空 ⇒ default）。</summary>
    public string ProfileId
    {
        get => _profileId;
        set => _profileId = string.IsNullOrWhiteSpace(value) ? DefaultProfile : value;
    }

    /// <summary>当前覆盖（只读快照；设置后指纹变化 ⇒ 旧缓存/旧写槽失效）。</summary>
    public IReadOnlyDictionary<string, string> Overrides
    {
        get => _overrides;
        set => _overrides = value is null
            ? EmptyOverrides
            : new Dictionary<string, string>(value, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public SurfaceSelection Select(string viewKey, string carrierId)
    {
        var view = viewKey ?? string.Empty;
        var carrier = carrierId ?? string.Empty;
        var overrides = _overrides;
        return new SurfaceSelection(view, _profileId, carrier, overrides, Fingerprint(view, _profileId, carrier, overrides));
    }

    /// <summary>选择指纹（结构事实；同输入必同值）。</summary>
    /// <param name="viewKey">视图键。</param>
    /// <param name="profileId">档。</param>
    /// <param name="carrierId">载体。</param>
    /// <param name="overrides">覆盖。</param>
    public static string Fingerprint(
        string viewKey,
        string profileId,
        string carrierId,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var pairs = overrides is null
            ? string.Empty
            : string.Join(
                ";",
                overrides.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key + "=" + pair.Value));

        var text = string.Join("|", viewKey ?? string.Empty, profileId ?? string.Empty, carrierId ?? string.Empty, pairs);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
