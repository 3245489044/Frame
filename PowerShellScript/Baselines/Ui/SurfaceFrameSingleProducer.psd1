@{
    Schema = 'gate-baseline-v1'
    Gate   = 'SurfaceFrameSingleProducer'
    # 判据载体 = 源码结构计数（渲染器内视图构造 / ProvideFor 生产者数 / 快照与 TryFrame 调用点）。
    MinChecks = @{ Value = 4; Origin = 'Derived'; DueWhen = '' }
}
