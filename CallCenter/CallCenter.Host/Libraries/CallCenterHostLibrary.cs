using TConfig;

namespace CallCenter.Host.Libraries;

/// <summary>
/// CallCenter 宿主自带的真实 C# 函数库（C#Track 发现源的最小真实样例）。
/// 遵循仓库既定跨程序集函数库约定：静态类 + <c>RegisterAll(ITrConfigRegistry)</c>，由
/// function-libraries.json 清单（assembly-qualified type）登记，经 <see cref="Core.Discovery.CSharpFunctionLibrarySource"/>
/// 反射执行并把注册函数投影为 CSG 工具 —— 而非在 Program 中硬编码工具描述。
/// P2 起此机制将承载 FileDriven.Business 等真实业务库，此处仅证明「真源替换」通道打通。
/// </summary>
public static class CallCenterHostLibrary
{
    /// <summary>注册确定性宿主函数。函数名即 C#Track 工具名，须在平台内唯一。</summary>
    public static void RegisterAll(ITrConfigRegistry registry)
    {
        registry.Register("cc_echo", (string text) => text, "回显输入文本（确定性）");
        registry.Register("cc_utc_now", () => DateTime.UtcNow.ToString("O"), "返回当前 UTC 时间（确定性）");
        registry.Register("cc_add", (double a, double b) => a + b, "两数相加（确定性）");
    }
}
