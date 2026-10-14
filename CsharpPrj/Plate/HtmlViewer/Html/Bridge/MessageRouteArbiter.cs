#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Plate.HtmlViewer;

namespace UiBackend.Html;

/// <summary>
/// 消息订阅仲裁（L0 桥，纯逻辑零 WebView2）——同一控件名的 <c>MessageReceived</c> 订阅权同一时刻至多一个 owner；
/// 抢占式获取撤销旧 owner，消除「plate 桥 + ECS 事件绑定」双路调用（根治同一 postMessage 重复副作用与双写）。
/// 订阅/解订阅经注入的委托落到宿主事件，保持与既有直连订阅一致的语义与顺序（可无控件单测）。
/// </summary>
public sealed class MessageRouteArbiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IDisposable> _owners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<EventHandler<HtmlMessageReceivedEventArgs>> _attach;
    private readonly Action<EventHandler<HtmlMessageReceivedEventArgs>> _detach;

    public MessageRouteArbiter(
        Action<EventHandler<HtmlMessageReceivedEventArgs>> attach,
        Action<EventHandler<HtmlMessageReceivedEventArgs>> detach)
    {
        _attach = attach ?? throw new ArgumentNullException(nameof(attach));
        _detach = detach ?? throw new ArgumentNullException(nameof(detach));
    }

    /// <summary>抢占式获取消息订阅权（撤销同控件名旧 owner）——后到者接管，顺序无关。</summary>
    public IDisposable Acquire(string controlName, EventHandler<HtmlMessageReceivedEventArgs> handler)
    {
        if (string.IsNullOrWhiteSpace(controlName)) throw new ArgumentNullException(nameof(controlName));
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            if (_owners.TryGetValue(controlName, out var prev))
                prev.Dispose();
            _attach(handler);
            var token = new Token(this, controlName, handler);
            _owners[controlName] = token;
            return token;
        }
    }

    /// <summary>仅当控件名未被占用时获取订阅权（返回 null = 已由他方持有）。</summary>
    public IDisposable? TryAcquireIfIdle(string controlName, EventHandler<HtmlMessageReceivedEventArgs> handler)
    {
        if (string.IsNullOrWhiteSpace(controlName)) return null;
        lock (_gate)
        {
            if (_owners.ContainsKey(controlName))
                return null;
        }
        return Acquire(controlName, handler);
    }

    /// <summary>控件名是否已有消息订阅 owner。</summary>
    public bool HasOwner(string controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName)) return false;
        lock (_gate)
            return _owners.ContainsKey(controlName);
    }

    /// <summary>消息订阅令牌（撤销即解订阅；仅当仍为当前 owner 时移除登记，避免误删他方接管后的条目）。</summary>
    private sealed class Token : IDisposable
    {
        private readonly MessageRouteArbiter _owner;
        private readonly string _controlName;
        private readonly EventHandler<HtmlMessageReceivedEventArgs> _handler;
        private int _disposed;

        public Token(MessageRouteArbiter owner, string controlName, EventHandler<HtmlMessageReceivedEventArgs> handler)
        {
            _owner = owner;
            _controlName = controlName;
            _handler = handler;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            lock (_owner._gate)
            {
                _owner._detach(_handler);
                if (_owner._owners.TryGetValue(_controlName, out var cur) && ReferenceEquals(cur, this))
                    _owner._owners.Remove(_controlName);
            }
        }
    }
}
