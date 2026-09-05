#nullable enable
using System.Runtime.CompilerServices;

// 反应式布局子系统（Phase 1）测试缝：FrontEndDriven.Tests 访问 LayoutSubsystem 测试态构造 + LayoutScheduler。
// 注：Directory.Build.props 设 GenerateAssemblyInfo=false，SDK 不自动生成 AssemblyInfo，故此处手动声明。
[assembly: InternalsVisibleTo("FrontEndDriven.Tests")]
