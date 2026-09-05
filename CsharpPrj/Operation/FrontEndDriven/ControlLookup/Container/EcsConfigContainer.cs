#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Common.PlaceholderFramework.Integrated.Handlers.ModuleLog;
using Framework.Tree;
using UniversalMiddleware.Abstractions;
using TConfig.Abstractions;
using Share.EventBindingOrchestrator;
using ECS.Core;
using FrontEndDriven.ECS;
using FrontEndDriven.ECS.Services;
using FrontEndDriven.Utils;
using UniversalMiddleware.Container;
using UniversalMiddleware.Container.Events;
using UniversalMiddleware.Container.Lifecycle;
using Modules.EventFramework.Bus;
using Modules.DiagnosticContext;
using IContainer = UniversalMiddleware.Container.IContainer;
using UniversalLayoutMath;
using CfgModels = TConfig;
using OverlapMath = UniversalLayoutMath.OverlapMath;
using SkeletonFlow;
using Common.Diagnostics;
using TConfig.ContextFrame.Abstractions;
using EcsContainer = UniversalMiddleware.Container.EcsContainer;

namespace FrontEndDriven.Container
{
    [SupportedOSPlatform("windows")]
    public class EcsConfigContainer : EcsContainer, IEntityTreeRoot
    {
        private readonly IECSStorage? _ecsStorage;
        private readonly IStorageRegistry? _storageRegistry;
        private CfgModels.UIConfiguration? _currentConfig;


        private static readonly ControlStyles CachedControlStyles =
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw;


        public CfgModels.UIConfiguration? CurrentConfig => _currentConfig;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ContainerConfig? ContainerConfig { get; set; }

        // 2026-08-06 重构（E8）：派生类遮蔽事件 ContentChanged 已删除——无订阅者且遮蔽基类
        // EcsContainer.ContentChanged（IConfigContainer 接口实现由基类提供），外部订阅本类事件将绑定到空事件收不到通知。

        public EcsConfigContainer(IECSStorage ecsStorage, IStorageRegistry storageRegistry)
            : base()
        {
            _ecsStorage = ecsStorage ?? throw new ArgumentNullException(nameof(ecsStorage));
            _storageRegistry = storageRegistry ?? throw new ArgumentNullException(nameof(storageRegistry));
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Dock = DockStyle.Fill;

            // P2 清理：删除恒真判断（原 _cachedControlStyles 恒非 0）与常量缓存过度设计，直接 SetStyle
            // （对齐 ContainerControl.ContainerView 的同款清理，消除两处同类控件清理结果不一致）。
            SetStyle(CachedControlStyles, true);
            UpdateStyles();
        }

        // 2026-08-06 重构（E8）：ClearContainerContent 已删除——容器内容清理统一经作用域销毁路径
        // （RenderStageRunner.DestroyScopeEntities），容器仅维护自身 Controls，避免「容器自清 vs ECS 销毁」职责重叠。

        // S4：composition 生命周期唯一由 EcsGate 持有（应用级），本容器不拥有 composition 所有权——
        // Dispose 仅清自身控件集合，绝不 Dispose composition（作用域销毁经 EcsGate.DestroyScopeEntities）。
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Lifecycle.TryTransitionTo(ContainerLifecycleState.Disposing);
                Controls.Clear();
                _currentConfig = null;
                Lifecycle.TryTransitionTo(ContainerLifecycleState.Disposed);
            }
            base.Dispose(disposing);
        }
    }
}