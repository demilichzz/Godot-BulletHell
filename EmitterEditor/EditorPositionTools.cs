using Godot;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

/// <summary>把静态布局位移写回原协议，保留表达式与复制指令。</summary>
public static class EditorPositionTools
{
    /// <summary>判断表单字段是否能改变当前静态出生位置。</summary>
    /// <param name="path">原始JSON字段指针。</param>
    /// <returns>是否需要遵循后代位置保持开关。</returns>
    public static bool IsSpatial(string path) => path.Contains("MoveQueue", StringComparison.Ordinal) || path.Contains("/PathQueue", StringComparison.Ordinal) || path == "/Core/RefObject" ||
        path.EndsWith("/BaseAttributes", StringComparison.Ordinal) || path.Contains("/BaseAttributes/", StringComparison.Ordinal) && int.TryParse(path[(path.LastIndexOf('/') + 1)..], out _);

    /// <summary>在一次文档事务中平移某个基础项，不修改其速度、角度或生命周期。</summary>
    /// <param name="document">正在编辑的文档。</param>
    /// <param name="path">Creator指针。</param>
    /// <param name="basis">基础项零基下标。</param>
    /// <param name="delta">世界逻辑像素位移。</param>
    public static void Translate(EmitterDocument document, string path, int basis, Vector2 delta)
    {
        if (delta.LengthSquared() < 0.00000001f) return;
        // 解析保留表达式的有效配置；逐级创建必要的复制覆盖。
        var resolved = EditorLayout.Resolve(document.Root);
        var effective = At(resolved, path).AsObject();
        var node = Editable(document.Root, resolved, path);
        var bases = effective["BaseAttributes"] as JsonArray ?? new JsonArray(new JsonObject());
        if (basis < 0 || basis >= bases.Count) throw new InvalidOperationException("选中的基础项已不存在。");
        var queue = bases[basis]!["RefMoveQueue"] as JsonArray ?? new JsonArray();
        // 末尾直角位移直接加补偿，不影响原队列对齐。
        if (queue.Count > 0 && queue[^1]?["Type"]?.ToString() == "XYMove")
        {
            var replacement = (JsonArray)queue.DeepClone(); AddOffset(replacement[^1]!.AsObject(), delta);
            Basis(node, basis)["RefMoveQueue"] = replacement; return;
        }
        // 有非空增量或随机队列时，所有基础项同步补零槽，零随机宽度不消耗随机数。
        string[] companions = { "AddAttributes", "RandDiffAttributes/Batch", "RandDiffAttributes/Member" };
        bool aligned = companions.Any(location => Group(effective, location, false)?["RefMoveQueue"] is JsonArray list && list.Count > 0);
        for (int index = 0; index < bases.Count; index++)
        {
            if (!aligned && index != basis) continue;
            var replacement = bases[index]!["RefMoveQueue"]?.DeepClone() as JsonArray ?? new JsonArray();
            replacement.Add(new JsonObject { ["Type"] = "XYMove", ["X"] = index == basis ? delta.X : 0, ["Y"] = index == basis ? delta.Y : 0 });
            Basis(node, index)["RefMoveQueue"] = replacement;
        }
        if (aligned) foreach (string location in companions)
        {
            if (Group(effective, location, false)?["RefMoveQueue"] is not JsonArray { Count: > 0 } list) continue;
            var replacement = (JsonArray)list.DeepClone(); replacement.Add(new JsonObject { ["Type"] = "XYMove", ["X"] = 0, ["Y"] = 0 });
            Group(node, location, true)!["RefMoveQueue"] = replacement;
        }
    }

