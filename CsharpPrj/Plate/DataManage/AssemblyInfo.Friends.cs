// 取证件 SqliteWriteOwnerRegistry(internal) 暴露给持久化回归测试（Agent.Persistence.Tests），
// 用于验证 registry 精确增删/无并发竞态虚报/借出点栈采集。本程序集未签名，无需公钥。
// 另暴露给 Operation.DataStoreDriven：Modules.Threading 版单写 host（ThreadingSqliteWriteHost）复用 DbPathOf 归一化键，
// 保持「单一写权登记 + 单写通道」跨程序集单实现（对齐 SingleImplementation，不复制 DbPathOf）。
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Agent.Persistence.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Operation.DataStoreDriven")]
