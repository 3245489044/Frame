# BasicMethod/Common 整合任务清单

## 已完成的工作

### 任务1: UI 线程调度逻辑整合 ✅
- [x] 从 AsyncHelper.cs 中删除 RunOnUiThread / ExecuteOnUiThread 方法（保留 FireAndForget、WithTimeout、WithRetry）
- [x] AsyncHelper.cs 增加带 operationName 参数的 FireAndForget 重载
- [x] AsyncFormBase.cs 的 InvokeIfRequired / BeginInvokeIfRequired / SafeSet / SafeGet 改为委托至 ControlExtensions 扩展方法
- [x] AsyncSyncOrchestrator.cs 的 FireAndForget 方法改为委托给 AsyncHelper

### 任务2: 命名空间统一 ✅
- [x] AsyncSyncOrchestrator.cs: `Common.AsyncSync` → `BasicMethod.Common.AsyncSync`
- [x] IAsyncSyncOrchestrator.cs: `Common.AsyncSync` → `BasicMethod.Common.AsyncSync`
- [x] AsyncStepCoordinator.cs: `Common.StepCoordination` → `BasicMethod.Common.StepCoordination`
- [x] IStepCoordinator.cs: `Common.StepCoordination` → `BasicMethod.Common.StepCoordination`
- [x] StepCoordinationExtensions.cs: `Common.StepCoordination` → `BasicMethod.Common.StepCoordination`
- [x] BasicValidator.cs: `Common.GraphMindStudio` → `BasicMethod.Common.Utilities`

### 任务3: 移除 Newtonsoft.Json 依赖 ✅
- [x] BasicValidator.cs: `JToken.Parse` → `JsonDocument.Parse` (System.Text.Json)

## 待完成的工作

### 任务4: 剩余命名空间统一（影响范围大，需谨慎）
- [ ] Common/Models/*.cs: `Common.Models` → `BasicMethod.Common.Models`
- [ ] Common/DiscreteChange/*.cs: `Common.DiscreteChange` → `BasicMethod.Common.DiscreteChange`
- [ ] Common/ReferenceManagement/*.cs: `Common.ReferenceManagement` → `BasicMethod.Common.ReferenceManagement`
- [ ] Common/Serialization/*.cs: `Common.NewtonSoftJsonFramework` / `Common.SystemJsonFramework` → `BasicMethod.Common.Serialization`
- [ ] Common/Configuration/*.cs: `Common.GraphMindStudio` / `Common.RemoteProcess.Configuration` → `BasicMethod.Common.Configuration`
- [ ] Common/Functions/*.cs: `Common.GraphMindStudio` → `BasicMethod.Common.Functions`
- [ ] Common/Utilities/*.cs: `Common.GraphMindStudio` / `Common.FileProcess` → `BasicMethod.Common.Utilities`
- [ ] Common/TextProcessing/*.cs: `Common.GraphMindStudio` → `BasicMethod.Common.TextProcessing`
- [ ] 更新所有外部引用（Pipeline、BasicAlgorithm、UniversalReference 等）

### 任务5: 序列化框架整合
- [ ] 评估是否统一到 System.Text.Json
- [ ] 删除 Serialization/NewtonSoftJsonFramework.cs
- [ ] 删除 Serialization/DictionaryConverter.cs
- [ ] 完善 SystemJsonFramework.cs
