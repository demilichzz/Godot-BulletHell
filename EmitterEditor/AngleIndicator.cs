using Godot;
using System;

/// <summary>弧度方向示意，0向右、π/2向下、顺时针为正。</summary>
public partial class AngleIndicator : Control
{
    // 输入保留原圈数，图示只显示最终方向；无效表达式显示叉号。
    private double? _angle;
    /// <summary>读取表达式并更新图示，不消费随机。</summary>
    /// <param name="text">数值或PI/TAU表达式。</param>
    public void SetText(string text)
    {
        try
        {
            // 复用游戏数值转换器，避免两套表达式规则。
            using var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(text));
            _angle = JsonData.Read<double>(document.RootElement);
            TooltipText = $"{_angle:G7} rad / {VMath.RadiansToDegrees(_angle.Value):G7}°\n0向右，π/2向下；顺时针为正。增量、偏移字段显示其自身角度，不代表最终运动方向。";
        }
        catch { _angle = null; TooltipText = "表达式无效，无法显示方向。"; }
        QueueRedraw();
    }
    /// <summary>绘制圆、射线和箭头。</summary>
    public override void _Draw()
    {
        // 方向图示的控件局部圆心。
        var center = Size / 2;
        DrawArc(center, 16, 0, Mathf.Tau, 32, new Color("7894aa"), 1);
        if (_angle is null) { DrawLine(center - Vector2.One * 5, center + Vector2.One * 5, Colors.IndianRed, 2); return; }
        // 经规范化后的显示方向单位向量。
        var direction = Vector2.FromAngle((float)VMath.StandardizationAngle(_angle.Value));
        // 方向箭头末端，距圆心16像素。
        var tip = center + direction * 16;
        DrawLine(center, tip, new Color("ffd48a"), 2);
        DrawLine(tip, tip - direction.Rotated(0.5f) * 7, new Color("ffd48a"), 2);
        DrawLine(tip, tip - direction.Rotated(-0.5f) * 7, new Color("ffd48a"), 2);
    }
}
