using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 两级路由注册表：action 级 + action×op 级。未命中 op → 由注入的 <c>unknownOpError</c> 产出 opError
    /// （携带 allowed 白名单），或回落注册时给定的 <c>fallback</c>。注册序即 <see cref="Inventory"/> 序。
    /// <para>通用件：零领域类型 —— 诊断文案（unknownOpError）与 op 归一（normalizeOp）一律由调用方以委托注入
    /// （域侧单源，禁在框架内再造第二套文案），注册表本身不含任何领域字面量。</para>
    /// </summary>
    /// <typeparam name="TRequest">请求载体类型（由调用方契约给定）。</typeparam>
    /// <typeparam name="TContext">handler 上下文类型（如协议解包后的原始文本）。</typeparam>
    public sealed class OpActionRegistry<TRequest, TContext>
    {
        private sealed record OpAction(
            string Label, string Allowed, Func<TRequest, string?> OpSource, string DefaultOp,
            Func<TRequest, TContext, Task<string>>? Fallback);

        private readonly Func<object?, string, string, string> _unknownOpError;
        private readonly Func<object?, string, string> _normalizeOp;
        private readonly Dictionary<string, Func<TRequest, TContext, Task<string>>> _actionLevel;
        private readonly Dictionary<string, OpAction> _opActions;
        private readonly Dictionary<string, Func<TRequest, TContext, Task<string>>> _opHandlers;
        private readonly List<(string Action, string Op)> _inventory = new();

        /// <param name="unknownOpError">未知 op 诊断文案工厂（域侧注入；参数序：raw, label, allowed）。</param>
        /// <param name="normalizeOp">op 归一（域侧注入；参数序：raw, defaultOp → 归一 op）。</param>
        /// <param name="comparer">查表比较器（默认忽略大小写）。</param>
        public OpActionRegistry(
            Func<object?, string, string, string> unknownOpError,
            Func<object?, string, string> normalizeOp,
            IEqualityComparer<string>? comparer = null)
        {
            _unknownOpError = unknownOpError ?? throw new ArgumentNullException(nameof(unknownOpError));
            _normalizeOp = normalizeOp ?? throw new ArgumentNullException(nameof(normalizeOp));
            var table = comparer ?? StringComparer.OrdinalIgnoreCase;
            _actionLevel = new Dictionary<string, Func<TRequest, TContext, Task<string>>>(table);
            _opActions = new Dictionary<string, OpAction>(table);
            _opHandlers = new Dictionary<string, Func<TRequest, TContext, Task<string>>>(table);
        }

        /// <summary>注册无 op 语义的 action（协议解包后整段执行）。</summary>
        public void RegisterAction(string action, Func<TRequest, TContext, Task<string>> handler)
        {
            _actionLevel[action] = handler;
            _inventory.Add((action, string.Empty));
        }

        /// <summary>注册带 op 语义的 action：ops = op → handler；未命中 → fallback（非 null）或 unknownOpError。</summary>
        public void RegisterOpAction(string action, string label, string allowed,
            Func<TRequest, string?> opSource, string defaultOp,
            IReadOnlyDictionary<string, Func<TRequest, TContext, Task<string>>> ops,
            Func<TRequest, TContext, Task<string>>? fallback = null)
        {
            _opActions[action] = new OpAction(label, allowed, opSource, defaultOp, fallback);
            foreach (var kv in ops)
            {
                _opHandlers[Key(action, kv.Key)] = kv.Value;
                _inventory.Add((action, kv.Key));
            }
        }

        /// <summary>解析 action：命中 → handler；op 非法 → opError（含 allowed 白名单）；未知 action → false 且 opError=null。</summary>
        public bool TryResolve(string action, TRequest req,
            out Func<TRequest, TContext, Task<string>> handler, out string? opError)
        {
            handler = null!;
            opError = null;
            if (_actionLevel.TryGetValue(action, out var direct))
            {
                handler = direct;
                return true;
            }
            if (!_opActions.TryGetValue(action, out var meta)) return false;

            var raw = meta.OpSource(req);
            var op = _normalizeOp(raw, meta.DefaultOp);
            if (_opHandlers.TryGetValue(Key(action, op), out var found))
            {
                handler = found;
                return true;
            }
            if (meta.Fallback is not null)
            {
                handler = meta.Fallback;
                return true;
            }
            opError = _unknownOpError(raw, meta.Label, meta.Allowed);
            return false;
        }

        /// <summary>已注册路由清单（装配对账 / 诊断 / 测试用；注册序）。</summary>
        public IReadOnlyList<(string Action, string Op)> Inventory => _inventory;

        /// <summary>复合查表键（action 与 op 不可歧义拼接）。</summary>
        private static string Key(string action, string op) => action + "\u0001" + op;
    }
}
