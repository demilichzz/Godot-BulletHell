using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>领域共享的严格JSON读取，不依赖战斗实例或Creator运行状态。</summary>
public static class JsonData
{
    // 一次配置后只读复用，不按字段创建转换器。
    private static readonly JsonSerializerOptions Options = CreateOptions();
    /// <summary>建立严格属性、字符串枚举与常量小数表达式规则。</summary>
    /// <returns>初始化完成且不可修改的序列化选项。</returns>
    private static JsonSerializerOptions CreateOptions()
    {
        // 整数字段仍不接受表达式。
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new JsonNumberConverter());
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
    /// <summary>读取Godot数据文件。</summary>
    /// <param name="path">JSON资源路径。</param>
    /// <returns>文件完整文本。</returns>
    internal static string ReadFile(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        return file is null ? throw new System.IO.IOException($"无法读取{path}：{Godot.FileAccess.GetOpenError()}") : file.GetAsText();
    }

    /// <summary>统一解析重复字段并附加来源信息。</summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="json">JSON文本。</param>
    /// <param name="source">来源名称。</param>
    /// <param name="read">仅在加载阶段使用的对象读取函数。</param>
    /// <returns>已构造的目标对象。</returns>
    internal static T Parse<T>(string json, string source, Func<JsonElement, T> read)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            CheckDuplicates(document.RootElement, "$");
            return read(document.RootElement);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException or FormatException)
        {
            throw new JsonException($"{source}: {error.Message}", error);
        }
    }

    /// <summary>直接将一个属性组反序列化，拒绝未知字段。</summary>
    /// <typeparam name="T">属性类型。</typeparam>
    /// <param name="element">对应JSON值。</param>
    /// <returns>已构造的非空属性对象。</returns>
    internal static T Read<T>(JsonElement element)
    {
        return element.Deserialize<T>(Options) ?? throw new JsonException("属性值不能为null。");
    }

    /// <summary>取得必须存在的非空属性。</summary>
    /// <param name="element">父JSON对象。</param>
    /// <param name="name">必需字段名。</param>
    /// <returns>对应JSON值。</returns>
    internal static JsonElement Required(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : throw new JsonException(name + "缺失或为null。");

    /// <summary>校验对象的所有字段在允许集合内。</summary>
    /// <param name="element">JSON对象。</param>
    /// <param name="allowed">合法字段名。</param>
    internal static void CheckFields(JsonElement element, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException("需要JSON对象。");
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) throw new JsonException(property.Name + ": 未知字段。");
    }

    /// <summary>递归检查重复字段，保留完整错误路径。</summary>
    /// <param name="element">当前值。</param>
    /// <param name="path">JSON路径。</param>
    private static void CheckDuplicates(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException(path + "." + property.Name + ": 重复字段。");
                CheckDuplicates(property.Value, path + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var value in element.EnumerateArray()) CheckDuplicates(value, $"{path}[{index++}]");
        }
    }
}
