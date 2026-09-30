using System;
using System.Collections.Generic;

/// <summary>从参考点依次求起终点的一段路径；位移单位为逻辑像素。</summary>
public sealed record VPathSegmentAttribute
{
    /// <summary>路径模式：XY、Bezier或Function。</summary>
    public string PathMode { get; init; } = "XY";
    /// <summary>从本段参考点计算起点的动作；首段参考父位置，后续段参考上一段终点。</summary>
    public IReadOnlyList<VNodeMoveActionAttribute> StartMoveQueue { get; init; } = Array.Empty<VNodeMoveActionAttribute>();
    /// <summary>从起点计算终点的动作；省略或空队列表示不移动。</summary>
    public IReadOnlyList<VNodeMoveActionAttribute> EndMoveQueue { get; init; } = Array.Empty<VNodeMoveActionAttribute>();
    /// <summary>Bezier控制点，相对本段参考点，按屏幕右/下方向定义，单位为像素。</summary>
    public IReadOnlyList<VPathPointAttribute> ControlPoints { get; init; } = Array.Empty<VPathPointAttribute>();
    /// <summary>仅Function使用：Absolute返回世界坐标，Relative使用起点与端点连线坐标系；默认Absolute。</summary>
    public string AxisMode { get; init; } = "Absolute";
    /// <summary>Function的X坐标表达式，支持t、L、SX/SY/EX/EY，单位为像素。</summary>
    public string? X { get; init; }
    /// <summary>Function的Y坐标表达式，支持t、L、SX/SY/EX/EY，单位为像素。</summary>
    public string? Y { get; init; }
    /// <summary>Function参数起值，默认0，须小于TMax。</summary>
    public double TMin { get; init; }
    /// <summary>Function参数终值，默认1，须大于TMin。</summary>
    public double TMax { get; init; } = 1;
    /// <summary>曲线弧长估算的参数区间数，默认1024，允许2至65536；直线不使用。</summary>
    public int Samples { get; init; } = 1024;
}
