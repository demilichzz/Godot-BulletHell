using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

/// <summary>编辑器静态布局与路径采样；无战斗对象、随机抽样或全局绑定。</summary>
public sealed class EditorLayout
{
    /// <summary>每个Creator基础项的静态世界坐标，单位为逻辑像素。</summary>
    public List<EditorCanvas.Marker> Markers { get; } = new();
    /// <summary>路径所属Creator、基础项与世界坐标折线。</summary>
    public List<(string Path, int Basis, Vector2[] Points)> Paths { get; } = new();

    /// <summary>按原始表达式展开复制声明，仅读取独立副本。</summary>
    /// <param name="root">已经通过加载器校验的文档。</param>
    /// <returns>保留表达式的展开文档。</returns>
    public static JsonObject Resolve(JsonObject root)
    {
        // 展开仅供定位与构造最小覆盖，永远不直接写回整份文档。
        var result = (JsonObject)root.DeepClone(); Expand(result["VNodes"]!.AsObject()); return result;
    }

    /// <summary>按同级声明顺序展开复制节点，与协议的名称后缀和数组合并方式一致。</summary>
    /// <param name="node">独立Creator声明。</param>
    private static void Expand(JsonObject node)
    {
        if (node["Children"] is not JsonArray children) return;
        // 已展开的同级源配置。
        var sources = new Dictionary<string, JsonObject>();
        for (int index = 0; index < children.Count; index++)
        {
            var child = children[index]!.AsObject(); var core = child["Core"]!.AsObject();
            if (core["CopySource"] is JsonValue reference)
            {
                var copy = (JsonObject)sources[reference.ToString()].DeepClone();
                Rename(copy, "_copy_" + core["Name"]);
                var patch = (JsonObject)child.DeepClone(); patch["Core"]!.AsObject().Remove("CopySource"); patch["Core"]!.AsObject().Remove("Name");
                Merge(copy, patch, true);
                if (patch.ContainsKey("Children")) Expand(copy);
                children[index] = copy;
            }
            else Expand(child);
            child = children[index]!.AsObject();
            if (child["Core"]?["Name"] is JsonValue name) sources[name.ToString()] = child;
        }
    }

    /// <summary>追加复制树名称后缀。</summary>
    /// <param name="node">克隆的源节点。</param>
    /// <param name="suffix">协议名称后缀。</param>
    private static void Rename(JsonObject node, string suffix)
    {
        if (node["Core"]?["Name"] is JsonValue name) node["Core"]!["Name"] = name.ToString() + suffix;
        if (node["Children"] is JsonArray children) foreach (var child in children) Rename(child!.AsObject(), suffix);
    }

    /// <summary>递归合并覆盖，Creator基础数组按下标合并，其他数组替换。</summary>
    /// <param name="target">独立目标对象。</param>
    /// <param name="patch">原始覆盖声明。</param>
    /// <param name="creator">是否位于Creator顶层。</param>
    private static void Merge(JsonObject target, JsonObject patch, bool creator = false)
    {
        foreach (var pair in patch)
        {
            if (creator && pair.Key == "BaseAttributes" && pair.Value is JsonArray edits)
            {
                if (edits.Count == 0) continue;
                var bases = target["BaseAttributes"] as JsonArray ?? new JsonArray(new JsonObject());
                if (target["BaseAttributes"] is null) target["BaseAttributes"] = bases;
                for (int index = 0; index < edits.Count; index++)
                    if (index < bases.Count) Merge(bases[index]!.AsObject(), edits[index]!.AsObject()); else bases.Add(edits[index]!.DeepClone());
            }
            else if (pair.Value is JsonObject obj && target[pair.Key] is JsonObject existing) Merge(existing, obj);
            else target[pair.Key] = pair.Value?.DeepClone();
        }
    }

    /// <summary>从已校验的运行定义建立静态布局。</summary>
    /// <param name="emitter">未启动的运行定义。</param>
    public EditorLayout(VBulletEmitter emitter)
    {
        Add(emitter.Root, "/VNodes", emitter.Core.RefObject == "Boss" ? BattleConfig.BossSpawn : Vector2.Zero);
    }

    /// <summary>按Creator前序生成图标与路径；子树使用父第一基础项。</summary>
    /// <param name="creator">当前定义。</param>
    /// <param name="path">JSON路径。</param>
    /// <param name="origin">父参考世界位置，逻辑像素。</param>
    private void Add(VNodeCreator creator, string path, Vector2 origin)
    {
        // VPath在出生位移之前采样；激光在出生位置计算自身路径。
        Vector2[]? geometry = creator is VPathCreator vp ? Sample(vp.Geometry, origin) : null;
        Vector2 first = origin;
        for (int index = 0; index < creator.BaseAttributes.Count; index++)
        {
            var moves = creator.BaseAttributes[index].RefMoveQueue;
            Vector2 position = Move(geometry is null ? origin : geometry[0], moves);
            if (index == 0) first = position;
            Markers.Add(new EditorCanvas.Marker(path, index, position, (creator.Core.Name ?? creator.Core.Type) + $" [{index}]"));
            if (geometry is not null) Paths.Add((path, index, geometry.Select(point => Move(point, moves)).ToArray()));
            if (creator is VLaserCreator { Geometry: { } laserPath }) Paths.Add((path, index, Sample(laserPath, position)));
        }
        for (int index = 0; index < creator.Children.Count; index++)
            Add(creator.Children[index], path + "/Children/" + index, first);
    }

    /// <summary>顺序计算无增量、无随机的基础出生位移。</summary>
    /// <param name="position">初始世界逻辑坐标。</param>
    /// <param name="moves">位移动作。</param>
    /// <returns>最终世界逻辑坐标。</returns>
    private static Vector2 Move(Vector2 position, IReadOnlyList<VNodeMoveActionAttribute> moves)
    {
        foreach (var move in moves)
            position = move.Type == "PMove" ? VMath.PolarMove(position, move.Angle ?? 0, move.Dist ?? 0)
                : move.Type == "TarMove" ? VMath.TargetMove(position, new Vector2((float)(move.X ?? 0), (float)(move.Y ?? 0)), move.Dist ?? 0)
                : position + new Vector2((float)(move.X ?? 0), (float)(move.Y ?? 0));
        if (!position.IsFinite()) throw new InvalidOperationException("静态布局坐标溢出。");
        return position;
    }

    /// <summary>直接复用已编译的公共几何，瞄准段使用编辑器固定玩家位置。</summary>
    /// <param name="geometry">游戏加载器校验后的不可变几何。</param>
    /// <param name="origin">路径参考世界坐标，逻辑像素。</param>
    /// <returns>包含所有连接点的世界坐标折线。</returns>
    private static Vector2[] Sample(VPathGeometry geometry, Vector2 origin)
    {
        // 保留现有显示密度及每段参考点的浮点转换顺序，不重新序列化或编译函数。
        var points = new List<Vector2>(); Vector2 reference = origin;
        for (int index = 0; index < geometry.Segments.Count; index++)
        {
            int pointCount = geometry.Segments[index].PathMode == "XY" ? 2 : 257;
            var sampled = geometry.SampleSegment(index, reference, pointCount, () => BattleConfig.PlayerSpawn)
                .Select(offset => reference + offset).ToArray();
            if (points.Count > 0 && sampled[0].DistanceTo(reference) > 0.001) throw new InvalidOperationException("路径连接点偏差超过0.001像素。");
            points.AddRange(points.Count == 0 ? sampled : sampled.Skip(1)); reference = sampled[^1];
        }
        return points.ToArray();
    }
}
