/// <summary>发射器的参考对象、共通伤害及停止策略。</summary>
public sealed record EmitterCoreAttribute
{
    /// <summary>发射器非空标识。</summary>
    public string Id { get; init; } = "";
    /// <summary>数据格式版本，当前为2。</summary>
    public int Version { get; init; } = 2;
    /// <summary>Boss表示所属Boss，null表示世界原点。</summary>
    public string? RefObject { get; init; } = "Boss";
    /// <summary>所有新生子弹的阵营。</summary>
    public BulletTeam Team { get; init; } = BulletTeam.Enemy;
    /// <summary>所有新生子弹的正整数伤害。</summary>
    public int Damage { get; init; } = 1;
    /// <summary>停止策略，KeepBullets或ClearBullets。</summary>
    public string StopMode { get; init; } = "KeepBullets";
}
