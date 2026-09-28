/// <summary>Center随机总宽度；零宽度不消耗随机，时间不允许随机。</summary>
public sealed record VNodeRandDiffAttribute
{
    /// <summary>一次生成规则触发的全部轮次共享一次抽样。</summary>
    public VNodeSpawnAttribute Batch { get; init; } = new();
    /// <summary>每个实际出生成员独立抽样，包括首颗。</summary>
    public VNodeSpawnAttribute Member { get; init; } = new();
}
