using static JsonData;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>节点出生与路径端点共用的严格位移协议读取。</summary>
internal static class VMoveJson
{
    /// <summary>读取三种共用位移动作，冻结列表并拒绝混用字段。</summary>
    /// <param name="element">不可为null的动作数组。</param>
    /// <param name="random">是否表示非负随机总宽度，路径端点队列使用false。</param>
    /// <returns>经过校验的只读动作队列。</returns>
    internal static IReadOnlyList<VNodeMoveActionAttribute> ReadQueue(JsonElement element, bool random = false)
    {
        if (element.ValueKind != JsonValueKind.Array) throw new JsonException("位移队列必须为数组且不可为null。");
        // 每个动作缺省数值为0，显式null仍视为无效配置。
        var actions = Read<VNodeMoveActionAttribute[]>(element);
        foreach (var action in actions)
        {
            if (action is null) throw new JsonException("位移动作不可为null。");
            if (action.Type == "PMove" && action.X is null && action.Y is null)
            { ValidateNumber(action.Angle ?? 0, random); ValidateNumber(action.Dist ?? 0, random); }
            else if (action.Type == "XYMove" && action.Angle is null && action.Dist is null)
            { ValidateNumber(action.X ?? 0, random); ValidateNumber(action.Y ?? 0, random); }
            else if (action.Type == "TarMove" && action.Angle is null)
            { ValidateNumber(action.X ?? 0, random); ValidateNumber(action.Y ?? 0, random); ValidateNumber(action.Dist ?? 0, random); }
            else throw new JsonException("位移Type与字段不匹配。");
        }
        foreach (var action in element.EnumerateArray())
            foreach (var field in action.EnumerateObject())
                if (field.Value.ValueKind == JsonValueKind.Null) throw new JsonException("位移字段不可显式为null。");
        return Array.AsReadOnly(actions);
    }
    /// <summary>验证有限值和随机宽度。</summary>
    /// <param name="value">属性值或总宽度。</param>
    /// <param name="random">是否为非负宽度。</param>
    internal static void ValidateNumber(double value, bool random)
    {
        if (!double.IsFinite(value) || (random && value < 0)) throw new JsonException("属性值必须有限，随机宽度必须非负。");
    }

}
