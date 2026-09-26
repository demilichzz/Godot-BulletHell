/// <summary>节点队列共用的标识、数量、寿命与运动模式。</summary>
public record VNodeCoreAttribute
{
    /// <summary>Emitter内唯一的非空定义标识。</summary>
    public string Id { get; init; } = "";
    /// <summary>数据格式版本，当前为2。</summary>
    public int Version { get; init; } = 2;
    /// <summary>每批生成的正整数数量。</summary>
    public int Amount { get; init; } = 1;
    /// <summary>正数寿命秒数；节点省略表示持续至父对象结束。</summary>
    public double? LifeTimeS { get; init; }
    /// <summary>为真时沿外部Angle施加加速度。</summary>
    public bool AAngleIsSameAsAngle { get; init; } = true;
    /// <summary>出生角度来源，Fixed或AimPlayer。</summary>
    public string AngleMode { get; init; } = "Fixed";
}
