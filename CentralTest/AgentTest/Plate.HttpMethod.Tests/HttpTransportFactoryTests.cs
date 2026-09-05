using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Plate.HttpMethod.Transport;

namespace Plate.HttpMethod.Tests
{
    [TestClass]
    public class HttpTransportFactoryTests
    {
        [TestMethod]
        public void Create_SameDefaults_ReturnsSharedInstance()
        {
            var a = HttpTransportFactory.Create();
            var b = HttpTransportFactory.Create();
            Assert.AreSame(a, b, "相同默认配置应复用同一进程级连接池单例");
        }

        [TestMethod]
        public void Create_SameOptions_ReturnsSharedInstance()
        {
            var o = new HttpTransportOptions { MaxConnectionsPerServer = 4 };
            Assert.AreSame(HttpTransportFactory.Create(o), HttpTransportFactory.Create(o), "相同配置指纹应复用同一池");
        }

        [TestMethod]
        public void Create_DifferentOptions_ReturnsDistinctPools()
        {
            var a = HttpTransportFactory.Create(new HttpTransportOptions { MaxConnectionsPerServer = 2 });
            var b = HttpTransportFactory.Create(new HttpTransportOptions { MaxConnectionsPerServer = 8 });
            Assert.AreNotSame(a, b, "不同配置应隔离为独立池（各自连接治理）");
        }

        [TestMethod]
        public void CreateInstance_ReturnsIndependentInstance()
        {
            var a = HttpTransportFactory.CreateInstance();
            var b = HttpTransportFactory.CreateInstance();
            Assert.AreNotSame(a, b, "CreateInstance 每次新建独立池，供调用方自行 Dispose");
            a.Dispose();
            b.Dispose();
        }

        [TestMethod]
        public void SharedPool_HasNoHardTimeout()
        {
            var c = HttpTransportFactory.Create();
            Assert.AreEqual(Timeout.InfiniteTimeSpan, c.Timeout, "受管连接池不设硬编码单点超时，由调用方显式施加");
        }
    }
}
