/// <summary>节点队列共用的标识、数量、寿命与运动模式。</summary>
public record VNodeCoreAttribute
{
    /// <summary>树建立时按路径分配的只读标识，不属于JSON。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Id { get; internal set; } = "";
    /// <summary>可选名称，同一Emitter内非空名称唯一。</summary>
    public string? Name { get; init; }
    /// <summary>生成器类型，JSON必须显式填写VNode、VBullet或生成纯节点的VPath。</summary>
    public string Type { get; init; } = "VNode";
    /// <summary>基础列表的正整数生成轮数；VPath限单基础项，此值为路径总节点数。</summary>
    public int Amount { get; init; } = 1;
    /// <summary>正整数寿命毫秒数；节点省略表示持续至父对象结束。</summary>
    public long? LifeTimeMs { get; init; }
    /// <summary>Follow或Snapshot；省略时VNode/VPath跟随、VBullet快照。</summary>
    public string? CreatePositionMode { get; init; }
    /// <summary>为真时沿外部Angle施加加速度。</summary>
    public bool AAngleIsSameAsAngle { get; init; } = true;
    /// <summary>出生角度来源，Fixed或AimPlayer。</summary>
    public string AngleMode { get; init; } = "Fixed";
}
