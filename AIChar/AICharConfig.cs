using System;

/// <summary>一场AI角色的创建配置；重开保留配置并重新创建策略和随机流。</summary>
public sealed record AICharConfig
{
    /// <summary>是否加入AI角色，默认关闭。</summary>
    public bool Enabled { get; init; }
    /// <summary>独立随机流初始种子，默认1，不从战斗流抽取。</summary>
    public int Seed { get; init; } = 1;
    /// <summary>每次创建独立策略实例的工厂，默认基础密度策略。</summary>
    public Func<IAIStrategy> StrategyFactory { get; init; } = () => new BasicAIStrategy();
}