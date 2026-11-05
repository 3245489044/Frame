using System;

namespace Modules.Observe;

/// <summary>收敛证据：第三级 ack 必须携带，表明「可观测投影已就绪」是事实而非猜测。</summary>
public sealed record ConvergedProof(string Detail, DateTimeOffset At);
