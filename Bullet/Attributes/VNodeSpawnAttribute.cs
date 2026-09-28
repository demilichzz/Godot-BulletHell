using System;
using System.Collections.Generic;

/// <summary>基础成员、逐轮增量或随机宽度；运动单位沿用VNodeMoveAttribute。</summary>
public sealed record VNodeSpawnAttribute : VNodeMoveAttribute
{
    /// <summary>非负整数毫秒；基础组相对批次触发，增量组表示逐轮延迟。</summary>
    public long SpawnDelayMs { get; init; }
    /// <summary>按声明顺序求和的出生位移，默认空。</summary>
    public IReadOnlyList<VNodeMoveActionAttribute> RefMoveQueue { get; init; } = Array.Empty<VNodeMoveActionAttribute>();
}
