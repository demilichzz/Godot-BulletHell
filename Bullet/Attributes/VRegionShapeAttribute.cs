using Godot;
using System.Text.Json;

/// <summary>完整世界区域的数据定义，支持圆形和矩形；数值支持普通PI/TAU表达式。</summary>
public sealed record VRegionShapeAttribute
{
    /// <summary>必填Circle或Rectangle，区分大小写。</summary>
    public string Type { get; init; } = "";
    /// <summary>圆心或矩形左上角的世界横坐标，逻辑像素，必填。</summary>
    public double? X { get; init; }
    /// <summary>圆心或矩形左上角的世界纵坐标，逻辑像素，必填。</summary>
    public double? Y { get; init; }
    /// <summary>Circle的正半径，逻辑像素。</summary>
    public double? Radius { get; init; }
    /// <summary>Rectangle的正宽度，逻辑像素。</summary>
    public double? Width { get; init; }
    /// <summary>Rectangle的正高度，逻辑像素。</summary>
    public double? Height { get; init; }

    /// <summary>严格读取形状专属字段，不接受null或反射选边、选弧字段。</summary>
    /// <param name="element">非空区域JSON对象。</param>
    /// <returns>加载阶段可转换为不可变形状的配置。</returns>
    internal static VRegionShapeAttribute Read(JsonElement element)
    {
        // 根据类型限制字段；统一反序列化器负责表达式和重复字段校验。
        string type = JsonData.Required(element, "Type").GetString() ?? "";
        JsonData.CheckFields(element, type switch
        {
            "Circle" => new[] { "Type", "X", "Y", "Radius" },
            "Rectangle" => new[] { "Type", "X", "Y", "Width", "Height" },
            _ => throw new JsonException("OutsideRegion.Type只支持Circle或Rectangle。")
        });
        foreach (var field in element.EnumerateObject())
            if (field.Value.ValueKind == JsonValueKind.Null) throw new JsonException("出界区域字段不能显式为null。");
        return JsonData.Read<VRegionShapeAttribute>(element);
    }

    /// <summary>建立可由同一Creator全部弹幕共享的不可变形状，校验数值范围。</summary>
    /// <returns>使用世界坐标的完整圆形或矩形。</returns>
    internal IRegionShape Create()
    {
        if (!X.HasValue || !Y.HasValue) throw new JsonException("出界区域必须提供X和Y。");
        // 形状构造函数验证有限性、正尺寸及坐标溢出。
        var origin = new Vector2((float)X.Value, (float)Y.Value);
        if (Type == "Circle" && Radius.HasValue && !Width.HasValue && !Height.HasValue)
            return new CircleRegionShape(origin, Radius.Value);
        if (Type == "Rectangle" && Width.HasValue && Height.HasValue && !Radius.HasValue)
            return new RectangleRegionShape(new Rect2(origin, new Vector2((float)Width.Value, (float)Height.Value)));
        throw new JsonException("出界区域类型与字段不匹配。");
    }
}
