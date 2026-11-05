namespace Share.Signal;

/// <summary>生命周期的一条合法边（from → to）。</summary>
public sealed record SignalLifecycleEdge(string From, string To);