    /// <summary>按前序补偿后代，使每个仍存在的基础图标保持原静态世界位置。</summary>
    /// <param name="document">已应用父位置修改的事务内文档。</param>
    /// <param name="path">被修改的Creator；空字符串表示Emitter。</param>
    /// <param name="before">修改前的完整布局。</param>
    public static void PreserveChildren(EmitterDocument document, string path, EditorLayout before)
    {
        foreach (var marker in before.Markers.Where(marker => path.Length == 0 || marker.Path.StartsWith(path + "/Children/", StringComparison.Ordinal)))
        {
            // 每次按最新父位置重算，避免世界目标TarMove与多层参考的累积误差。
            var current = new EditorLayout(document.Validate(), document.Root).Markers.Find(item => item.Path == marker.Path && item.Basis == marker.Basis);
            if (current is not null) Translate(document, marker.Path, marker.Basis, marker.Position - current.Position);
        }
        document.Validate();
    }

    /// <summary>在表达式外叠加偏移，不把PI/TAU原文求值写回。</summary>
    /// <param name="move">末尾XYMove动作。</param>
    /// <param name="delta">世界逻辑像素偏移。</param>
    private static void AddOffset(JsonObject move, Vector2 delta)
    {
        foreach (var pair in new[] { (Key: "X", Delta: delta.X), (Key: "Y", Delta: delta.Y) })
        {
            if (pair.Delta == 0) continue;
            if (move[pair.Key] is JsonValue value && value.TryGetValue<string>(out var expression))
                move[pair.Key] = $"({expression}) + ({pair.Delta.ToString("R", CultureInfo.InvariantCulture)})";
            else move[pair.Key] = (move[pair.Key] is null ? 0 : double.Parse(move[pair.Key]!.ToJsonString(), CultureInfo.InvariantCulture)) + pair.Delta;
        }
    }

    /// <summary>取得或创建按下标合并的基础项覆盖。</summary>
    /// <param name="node">原始Creator声明。</param>
    /// <param name="index">零基下标。</param>
    /// <returns>仅包含显式字段的基础项。</returns>
    private static JsonObject Basis(JsonObject node, int index)
    {
        if (node["BaseAttributes"] is not JsonArray) node["BaseAttributes"] = new JsonArray();
        var bases = node["BaseAttributes"]!.AsArray();
        while (bases.Count <= index) bases.Add(new JsonObject());
        return bases[index]!.AsObject();
    }

    /// <summary>取得嵌套增量或随机组，可按需创建覆盖对象。</summary>
    /// <param name="node">Creator声明。</param>
    /// <param name="path">相对组路径。</param>
    /// <param name="create">是否创建缺省组。</param>
    /// <returns>属性组或空。</returns>
    private static JsonObject? Group(JsonObject node, string path, bool create)
    {
        foreach (string part in path.Split('/'))
        {
            if (node[part] is not JsonObject) { if (!create) return null; node[part] = new JsonObject(); }
            node = node[part]!.AsObject();
        }
        return node;
    }

    /// <summary>查找展开JSON中的Creator。</summary>
    /// <param name="root">完整文档。</param>
    /// <param name="path">仅含VNodes和Children下标的指针。</param>
    /// <returns>对应节点。</returns>
    private static JsonNode At(JsonObject root, string path)
    {
        JsonNode node = root;
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries)) node = node is JsonArray array ? array[int.Parse(part)]! : node[part]!;
        return node;
    }

    /// <summary>定位原文节点，继承子树首次编辑时显式覆盖Children，保留其他复制关系。</summary>
    /// <param name="root">原始文档。</param>
    /// <param name="resolved">已展开且保留表达式的独立副本。</param>
    /// <param name="path">Creator指针。</param>
    /// <returns>可编辑的原文节点。</returns>
    private static JsonObject Editable(JsonObject root, JsonObject resolved, string path)
    {
        var node = root["VNodes"]!.AsObject(); var source = resolved["VNodes"]!.AsObject();
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 2; index < parts.Length; index += 2)
        {
            if (node["Children"] is not JsonArray) node["Children"] = source["Children"]!.DeepClone();
            int child = int.Parse(parts[index]); node = node["Children"]![child]!.AsObject(); source = source["Children"]![child]!.AsObject();
        }
        return node;
    }
}
