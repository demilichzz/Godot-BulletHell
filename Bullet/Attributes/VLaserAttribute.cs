using Godot;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>两种激光共用的不可变定义；长度为逻辑像素，时间为整数毫秒。</summary>
public sealed record VLaserAttribute
{
    /// <summary>Fixed为定点激光，Path为沿冻结路径移动，默认Fixed。</summary>
    public string Mode { get; init; } = "Fixed";
    /// <summary>无PathQueue时的定点亮芯长度或移动亮芯目标弧长，正数像素，默认240；固定路径使用完整路径长度。</summary>
    public double Length { get; init; } = 240;
    /// <summary>运行时主体全宽，正数像素，默认8；JSON由Core.Width提供，不含外发光。</summary>
    [JsonIgnore]
    public double Width { get; init; } = 8;
    /// <summary>运行时混合模式，Mix、Add或仅亮芯置顶加算的CoreAdd，默认Mix；JSON由Display.BlendMode提供。</summary>
    [JsonIgnore]
    public string BlendMode { get; init; } = "Mix";
    /// <summary>碰撞全宽，正数且不大于主体宽度，默认6像素。</summary>
    public double HitWidth { get; init; } = 6;
    /// <summary>两端显示形状，Point尖角或Round圆形，默认Round。</summary>
    public string EndCap { get; init; } = "Round";
    /// <summary>完整尖角15度时的单端收束长度，像素，由主体宽度计算，不属于JSON。</summary>
    [JsonIgnore]
    public double TipLength => Width * 0.5 / Math.Tan(VMath.DegreesToRadians(7.5));
    /// <summary>主体的不透明HTML颜色，默认青蓝色；预警和消退透明度由阶段控制。</summary>
    public string Color { get; init; } = "#55CCFF";
    /// <summary>亮芯的不透明HTML颜色，默认白色；亮芯宽度为主体的75%。</summary>
    public string CoreColor { get; init; } = "#FFFFFF";
    /// <summary>外发光的不透明HTML颜色，省略或null时沿用主体颜色；向外逐渐透明。</summary>
    public string? GlowColor { get; init; }
    /// <summary>定点预警时长，非负整数毫秒，默认600；路径模式不使用。</summary>
    public long WarningMs { get; init; } = 600;
    /// <summary>定点展开时长，非负整数毫秒，默认150；路径模式不使用。</summary>
    public long ExpandMs { get; init; } = 150;
    /// <summary>定点生效时长，正整数毫秒，默认1000；路径模式不使用。</summary>
    public long ActiveMs { get; init; } = 1000;
    /// <summary>定点消退时长，非负整数毫秒，默认250；路径模式不使用。</summary>
    public long FadeMs { get; init; } = 250;
    /// <summary>路径模式必填的出生后结束时刻，正整数毫秒；Fixed由四阶段相加，不使用此字段。</summary>
    public long? EndMs { get; init; }
    /// <summary>路径模式的固定前进速度，正数像素/秒，默认240；Fixed不使用。</summary>
    public double TravelSpeed { get; init; } = 240;
    /// <summary>VPath几何采样点数，包含连接点，范围2至4096，默认128；不表示激光条数。</summary>
    public int PathPointCount { get; init; } = 128;

    /// <summary>配置总寿命，正整数毫秒；路径实际走完时可以提前释放。</summary>
    [JsonIgnore]
    public long DurationMs => Mode == "Path" ? EndMs!.Value : checked(WarningMs + ExpandMs + ActiveMs + FadeMs);

    /// <summary>检查定义，所有阶段边界必须能转换为内部整数时间单位。</summary>
    internal void Validate()
    {
        if (Mode is not ("Fixed" or "Path") || EndCap is not ("Point" or "Round"))
            throw new JsonException("Laser.Mode只支持Fixed/Path，EndCap只支持Point/Round。");
        if (BlendMode is not ("Mix" or "Add" or "CoreAdd")) throw new JsonException("激光Display.BlendMode只支持Mix、Add或CoreAdd。");
        // 显示与判定长度均限制为可表示的正单精度像素。
        foreach (double value in new[] { Length, Width, HitWidth, TravelSpeed })
            if (!double.IsFinite(value) || value <= 0 || value > float.MaxValue || (float)value == 0)
                throw new JsonException("激光长度、宽度和移动速度必须为正有限数。");
        if (HitWidth > Width || PathPointCount < 2 || PathPointCount > 4096)
            throw new JsonException("HitWidth不得超过Width，PathPointCount须为2至4096。");
        if (WarningMs < 0 || ExpandMs < 0 || ActiveMs <= 0 || FadeMs < 0
            || (Mode == "Path" && (EndMs is null || EndMs <= 0)) || (Mode == "Fixed" && EndMs is not null))
            throw new JsonException("定点使用四阶段时长；路径必须使用正整数EndMs定义总持续时间。");
        // 各层分别校验；外光可省略，由主体颜色补全，透明度统一由显示阶段计算。
        foreach (var (name, color) in new[] { (nameof(Color), Color), (nameof(CoreColor), CoreColor), (nameof(GlowColor), GlowColor ?? Color) })
            if (color is null || !Godot.Color.HtmlIsValid(color) || Godot.Color.FromHtml(color).A != 1)
                throw new JsonException($"Laser.{name}须为不透明HTML颜色。");
        _ = checked(DurationMs * VTimerProcessor.UnitsPerMillisecond);
    }
}
