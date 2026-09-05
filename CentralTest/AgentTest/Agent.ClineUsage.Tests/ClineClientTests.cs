using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Modules;

namespace Agent.Modules.Tests
{
    /// <summary>
    /// ClineClient 测试：
    ///  - 构造器单元测试（确定性，不触 node）
    ///  - 私有逻辑反射测试（Deserialize / ComputeInnerTimeoutSec）
    ///  - 真实 AI 调用门控测试（Ask/Plan/Resume，需显式设置 CLINE_RUN_LLM=1）
    /// 注：单次 node/cli.js 路径已移除（唯一 daemon 池通道），AppendOption 等单次路径逻辑不再存在。
    /// </summary>
    [TestClass]
    [TestCategory("Modules_Unit")]
    public class ClineClientTests
    {
        // ---- 构造器单元测试 ----

        [TestMethod]
        public void Ctor_默认参数_应返回客户端实例()
        {
            // 单次 node 路径已移除（唯一 daemon 池通道）：构造不再解析 cli.js，不应抛 FileNotFoundException。
            var client = new ClineClient();

            Assert.IsNotNull(client, "默认构造不应返回 null");
        }

        // ---- 私有逻辑反射测试 ----

        [TestMethod]
        public void Deserialize_合法JSON_应返回实例()
        {
            var generic = GetStaticGenericMethod("Deserialize").MakeGenericMethod(typeof(ClineRunResult));
            const string json = """{ "ok": true, "exitCode": 0, "resultText": "hi" }""";

            var result = generic.Invoke(null, new object[] { json }) as ClineRunResult;

            Assert.IsNotNull(result, "合法 JSON 应反序列化出实例");
            Assert.IsTrue(result!.Ok, "ok 应为 true");
            Assert.AreEqual("hi", result.Text, "resultText 应解析");
        }

        [TestMethod]
        public void Deserialize_非法JSON_应返回null()
        {
            var generic = GetStaticGenericMethod("Deserialize").MakeGenericMethod(typeof(ClineRunResult));

            var result = generic.Invoke(null, new object[] { "{ not json" }) as ClineRunResult;

            Assert.IsNull(result, "非法 JSON 应返回 null 而非抛出");
        }

        [TestMethod]
        public void ComputeInnerTimeoutSec_预算充足_应返回deadline减margin()
        {
            var method = GetStaticMethod("ComputeInnerTimeoutSec");
            var result = (int?)method.Invoke(null, new object?[] { 60, 5 });
            Assert.AreEqual(55, result, "内层自报超时应为 60-5=55s（预算 > 2*margin 才扣）");
        }

        [TestMethod]
        public void ComputeInnerTimeoutSec_预算不足_不扣margin避免竞速()
        {
            var method = GetStaticMethod("ComputeInnerTimeoutSec");
            var result = (int?)method.Invoke(null, new object?[] { 3, 5 });
            Assert.AreEqual(3, result, "预算 ≤ 2*margin 时不扣 margin（由框架 AEF 兜底，杜绝内层≥框架竞速）");
        }

        [TestMethod]
        public void ComputeInnerTimeoutSec_无预算_应返回null()
        {
            var method = GetStaticMethod("ComputeInnerTimeoutSec");
            var result = (int?)method.Invoke(null, new object?[] { null, 5 });
            Assert.IsNull(result, "未显式预算时内层超时应为 null（用默认）");
        }

        private static MethodInfo GetStaticMethod(string name)
        {
            // ComputeInnerTimeoutSec 已从 ClineClient 迁至 internal 的 DaemonTimeout（Agent.Modules.Pool）。
            // 测试程序集无 InternalsVisibleTo，故在已加载程序集中按类型全名反射解析（方法本身为 public static）。
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Agent.Modules.Pool.DaemonTimeout"))
                .FirstOrDefault(t => t is not null);
            return type?.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException($"未找到私有静态方法 {name}");
        }

        private static MethodInfo GetStaticGenericMethod(string name)
            => typeof(ClineClient).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException($"未找到私有静态方法 {name}");
    }
}
