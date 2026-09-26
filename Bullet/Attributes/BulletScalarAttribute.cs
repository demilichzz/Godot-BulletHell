/// <summary>位移标量的基础值、索引增量与两种随机宽度。</summary>
public sealed record BulletScalarAttribute
{
    /// <summary>基础值，单位由所属角度或位移字段决定。</summary>
    public double Value { get; init; } = 0;
    /// <summary>每队列共享的非负随机总宽度，Center模式。</summary>
    public double RandDiff { get; init; } = 0;
    /// <summary>按出生索引累加的固定增量，可为负。</summary>
    public double ValueAdd { get; init; } = 0;
    /// <summary>包括首颗在内逐颗独立抽样的非负随机总宽度。</summary>
    public double RandDiffAdd { get; init; } = 0;
}
