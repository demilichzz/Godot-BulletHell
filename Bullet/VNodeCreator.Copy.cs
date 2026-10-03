using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>仅在加载阶段展开同级CopySource，运行树不保存复制依赖。</summary>
public partial class VNodeCreator
{
    /// <summary>展开复制后调用原有严格读取流程；没有复制指令时不创建JSON对象树。</summary>
    /// <param name="element">独立Creator根对象。</param>
    /// <returns>完成字段、类型和运动校验的独立Creator。</returns>
    internal static VNodeCreator ReadCreator(JsonElement element)
    {
        if (!HasCopySource(element)) return ReadExpandedCreator(element, "$");
        // 复制只处理配置，所有随机、成员与时间线状态仍在实际生成时创建。
        var root = JsonNode.Parse(element.GetRawText()) as JsonObject
            ?? throw new JsonException("$: Creator必须为对象。");
        if (root["Core"] is JsonObject core && core.ContainsKey("CopySource"))
            throw new JsonException("$.Core.CopySource: 根节点不能使用CopySource。");
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        ExpandChildren(root, "$", references);
        // 命名重写后先提供准确JSON路径，再由现有树初始化建立查找索引。
        try
        {
            ValidateExpandedNames(root, "$", new Dictionary<string, string>(StringComparer.Ordinal));
            return ReadExpandedCreator(JsonSerializer.SerializeToElement(root), "$");
        }
        catch (JsonException error)
        {
            // 类型校验在展开后执行，仍补回最深复制声明的引用名称。
            string? reference = null;
            int depth = -1;
            foreach (var entry in references)
                if (error.Message.Contains(entry.Key, StringComparison.Ordinal) && entry.Key.Length > depth)
                {
                    reference = entry.Value;
                    depth = entry.Key.Length;
                }
            if (reference is not null) throw new JsonException($"CopySource '{reference}': {error.Message}", error);
            throw;
        }
    }

    /// <summary>检查Creator树中是否出现复制指令，不解释其他属性组。</summary>
    /// <param name="element">当前JSON值。</param>
    /// <returns>存在Core.CopySource时为真，包括显式null。</returns>
    private static bool HasCopySource(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (element.TryGetProperty("Core", out var core) && core.ValueKind == JsonValueKind.Object
            && core.TryGetProperty("CopySource", out _)) return true;
        if (element.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray())
                if (HasCopySource(child)) return true;
        return false;
    }

    /// <summary>按Children声明顺序展开本层，并递归处理原始子树或显式替换的子树。</summary>
    /// <param name="parent">已独立拥有其配置的父Creator。</param>
    /// <param name="path">父Creator的JSON路径。</param>
    /// <param name="references">复制声明路径及来源名称，用于展开后的字段错误诊断。</param>
    private static void ExpandChildren(JsonObject parent, string path, Dictionary<string, string> references)
    {
        if (!parent.TryGetPropertyValue("Children", out var value)) return;
        if (value is not JsonArray children) throw new JsonException(path + ".Children: 必须为数组。");
        // 只登记本层早于当前项的最终名称，并保留是否为复制对象的标记。
        var earlier = new Dictionary<string, (JsonObject Data, bool Copied)>(StringComparer.Ordinal);
        for (int index = 0; index < children.Count; index++)
        {
            string childPath = $"{path}.Children[{index}]";
            var declaration = children[index] as JsonObject ?? throw new JsonException(childPath + ": Creator必须为对象。");
            var core = declaration["Core"] as JsonObject ?? throw new JsonException(childPath + ".Core: 必须为对象。");
            bool copied = core.ContainsKey("CopySource");
            JsonObject expanded;
            if (copied)
            {
                // Name是本次后缀标识；来源只从当前Children已解析的原始节点取得。
                string sourceName = CopyName(core["CopySource"], childPath + ".Core.CopySource");
                string suffix = CopyName(core["Name"], childPath + ".Core.Name");
                references[childPath] = sourceName;
                if (!earlier.TryGetValue(sourceName, out var source) || source.Copied)
                    throw new JsonException($"{childPath}.Core.CopySource '{sourceName}': 只能引用本层更早声明、未使用CopySource的Creator。");
                try
                {
                    if (core.ContainsKey("Type") && !JsonNode.DeepEquals(core["Type"], source.Data["Core"]!["Type"]))
                        throw new JsonException("Core.Type必须与复制源相同。");
                    expanded = (JsonObject)source.Data.DeepClone();
                    RenameInherited(expanded, "_copy_" + suffix);
                    // 特殊名称已确定；剔除加载指令与后缀标识，避免普通合并覆盖最终Name。
                    var patch = (JsonObject)declaration.DeepClone();
                    ((JsonObject)patch["Core"]!).Remove("CopySource");
                    ((JsonObject)patch["Core"]!).Remove("Name");
                    bool replacesChildren = patch.ContainsKey("Children");
                    MergeCopyObject(expanded, patch, true);
                    if (replacesChildren) ExpandChildren(expanded, childPath, references);
                }
                catch (JsonException error)
                {
                    throw new JsonException($"{childPath} (CopySource '{sourceName}'): {error.Message}", error);
                }
            }
            else
            {
                expanded = declaration;
                ExpandChildren(expanded, childPath, references);
            }
            if (copied) children[index] = expanded;
            // 未命名节点仍可运行，但不能作为复制来源；类型错误留给严格读取器报告。
            if (expanded["Core"]?["Name"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var name)
                && !string.IsNullOrWhiteSpace(name) && !earlier.TryAdd(name, (expanded, copied)))
                throw new JsonException($"{childPath}.Core.Name: 重复Creator名称 '{name}'。");
        }
    }

