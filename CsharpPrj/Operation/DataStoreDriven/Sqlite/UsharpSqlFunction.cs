using System;
using System.Data.SQLite;
using Plate.DslMethod.Usharp;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// <c>usharp_get(doc, path)</c> 确定性 SQL 函数的**唯一注册点**（USharp 纯化施工单 §1.6）：
/// 以中立 <see cref="UsharpGet.Extract"/> 实现投影，经 <see cref="SQLiteFunction.RegisterFunction(Type)"/> 作
/// **进程级全局注册**（System.Data.SQLite 于每条新连接自动装载注册表），使所有连接（读/写/事务/测试）均具备该函数，
/// 从而过滤/排序/分页/CAS 可在 SQL 侧下推（配 <c>usharp_get</c> 表达式索引）。
/// </summary>
public static class UsharpSqlFunction
{
    /// <summary>SQL 函数名（表达式索引须与此逐字一致）。</summary>
    public const string Name = "usharp_get";

    private static readonly object Gate = new();
    private static bool _registered;

    /// <summary>注册函数（幂等；进程内一次）。组合根 / 首次使用 USharp codec 时调用。</summary>
    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered) return;
            try
            {
                SQLiteFunction.RegisterFunction(typeof(UsharpGetFunction));
            }
            catch (ArgumentException)
            {
                // 已注册（同名/同类型）——幂等容忍。
            }
            _registered = true;
        }
    }
}

/// <summary><c>usharp_get(doc, path)</c> 标量函数实现（确定性 ⇒ 允许用于表达式索引）。</summary>
[SQLiteFunction(FuncType = FunctionType.Scalar, Name = UsharpSqlFunction.Name, Arguments = 2,
    FuncFlags = SQLiteFunctionFlags.SQLITE_DETERMINISTIC)]
internal sealed class UsharpGetFunction : SQLiteFunction
{
    /// <inheritdoc />
    public override object Invoke(object[] args)
    {
        var doc = args.Length > 0 ? args[0] as string : null;
        var path = args.Length > 1 ? args[1] as string : null;
        return UsharpGet.Extract(doc, path) is { } value ? value : (object)DBNull.Value;
    }
}
