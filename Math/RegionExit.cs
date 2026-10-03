using Godot;
using System;

/// <summary>可组合的矩形边标记；方向使用屏幕坐标。</summary>
[Flags]
public enum RectangleEdges
{
    /// <summary>不选择任何边。</summary>
    None = 0,
    /// <summary>左边。</summary>
    Left = 1,
    /// <summary>右边。</summary>
    Right = 2,
    /// <summary>上边。</summary>
    Top = 4,
    /// <summary>下边。</summary>
    Bottom = 8,
    /// <summary>选择四条边。</summary>
    All = Left | Right | Top | Bottom
}

/// <summary>线段从区域内向外穿越时的首次边界接触。</summary>
public readonly record struct RegionExit
{
    /// <summary>接触世界位置，逻辑像素。</summary>
    public Vector2 Point { get; init; }
    /// <summary>边界外向单位法线；矩形角点为两条外法线的归一化和。</summary>
    public Vector2 Normal { get; init; }
    /// <summary>接触时刻占传入线段的比例，范围0至1。</summary>
    public double Fraction { get; init; }
    /// <summary>矩形接触边；角点可同时包含两边，圆形为None。</summary>
    public RectangleEdges Edges { get; init; }
}
