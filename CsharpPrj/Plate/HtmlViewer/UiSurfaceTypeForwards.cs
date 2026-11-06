#nullable enable

using System.Runtime.CompilerServices;
using UiBackend.Abstractions;
using UiBackend.Html;

// 容器契约（视图程序 / 容器 / 结构槽 / 状态迁移）已下沉 Plate.Surface（BCL-only）。
// 命名空间保持不变，仅程序集边界变化；转发使已编译消费方（Agent 烘焙轨）无需重编即可解析类型。
// 新增契约件时：一律放 Plate.Surface 并在此追加一行转发。
[assembly: TypeForwardedTo(typeof(UiViewLifecycle))]
[assembly: TypeForwardedTo(typeof(UiViewContainerRole))]
[assembly: TypeForwardedTo(typeof(UiViewContainer))]
[assembly: TypeForwardedTo(typeof(UiViewStateMigration))]
[assembly: TypeForwardedTo(typeof(UiViewBlueprint))]
[assembly: TypeForwardedTo(typeof(UiScrollState))]
[assembly: TypeForwardedTo(typeof(UiInteractionSnapshot))]
[assembly: TypeForwardedTo(typeof(UiViewContainerSlot))]
[assembly: TypeForwardedTo(typeof(UiViewContainerSlots))]
[assembly: TypeForwardedTo(typeof(UiViewBlueprintCatalog))]
[assembly: TypeForwardedTo(typeof(UiViewStateMigrationResult))]
[assembly: TypeForwardedTo(typeof(UiViewStateMigrationPolicy))]
[assembly: TypeForwardedTo(typeof(UiViewNames))]