    /// <summary>读取复制指令所需的非空名称，精确保留大小写及非空字符串内容。</summary>
    /// <param name="value">JSON名称值。</param>
    /// <param name="path">字段错误路径。</param>
    /// <returns>非空白字符串。</returns>
    private static string CopyName(JsonNode? value, string path)
        => value is JsonValue text && text.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name)
            ? name : throw new JsonException(path + ": 复制源及新Name必须为非空字符串。");

    /// <summary>为继承的整棵子树追加本次后缀；未命名节点不产生名称。</summary>
    /// <param name="creator">深复制后的Creator配置。</param>
    /// <param name="suffix">包含_copy_前缀的后缀。</param>
    private static void RenameInherited(JsonObject creator, string suffix)
    {
        if (creator["Core"] is JsonObject core && core["Name"] is JsonValue value
            && value.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name))
            core["Name"] = name + suffix;
        if (creator["Children"] is JsonArray children)
            foreach (var child in children)
                if (child is JsonObject node) RenameInherited(node, suffix);
    }

    /// <summary>递归合并对象，只有Creator顶层BaseAttributes按下标合并；其他数组整体替换。</summary>
    /// <param name="target">独立的源配置副本。</param>
    /// <param name="patch">显式覆盖配置。</param>
    /// <param name="creatorRoot">是否位于Creator顶层。</param>
    private static void MergeCopyObject(JsonObject target, JsonObject patch, bool creatorRoot = false)
    {
        // 每个赋入值独立克隆，避免JsonNode父引用或原始声明被共享。
        foreach (var field in patch)
        {
            if (creatorRoot && field.Key == "BaseAttributes" && field.Value is JsonArray edits)
            {
                // VPath省略基础列表等价于[{}]；空覆盖数组不改变其默认值。
                var values = target["BaseAttributes"] as JsonArray;
                if (values is null && edits.Count > 0)
                {
                    values = new JsonArray(new JsonObject());
                    target["BaseAttributes"] = values;
                }
                for (int index = 0; index < edits.Count; index++)
                {
                    if (edits[index] is not JsonObject edit) throw new JsonException($"BaseAttributes[{index}]: 必须为对象。");
                    if (index < values!.Count && values[index] is JsonObject basis) MergeCopyObject(basis, edit);
                    else if (index < values.Count) values[index] = edit.DeepClone();
                    else values.Add(edit.DeepClone());
                }
            }
            else if (field.Value is JsonObject edit && target[field.Key] is JsonObject basis)
                MergeCopyObject(basis, edit);
            else target[field.Key] = field.Value?.DeepClone();
        }
    }

    /// <summary>检查最终整树名称唯一性，保留发生重名的两处JSON路径。</summary>
    /// <param name="creator">展开后的Creator。</param>
    /// <param name="path">当前JSON路径。</param>
    /// <param name="names">本树已有名称与声明位置。</param>
    private static void ValidateExpandedNames(JsonObject creator, string path, Dictionary<string, string> names)
    {
        if (creator["Core"] is JsonObject core && core["Name"] is JsonValue value
            && value.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name))
        {
            if (names.TryGetValue(name, out var previous))
                throw new JsonException($"{path}.Core.Name: 重复Creator名称 '{name}'，已在{previous}定义。");
            names.Add(name, path);
        }
        if (creator["Children"] is JsonArray children)
            for (int index = 0; index < children.Count; index++)
                if (children[index] is JsonObject node) ValidateExpandedNames(node, $"{path}.Children[{index}]", names);
    }
}
