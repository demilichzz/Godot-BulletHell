using Godot;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>JSON中的世界反射区域；矩形选边，圆形选顺时针弧，数值支持普通PI/TAU表达式。</summary>
public sealed record VReflectionRegionAttribute
{
    /// <summary>必填Rectangle或Circle，区分大小写。</summary>
    public string Type { get; init; } = "";
    /// <summary>矩形左上角或圆心世界横坐标，像素，必填。</summary>
    public double? X { get; init; }
    /// <summary>矩形左上角或圆心世界纵坐标，像素，必填。</summary>
    public double? Y { get; init; }
    /// <summary>Rectangle正宽度，像素。</summary>
    public double? Width { get; init; }
    /// <summary>Rectangle正高度，像素。</summary>
    public double? Height { get; init; }
    /// <summary>Circle正半径，像素。</summary>
    public double? Radius { get; init; }
    /// <summary>Rectangle可选边数组；省略四边，空数组不反射，支持Left/Right/Top/Bottom。</summary>
    public IReadOnlyList<string>? Edges { get; init; }
    /// <summary>Circle顺时针弧起始角度，默认0弧度。</summary>
    public double? AngleMin { get; init; }
    /// <summary>Circle顺时针弧终止角度，默认TAU弧度；小于起始角可跨零。</summary>
    public double? AngleMax { get; init; }

    /// <summary>读取严格的模式字段并建立不可变区域，避免运行对象持有可变JSON数组。</summary>
    /// <param name="element">非空区域对象。</param>
    /// <returns>已经校验的配置；创建实例时进一步校验数值范围。</returns>
    internal static VReflectionRegionAttribute Read(JsonElement element)
    {
        string type = VNodeCreator.Required(element, "Type").GetString() ?? "";
        VNodeCreator.CheckFields(element, type switch
        {
            "Rectangle" => new[] { "Type", "X", "Y", "Width", "Height", "Edges" },
            "Circle" => new[] { "Type", "X", "Y", "Radius", "AngleMin", "AngleMax" },
            _ => throw new JsonException("ReflectionRegion.Type只支持Rectangle或Circle。")
        });
        foreach (var field in element.EnumerateObject())
            if (field.Value.ValueKind == JsonValueKind.Null) throw new JsonException("反射区域字段不能显式为null。");
        return VNodeCreator.Read<VReflectionRegionAttribute>(element);
    }

    /// <summary>创建经严格校验的世界反射区域。</summary>
    /// <returns>无可变列表的区域实例，可安全在多个子弹间共享。</returns>
    internal VReflectionRegion Create()
    {
        if (!X.HasValue || !Y.HasValue) throw new JsonException("反射区域必须提供X和Y。");
        // 最终坐标和尺寸经形状构造函数检查有限性、正值及溢出。
        var origin = new Vector2((float)X.Value, (float)Y.Value);
        if (Type == "Circle" && Radius.HasValue && !Width.HasValue && !Height.HasValue && Edges is null)
            return new VReflectionRegion(new CircleRegionShape(origin, Radius.Value), AngleMin ?? 0, AngleMax ?? System.Math.Tau);
        if (Type != "Rectangle" || !Width.HasValue || !Height.HasValue || Radius.HasValue || AngleMin.HasValue || AngleMax.HasValue)
            throw new JsonException("反射区域类型与字段不匹配。");
        RectangleEdges edges = Edges is null ? RectangleEdges.All : RectangleEdges.None;
        if (Edges is not null)
            foreach (var name in Edges)
            {
                var edge = name switch
                {
                    "Left" => RectangleEdges.Left,
                    "Right" => RectangleEdges.Right,
                    "Top" => RectangleEdges.Top,
                    "Bottom" => RectangleEdges.Bottom,
                    _ => throw new JsonException("反射边只支持Left、Right、Top、Bottom。")
                };
                if ((edges & edge) != 0) throw new JsonException("反射边不能重复。");
                edges |= edge;
            }
        return new VReflectionRegion(new RectangleRegionShape(new Rect2(origin, new Vector2((float)Width.Value, (float)Height.Value))), edges);
    }
}
